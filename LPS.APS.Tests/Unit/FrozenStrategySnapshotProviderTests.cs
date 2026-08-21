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

        // B-5 缓存：快照缓存为进程级静态字典（内容不可变、无需失效），测试间必须隔离
        FrozenStrategySnapshotProvider.ClearCache();
    }

    /// <summary>B-5 测试辅助：装配一套合法三版本（六块 JSON 均为合法内容，P0-04 不误伤）</summary>
    private void SetupValidVersions(
        long strategyProfileVersionId,
        long ruleSetVersionId,
        long parameterSetVersionId)
    {
        var strategyProfileVersion = new StrategyProfileVersion
        {
            Id = strategyProfileVersionId,
            StrategyProfileId = 1,
            VersionCode = "V1.0",
            RuleSetVersionId = ruleSetVersionId,
            ParameterSetVersionId = parameterSetVersionId,
            Status = "PUBLISHED"
        };

        var ruleSetVersion = new RuleSetVersion
        {
            Id = ruleSetVersionId,
            RuleSetId = 2,
            VersionCode = "R1.0",
            DemandPriorityJson = JsonSerializer.Serialize(new DemandPriorityBlock
            {
                Segments = new List<PrioritySegment>
                {
                    new PrioritySegment { SegmentOrder = 1, SegmentName = "紧急订单", IsEnabled = true }
                }
            }),
            Status = "PUBLISHED"
        };

        var parameterSetVersion = new ParameterSetVersion
        {
            Id = parameterSetVersionId,
            ParameterSetId = 3,
            VersionCode = "P1.0",
            LockJson = JsonSerializer.Serialize(new LockBlock
            {
                Trigger = new ProtectionTriggerParams { UseRemainingTimeThreshold = true, RemainingTimeThresholdHours = 24 }
            }),
            SupplyJson = JsonSerializer.Serialize(new SupplyBlock
            {
                Inventory = new InventoryAvailabilityRule { IsEnabled = true, WarehousePriority = new List<string> { "WH01" } }
            }),
            ProcurementJson = JsonSerializer.Serialize(new ProcurementBlock
            {
                PlanningYields = new List<PlanningYieldRule>
                {
                    new PlanningYieldRule { MaterialId = "MAT001", YieldPercent = 0.95m }
                }
            }),
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
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_JSON为空_装载失败抛异常()
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

        // Act & Assert（P0-04：必填 Block 缺失 → Snapshot 装载失败，不静默回退空 Block）
        var act = async () => await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DemandPriorityJson 为空/缺失*");
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_JSON格式错误_装载失败抛异常()
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

        // Act & Assert（P0-04：JSON/内容损坏 → Snapshot 装载失败，不静默回退空 Block）
        var act = async () => await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DemandPriorityJson 格式无效*");
    }

    // ===================== B-5：Snapshot 缓存（清单 #51 性能红线 / 契约 C2-4） =====================

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_同一版本二次调用_命中缓存不重复查库()
    {
        // Arrange（B-5：一次 Run 只加载一次 Snapshot；同 VersionId 缓存命中）
        const long strategyProfileVersionId = 101;
        const long ruleSetVersionId = 201;
        const long parameterSetVersionId = 301;
        SetupValidVersions(strategyProfileVersionId, ruleSetVersionId, parameterSetVersionId);

        // Act：同一 VersionId 连续调用两次
        var first = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);
        var second = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        // Assert：二次调用命中缓存，仓储不再查询（各仅一次）
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        _mockStrategyProfileRepo.Verify(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()), Times.Once);
        _mockRuleSetRepo.Verify(r => r.GetByIdAsync(ruleSetVersionId, It.IsAny<CancellationToken>()), Times.Once);
        _mockParameterSetRepo.Verify(r => r.GetByIdAsync(parameterSetVersionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_不同版本_互不污染各走仓储()
    {
        // Arrange（B-5 / 契约 C2-4：Cache Key 含 VersionId，不同版本绝不互相污染）
        SetupValidVersions(102, 202, 302);
        SetupValidVersions(103, 203, 303);

        // Act：102 → 103 → 102 再读（验证加载 103 不会覆盖/污染 102 的缓存条目）
        var snapshotA = await _provider.GetFrozenStrategySnapshotAsync(102, CancellationToken.None);
        var snapshotB = await _provider.GetFrozenStrategySnapshotAsync(103, CancellationToken.None);
        var snapshotA2 = await _provider.GetFrozenStrategySnapshotAsync(102, CancellationToken.None);

        // Assert：
        // ① 各自返回对应版本内容（不串数据）
        snapshotA.RuleSetVersionId.Should().Be(202);
        snapshotB.RuleSetVersionId.Should().Be(203);
        // ② 102 第三次读取仍返回 102 内容（未被 103 污染）且命中缓存（仓储仍只查一次）
        snapshotA2.RuleSetVersionId.Should().Be(202);
        snapshotA2.Procurement.PlanningYields.Should().HaveCount(1);
        _mockStrategyProfileRepo.Verify(r => r.GetByIdAsync(102, It.IsAny<CancellationToken>()), Times.Once);
        _mockStrategyProfileRepo.Verify(r => r.GetByIdAsync(103, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_缓存命中_反序列化重建独立实例_内容值相等FrozenAt刷新()
    {
        // Arrange（B-5：Run 内不刷新——命中时从 JSON 快照反序列化重建，六块内容与首次一致；FrozenAt=本次调用时点）
        const long strategyProfileVersionId = 104;
        SetupValidVersions(strategyProfileVersionId, 204, 304);

        // Act：连续调用两次（间隔确保 FrozenAt 时间戳变化）
        var first = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);
        await System.Threading.Tasks.Task.Delay(10);
        var second = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        // Assert：
        // ① 每次返回独立实例（JSON 重建，杜绝共享可变对象污染——消费者就地修改不影响缓存）
        second.Should().NotBeSameAs(first);
        second.DemandPriority.Should().NotBeSameAs(first.DemandPriority);
        second.Lock.Should().NotBeSameAs(first.Lock);
        second.Supply.Should().NotBeSameAs(first.Supply);
        second.Procurement.Should().NotBeSameAs(first.Procurement);
        second.SolverStrategy.Should().NotBeSameAs(first.SolverStrategy);
        second.CandidateGuardrail.Should().NotBeSameAs(first.CandidateGuardrail);
        // ② 六块内容值相等（Run 内不刷新）
        second.StrategyProfileVersionId.Should().Be(first.StrategyProfileVersionId);
        second.RuleSetVersionId.Should().Be(first.RuleSetVersionId);
        second.ParameterSetVersionId.Should().Be(first.ParameterSetVersionId);
        second.DemandPriority.Segments.Should().HaveCount(1);
        second.DemandPriority.Segments[0].SegmentName.Should().Be("紧急订单");
        second.Lock.Trigger.RemainingTimeThresholdHours.Should().Be(24);
        second.Supply.Inventory.WarehousePriority.Should().Contain("WH01");
        second.Procurement.PlanningYields.Should().HaveCount(1);
        // ③ FrozenAt 刷新为本次调用时点（冻结时点语义，跨 Run 不陈旧）
        second.FrozenAt.Should().BeAfter(first.FrozenAt);
        // ④ 命中缓存不再查库
        _mockStrategyProfileRepo.Verify(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetFrozenStrategySnapshotAsync_装载失败_不写入缓存_同版本重试重新走仓储()
    {
        // Arrange（P0-04 × B-5 交互：坏配置装载失败绝不写入缓存，避免"陈旧坏快照"被后续同版本命中）
        const long strategyProfileVersionId = 105;

        // 第一次：DemandPriorityJson 为空 → 装载失败
        var badStrategyProfileVersion = new StrategyProfileVersion
        {
            Id = strategyProfileVersionId,
            RuleSetVersionId = 205,
            ParameterSetVersionId = 305
        };
        var badRuleSetVersion = new RuleSetVersion { Id = 205, DemandPriorityJson = null };
        var badParameterSetVersion = new ParameterSetVersion { Id = 305, LockJson = "{}", SupplyJson = "{}", ProcurementJson = "{}" };

        _mockStrategyProfileRepo
            .Setup(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(badStrategyProfileVersion);
        _mockRuleSetRepo
            .Setup(r => r.GetByIdAsync(205, It.IsAny<CancellationToken>()))
            .ReturnsAsync(badRuleSetVersion);
        _mockParameterSetRepo
            .Setup(r => r.GetByIdAsync(305, It.IsAny<CancellationToken>()))
            .ReturnsAsync(badParameterSetVersion);

        var act = async () => await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DemandPriorityJson 为空/缺失*");

        // 修复配置后重试同一 VersionId → 必须重新走仓储（证明失败未写入缓存，无陈旧坏快照）
        SetupValidVersions(strategyProfileVersionId, 205, 305);

        var snapshot = await _provider.GetFrozenStrategySnapshotAsync(strategyProfileVersionId, CancellationToken.None);

        snapshot.Should().NotBeNull();
        snapshot.DemandPriority.Segments.Should().HaveCount(1);
        // 失败装载 1 次 + 修复后重试成功 1 次 = 共 2 次仓储查询（若失败写入了缓存，则重试命中缓存仅 1 次）
        _mockStrategyProfileRepo.Verify(r => r.GetByIdAsync(strategyProfileVersionId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
