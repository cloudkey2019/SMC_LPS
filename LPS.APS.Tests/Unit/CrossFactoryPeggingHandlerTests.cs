using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 跨厂 SH 级（INTER_FACTORY_ORDER）履行闭合 Fixture 测试。
/// 契约（PM 2026-09-01）：同 SH 内部按「Transit → Received → 未生产」闭合，未生产才进源厂生产需求。
/// 用构造的 SupplyFact 假事实驱动逻辑验证，不依赖 5号位真实数据源
/// （当前 SH 级 Transit/Received 事实未接，故以构造假事实驱动逻辑验证）。
/// </summary>
public class CrossFactoryPeggingHandlerTests
{
    private readonly CrossFactoryPeggingHandler _handler = new(NullLogger<CrossFactoryPeggingHandler>.Instance);

    private static SupplyFact Supply(
        string supplyType,
        string sourceKey,
        decimal qty,
        int materialId = 1,
        int factoryId = 10)
        => new()
        {
            SupplyType = supplyType,
            SourceKey = sourceKey,
            AvailableQuantity = qty,
            MaterialId = materialId,
            FactoryId = factoryId
        };

    // ── ConsumeInterFactoryShipment：同 SH Transit → Received → 未生产 闭合 ──

    [Fact]
    public void Consume_TransitThenReceived_RemainderIsUnproduced()
    {
        var transit = new[] { Supply("INTERPLANT_IN_TRANSIT", "SH-100", 30m) };
        var received = new[] { Supply("INTER_FACTORY_RECEIVED", "SH-100", 20m) };

        var result = _handler.ConsumeInterFactoryShipment("SH-100", 100m, transit, received);

        result.ShipmentNo.Should().Be("SH-100");
        result.TotalRemainingQty.Should().Be(100m);
        result.ConsumedTransitQty.Should().Be(30m);
        result.ConsumedReceivedQty.Should().Be(20m);
        result.UnproducedQty.Should().Be(50m);
    }

    [Fact]
    public void Consume_TransitExceedsRemaining_CapsAtRemaining()
    {
        var transit = new[] { Supply("INTERPLANT_IN_TRANSIT", "SH-100", 80m) };
        var received = Array.Empty<SupplyFact>();

        var result = _handler.ConsumeInterFactoryShipment("SH-100", 50m, transit, received);

        result.ConsumedTransitQty.Should().Be(50m);
        result.ConsumedReceivedQty.Should().Be(0m);
        result.UnproducedQty.Should().Be(0m);
    }

    [Fact]
    public void Consume_TransitPlusReceivedExceedsRemaining_ReceivedIsCapped()
    {
        var transit = new[] { Supply("INTERPLANT_IN_TRANSIT", "SH-100", 60m) };
        var received = new[] { Supply("INTER_FACTORY_RECEIVED", "SH-100", 60m) };

        var result = _handler.ConsumeInterFactoryShipment("SH-100", 100m, transit, received);

        result.ConsumedTransitQty.Should().Be(60m);
        result.ConsumedReceivedQty.Should().Be(40m);
        result.UnproducedQty.Should().Be(0m);
    }

    [Fact]
    public void Consume_IgnoresOtherShipmentNos()
    {
        var transit = new[]
        {
            Supply("INTERPLANT_IN_TRANSIT", "SH-100", 30m),
            Supply("INTERPLANT_IN_TRANSIT", "SH-OTHER", 999m)
        };
        var received = Array.Empty<SupplyFact>();

        var result = _handler.ConsumeInterFactoryShipment("SH-100", 100m, transit, received);

        result.ConsumedTransitQty.Should().Be(30m);
    }

    // ── CalculateDownstreamAvailableTime：跨厂 LT 三元组传播 ──

    [Fact]
    public void DownstreamAvailableTime_AddsAllThreeLeadTimeComponents()
    {
        var upstream = new DateTime(2026, 9, 1);
        var leadTime = new CrossFactoryLeadTime { TransportDays = 2, InspectionDays = 1, TransferDays = 1 };

        var result = _handler.CalculateDownstreamAvailableTime(upstream, leadTime);

        result.Should().Be(new DateTime(2026, 9, 5));
    }

    [Fact]
    public void DownstreamAvailableTime_ZeroLeadTime_ReturnsUpstream()
    {
        var upstream = new DateTime(2026, 9, 1);
        var leadTime = new CrossFactoryLeadTime { TransportDays = 0, InspectionDays = 0, TransferDays = 0 };

        var result = _handler.CalculateDownstreamAvailableTime(upstream, leadTime);

        result.Should().Be(upstream);
    }

    // ── DeduplicateCrossFactorySupplies：同物理批次优先 Received、其次 Transit ──

    [Fact]
    public void Deduplicate_PrefersReceivedOverTransitForSameBatch()
    {
        var supplies = new[]
        {
            Supply("INTERPLANT_IN_TRANSIT", "SH-100", 30m),
            Supply("INTER_FACTORY_RECEIVED", "SH-100", 20m)
        };

        var result = _handler.DeduplicateCrossFactorySupplies(supplies);

        result.Should().ContainSingle();
        result[0].SupplyType.Should().Be("INTER_FACTORY_RECEIVED");
    }

    [Fact]
    public void Deduplicate_KeepsDistinctBatches()
    {
        var supplies = new[]
        {
            Supply("INTERPLANT_IN_TRANSIT", "SH-100", 30m),
            Supply("INTERPLANT_IN_TRANSIT", "SH-200", 30m)
        };

        var result = _handler.DeduplicateCrossFactorySupplies(supplies);

        result.Should().HaveCount(2);
    }
}
