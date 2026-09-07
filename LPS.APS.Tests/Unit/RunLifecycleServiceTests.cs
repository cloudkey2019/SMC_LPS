using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;
using GovernanceAuditLog = LPS.APS.Core.Entities.Auth.GovernanceAuditLog;
using PlanVersion = LPS.APS.Core.Entities.APS.PlanVersion;
using StrategyProfileVersion = LPS.APS.Core.Entities.APS.StrategyProfileVersion;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// P0-08 验收：ScheduleRun 运行生命周期治理（IRunLifecycleService）
/// 覆盖：ExpectedDomainKeysJson 冻结规则校验（FULL≥1 / RESCHEDULE 恰1）、Candidate 最小确认、
///       候选激活（CANDIDATE→ACTIVE + 同域唯一预检）、FAILED 恢复新建、Run 引用追溯。
/// 边界：不重写 2号位运行状态执行流转；旧 FAILED 记录绝不动（不回改 RUNNING）。
/// </summary>
public class RunLifecycleServiceTests
{
    private readonly Mock<IScheduleRunRepository> _scheduleRunRepo = new();
    private readonly Mock<IPlanVersionRepository> _planVersionRepo = new();
    private readonly Mock<IStrategyProfileVersionRepository> _strategyProfileVersionRepo = new();
    private readonly Mock<IRuleSetVersionRepository> _ruleSetRepo = new();
    private readonly Mock<IParameterSetVersionRepository> _parameterSetRepo = new();
    private readonly Mock<IGovernanceAuditLogRepository> _auditRepo = new();
    private readonly RunLifecycleService _service;

    public RunLifecycleServiceTests()
    {
        _service = new RunLifecycleService(
            _scheduleRunRepo.Object,
            _planVersionRepo.Object,
            _strategyProfileVersionRepo.Object,
            _ruleSetRepo.Object,
            _parameterSetRepo.Object,
            _auditRepo.Object);
    }

    /// <summary>构造合法 FULL_SCHEDULE 运行（两 Domain）</summary>
    private static ScheduleRunGov FullRun(int id = 1, string? expectedJson = """["D1","D2"]""")
        => new()
        {
            Id = id,
            RunType = "FULL_SCHEDULE",
            Status = "RUNNING",
            TriggeredBy = "Hangfire",
            DataCutoffTime = DateTime.UtcNow,
            StrategyProfileVersionId = 10,
            ExpectedDomainKeysJson = expectedJson,
            StartedAt = DateTime.UtcNow,
        };

    /// <summary>构造合法 RESCHEDULE（Candidate）运行（单 Domain）</summary>
    private static ScheduleRunGov CandidateRun(int id = 2, string? expectedJson = """["D1"]""")
        => new()
        {
            Id = id,
            RunType = "MANUAL_RESCHEDULE",
            Status = "RUNNING",
            TriggeredBy = "API",
            DataCutoffTime = DateTime.UtcNow,
            StrategyProfileVersionId = 11,
            ExpectedDomainKeysJson = expectedJson,
            StartedAt = DateTime.UtcNow,
        };

    /// <summary>构造 CANDIDATE 计划版本（D7：候选身份在 VersionCategory，Status 走执行词表完成态 Computed；默认关联可激活来源 Run 2）</summary>
    private static PlanVersion CandidateVersion(int id = 5, string? domainKey = "D1", int? sourceScheduleRunId = 2)
        => new()
        {
            Id = id,
            VersionCode = "V-CAND-5",
            VersionCategory = "CANDIDATE",
            DomainKey = domainKey,
            Status = "Computed",
            SourceScheduleRunId = sourceScheduleRunId,
            CreatedAt = DateTime.UtcNow,
        };

    /// <summary>构造"已完成最小人工确认"的审计记录（P0-04 激活硬前置）</summary>
    private static IReadOnlyList<GovernanceAuditLog> ConfirmedLogs(int planVersionId = 5)
        => new[]
        {
            new GovernanceAuditLog
            {
                OperationType = "ConfirmCandidate",
                EntityType = "PlanVersion",
                EntityId = planVersionId,
                OperatedBy = "u1",
                OperatedAt = DateTime.UtcNow,
            },
        };

