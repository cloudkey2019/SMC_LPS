namespace LPS.APS.Core.Dto;

/// <summary>
/// 上游需求实体（用于排序的业务对象）
///
/// 包含排序所需的业务字段（OrderType, DelayStatus, DueDate等）
/// 排序后由 IDemandPriorityExecutor 赋值 DemandSequence，用于创建 LogicalProductionDemand。
///
/// 职责边界：
/// - 2号位将订单/需求转换为本 DTO，交由 IDemandPriorityExecutor 消费 3号位策略排序
/// - DemandSequence 是"计算层 → Priority Segment → 段内排序"的最终业务顺序（1,2,3...）
/// </summary>
public sealed class UpstreamDemand
{
    public string DemandKey { get; init; } = default!;
    public string? OrderType { get; init; }
    public string? DelayStatus { get; init; }
    public string? CustomerTier { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime? IssueDate { get; init; }
    public string? ProtectionStatus { get; init; }

    /// <summary>
    /// 排序后的业务顺序（1 起），由 IDemandPriorityExecutor.ExecutePrioritySort 赋值。
    /// </summary>
    public int DemandSequence { get; set; }

    /// <summary>
    /// 原始需求对象引用（供2号位排序后回溯到订单行，如 OrderPeggingRow）。
    /// </summary>
    public object? SourceDemand { get; init; }
}
