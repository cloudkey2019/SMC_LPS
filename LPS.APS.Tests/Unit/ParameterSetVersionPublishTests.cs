using FluentAssertions;
using LPS.APS.Application.Services;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// R02 验收：发布 ParameterSetVersion 新 Run 可引用、旧 Run 不变
/// 场景：发布新版本 V2 后，V2 为新 PUBLISHED 记录；旧版本 V1 记录（Id/内容）保持不变——
/// 旧 Run 引用 V1 不因新版本发布而漂移，新 Run 可引用 V2。
/// </summary>
public class ParameterSetVersionPublishTests
{
    private readonly Mock<IParameterSetVersionRepository> _repo = new();
    private readonly GovernanceVersionService _service;

    public ParameterSetVersionPublishTests()
    {
        _service = new GovernanceVersionService(
            Mock.Of<IRuleSetVersionRepository>(),
            _repo.Object,
            Mock.Of<IStrategyProfileRepository>(),
            Mock.Of<IStrategyProfileVersionRepository>(),
            Mock.Of<IGovernanceAuditLogRepository>());
    }

    /// <summary>构造合法五块参数 JSON（P0-05 + P0-02b：发布前强制校验，测试数据须含 SolverStrategy/CandidateGuardrail 合法内容）</summary>
    private static (string LockJson, string SupplyJson, string ProcurementJson, string SolverStrategyJson, string CandidateGuardrailJson) CreateValidJson()
    {
        var lockJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.LockBlock
        {
            Trigger = new LPS.APS.Core.Dto.ProtectionTriggerParams
            {
                UseRemainingTimeThreshold = true,
                RemainingTimeThresholdHours = 24,
                ProtectDelayed = true
            }
        });

