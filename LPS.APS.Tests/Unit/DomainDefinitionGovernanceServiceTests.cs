using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;
using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using AuditLog = LPS.APS.Core.Entities.Auth.AuditLog;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 域定义治理服务单元测试（F-G4 跨 scope 越权 + N-04 越权异常类型判别，3号位）
/// 覆盖 EnsureDomainAllowsAsync fail-closed：Create / Update / SetActive 写入前，
/// 仅 Domain 维度授权才放行；未授权、空范围、仅 Factory 维度授权均拒绝（抛 ScopeViolationException → 403），且不落库、不审计。
/// N-04（0号位 复审 v1.1）：业务越权抛 ScopeViolationException（与参数/状态非法 InvalidOperationException → 400 判别）。
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
        // G2 默认：scope 组合唯一性通过（仅被测用例按需覆盖为 true）
        _repository
            .Setup(r => r.ExistsActiveScopeComboAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _service = new DomainDefinitionGovernanceService(
            _repository.Object,
            _auditRepo.Object,
            _dataScopeService.Object);
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

    // ==================== CreateAsync：跨 scope 越权（N-04：ScopeViolationException） ====================

    [Fact]
    public async Task Create_业务范围未授权Domain_抛ScopeViolationException()
    {
        // Arrange：用户仅授权 D2，目标 Domain=D1
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D2") }));

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("DOM1"), 1, "u1", CancellationToken.None);

        // Assert：业务越权 → ScopeViolationException（Web 层映射 403），不落库、不审计
        await act.Should().ThrowAsync<ScopeViolationException>();
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_空范围_抛ScopeViolationException()
    {
        // Arrange：无任何范围 → 安全默认拒绝全部
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Empty);

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("DOM1"), 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_仅Factory维度授权_未授权Domain_抛ScopeViolationException()
    {
        // Arrange（F-G4）：仅 Factory 维度授权，不得作为 Domain 别名放行
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("DOM1"), 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
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

    // ==================== R-01（0号位 复审 v1.1，方案B）审计写失败故障注入 ====================

    [Fact]
    public async Task Create_审计写失败_抛异常_业务状态已提交_审计缺失有对账日志兜底()
    {
        // Arrange：预检通过（审计库可写）、业务写成功、实际审计写失败（Auth 在预检后故障）→ 模拟 R-01「业务已写、审计缺」窗口；
        // 对账日志由 AuditLogRepository.AddAsync 统一落（AuditReconcileTests 已证契约），此处反证 fail-closed + 写顺序
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        _auditRepo
            .Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("模拟审计写入失败"));
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainDefinition { Id = 1, DomainKey = "DOM1", IsActive = true, ScopeType = "FAMILY", ProductFamilyId = 1 });

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("DOM1"), 1, "u1", CancellationToken.None);

        // Assert：fail-closed——异常传播（不静默吞掉审计）、业务写已提交（窗口存在，跨库无法回滚）、审计尝试一次（供对账）
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("模拟审计写入失败");
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ==================== UpdateAsync：跨 scope 越权（N-04） ====================

    [Fact]
    public async Task Update_业务范围未授权Domain_抛ScopeViolationException()
    {
        // Arrange：既有 Domain=DOM1，用户仅授权 D2 → 拒绝（DOM1 为合法 DomainKey，命中 scope 检查而非 key 校验）
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "DOM1"));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D2") }));

        var input = ValidFamilyInput("DOM1");
        input.DomainName = "改名";

        // Act
        var act = async () => await _service.UpdateAsync(10, input, 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ==================== SetActiveAsync：跨 scope 越权（N-04） ====================

    [Fact]
    public async Task SetActive_业务范围未授权Domain_抛ScopeViolationException()
    {
        // Arrange：既有 Domain=D1（停用态），用户仅授权 D2 → 拒绝
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "D1", isActive: false));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D2") }));

        // Act
        var act = async () => await _service.SetActiveAsync(10, true, 1, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
        _repository.Verify(r => r.SetActiveAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetActive_域越权但状态幂等_仍抛ScopeViolationException()
    {
        // Arrange：既有 Domain=D1（已启用态），用户仅授权 D2，enable 幂等（目标态=当前态）→ 越权仍须 403（防幂等 Id 窥探）
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "D1", isActive: true));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D2") }));

        // Act：对已启用域幂等 enable（命中幂等早返回路径）
        var act = async () => await _service.SetActiveAsync(10, true, 1, "u1", CancellationToken.None);

        // Assert：越权校验先于幂等 → 仍 403，绝不返回 existing 200
        await act.Should().ThrowAsync<ScopeViolationException>();
        _repository.Verify(r => r.SetActiveAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ==================== G2：scope 组合唯一性（合法性校验职责，状态/参数类 InvalidOperationException → 400） ====================

    [Fact]
    public async Task Create_同产品族已启用FAMILY域_抛异常()
    {
        // Arrange：同 ProductFamilyId=1 已存在启用 FAMILY 域（DomainKey 不同）→ 组合判重拒绝
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "DOM2") }));
        _repository
            .Setup(r => r.ExistsActiveScopeComboAsync(
                "FAMILY", 1, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var act = async () => await _service.CreateAsync(ValidFamilyInput("DOM2"), 1, "u1", CancellationToken.None);

        // Assert：组合重复 → 拒绝（状态/校验类 InvalidOperationException → 400），不落库、不审计
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*同一产品族仅允许一个启用域*");
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_同工厂产品族已启用FACTORY_FAMILY域_抛异常()
    {
        // Arrange：FACTORY_FAMILY 同 FactoryId=9 + ProductFamilyId=2 已启用 → 拒绝
        _repository.Setup(r => r.FactoryExistsAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "DOM3") }));
        _repository
            .Setup(r => r.ExistsActiveScopeComboAsync(
                "FACTORY_FAMILY", 2, 9, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var input = new DomainDefinition
        {
            DomainKey = "DOM3",
            DomainName = "厂族测试域",
            ScopeType = "FACTORY_FAMILY",
            ProductFamilyId = 2,
            FactoryId = 9,
        };

        // Act
        var act = async () => await _service.CreateAsync(input, 1, "u1", CancellationToken.None);

        // Assert：组合重复 → 拒绝
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*同一工厂+产品族组合仅允许一个启用域*");
        _repository.Verify(r => r.CreateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_同组合已启用但排除自身_放行()
    {
        // Arrange：既有 Id=10（FAMILY, ProductFamilyId=1）更新，组合判重须排除自身 → 放行
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "DOM1"));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "DOM1") }));

        var input = ValidFamilyInput("DOM1");
        input.DomainName = "改名";

        // Act
        var result = await _service.UpdateAsync(10, input, 1, "u1", CancellationToken.None);

        // Assert：放行更新（仓储 ExistsActiveScopeComboAsync 默认 false，排除自身）
        result.DomainName.Should().Be("改名");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<DomainDefinition>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetActive_启用时同组合已启用_抛异常()
    {
        // Arrange：既有 Id=10 停用，启用时同 ProductFamilyId=1 已有其他启用域 → 拒绝启用
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "D1", isActive: false));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "D1") }));
        _repository
            .Setup(r => r.ExistsActiveScopeComboAsync(
                "FAMILY", 1, It.IsAny<int?>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var act = async () => await _service.SetActiveAsync(10, true, 1, "u1", CancellationToken.None);

        // Assert：拒绝启用（排除自身 10 后仍有同组合启用域）→ InvalidOperationException（400）
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repository.Verify(r => r.SetActiveAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ==================== N-03（0号位 复审 v1.1）用户态读取 Domain Scope ====================

    [Fact]
    public async Task GetByIdAsync_域越权_抛ScopeViolationException()
    {
        // Arrange：目标域 DOM1，用户仅授权 DOM2 → 403（防通过 Id 窥探他域定义）
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "DOM1"));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "DOM2") }));

        // Act
        var act = async () => await _service.GetByIdAsync(10, 1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
    }

    [Fact]
    public async Task GetByIdAsync_无域授权_抛ScopeViolationException()
    {
        // Arrange：仅 Factory 授权（无 Domain）→ fail-closed 403
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "DOM1"));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));

        // Act
        var act = async () => await _service.GetByIdAsync(10, 1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
    }

    [Fact]
    public async Task GetByIdAsync_授权同域_放行()
    {
        // Arrange：目标域 DOM1，用户授权 DOM1 → 放行
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "DOM1"));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "DOM1") }));

        // Act
        var result = await _service.GetByIdAsync(10, 1, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.DomainKey.Should().Be("DOM1");
    }

    [Fact]
    public async Task GetByIdAsync_Global放行()
    {
        // Arrange：Global 不过滤
        _repository.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(ExistingDefinition(10, "DOM1"));
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);

        // Act
        var result = await _service.GetByIdAsync(10, 1, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllAsync_非Global仅返回授权域()
    {
        // Arrange：全量含 DOM1/DOM2；授权仅 DOM1
        _repository.Setup(r => r.GetAllAsync(It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ExistingDefinition(10, "DOM1"), ExistingDefinition(20, "DOM2") });
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "DOM1") }));

        // Act
        var result = await _service.GetAllAsync(1, CancellationToken.None);

        // Assert：仅授权域返回，DOM2 被隐藏
        result.Should().ContainSingle();
        result[0].DomainKey.Should().Be("DOM1");
    }

    [Fact]
    public async Task GetAllAsync_非Global无域授权_返回空列表()
    {
        // Arrange：仅 Factory 授权（无 Domain）
        _repository.Setup(r => r.GetAllAsync(It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ExistingDefinition(10, "DOM1") });
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));

        // Act
        var result = await _service.GetAllAsync(1, CancellationToken.None);

        // Assert：fail-closed 空列表（禁止先全量返回再前端隐藏）
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_Global返回全量()
    {
        // Arrange：Global 不过滤
        _repository.Setup(r => r.GetAllAsync(It.IsAny<bool?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ExistingDefinition(10, "DOM1"), ExistingDefinition(20, "DOM2") });
        _dataScopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);

        // Act
        var result = await _service.GetAllAsync(1, CancellationToken.None);

        // Assert：DOM1 与 DOM2 均返回
        result.Should().HaveCount(2);
    }
}
