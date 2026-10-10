using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LPS.APS.Application.Models;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Setup;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Exceptions;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;
// 消歧：APS 命名空间存在实体 Task，别名定向 System.Threading.Tasks.Task（using 别名优先级高于命名空间成员）
using Task = System.Threading.Tasks.Task;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// Setup 换型规则治理 P1-04 Department Scope 反证（3号位）。
/// 覆盖：CreateExact/CreateDefault 部门越权 403 + 部门不存在 404、UpdateExact 老归属/新归属分别校验（防跨部门迁移 IDOR）、
/// Delete 部门越权 403、Create/Update 审计前置预检失败 → 业务零落库（P1-03 同模式）。
/// 说明：SetupRuleService 全接口依赖（IRuleSetVersionRepository/IMasterDataLookupRepository/IDataScopeService 均可 mock），
/// Scope 校验先于版本查询/写库 → 单测可直达 403/404 分支（不似 StageLeadTimeParam 受非 virtual 直连限制）。
/// </summary>
public class SetupRuleServiceScopeTests
{
    private const long VersionId = 2;
    private const int DeptInScope = 10;      // "DEPT-A"（授权内）
    private const int DeptOldOutOfScope = 20; // "DEPT-B"（老归属，越权）
    private const int DeptNewOutOfScope = 30; // "DEPT-C"（新归属，越权）
    private const int ActorUserId = 7;

    private readonly Mock<IRuleSetVersionRepository> _versionRepo = new();
    private readonly Mock<IMasterDataLookupRepository> _masterData = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<IGovernanceVersionService> _governance = new();
    private readonly Mock<ISetupUncoveredStatRepository> _uncovered = new();
    private readonly Mock<IDataScopeService> _dataScope = new();

    private SetupRuleService CreateService() => new(
        _versionRepo.Object,
        _masterData.Object,
        _auditRepo.Object,
        _governance.Object,
        _uncovered.Object,
        _dataScope.Object);

    /// <summary>SetupDeptCode 回带：DeptInScope → DEPT-A；DeptOldOutOfScope → DEPT-B；DeptNewOutOfScope → DEPT-C；其余 → null（不存在）。</summary>
    private void SetupDepartmentCodeLookup()
    {
        _masterData.Setup(m => m.GetDepartmentCodeAsync(DeptInScope, It.IsAny<CancellationToken>())).ReturnsAsync("DEPT-A");
        _masterData.Setup(m => m.GetDepartmentCodeAsync(DeptOldOutOfScope, It.IsAny<CancellationToken>())).ReturnsAsync("DEPT-B");
        _masterData.Setup(m => m.GetDepartmentCodeAsync(DeptNewOutOfScope, It.IsAny<CancellationToken>())).ReturnsAsync("DEPT-C");
    }

