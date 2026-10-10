using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 红线⑤⑥ SH 校验（ValidateShConsistency 纯函数）测试。
/// 契约（PM pm回复826 §2）：
///   ⑤ SH 同 SH 匹配不串 SH —— 同一需求（DemandKey）的 SH 分配必须指向同一出荷指示号；
///   ⑥ 同一 SH 份额不重复计量 —— 同一 SH 分配合计不得超过其指令总量（Order.Quantity）。
/// </summary>
public class ValidateShConsistencyTests
{
    private static SupplyAllocationItem Sh(string demandKey, string shNo, decimal qty)
        => new()
        {
            AllocationSequence = 1,
            DemandKey = demandKey,
            ShippingInstructionNo = shNo,
            AllocatedQuantity = qty,
            SourceType = SupplySourceType.INTER_FACTORY_ORDER,
            SourceReference = shNo
        };

    [Fact]
    public void 无SH分配_通过()
    {
        var allocations = new List<SupplyAllocationItem>
        {
            new() { DemandKey = "D1", AllocatedQuantity = 10m, SourceType = SupplySourceType.INVENTORY }
        };

        var errors = PeggingOrchestrator.ValidateShConsistency(allocations, new Dictionary<string, decimal>());

        errors.Should().BeEmpty();
    }

    [Fact]
    public void 单需求单SH_不超总量_通过()
    {
        var allocations = new[] { Sh("D1", "SH-100", 30m) };
        var totals = new Dictionary<string, decimal> { ["SH-100"] = 50m };

        var errors = PeggingOrchestrator.ValidateShConsistency(allocations, totals);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void 同一需求跨两个SH_打串单()
    {
        var allocations = new[]
        {
            Sh("D1", "SH-100", 20m),
            Sh("D1", "SH-200", 10m)
        };
        var totals = new Dictionary<string, decimal> { ["SH-100"] = 50m, ["SH-200"] = 50m };

        var errors = PeggingOrchestrator.ValidateShConsistency(allocations, totals);

        errors.Should().ContainSingle(e => e.Contains("串单") && e.Contains("D1"));
    }

    [Fact]
    public void 不同需求各自SH_不误报()
    {
        var allocations = new[]
        {
            Sh("D1", "SH-100", 20m),
            Sh("D2", "SH-200", 10m)
        };
        var totals = new Dictionary<string, decimal> { ["SH-100"] = 50m, ["SH-200"] = 50m };

        var errors = PeggingOrchestrator.ValidateShConsistency(allocations, totals);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void SH分配合计超总量_打重复计量()
    {
        // 同一 SH（单一供给身份）被多个需求分配合计 120 > 指令总量 100
        var allocations = new[]
        {
            Sh("D1", "SH-100", 60m),
            new SupplyAllocationItem
            {
                AllocationSequence = 2,
                DemandKey = "D2",
                ShippingInstructionNo = "SH-100",
                AllocatedQuantity = 60m,
                SourceType = SupplySourceType.INTER_FACTORY_ORDER,
                SourceReference = "SH-100"
            }
        };
        var totals = new Dictionary<string, decimal> { ["SH-100"] = 100m };

        var errors = PeggingOrchestrator.ValidateShConsistency(allocations, totals);

        errors.Should().ContainSingle(e => e.Contains("重复计量") && e.Contains("SH-100"));
    }
}
