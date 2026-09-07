using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// FrozenStrategySnapshot → FrozenFactParameters 投影测试（2号位阶段 3，PI Position 两级前置）。
/// 契约：DefaultPurchaseLt 取 Warehouse 级默认（MaterialId 空）；OverdueMargin 取 MinimumExtraDays；
/// ArrivalToUsableOffsets 按 Receiving Warehouse 投影为 int 小时数。纯投影、确定性，供 5号位事实计算消费。
/// </summary>
public class BuildFrozenFactParametersTests
{
    private static FrozenStrategySnapshot Snapshot(
        long versionId = 42,
        PurchaseLtRule[]? ltRules = null,
        int? overdueMarginDays = null,
        WarehouseOffsetRule[]? offsets = null)
        => new()
        {
            StrategyProfileVersionId = versionId,
            Procurement = new ProcurementBlock
            {
                DefaultPurchaseLt = ltRules ?? [],
                OverdueMargin = new OverdueMarginParams
                {
                    MarginPercent = 10m,
                    MinimumExtraDays = overdueMarginDays ?? 3
                },
                ArrivalToUsableOffsets = offsets ?? []
            }
        };

    [Fact]
    public void Build_投影版本号与逾期容差_空规则时默认0()
    {
        var snapshot = Snapshot(versionId: 42, overdueMarginDays: 5);

        var result = PeggingOrchestrator.BuildFrozenFactParameters(snapshot);

        result.StrategyProfileVersionId.Should().Be(42);
        result.OverdueMargin.Should().Be(5);
        result.DefaultPurchaseLt.Should().Be(0);           // 无 LT 规则 → 0
        result.ArrivalToUsableOffsets.Should().BeEmpty();
    }

    [Fact]
    public void Build_DefaultPurchaseLt取Warehouse级默认_忽略Material级()
    {
        var snapshot = Snapshot(ltRules: new[]
        {
            new PurchaseLtRule { WarehouseCode = "WH-A", MaterialId = null, DefaultLtDays = 7 },
            new PurchaseLtRule { WarehouseCode = "WH-A", MaterialId = "100", DefaultLtDays = 14 }
        });

        var result = PeggingOrchestrator.BuildFrozenFactParameters(snapshot);

        result.DefaultPurchaseLt.Should().Be(7);           // 取 Warehouse 级默认（MaterialId 空），非 Material 级 14
    }

    [Fact]
    public void Build_ArrivalToUsableOffsets按仓库投影_并取整()
    {
        var snapshot = Snapshot(offsets: new[]
        {
            new WarehouseOffsetRule { WarehouseCode = "WH-A", OffsetHours = 2.4 },
            new WarehouseOffsetRule { WarehouseCode = "WH-B", OffsetHours = 8.0 }
        });

        var result = PeggingOrchestrator.BuildFrozenFactParameters(snapshot);

        result.ArrivalToUsableOffsets.Should().HaveCount(2);
        result.ArrivalToUsableOffsets["WH-A"].Should().Be(2);  // 2.4 → round 2
        result.ArrivalToUsableOffsets["WH-B"].Should().Be(8);
    }
}