        var supplyJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.SupplyBlock
        {
            Inventory = new LPS.APS.Core.Dto.InventoryAvailabilityRule
            {
                IsEnabled = true,
                WarehousePriority = new List<string> { "WH01", "WH02" }
            }
        });

        var procurementJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.ProcurementBlock
        {
            PlanningYields =
            [
                new LPS.APS.Core.Dto.PlanningYieldRule
                {
                    MaterialId = "MAT001",
                    YieldPercent = 95
                }
            ],
            DefaultPurchaseLt =
            [
                new LPS.APS.Core.Dto.PurchaseLtRule
                {
                    WarehouseCode = "WH01",
                    DefaultLtDays = 3
                }
            ],
            ArrivalToUsableOffsets =
            [
                new LPS.APS.Core.Dto.WarehouseOffsetRule
                {
                    WarehouseCode = "WH01",
                    OffsetHours = 2
                }
            ],
            OverdueMargin = new LPS.APS.Core.Dto.OverdueMarginParams
            {
                MarginPercent = 10,
                MinimumExtraDays = 1
            }
        });

        var solverStrategyJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.SolverStrategyBlock
        {
            Mode = LPS.APS.Core.Dto.SolverStrategyMode.Forward,
            OnTimeTarget = new LPS.APS.Core.Dto.OnTimeTargetParams
            {
                TargetPercent = 90,
                IsPrimaryObjective = true
            },
            Split = new LPS.APS.Core.Dto.SplitParams { MaxOptimizationSplitCount = 3, MinBatchQty = 1 },
            Setup = new LPS.APS.Core.Dto.SetupParams { DefaultSetupMinutes = 30, SetupLookAheadSize = 5 },
            StageOverlap = new LPS.APS.Core.Dto.StageOverlapParams { AllowOverlap = true, ThresholdPercent = 50 }
        });

        var candidateGuardrailJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.CandidateGuardrailBlock
        {
            NormalMs = 60_000,
            SoftMs = 90_000,
            LocalHardMs = 180_000,
            ImpactedTaskWarningPercent = 30,
            MaxRepairAttempts = 5,
            MaxPropagationRounds = 10,
            ResourceTopN = 5,
            SplitAlternatives = 3,
            WarnOnlyOnMaxImpacted = true
        });

        return (lockJson, supplyJson, procurementJson, solverStrategyJson, candidateGuardrailJson);
    }

    [Fact]
    public async Task Publish_DraftVersion_StatusBecomesPublished()
    {
        // Arrange
        var (lockJson, supplyJson, procurementJson, solverStrategyJson, candidateGuardrailJson) = CreateValidJson();
        var version = new ParameterSetVersion
        {
            Id = 1,
            ParameterSetId = 20,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Draft,
            LockJson = lockJson,
            SupplyJson = supplyJson,
            ProcurementJson = procurementJson,
            SolverStrategyJson = solverStrategyJson,
            CandidateGuardrailJson = candidateGuardrailJson,
        };
        _repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        await _service.PublishParameterSetVersionAsync(1, "tester");

        // Assert
        version.Status.Should().Be(GovernanceVersionStatus.Published);
        version.PublishedAt.Should().NotBeNull();
        // P0-02b：发布时聚合五子块 → ContentSnapshotJson（Run 装载重放载体，R14~R17 发布侧）
        version.ContentSnapshotJson.Should().NotBeNullOrWhiteSpace();
        version.ContentSnapshotJson.Should().Contain("\"SolverStrategy\"");
        version.ContentSnapshotJson.Should().Contain("\"CandidateGuardrail\"");
        version.ContentSnapshotJson.Should().Contain("\"Lock\"");
        version.ContentSnapshotJson.Should().Contain("\"Supply\"");
        version.ContentSnapshotJson.Should().Contain("\"Procurement\"");
        _repo.Verify(r => r.UpdateAsync(version, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_NewV2_OldV1RecordUnchanged()
    {
        // Arrange —— R02 核心：V1 已发布（旧 Run 引用），V2 为草稿（新 Run 将引用）
        var v1 = new ParameterSetVersion
        {
            Id = 1,
            ParameterSetId = 20,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
            PublishedAt = new DateTime(2026, 8, 1),
        };
        var (lockJson, supplyJson, procurementJson, solverStrategyJson, candidateGuardrailJson) = CreateValidJson();
        var v2 = new ParameterSetVersion
        {
            Id = 2,
            ParameterSetId = 20,
            VersionCode = "V2",
            Status = GovernanceVersionStatus.Draft,
            LockJson = lockJson,
            SupplyJson = supplyJson,
            ProcurementJson = procurementJson,
            SolverStrategyJson = solverStrategyJson,
            CandidateGuardrailJson = candidateGuardrailJson,
        };
        _repo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(v2);

        // Act —— 发布 V2
        await _service.PublishParameterSetVersionAsync(2, "tester");

        // Assert —— V2 成为新 PUBLISHED；V1 记录完全不变（旧 Run 引用 V1 不受影响）
        v2.Status.Should().Be(GovernanceVersionStatus.Published);
        v2.PublishedAt.Should().NotBeNull();
        v1.Id.Should().Be(1);
        v1.VersionCode.Should().Be("V1");
        v1.Status.Should().Be(GovernanceVersionStatus.Published);
        v1.PublishedAt.Should().Be(new DateTime(2026, 8, 1));
    }

    [Fact]
    public async Task Publish_AlreadyPublishedVersion_ThrowsInvalidOperation()
    {
        // Arrange —— 历史不可覆盖（R01 语义延伸至参数集）；
        // 版本带合法 JSON，穿透 P0-05 校验后由状态机拒绝（确保拦截点确为"已发布"）
        var (lockJson, supplyJson, procurementJson, solverStrategyJson, candidateGuardrailJson) = CreateValidJson();
        var version = new ParameterSetVersion
        {
            Id = 1,
            ParameterSetId = 20,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
            LockJson = lockJson,
            SupplyJson = supplyJson,
            ProcurementJson = procurementJson,
            SolverStrategyJson = solverStrategyJson,
            CandidateGuardrailJson = candidateGuardrailJson,
        };
        _repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var act = () => _service.PublishParameterSetVersionAsync(1, "tester");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ParameterSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_InvalidSolverStrategy_RejectedByValidator()
    {
        // Arrange（P0-02b：SolverStrategyValidator 接入发布链——非法 TargetPercent=150 → 发布前校验拒绝）
        var (lockJson, supplyJson, procurementJson, _, candidateGuardrailJson) = CreateValidJson();
        var invalidSolverStrategyJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.SolverStrategyBlock
        {
            Mode = LPS.APS.Core.Dto.SolverStrategyMode.Forward,
            OnTimeTarget = new LPS.APS.Core.Dto.OnTimeTargetParams { TargetPercent = 150 }   // 越界 0~100
        });

        var version = new ParameterSetVersion
        {
            Id = 3,
            ParameterSetId = 20,
            VersionCode = "V3",
            Status = GovernanceVersionStatus.Draft,
            LockJson = lockJson,
            SupplyJson = supplyJson,
            ProcurementJson = procurementJson,
            SolverStrategyJson = invalidSolverStrategyJson,
            CandidateGuardrailJson = candidateGuardrailJson,
        };
        _repo.Setup(r => r.GetByIdAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var act = () => _service.PublishParameterSetVersionAsync(3, "tester");

        // Assert（P0-02b：Validator 拒绝路径——不得发布、不得落库）
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*发布前校验失败*");
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ParameterSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_InvalidCandidateGuardrail_RejectedByValidator()
    {
        // Arrange（P0-02b：CandidateGuardrailValidator 接入发布链——非法时序 NormalMs>SoftMs → 发布前校验拒绝）
        var (lockJson, supplyJson, procurementJson, solverStrategyJson, _) = CreateValidJson();
        var invalidGuardrailJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.CandidateGuardrailBlock
        {
            NormalMs = 120_000,     // > SoftMs，违反 Normal <= Soft 时序
            SoftMs = 90_000,
            LocalHardMs = 180_000
        });

        var version = new ParameterSetVersion
        {
            Id = 4,
            ParameterSetId = 20,
            VersionCode = "V4",
            Status = GovernanceVersionStatus.Draft,
            LockJson = lockJson,
            SupplyJson = supplyJson,
            ProcurementJson = procurementJson,
            SolverStrategyJson = solverStrategyJson,
            CandidateGuardrailJson = invalidGuardrailJson,
        };
        _repo.Setup(r => r.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var act = () => _service.PublishParameterSetVersionAsync(4, "tester");

        // Assert（P0-02b：Validator 拒绝路径——不得发布、不得落库）
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*发布前校验失败*");
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ParameterSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
