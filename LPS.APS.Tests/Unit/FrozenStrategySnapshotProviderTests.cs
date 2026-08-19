using System.Text.Json;
using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// FrozenStrategySnapshotProvider 单元测试（阶段 B 装配逻辑验收）
/// 测试范围：
/// 1. 正常装配流程（三版本级联加载 + 六块反序列化）
/// 2. 异常处理（版本不存在场景）
/// 3. JSON 反序列化容错（空值、格式错误）
/// </summary>
public class FrozenStrategySnapshotProviderTests
{
    private readonly Mock<IStrategyProfileVersionRepository> _mockStrategyProfileRepo;
    private readonly Mock<IRuleSetVersionRepository> _mockRuleSetRepo;
    private readonly Mock<IParameterSetVersionRepository> _mockParameterSetRepo;
    private readonly FrozenStrategySnapshotProvider _provider;

    public FrozenStrategySnapshotProviderTests()
    {
        _mockStrategyProfileRepo = new Mock<IStrategyProfileVersionRepository>();
        _mockRuleSetRepo = new Mock<IRuleSetVersionRepository>();
        _mockParameterSetRepo = new Mock<IParameterSetVersionRepository>();

        _provider = new FrozenStrategySnapshotProvider(
            _mockStrategyProfileRepo.Object,
            _mockRuleSetRepo.Object,
            _mockParameterSetRepo.Object
        );
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_正常装配六块_成功()
    {
        // Arrange
        const long strategyProfileVersionId = 100;
        const long ruleSetVersionId = 200;
        const long parameterSetVersionId = 300;

        var strategyProfileVersion = new StrategyProfileVersion
        {
            Id = strategyProfileVersionId,
            StrategyProfileId = 1,
            VersionCode = "V1.0",
            RuleSetVersionId = ruleSetVersionId,
            ParameterSetVersionId = parameterSetVersionId,
            Status = "PUBLISHED"
        };

        var demandPriorityJson = JsonSerializer.Serialize(new DemandPriorityBlock
        {
            Segments = new List<PrioritySegment>
            {
                new PrioritySegment
                {
                    SegmentOrder = 1,
                    SegmentName = "紧急订单",
                    IsEnabled = true
                }
            }
        });

        var ruleSetVersion = new RuleSetVersion
        {
            Id = ruleSetVersionId,
            RuleSetId = 2,
            VersionCode = "R1.0",
            DemandPriorityJson = demandPriorityJson,
            Status = "PUBLISHED"
        };

        var lockJson = JsonSerializer.Serialize(new LockBlock
        {
            Trigger = new ProtectionTriggerParams
            {
                UseRemainingTimeThreshold = true,
                RemainingTimeThresholdHours = 24
            }
        });

        var supplyJson = JsonSerializer.Serialize(new SupplyBlock
        {
            Inventory = new InventoryAvailabilityRule
            {
                IsEnabled = true,
                WarehousePriority = new List<string> { "WH01", "WH02" }
            }
        });

        var procurementJson = JsonSerializer.Serialize(new ProcurementBlock
        {
            PlanningYields = new List<PlanningYieldRule>
            {
                new PlanningYieldRule
                {
                    MaterialId = "MAT001",
                    YieldPercent = 0.95m
                }
            }
        });

        var parameterSetVersion = new ParameterSetVersion
        {
            Id = parameterSetVersionId,
            ParameterSetId = 3,
            VersionCode = "P1.0",
            LockJson = lockJson,
            SupplyJson = supplyJson,
            ProcurementJson = procurementJson,
            Status = "PUBLISHED"
        };

        _mockStrategyProfileRepo
            .Setup(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyProfileVersion);

        _mockRuleSetRepo
            .Setup(r => r.GetByIdAsync(ruleSetVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ruleSetVersion);

        _mockParameterSetRepo
            .Setup(r => r.GetByIdAsync(parameterSetVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parameterSetVersion);

        // Act
        var snapshot = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.StrategyProfileVersionId.Should().Be(strategyProfileVersionId);
        snapshot.RuleSetVersionId.Should().Be(ruleSetVersionId);
        snapshot.ParameterSetVersionId.Should().Be(parameterSetVersionId);
        snapshot.FrozenAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        // 验证六块装配
        snapshot.DemandPriority.Should().NotBeNull();
        snapshot.DemandPriority.Segments.Should().HaveCount(1);
        snapshot.DemandPriority.Segments[0].SegmentName.Should().Be("紧急订单");

        snapshot.Lock.Should().NotBeNull();
        snapshot.Lock.Trigger.Should().NotBeNull();
        snapshot.Lock.Trigger.RemainingTimeThresholdHours.Should().Be(24);

        snapshot.Supply.Should().NotBeNull();
        snapshot.Supply.Inventory.Should().NotBeNull();
        snapshot.Supply.Inventory.WarehousePriority.Should().Contain("WH01");

        snapshot.Procurement.Should().NotBeNull();
        snapshot.Procurement.PlanningYields.Should().HaveCount(1);
        snapshot.Procurement.PlanningYields[0].YieldPercent.Should().Be(0.95m);

        snapshot.SolverStrategy.Should().NotBeNull();
        snapshot.CandidateGuardrail.Should().NotBeNull();

        // 验证仓储调用
        _mockStrategyProfileRepo.Verify(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRuleSetRepo.Verify(r => r.GetByIdAsync(ruleSetVersionId, It.IsAny<CancellationToken>()), Times.Once);
        _mockParameterSetRepo.Verify(r => r.GetByIdAsync(parameterSetVersionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_策略包版本不存在_抛出异常()
    {
        // Arrange
        const long strategyProfileVersionId = 999;

        _mockStrategyProfileRepo
            .Setup(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StrategyProfileVersion?)null);

        // Act & Assert
        var act = async () => await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"策略包版本不存在：{strategyProfileVersionId}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_规则集版本不存在_抛出异常()
    {
        // Arrange
        const long strategyProfileVersionId = 100;
        const long ruleSetVersionId = 999;

        var strategyProfileVersion = new StrategyProfileVersion
        {
            Id = strategyProfileVersionId,
            RuleSetVersionId = ruleSetVersionId,
            ParameterSetVersionId = 300
        };

        _mockStrategyProfileRepo
            .Setup(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyProfileVersion);

        _mockRuleSetRepo
            .Setup(r => r.GetByIdAsync(ruleSetVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuleSetVersion?)null);

        // Act & Assert
        var act = async () => await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"规则集版本不存在：{ruleSetVersionId}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_参数集版本不存在_抛出异常()
    {
        // Arrange
        const long strategyProfileVersionId = 100;
        const long ruleSetVersionId = 200;
        const long parameterSetVersionId = 999;

        var strategyProfileVersion = new StrategyProfileVersion
        {
            Id = strategyProfileVersionId,
            RuleSetVersionId = ruleSetVersionId,
            ParameterSetVersionId = parameterSetVersionId
        };

        var ruleSetVersion = new RuleSetVersion
        {
            Id = ruleSetVersionId
        };

        _mockStrategyProfileRepo
            .Setup(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyProfileVersion);

        _mockRuleSetRepo
            .Setup(r => r.GetByIdAsync(ruleSetVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ruleSetVersion);

        _mockParameterSetRepo
            .Setup(r => r.GetByIdAsync(parameterSetVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParameterSetVersion?)null);

        // Act & Assert
        var act = async () => await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"参数集版本不存在：{parameterSetVersionId}");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_JSON为空_返回默认块()
    {
        // Arrange
        const long strategyProfileVersionId = 100;

        var strategyProfileVersion = new StrategyProfileVersion
        {
            Id = strategyProfileVersionId,
            RuleSetVersionId = 200,
            ParameterSetVersionId = 300
        };

        var ruleSetVersion = new RuleSetVersion
        {
            Id = 200,
            DemandPriorityJson = null
        };

        var parameterSetVersion = new ParameterSetVersion
        {
            Id = 300,
            LockJson = string.Empty,
            SupplyJson = "   ",
            ProcurementJson = null
        };

        _mockStrategyProfileRepo
            .Setup(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyProfileVersion);

        _mockRuleSetRepo
            .Setup(r => r.GetByIdAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ruleSetVersion);

        _mockParameterSetRepo
            .Setup(r => r.GetByIdAsync(300, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parameterSetVersion);

        // Act
        var snapshot = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        // Assert
        snapshot.DemandPriority.Should().NotBeNull();
        snapshot.Lock.Should().NotBeNull();
        snapshot.Supply.Should().NotBeNull();
        snapshot.Procurement.Should().NotBeNull();
        snapshot.SolverStrategy.Should().NotBeNull();
        snapshot.CandidateGuardrail.Should().NotBeNull();
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_JSON格式错误_返回默认块()
    {
        // Arrange
        const long strategyProfileVersionId = 100;

        var strategyProfileVersion = new StrategyProfileVersion
        {
            Id = strategyProfileVersionId,
            RuleSetVersionId = 200,
            ParameterSetVersionId = 300
        };

        var ruleSetVersion = new RuleSetVersion
        {
            Id = 200,
            DemandPriorityJson = "{ invalid json }"
        };

        var parameterSetVersion = new ParameterSetVersion
        {
            Id = 300,
            LockJson = "not json at all",
            SupplyJson = "{\"unclosed\":",
            ProcurementJson = "[]"
        };

        _mockStrategyProfileRepo
            .Setup(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyProfileVersion);

        _mockRuleSetRepo
            .Setup(r => r.GetByIdAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ruleSetVersion);

        _mockParameterSetRepo
            .Setup(r => r.GetByIdAsync(300, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parameterSetVersion);

        // Act
        var snapshot = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.DemandPriority.Should().NotBeNull();
        snapshot.Lock.Should().NotBeNull();
        snapshot.Supply.Should().NotBeNull();
        snapshot.Procurement.Should().NotBeNull();
    }
}
