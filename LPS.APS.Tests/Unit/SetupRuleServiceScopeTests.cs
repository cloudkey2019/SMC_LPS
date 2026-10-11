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

    /// <summary>N-01 版本构建（可指定版本 Id，用于双版本 Diff 对比）。</summary>
    private static RuleSetVersion BuildVersion(long id, params SetupTransitionRule[] rules) => new()
    {
        Id = id,
        RuleSetId = 1,
        VersionCode = id == VersionId ? "V1" : "V2",
        Status = GovernanceVersionStatus.Draft,
        ContentSnapshotJson = SetupTransitionRuleProjector.SetRules("{}", rules),
    };

    /// <summary>EXACT 规则（Id = BuildRuleId(VersionId, seq)，部门可指定；OperationCode/SetupMinutes 可覆盖用于 Diff 差异构造）。</summary>
    private static SetupTransitionRule BuildExactRule(int seq, int departmentId, string operationCode = "OP10", decimal setupMinutes = 30) => new()
    {
        Id = SetupTransitionRuleProjector.BuildRuleId(VersionId, seq),
        RuleSetVersionId = VersionId,
        ProductionDepartmentId = departmentId,
        StageCode = "S1",
        OperationCode = operationCode,
        ResourceId = 1,
        FromMaterialId = 100,
        ToMaterialId = 200,
        RuleType = SetupTransitionRuleType.Exact,
        SetupMinutes = setupMinutes,
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

    // ---------- 3-NEW-P0-01（0号位 再审核 2026-10-10）列表/详情 Department Scope ----------

    [Fact]
    public async Task ListAsync_非Global仅返回授权部门规则()
    {
        // Arrange：版本含 DEPT-A(10) 与 DEPT-B(20) 两条 EXACT 规则；授权仅 DEPT-A
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptInScope), BuildExactRule(2, DeptOldOutOfScope)));
        SetupDepartmentCodeLookup();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Department, "DEPT-A") }));
        var svc = CreateService();

        // Act
        var result = await svc.ListAsync(VersionId, SetupTransitionRuleType.Exact, ActorUserId, CancellationToken.None);

        // Assert：仅 DEPT-A 规则返回，DEPT-B 规则被隐藏（防通过版本 Id 窥探）
        result.Should().ContainSingle();
        result[0].ProductionDepartmentId.Should().Be(DeptInScope);
    }

    [Fact]
    public async Task ListAsync_非Global无部门授权_返回空列表()
    {
        // Arrange：仅 Factory 授权（无 Department）
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptInScope)));
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));
        var svc = CreateService();

        // Act
        var result = await svc.ListAsync(VersionId, SetupTransitionRuleType.Exact, ActorUserId, CancellationToken.None);

        // Assert：fail-closed 空列表（禁止先全量返回再前端隐藏）
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_Global返回全量()
    {
        // Arrange：Global 用户不过滤
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptInScope), BuildExactRule(2, DeptOldOutOfScope)));
        SetupDepartmentCodeLookup();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        var svc = CreateService();

        // Act
        var result = await svc.ListAsync(VersionId, SetupTransitionRuleType.Exact, ActorUserId, CancellationToken.None);

        // Assert：DEPT-A 与 DEPT-B 规则均返回
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_目标规则部门越权_抛ScopeViolationException()
    {
        // Arrange：目标规则属 DEPT-B（越权）；授权仅 DEPT-A
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptOldOutOfScope)));
        SetupDepartmentCodeLookup();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Department, "DEPT-A") }));
        var svc = CreateService();
        var ruleId = SetupTransitionRuleProjector.BuildRuleId(VersionId, 1);

        // Act
        var act = () => svc.GetByIdAsync(ruleId, ActorUserId, CancellationToken.None);

        // Assert：防通过规则 Id 窥探他部门规则 → 403
        await act.Should().ThrowAsync<ScopeViolationException>();
    }

    [Fact]
    public async Task GetByIdAsync_Global放行()
    {
        // Arrange：Global 用户不过滤
        _versionRepo.Setup(r => r.GetByIdAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDraftVersion(BuildExactRule(1, DeptOldOutOfScope)));
        SetupDepartmentCodeLookup();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        var svc = CreateService();
        var ruleId = SetupTransitionRuleProjector.BuildRuleId(VersionId, 1);

        // Act
        var result = await svc.GetByIdAsync(ruleId, ActorUserId, CancellationToken.None);

        // Assert：放行返回 DTO
        result.Should().NotBeNull();
        result!.Id.Should().Be(ruleId);
    }

    // ---------- N-01/N-02（0号位 复审 v1.1）GetDiff / GetEligibility Department Scope ----------

    /// <summary>N-02 资源资格主数据：RES-1 属 DEPT-A(10)、RES-2 属 DEPT-B(20)；Operation=OP10 物料 100 合法资源 {1,2}。</summary>
    private void SetupEligibilityData()
    {
        _masterData.Setup(m => m.GetEligibleResourceIdsAsync("OP10", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1, 2 });
        _masterData.Setup(m => m.GetResourceInfosAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ResourceLookupInfo { Id = 1, ResourceCode = "RES-1", ResourceName = "设备一", ProductionDepartmentId = DeptInScope },
                new ResourceLookupInfo { Id = 2, ResourceCode = "RES-2", ResourceName = "设备二", ProductionDepartmentId = DeptOldOutOfScope },
            });
        _masterData.Setup(m => m.GetDepartmentInfosAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DepartmentLookupInfo { Id = DeptInScope, DeptCode = "DEPT-A", StageCode = "S1" },
                new DepartmentLookupInfo { Id = DeptOldOutOfScope, DeptCode = "DEPT-B", StageCode = "S1" },
            });
    }

    [Fact]
    public async Task GetEligibilityAsync_非Global仅返回授权部门资源()
    {
        // Arrange：RES-1 属 DEPT-A（授权）、RES-2 属 DEPT-B（越权）；授权仅 DEPT-A
        SetupDepartmentCodeLookup();
        SetupEligibilityData();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Department, "DEPT-A") }));
        var svc = CreateService();

        // Act
        var result = await svc.GetEligibilityAsync("OP10", 100, null, ActorUserId, CancellationToken.None);

        // Assert：仅授权部门资源回显，RES-2 被剔除（ResourceCodes/Resources 口径一致）
        result.ResourceCodes.Should().BeEquivalentTo(new[] { "RES-1" });
        result.Resources.Should().ContainSingle();
        result.Resources[0].ResourceCode.Should().Be("RES-1");
        result.Resources[0].DepartmentCode.Should().Be("DEPT-A");
    }

    [Fact]
    public async Task GetEligibilityAsync_非Global无部门授权_返回空结果()
    {
        // Arrange：仅 Factory 授权（无 Department）
        SetupDepartmentCodeLookup();
        SetupEligibilityData();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));
        var svc = CreateService();

        // Act
        var result = await svc.GetEligibilityAsync("OP10", 100, null, ActorUserId, CancellationToken.None);

        // Assert：fail-closed 空结果（禁止先全量再前端隐藏）
        result.ResourceCodes.Should().BeEmpty();
        result.Resources.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEligibilityAsync_Global返回全量()
    {
        // Arrange：Global 不过滤
        SetupDepartmentCodeLookup();
        SetupEligibilityData();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        var svc = CreateService();

        // Act
        var result = await svc.GetEligibilityAsync("OP10", 100, null, ActorUserId, CancellationToken.None);

        // Assert：RES-1 与 RES-2 均返回
        result.ResourceCodes.Should().HaveCount(2);
        result.Resources.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetDiffAsync_非Global仅统计授权部门变更()
    {
        // Arrange：源/目标版本差异含 DEPT-A（rule1 不变 + rule3 新增）与 DEPT-B（rule2 修改）；授权仅 DEPT-A
        _versionRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVersion(2,
                BuildExactRule(1, DeptInScope, "OP10", 30),
                BuildExactRule(2, DeptOldOutOfScope, "OP10", 30)));
        _versionRepo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVersion(3,
                BuildExactRule(1, DeptInScope, "OP10", 30),
                BuildExactRule(2, DeptOldOutOfScope, "OP10", 45),
                BuildExactRule(3, DeptInScope, "OP20", 40)));
        SetupDepartmentCodeLookup();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Department, "DEPT-A") }));
        var svc = CreateService();

        // Act
        var result = await svc.GetDiffAsync(2, 3, ActorUserId, CancellationToken.None);

        // Assert：仅授权部门变更入 Diff——新增 OP20（DEPT-A）、DEPT-B 的修改不计数（Add/Mod/Remove 口径一致）
        result.AddedCount.Should().Be(1);
        result.ModifiedCount.Should().Be(0);
        result.RemovedCount.Should().Be(0);
        result.SetupRuleChanges.Added.Should().ContainSingle(a => a.OperationCode == "OP20");
        result.SetupRuleChanges.Modified.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiffAsync_非Global无部门授权_返回空Diff()
    {
        // Arrange：仅 Factory 授权（无 Department）
        _versionRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVersion(2, BuildExactRule(1, DeptInScope)));
        _versionRepo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVersion(3, BuildExactRule(1, DeptInScope), BuildExactRule(2, DeptOldOutOfScope)));
        SetupDepartmentCodeLookup();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));
        var svc = CreateService();

        // Act
        var result = await svc.GetDiffAsync(2, 3, ActorUserId, CancellationToken.None);

        // Assert：fail-closed 空 Diff（三计数 0，禁止先全量再前端隐藏）
        result.AddedCount.Should().Be(0);
        result.ModifiedCount.Should().Be(0);
        result.RemovedCount.Should().Be(0);
        result.SetupRuleChanges.Added.Should().BeEmpty();
        result.SetupRuleChanges.Modified.Should().BeEmpty();
        result.SetupRuleChanges.Removed.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiffAsync_Global返回全量差异()
    {
        // Arrange：Global 不过滤——DEPT-A 新增 + DEPT-B 修改均入 Diff
        _versionRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVersion(2,
                BuildExactRule(1, DeptInScope, "OP10", 30),
                BuildExactRule(2, DeptOldOutOfScope, "OP10", 30)));
        _versionRepo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildVersion(3,
                BuildExactRule(1, DeptInScope, "OP10", 30),
                BuildExactRule(2, DeptOldOutOfScope, "OP10", 45),
                BuildExactRule(3, DeptInScope, "OP20", 40)));
        SetupDepartmentCodeLookup();
        _dataScope.Setup(d => d.ResolveScopeAsync(ActorUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        var svc = CreateService();

        // Act
        var result = await svc.GetDiffAsync(2, 3, ActorUserId, CancellationToken.None);

        // Assert：全量差异——新增 OP20（DEPT-A）+ 修改 OP10（DEPT-B）
        result.AddedCount.Should().Be(1);
        result.ModifiedCount.Should().Be(1);
        result.RemovedCount.Should().Be(0);
        result.SetupRuleChanges.Added.Should().ContainSingle(a => a.OperationCode == "OP20");
        result.SetupRuleChanges.Modified.Should().ContainSingle(m => m.OperationCode == "OP10");
    }
}
