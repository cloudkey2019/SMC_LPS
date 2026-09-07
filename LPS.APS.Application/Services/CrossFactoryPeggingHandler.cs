using LPS.APS.Core.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 跨厂Pegging处理器（2号位职责）
///
/// 职责边界（PM 2026-09-01 两类跨厂裁决）：
/// - STAGE_HANDOFF（大工艺接续型，PI级）：同一个 PI 沿大工艺跨厂继续生产，
///   跨厂在途（Interplant Transit）= PI Position 的一种当前位置，不是独立 Supply。
///   该链由 ProductionInstructionPositionCalculator.CalculateTransitPositions 处理
///   （TransitFacts → PositionSlice(INTERPLANT_IN_TRANSIT)），不经过本 handler。
/// - INTER_FACTORY_ORDER（厂间出荷指示型，SH级）：目标厂需求 → BS/KS 库存 → 具体 SH 承接，
///   同 SH 内部按「Transit → Received → 未生产」闭合，未生产才进源厂生产需求。
///   本 handler 的 Consume* 方法服务于该 SH 级履行闭合。
///
/// 5号位职责：提供 Transit/Received 事实（数量、可用时间、SH No 绑定）；2号位职责：Pegging 消费与 Quantity-Time 传播。
/// </summary>
public sealed class CrossFactoryPeggingHandler
{
    private readonly ILogger<CrossFactoryPeggingHandler> _logger;

    public CrossFactoryPeggingHandler(ILogger<CrossFactoryPeggingHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 消费同一 SH 的厂间出荷履行（防止重复计数）
    ///
    /// PM 冻结口径（INTER_FACTORY_ORDER，SH级）：
    /// - SH 内部 Transit、Received、未生产属于同一 SH 履行状态
    /// - 不能拆成多个外部 Supply 重复入池
    /// - 按顺序消费：Transit → Received → 剩余份额 = 未生产（触发源厂生产 Demand）
    /// </summary>
    public InterFactoryShipmentConsumption ConsumeInterFactoryShipment(
        string shipmentNo,
        decimal shipmentRemainingQty,
        IEnumerable<SupplyFact> transitSupplies,
        IEnumerable<SupplyFact> receivedSupplies)
    {
        var remaining = shipmentRemainingQty;

        // 1. 消费同 SH 的 Transit
        var transitQty = transitSupplies
            .Where(s => s.SourceKey == shipmentNo)
            .Sum(s => s.AvailableQuantity);

        var consumedTransit = Math.Min(remaining, transitQty);
        remaining -= consumedTransit;

        // 2. 消费同 SH 的 Received
        var receivedQty = receivedSupplies
            .Where(s => s.SourceKey == shipmentNo)
            .Sum(s => s.AvailableQuantity);

        var consumedReceived = Math.Min(remaining, receivedQty);
        remaining -= consumedReceived;

        // 3. 剩余部分 = SH 未生产份额（触发源厂生产 Demand）
        var unproducedQty = remaining;

        _logger.LogInformation(
            "Inter-factory shipment {SH} consumption: Total={Total}, Transit={Transit}, Received={Received}, Unproduced={Unproduced}",
            shipmentNo, shipmentRemainingQty, consumedTransit, consumedReceived, unproducedQty);

        return new InterFactoryShipmentConsumption
        {
            ShipmentNo = shipmentNo,
            TotalRemainingQty = shipmentRemainingQty,
            ConsumedTransitQty = consumedTransit,
            ConsumedReceivedQty = consumedReceived,
            UnproducedQty = unproducedQty
        };
    }

    /// <summary>
    /// 计算跨厂 Supply 的下游可用时间
    ///
    /// PM 冻结口径：
    /// - 已存在的 Transit/Received：直接使用 5号位提供的 AvailableTime
    /// - 本次 Solver 刚排出的源厂新增生产：1号位 FinalTask 完成时间 + 跨厂 LT = 下游 AvailableTime
    /// 跨厂 LT = Transport + Inspection + Transfer 三元组（由 5号位跨厂事实提供，经 CrossFactoryLeadTime 传入）。
    /// </summary>
    public DateTime CalculateDownstreamAvailableTime(
        DateTime upstreamCompletionTime,
        CrossFactoryLeadTime leadTime)
    {
        var totalLeadTimeDays = leadTime.TransportDays + leadTime.InspectionDays + leadTime.TransferDays;
        return upstreamCompletionTime.AddDays(totalLeadTimeDays);
    }

    /// <summary>
    /// 去重检查（防止 Transit 和 Received 重复计数）
    ///
    /// PM 冻结口径：
    /// - Transit/Received 自身按 SourceKey + MaterialId + FactoryId 去重
    /// - 防止一批货到货后同时还留在 Transit 里
    /// - 同一物理批次优先取 Received（已到货），其次 Transit（在途）
    /// </summary>
    public List<SupplyFact> DeduplicateCrossFactorySupplies(IEnumerable<SupplyFact> supplies)
    {
        var deduplicated = supplies
            .GroupBy(s => new
            {
                s.SourceKey,
                s.MaterialId,
                s.FactoryId,
                PhysicalKey = $"{s.SourceKey}_{s.MaterialId}_{s.FactoryId}"
            })
            .Select(g =>
            {
                // 优先取 Received（已到货），其次 Transit（在途）
                var received = g.FirstOrDefault(s => s.SupplyType?.Contains("RECEIVED", StringComparison.OrdinalIgnoreCase) == true);
                if (received != null)
                {
                    return received;
                }

                var transit = g.FirstOrDefault(s => s.SupplyType?.Contains("TRANSIT", StringComparison.OrdinalIgnoreCase) == true);
                if (transit != null)
                {
                    return transit;
                }

                return g.First();
            })
            .ToList();

        if (deduplicated.Count < supplies.Count())
        {
            _logger.LogInformation(
                "Deduplicated cross-factory supplies: {Original} → {Deduplicated}",
                supplies.Count(), deduplicated.Count);
        }

        return deduplicated;
    }
}

/// <summary>
/// 厂间出荷（Inter-factory Shipment）履行消费结果
/// </summary>
public sealed class InterFactoryShipmentConsumption
{
    public string ShipmentNo { get; init; } = default!;
    public decimal TotalRemainingQty { get; init; }
    public decimal ConsumedTransitQty { get; init; }
    public decimal ConsumedReceivedQty { get; init; }
    public decimal UnproducedQty { get; init; }
}

/// <summary>
/// 跨厂前置期（Transport/Inspection/Transfer 三元组，由 5号位跨厂事实提供）
/// </summary>
public sealed class CrossFactoryLeadTime
{
    public int TransportDays { get; init; }
    public int InspectionDays { get; init; }
    public int TransferDays { get; init; }
}