    /// <summary>构造 INSERT_ORDER_WHATIF 来源 Run（CTP/INSERT_IMPACT_ANALYSIS，永远不得激活）</summary>
    private static ScheduleRunGov WhatIfRun(int id = 3)
        => new()
        {
            Id = id,
            RunType = "INSERT_ORDER_WHATIF",
            Status = "COMPLETED",
            TriggeredBy = "API",
            DataCutoffTime = DateTime.UtcNow,
            ExpectedDomainKeysJson = """["D1"]""",
            StartedAt = DateTime.UtcNow,
        };

    // ==================== ValidateExpectedDomainKeysAsync ====================

    [Fact]
    public async Task Validate_Run不存在_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((ScheduleRunGov?)null);

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(999, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Validate_ExpectedDomainKeysJson为空_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullRun(expectedJson: null));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Validate_ExpectedDomainKeysJson非JSON数组_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullRun(expectedJson: """{"not":"array"}"""));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Validate_FullSchedule_两Domain_通过()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullRun(expectedJson: """["D1","D2"]"""));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(1, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Validate_FullSchedule_重复DomainKey_抛异常()
    {
        // Arrange（P1-01）：FULL 预期 Domain 集合含重复 DomainKey
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullRun(expectedJson: """["D1","D1"]"""));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Validate_FullSchedule_空数组_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FullRun(expectedJson: "[]"));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Validate_Reschedule_恰一Domain_通过()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CandidateRun(expectedJson: """["D1"]"""));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(2, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Validate_Reschedule_两Domain_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CandidateRun(expectedJson: """["D1","D2"]"""));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(2, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Validate_数组含空DomainKey_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CandidateRun(expectedJson: """["D1",""]"""));

        // Act
        var act = async () => await _service.ValidateExpectedDomainKeysAsync(2, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ==================== ConfirmCandidateAsync ====================

    [Fact]
    public async Task Confirm_版本不存在_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((PlanVersion?)null);

        // Act
        var act = async () => await _service.ConfirmCandidateAsync(999, "u1", null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Confirm_非候选VersionCategory_抛异常()
    {
        // Arrange（D7：候选身份在 VersionCategory，RESCHEDULE 非候选 → 拒绝）
        var version = CandidateVersion(5);
        version.VersionCategory = "RESCHEDULE";
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var act = async () => await _service.ConfirmCandidateAsync(5, "u1", null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Confirm_DomainKey为空_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CandidateVersion(5, domainKey: null));

        // Act
        var act = async () => await _service.ConfirmCandidateAsync(5, "u1", null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Confirm_同域已有ACTIVE_仍可正常确认()
    {
        // Arrange：Base ACTIVE 存在（正常"Base ACTIVE → Candidate 比较/确认 → 新 Candidate 采用"流程）
        var version = CandidateVersion(5, "D1");
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act：确认不预检同域 ACTIVE（P0-06：确认与唯一性无关，只记录事实）
        await _service.ConfirmCandidateAsync(5, "u1", "基于 Base ACTIVE 的新 Candidate", CancellationToken.None);

        // Assert：确认成功，仅记审计，不写 Activated、不转 ACTIVE、不触碰同域 ACTIVE
        version.Status.Should().Be("Computed");
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<GovernanceAuditLog>(l =>
                l.OperationType == "ConfirmCandidate"
                && l.EntityId == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Confirm_合法_仅记审计不写Activated()
    {
        // Arrange
        var version = CandidateVersion(5, "D1");
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        await _service.ConfirmCandidateAsync(5, "u1", "人工确认", CancellationToken.None);

        // Assert（P0-05）：确认不污染 ActivatedAt/ActivatedBy，状态保持 CANDIDATE，不写库
        version.ActivatedAt.Should().BeNull();
        version.ActivatedBy.Should().BeNull();
        version.Status.Should().Be("Computed");
        _planVersionRepo.Verify(r => r.UpdateAsync(It.IsAny<PlanVersion>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<GovernanceAuditLog>(l =>
                l.OperationType == "ConfirmCandidate"
                && l.EntityType == "PlanVersion"
                && l.EntityId == 5
                && l.OperatedBy == "u1"
                && l.Remarks!.Contains("5")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ==================== ActivateCandidateAsync ====================

    [Fact]
    public async Task Activate_非CANDIDATE状态_抛异常()
    {
        // Arrange
        var version = CandidateVersion(5);
        version.Status = "ACTIVE";
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var act = async () => await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Activate_DomainKey为空_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CandidateVersion(5, domainKey: null));

        // Act
        var act = async () => await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Activate_未确认_抛异常()
    {
        // Arrange（P0-04）：无 ConfirmCandidate 审计
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(CandidateVersion(5, "D1"));
        _auditRepo.Setup(r => r.GetByEntityAsync("PlanVersion", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<GovernanceAuditLog>());

        // Act
        var act = async () => await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert：激活被拒，未触碰来源 Run / 仓储写
        await act.Should().ThrowAsync<InvalidOperationException>();
        _planVersionRepo.Verify(r => r.ReplaceActiveAsync(
            It.IsAny<PlanVersion>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_来源Run为INSERT_ORDER_WHATIF_抛异常()
    {
        // Arrange（P0-03）：已确认，但来源 Run 为 INSERT_ORDER_WHATIF（CTP/INSERT_IMPACT_ANALYSIS，永远不得激活）
        var version = CandidateVersion(5, "D1", sourceScheduleRunId: 3);
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _auditRepo.Setup(r => r.GetByEntityAsync("PlanVersion", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfirmedLogs());
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(WhatIfRun(3));

        // Act
        var act = async () => await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert：无条件拒绝激活，未触发原子替换
        await act.Should().ThrowAsync<InvalidOperationException>();
        _planVersionRepo.Verify(r => r.ReplaceActiveAsync(
            It.IsAny<PlanVersion>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_SourceScheduleRunId为空_抛异常()
    {
        // Arrange（P0-03 防绕过）：无法证明来源 Run 可激活 → 拒绝
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CandidateVersion(5, "D1", sourceScheduleRunId: null));
        _auditRepo.Setup(r => r.GetByEntityAsync("PlanVersion", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfirmedLogs());

        // Act
        var act = async () => await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _planVersionRepo.Verify(r => r.ReplaceActiveAsync(
            It.IsAny<PlanVersion>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_同域已有ACTIVE_原子替换()
    {
        // Arrange（P0-06）：Base ACTIVE 存在不再是失败，激活走原子替换（归档旧 ACTIVE + 新版本置 ACTIVE）
        var version = CandidateVersion(5, "D1");
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _auditRepo.Setup(r => r.GetByEntityAsync("PlanVersion", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfirmedLogs());
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(CandidateRun(2));

        // Act
        await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert：CANDIDATE → ACTIVE + 走原子替换（非单版本 UpdateAsync），不因同域 ACTIVE 拒绝
        version.Status.Should().Be("ACTIVE");
        version.ActivatedAt.Should().NotBeNull();
        version.ActivatedBy.Should().Be("u1");
        _planVersionRepo.Verify(r => r.ReplaceActiveAsync(version, "u1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _planVersionRepo.Verify(r => r.UpdateAsync(It.IsAny<PlanVersion>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<GovernanceAuditLog>(l =>
                l.OperationType == "ActivateCandidate"
                && l.EntityType == "PlanVersion"
                && l.EntityId == 5
                && l.BeforeStatus == "Computed"
                && l.AfterStatus == "ACTIVE"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activate_合法_已确认且来源可激活_状态转ACTIVE()
    {
        // Arrange：已完成最小确认 + 来源 Run 为可激活的 MANUAL_RESCHEDULE
        var version = CandidateVersion(5, "D1");
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _auditRepo.Setup(r => r.GetByEntityAsync("PlanVersion", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConfirmedLogs());
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(CandidateRun(2));

        // Act
        await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert：激活 = 正式采用，CANDIDATE → ACTIVE + 写 ActivatedAt/ActivatedBy + 原子替换
        version.Status.Should().Be("ACTIVE");
        version.ActivatedAt.Should().NotBeNull();
        version.ActivatedBy.Should().Be("u1");
        _planVersionRepo.Verify(r => r.ReplaceActiveAsync(version, "u1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<GovernanceAuditLog>(l =>
                l.OperationType == "ActivateCandidate"
                && l.EntityType == "PlanVersion"
                && l.EntityId == 5
                && l.BeforeStatus == "Computed"
                && l.AfterStatus == "ACTIVE"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ==================== RecoverFailedRunAsync ====================

    [Fact]
    public async Task Recover_旧Run不存在_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((ScheduleRunGov?)null);

        // Act
        var act = async () => await _service.RecoverFailedRunAsync(999, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Recover_旧Run非FAILED_抛异常()
    {
        // Arrange
        var run = FullRun(1);
        run.Status = "COMPLETED";
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        // Act
        var act = async () => await _service.RecoverFailedRunAsync(1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Recover_合法_新建RUNNING继承基线_旧记录不动()
    {
        // Arrange
        var failed = FullRun(1, expectedJson: """["D1","D2"]""");
        failed.Status = "FAILED";
        failed.CompletedAt = DateTime.UtcNow;
        failed.ErrorMessage = "致命错误";
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(failed);
        _scheduleRunRepo.Setup(r => r.InsertForRecoveryAsync(failed, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);

        // Act
        var newId = await _service.RecoverFailedRunAsync(1, CancellationToken.None);

        // Assert：返回新 Id、继承 StrategyProfileVersionId + ExpectedDomainKeysJson 基线、旧 FAILED 记录不动
        newId.Should().Be(100);
        _scheduleRunRepo.Verify(r => r.InsertForRecoveryAsync(
            It.Is<ScheduleRunGov>(s =>
                s.StrategyProfileVersionId == 10
                && s.ExpectedDomainKeysJson == """["D1","D2"]"""
                && s.RunType == "FULL_SCHEDULE"),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        failed.Status.Should().Be("FAILED");   // 旧记录不回改 RUNNING
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<GovernanceAuditLog>(l =>
                l.OperationType == "RecoverFailedRun"
                && l.EntityType == "ScheduleRun"
                && l.EntityId == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recover_继承基线不合法_抛异常且不新建()
    {
        // Arrange：FAILED 的 FULL_SCHEDULE 但基线为空数组（不满足 ≥1）
        var failed = FullRun(1, expectedJson: "[]");
        failed.Status = "FAILED";
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(failed);

        // Act
        var act = async () => await _service.RecoverFailedRunAsync(1, CancellationToken.None);

        // Assert：不新建、旧记录不动
        await act.Should().ThrowAsync<InvalidOperationException>();
        _scheduleRunRepo.Verify(r => r.InsertForRecoveryAsync(
            It.IsAny<ScheduleRunGov>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        failed.Status.Should().Be("FAILED");
    }

    // ==================== GetRunReferenceTraceAsync ====================

    [Fact]
    public async Task Trace_Run不存在_抛异常()
    {
        // Arrange
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((ScheduleRunGov?)null);

        // Act
        var act = async () => await _service.GetRunReferenceTraceAsync(999, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Trace_合法_完整追溯链()
    {
        // Arrange
        var run = FullRun(1, expectedJson: """["D1"]""");
        run.Status = "COMPLETED";
        run.CompletedAt = DateTime.UtcNow;
        _scheduleRunRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrategyProfileVersion
            {
                Id = 10,
                VersionCode = "SPV-10",
                RuleSetVersionId = 20,
                ParameterSetVersionId = 30,
            });
        _ruleSetRepo.Setup(r => r.GetByIdAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuleSetVersion { Id = 20, VersionCode = "RS-20" });
        _parameterSetRepo.Setup(r => r.GetByIdAsync(30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParameterSetVersion { Id = 30, VersionCode = "PS-30" });
        _planVersionRepo.Setup(r => r.GetLatestByScheduleRunIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanVersion { Id = 7, Status = "ACTIVE", DomainKey = "D1" });

        // Act
        var trace = await _service.GetRunReferenceTraceAsync(1, CancellationToken.None);

        // Assert
        trace.ScheduleRunId.Should().Be(1);
        trace.RunType.Should().Be("FULL_SCHEDULE");
        trace.Status.Should().Be("COMPLETED");
        trace.StrategyProfileVersionId.Should().Be(10);
        trace.StrategyProfileVersionCode.Should().Be("SPV-10");
        trace.RuleSetVersionCode.Should().Be("RS-20");
        trace.ParameterSetVersionCode.Should().Be("PS-30");
        trace.ExpectedDomainKeysJson.Should().Be("""["D1"]""");
        trace.PlanVersionId.Should().Be(7);
        trace.PlanVersionStatus.Should().Be("ACTIVE");
    }

    // ==================== CreateCandidateRunAsync（B-1 白天候选运行创建） ====================

    /// <summary>构造白天候选运行创建入参（默认 MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT + D1）</summary>
    private static CandidateRunCreateSpec DaytimeSpec(
        string runType = "MANUAL_RESCHEDULE",
        string purpose = "MANUAL_ADJUSTMENT",
        string domainKey = "D1",
        int? basePlanVersionId = null)
        => new()
        {
            RunType = runType,
            Purpose = purpose,
            DomainKey = domainKey,
            BasePlanVersionId = basePlanVersionId,
            Actor = "u1",
        };

    /// <summary>构造 Domain=D1 的 Base ACTIVE 计划版本（白天 Candidate 锚定）</summary>
    private static PlanVersion ActiveBase(int id = 50, string? domainKey = "D1")
        => new()
        {
            Id = id,
            VersionCode = "V-BASE-50",
            VersionCategory = "DAILY_BASELINE",
            DomainKey = domainKey,
            Status = "ACTIVE",
            PlanHorizonStart = DateTime.Today,
            PlanHorizonEnd = DateTime.Today.AddDays(90),
            CreatedAt = DateTime.UtcNow,
        };

    /// <summary>构造默认 PUBLISHED 策略包版本列表（默认 1 条，无生效窗口限制）</summary>
    private static IReadOnlyList<StrategyProfileVersion> DefaultStrategyVersions(int count = 1)
    {
        var list = new List<StrategyProfileVersion>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new StrategyProfileVersion
            {
                Id = 11 + i,
                Status = "PUBLISHED",
                IsDefault = true,
            });
        }
        return list;
    }

    /// <summary>给每个用例铺默认策略版本 mock（合法窗口）</summary>
    private void SetupDefaultStrategyVersions(int count = 1)
        => _strategyProfileVersionRepo
            .Setup(r => r.GetDefaultByRunTypeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DefaultStrategyVersions(count));

    [Fact]
    public async Task Create_Actor为空_抛异常()
    {
        // Arrange
        var spec = DaytimeSpec();
        spec.Actor = string.Empty;

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(spec, CancellationToken.None);

        // Assert：不触发任何仓储写
        await act.Should().ThrowAsync<InvalidOperationException>();
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_RunType非白天候选类_抛异常()
    {
        // Arrange（FULL_SCHEDULE / SIMULATION 拒绝创建）
        var spec = DaytimeSpec(runType: "FULL_SCHEDULE");

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(spec, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Purpose不在RunType合法组合_抛异常()
    {
        // Arrange（实施包 §十九：MANUAL_RESCHEDULE 仅 MANUAL_ADJUSTMENT；CTP 仅配 INSERT_ORDER_WHATIF）
        var spec = DaytimeSpec(runType: "MANUAL_RESCHEDULE", purpose: "CTP");

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(spec, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_DomainKey为空_抛异常()
    {
        // Arrange
        var spec = DaytimeSpec(domainKey: string.Empty);

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(spec, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_Base版本不存在_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((PlanVersion?)null);

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(DaytimeSpec(basePlanVersionId: 999), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_Base非ACTIVE_抛异常()
    {
        // Arrange
        var baseVersion = ActiveBase();
        baseVersion.Status = "Created";
        _planVersionRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(baseVersion);

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(DaytimeSpec(basePlanVersionId: 50), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_BaseDomainKey与目标不一致_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(ActiveBase(domainKey: "D2"));

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(DaytimeSpec(basePlanVersionId: 50), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_未传Base且无当前ACTIVE_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync((PlanVersion?)null);

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(DaytimeSpec(), CancellationToken.None);

        // Assert：白天候选必须基于 Base ACTIVE
        await act.Should().ThrowAsync<InvalidOperationException>();
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_无默认策略版本_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync(ActiveBase());
        SetupDefaultStrategyVersions(0);

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(DaytimeSpec(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_多默认策略版本歧义_抛异常()
    {
        // Arrange（红线 #4：歧义拒绝，不盲目取第一个）
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync(ActiveBase());
        SetupDefaultStrategyVersions(2);

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(DaytimeSpec(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_合法_MANUAL_RESCHEDULE_冻结基线并审计()
    {
        // Arrange：Base ACTIVE + 唯一默认策略版本 + 原子写返回新 Run/壳
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync(ActiveBase());
        SetupDefaultStrategyVersions();
        _scheduleRunRepo.Setup(r => r.CreateCandidateRunAsync(
                It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CandidateRunCreatedResult { NewScheduleRunId = 200, NewPlanVersionId = 300 });

        // Act
        var result = await _service.CreateCandidateRunAsync(DaytimeSpec(), CancellationToken.None);

        // Assert：返回新 Run/壳 Id；冻结 RunType/Domain/Base/策略版本；写 CreateCandidateRun 审计
        result.NewScheduleRunId.Should().Be(200);
        result.NewPlanVersionId.Should().Be(300);
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.Is<CandidateRunCreateSpec>(s =>
                s.RunType == "MANUAL_RESCHEDULE"
                && s.DomainKey == "D1"
                && s.BasePlanVersionId == 50),
            It.IsAny<long>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<GovernanceAuditLog>(l =>
                l.OperationType == "CreateCandidateRun"
                && l.EntityType == "ScheduleRun"
                && l.EntityId == 200
                && l.AfterStatus == "RUNNING"
                && l.OperatedBy == "u1"
                && l.Remarks!.Contains("Purpose=MANUAL_ADJUSTMENT")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_合法_INSERT_ORDER_WHATIF_CTP_创建允许()
    {
        // Arrange（实施包 §十九：INSERT_ORDER_WHATIF+CTP 允许创建；激活由 ActivateCandidateAsync 拦截，已有测试覆盖）
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync(ActiveBase());
        SetupDefaultStrategyVersions();
        _scheduleRunRepo.Setup(r => r.CreateCandidateRunAsync(
                It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CandidateRunCreatedResult { NewScheduleRunId = 210, NewPlanVersionId = 310 });

        // Act
        var result = await _service.CreateCandidateRunAsync(
            DaytimeSpec(runType: "INSERT_ORDER_WHATIF", purpose: "CTP"), CancellationToken.None);

        // Assert
        result.NewScheduleRunId.Should().Be(210);
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.Is<CandidateRunCreateSpec>(s => s.RunType == "INSERT_ORDER_WHATIF" && s.Purpose == "CTP"),
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_合法_LOCAL_RESCHEDULE_INSERT_RESCHEDULE_成功()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync(ActiveBase());
        SetupDefaultStrategyVersions();
        _scheduleRunRepo.Setup(r => r.CreateCandidateRunAsync(
                It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CandidateRunCreatedResult { NewScheduleRunId = 220, NewPlanVersionId = 320 });

        // Act
        var result = await _service.CreateCandidateRunAsync(
            DaytimeSpec(runType: "LOCAL_RESCHEDULE", purpose: "INSERT_RESCHEDULE"), CancellationToken.None);

        // Assert
        result.NewScheduleRunId.Should().Be(220);
        _scheduleRunRepo.Verify(r => r.CreateCandidateRunAsync(
            It.Is<CandidateRunCreateSpec>(s => s.RunType == "LOCAL_RESCHEDULE" && s.Purpose == "INSERT_RESCHEDULE"),
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_仓储原子写失败_异常向上传播且不写审计()
    {
        // Arrange：Run 或壳任一失败（mock 抛）→ 服务不吞异常、不写审计
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", It.IsAny<CancellationToken>())).ReturnsAsync(ActiveBase());
        SetupDefaultStrategyVersions();
        _scheduleRunRepo.Setup(r => r.CreateCandidateRunAsync(
                It.IsAny<CandidateRunCreateSpec>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("模拟原子写失败"));

        // Act
        var act = async () => await _service.CreateCandidateRunAsync(DaytimeSpec(), CancellationToken.None);

        // Assert：异常向上传播；不产生审计（无孤立写入）
        await act.Should().ThrowAsync<InvalidOperationException>();
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<GovernanceAuditLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
