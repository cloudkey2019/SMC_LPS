using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 跨厂Pegging处理器（2号位职责）
///
/// 职责边界（PM 2026-09-09 跨厂SH单一Supply、分层Pegging 与 2/5号位统一裁决 v1.0）：
/// - STAGE_HANDOFF（大工艺接续型，PI级）：同一个 PI 沿大工艺跨厂继续生产，
///   跨厂在途（Interplant Transit）= PI Position 的一种当前位置，不是独立 Supply。
///   该链由 ProductionInstructionPositionCalculator.CalculateTransitPositions 处理
///   （TransitFacts → PositionSlice(INTERPLANT_IN_TRANSIT)），不经过本 handler。
/// - INTER_FACTORY_ORDER（厂间出荷指示型，SH级）：SH 本身是 Order（OrderType=SALES_ORDER
///   + CustomerSegment='跨厂'，OrderNo=SH号），单一 Supply 身份，禁拆 Transit/Received/Unproduced
///   三段式。单个 SH 整单一次入库。分两层 Pegging：第一层 Top Demand → SH Supply；
///   第二层 SH Demand → 源厂 Supply（源厂生产完成 → SourceReadyTime → 下游 SH AvailableTime）。
///
/// 时间传播原语：
///   - SourceReadyTime = max(该 SH 全部源厂 Task 的 PlannedEndTime) —— 整单最后一批完成时间。
///   - SH AvailableTime = SourceReadyTime + CrossFactoryLT（Transit/Inspection/Transfer 三元组天数）。
///
/// 5号位职责：提供 ERP 跨厂事实（在途/发运/到达）与 Strict Binding Evidence；不建 SH 主档。
/// 2号位职责：Pegging 消费与 Quantity-Time 传播（本 handler 承载时间原语，消费在主链 PeggingOrchestrator）。
/// </summary>
public sealed class CrossFactoryPeggingHandler
{
    private readonly ILogger<CrossFactoryPeggingHandler> _logger;

    public CrossFactoryPeggingHandler(ILogger<CrossFactoryPeggingHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 计算 SH 源厂就绪时间 = max(该 SH 全部源厂 Task 的 PlannedEndTime)。
    ///
    /// PM v1.0 口径：整单一次入库红线 → SourceReadyTime = 源厂生产份额最后一批完成时间，
    /// 即对源厂侧已落盘 Task 的完成时间取 max。无任何源厂 Task（源厂未排）时返回 null，
    /// 表示 SourceReadyTime 尚未产生，下游 SH AvailableTime 待 Layer-2 源厂求解后回传。
    /// </summary>
    public DateTime? CalculateSourceReadyTime(IEnumerable<DateTime?> sourceFactoryCompletionTimes)
    {
        ArgumentNullException.ThrowIfNull(sourceFactoryCompletionTimes);

        var valid = sourceFactoryCompletionTimes
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();

        if (valid.Count == 0)
        {
            _logger.LogDebug("SourceReadyTime 不产生：源厂侧无已排 Task（或完成时间为空）");
            return null;
        }

        var max = valid.Max();
        _logger.LogDebug("SourceReadyTime = {SourceReadyTime}（{TaskCount} 个源厂完成时间取 max）", max, valid.Count);
        return max;
    }

    /// <summary>
    /// 计算跨厂 Supply 的下游可用时间 = SourceReadyTime + 跨厂 LT。
    ///
    /// PM v1.0 口径：SH AvailableTime = SourceReadyTime + CrossFactoryLT。
    /// 跨厂 LT = Transport + Inspection + Transfer 三元组（由 CrossFactoryLeadTime 传入；
    /// LT 参数归属 5号位/3号位仍待 PM 终裁，代码口径不绑定归属）。
    /// </summary>
    public DateTime CalculateDownstreamAvailableTime(
        DateTime sourceReadyTime,
        CrossFactoryLeadTime leadTime)
    {
        var totalLeadTimeDays = leadTime.TransportDays + leadTime.InspectionDays + leadTime.TransferDays;
        return sourceReadyTime.AddDays(totalLeadTimeDays);
    }
}

/// <summary>
/// 跨厂前置期（Transport/Inspection/Transfer 三元组）。
/// 归属待 PM 终裁（v1.0 §十二「3号位治理跨厂运输提前期」 vs 旧注释「5号位提供」）。
/// </summary>
public sealed class CrossFactoryLeadTime
{
    public int TransportDays { get; init; }
    public int InspectionDays { get; init; }
    public int TransferDays { get; init; }
}