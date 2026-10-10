using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using AuditLog = LPS.APS.Core.Entities.Auth.AuditLog;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 域定义治理服务单元测试（F-G4 跨 scope 越权，3号位）
/// 覆盖 EnsureDomainAllowsAsync fail-closed：Create / Update / SetActive 写入前，
/// 仅 Domain 维度授权才放行；未授权、空范围、仅 Factory 维度授权均拒绝，且不落库、不审计。
/// 边界：DomainKey 唯一权威源 = DomainDefinition；Factory/ProductFamily 不得作为 Domain 别名放行。
/// </summary>
public class DomainDefinitionGovernanceServiceTests
{
    private readonly Mock<IDomainDefinitionRepository> _repository = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<IDataScopeService> _dataScopeService = new();
    private readonly DomainDefinitionGovernanceService _service;

    public DomainDefinitionGovernanceServiceTests()
    {
        // 默认引用存在 + 唯一性通过，只让「业务范围」成为被测变量
        _repository
            .Setup(r => r.ProductFamilyExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository
            .Setup(r => r.ExistsByKeyAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _service = new DomainDefinitionGovernanceService(
            _repository.Object,
            _auditRepo.Object,
            _dataScopeService.Object,
            Mock.Of<ILogger<DomainDefinitionGovernanceService>>());
    }

    /// <summary>构造合法 FAMILY 域定义入参（FactoryId 必须为空）</summary>
    /// <remarks>
    /// 【2026-09-29 修正】原默认值为 <c>"D1"</c> —— **违反现行 DomainKey 校验规则**（`DomainKeyPattern`：
    /// 首字符大写字母、**4-50 字符**、仅 [A-Z0-9_-]，见 <c>DomainDefinitionGovernanceService.ValidateCoreAsync</c> 的 F2 注释）。
    /// 后果：本类唯一走到 key 校验的正例 `Create_授权同Domain_放行` 恒失败；另外 3 个负例在 **scope 检查**处
    /// 先抛（scope 校验先于 key 校验），恰好因对而绿、未暴露该问题。改用 4 字符合法键。
    /// </remarks>
    private static DomainDefinition ValidFamilyInput(string domainKey = "DOM1")
        => new()
        {
            DomainKey = domainKey,
            DomainName = "测试域",
            ScopeType = "FAMILY",
            ProductFamilyId = 1,
            FactoryId = null,
        };

    /// <summary>构造既有 D1 域定义</summary>
    private static DomainDefinition ExistingDefinition(int id = 10, string domainKey = "D1", bool isActive = true)
        => new()
        {
            Id = id,
            DomainKey = domainKey,
            DomainName = "既有域",
            ScopeType = "FAMILY",
            ProductFamilyId = 1,
            FactoryId = null,
            IsActive = isActive,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };

    // ==================== CreateAsync：跨 scope 越权 ====================

    [Fact]
    public async Task Create_业务范围未授权Domain_抛异常()
    {
        // Arrange：用户仅授权 D2，目标 Domain=D1
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D2") }));

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("D1"), 1, "u1", CancellationToken.None);

        // Assert：范围拒绝，不落库、不审计
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_空范围_抛异常()
    {
        // Arrange：无任何范围 → 安全默认拒绝全部
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Empty);

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("D1"), 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_仅Factory维度授权_未授权Domain_抛异常()
    {
        // Arrange（F-G4）：仅 Factory 维度授权，不得作为 Domain 别名放行
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("D1"), 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_授权同Domain_放行()
    {
        // Arrange（负向控制）：授权同域 → 放行并落库审计
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "DOM1") }));
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainDefinition { Id = 1, DomainKey = "DOM1", IsActive = true, ScopeType = "FAMILY", ProductFamilyId = 1 });

        // Act
        var result = await _service.CreateAsync(ValidFamilyInput("DOM1"), 1, "u1", CancellationToken.None);

        // Assert
        result.Id.Should().Be(1);
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<AuditLog>(l => l.ActionCode == "Create" && l.EntityId == "1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ==================== UpdateAsync：跨 scope 越权 ====================

    [Fact]
    public async Task Update_业务范围未授权Domain_抛异常()
    {
        // Arrange：既有 Domain=D1，用户仅授权 D2 → 拒绝
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "D1"));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D2") }));

        var input = ValidFamilyInput("D1");
        input.DomainName = "改名";

        // Act
        var act = async () => await _service.UpdateAsync(10, input, 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ==================== SetActiveAsync：跨 scope 越权 ====================

    [Fact]
    public async Task SetActive_业务范围未授权Domain_抛异常()
    {
        // Arrange：既有 Domain=D1（停用态），用户仅授权 D2 → 拒绝
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "D1", isActive: false));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D2") }));

        // Act
        var act = async () => await _service.SetActiveAsync(10, true, 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repository.Verify(r => r.SetActiveAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}