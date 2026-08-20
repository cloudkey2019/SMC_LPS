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

    /// <summary>构造 CANDIDATE 计划版本</summary>
    private static PlanVersion CandidateVersion(int id = 5, string? domainKey = "D1")
        => new()
        {
            Id = id,
            VersionCode = "V-CAND-5",
            VersionCategory = "RESCHEDULE",
            DomainKey = domainKey,
            Status = "CANDIDATE",
            CreatedAt = DateTime.UtcNow,
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
    public async Task Confirm_非CANDIDATE状态_抛异常()
    {
        // Arrange
        var version = CandidateVersion(5);
        version.Status = "BUILDING";
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
    public async Task Confirm_同域已有ACTIVE_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(CandidateVersion(5, "D1"));
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanVersion { Id = 3, DomainKey = "D1", Status = "ACTIVE" });

        // Act
        var act = async () => await _service.ConfirmCandidateAsync(5, "u1", null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Confirm_合法_写Activated并记审计()
    {
        // Arrange
        var version = CandidateVersion(5, "D1");
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanVersion?)null);

        // Act
        await _service.ConfirmCandidateAsync(5, "u1", "人工确认", CancellationToken.None);

        // Assert：确认仅写 ActivatedAt/ActivatedBy，状态保持 CANDIDATE（激活是独立步骤）
        version.ActivatedAt.Should().NotBeNull();
        version.ActivatedBy.Should().Be("u1");
        version.Status.Should().Be("CANDIDATE");
        _planVersionRepo.Verify(r => r.UpdateAsync(version, It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Activate_同域已有ACTIVE_抛异常()
    {
        // Arrange
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(CandidateVersion(5, "D1"));
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanVersion { Id = 3, DomainKey = "D1", Status = "ACTIVE" });

        // Act
        var act = async () => await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Activate_合法_状态转ACTIVE并记审计()
    {
        // Arrange
        var version = CandidateVersion(5, "D1");
        _planVersionRepo.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _planVersionRepo.Setup(r => r.GetActiveByDomainKeyAsync("D1", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanVersion?)null);

        // Act
        await _service.ActivateCandidateAsync(5, "u1", CancellationToken.None);

        // Assert：激活 = 正式采用，CANDIDATE → ACTIVE + 写 ActivatedAt/ActivatedBy
        version.Status.Should().Be("ACTIVE");
        version.ActivatedAt.Should().NotBeNull();
        version.ActivatedBy.Should().Be("u1");
        _planVersionRepo.Verify(r => r.UpdateAsync(version, It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(
            It.Is<GovernanceAuditLog>(l =>
                l.OperationType == "ActivateCandidate"
                && l.EntityType == "PlanVersion"
                && l.EntityId == 5
                && l.BeforeStatus == "CANDIDATE"
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
}