    /// <summary>Scope 判定：DEPT-A 放行；DEPT-B/DEPT-C 越权（ScopeViolationException → Controller 映射 403）。</summary>
    private void SetupScopePolicy()
    {
        _dataScope.Setup(d => d.EnsureInScopeAsync(ActorUserId, DataScopeTypes.Department, "DEPT-B", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ScopeViolationException(DataScopeTypes.Department, "DEPT-B"));
        _dataScope.Setup(d => d.EnsureInScopeAsync(ActorUserId, DataScopeTypes.Department, "DEPT-C", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ScopeViolationException(DataScopeTypes.Department, "DEPT-C"));
    }

    /// <summary>构建 DRAFT 版本（快照承载指定规则；ContentSnapshotJson 经 SetRules 生成合法子块）。</summary>
    private static RuleSetVersion BuildDraftVersion(params SetupTransitionRule[] rules) => new()
    {
        Id = VersionId,
        RuleSetId = 1,
        VersionCode = "V1",
        Status = GovernanceVersionStatus.Draft,
        ContentSnapshotJson = SetupTransitionRuleProjector.SetRules("{}", rules),
    };

    /// <summary>EXACT 规则（Id = BuildRuleId(VersionId, seq)，部门可指定）。</summary>
    private static SetupTransitionRule BuildExactRule(int seq, int departmentId) => new()
    {
        Id = SetupTransitionRuleProjector.BuildRuleId(VersionId, seq),
        RuleSetVersionId = VersionId,
        ProductionDepartmentId = departmentId,
        StageCode = "S1",
        OperationCode = "OP10",
        ResourceId = 1,
        FromMaterialId = 100,
        ToMaterialId = 200,
        RuleType = SetupTransitionRuleType.Exact,
        SetupMinutes = 30,
        IsActive = true,
        CreatedBy = "u",
        CreatedAt = DateTime.Now,
    };

    private static SetupRuleExactInput BuildExactInput(int departmentId) => new()
    {
        RuleSetVersionId = VersionId,
        ProductionDepartmentId = departmentId,
        StageCode = "S1",
        OperationCode = "OP10",
        ResourceId = 1,
        FromMaterialId = 100,
        ToMaterialId = 200,
        SetupMinutes = 30,
    };

    private static SetupRuleDefaultInput BuildDefaultInput(int departmentId) => new()
    {
        RuleSetVersionId = VersionId,
        ProductionDepartmentId = departmentId,
        StageCode = "S1",
        OperationCode = "OP10",
        ResourceId = 1,
        SetupMinutes = 30,
    };

    // ---------- Create：部门 Scope 校验（校验先于版本查询 → 无需 mock 版本仓储） ----------

    [Fact]
    public async Task CreateExactAsync_部门越权_抛ScopeViolationException_且未触达版本仓储()
    {
        // Arrange
        SetupDepartmentCodeLookup();
        SetupScopePolicy();
        var svc = CreateService();

        // Act
        var act = () => svc.CreateExactAsync(BuildExactInput(DeptOldOutOfScope), ActorUserId, "u", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
        _versionRepo.Verify(r => r.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateExactAsync_部门不存在_抛ResourceNotFoundException_404()
    {
        // Arrange：Dept 999 无 Code 回带 → 404
        _masterData.Setup(m => m.GetDepartmentCodeAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var svc = CreateService();

        // Act
        var act = () => svc.CreateExactAsync(BuildExactInput(999), ActorUserId, "u", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ResourceNotFoundException>();
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateDefaultAsync_部门越权_抛ScopeViolationException()
    {
        // Arrange
        SetupDepartmentCodeLookup();
        SetupScopePolicy();
        var svc = CreateService();

        // Act
        var act = () => svc.CreateDefaultAsync(BuildDefaultInput(DeptOldOutOfScope), ActorUserId, "u", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- UpdateExact：老归属 + 新归属分别校验（防跨部门迁移 IDOR） ----------

    [Fact]
    public async Task UpdateExactAsync_老记录部门越权_抛ScopeViolationException_新部门未触达()
    {
        // Arrange：老记录 DEPT-B（越权），input 新归属 DEPT-A
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptOldOutOfScope)));
        SetupDepartmentCodeLookup();
        SetupScopePolicy();
        var svc = CreateService();
        var ruleId = SetupTransitionRuleProjector.BuildRuleId(VersionId, 1);

        // Act
        var act = () => svc.UpdateExactAsync(ruleId, BuildExactInput(DeptInScope), ActorUserId, "u", CancellationToken.None);

        // Assert：老归属 DEPT-B 首轮即拒，新归属 DEPT-A 未被查询/判定
        await act.Should().ThrowAsync<ScopeViolationException>();
        _dataScope.Verify(d => d.EnsureInScopeAsync(ActorUserId, DataScopeTypes.Department, "DEPT-B", It.IsAny<CancellationToken>()), Times.Once);
        _masterData.Verify(m => m.GetDepartmentCodeAsync(DeptInScope, It.IsAny<CancellationToken>()), Times.Never);
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateExactAsync_新部门越权_抛ScopeViolationException_老归属已通过()
    {
        // Arrange：老记录 DEPT-A（授权内），input 新归属 DEPT-C（越权）
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptInScope)));
        SetupDepartmentCodeLookup();
        SetupScopePolicy();
        var svc = CreateService();
        var ruleId = SetupTransitionRuleProjector.BuildRuleId(VersionId, 1);

        // Act
        var act = () => svc.UpdateExactAsync(ruleId, BuildExactInput(DeptNewOutOfScope), ActorUserId, "u", CancellationToken.None);

        // Assert：老归属 DEPT-A 通过后，新归属 DEPT-C 越权拦截
        await act.Should().ThrowAsync<ScopeViolationException>();
        _dataScope.Verify(d => d.EnsureInScopeAsync(ActorUserId, DataScopeTypes.Department, "DEPT-A", It.IsAny<CancellationToken>()), Times.Once);
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- DeleteAsync：目标部门 Scope ----------

    [Fact]
    public async Task DeleteAsync_部门越权_抛ScopeViolationException_且未写库()
    {
        // Arrange：老记录 DEPT-B（越权）
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptOldOutOfScope)));
        SetupDepartmentCodeLookup();
        SetupScopePolicy();
        var svc = CreateService();
        var ruleId = SetupTransitionRuleProjector.BuildRuleId(VersionId, 1);

        // Act
        var act = () => svc.DeleteAsync(ruleId, ActorUserId, "u", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ScopeViolationException>();
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- 审计前置预检（P1-03 同模式）：预检失败 → 业务零落库 ----------

    [Fact]
    public async Task CreateExactAsync_审计预检失败_抛异常_业务零落库()
    {
        // Arrange：Scope 通过 + DRAFT 版本；EnsureWritableAsync 注入失败
        SetupDepartmentCodeLookup();
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion());
        _auditRepo.Setup(a => a.EnsureWritableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("APS_Auth 审计库不可写（测试注入）"));
        var svc = CreateService();

        // Act
        var act = () => svc.CreateExactAsync(BuildExactInput(DeptInScope), ActorUserId, "u", CancellationToken.None);

        // Assert：预检失败 → 版本子块零写入（fail-before-write）
        await act.Should().ThrowAsync<InvalidOperationException>();
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateExactAsync_审计预检失败_抛异常_业务零落库()
    {
        // Arrange：老归属在 Scope 内；EnsureWritableAsync 注入失败
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptInScope)));
        SetupDepartmentCodeLookup();
        _auditRepo.Setup(a => a.EnsureWritableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("APS_Auth 审计库不可写（测试注入）"));
        var svc = CreateService();
        var ruleId = SetupTransitionRuleProjector.BuildRuleId(VersionId, 1);

        // Act
        var act = () => svc.UpdateExactAsync(ruleId, BuildExactInput(DeptInScope), ActorUserId, "u", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _versionRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
