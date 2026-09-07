namespace LPS.APS.Core.Dto;

/// <summary>
/// 优先级段配置（Demand排序的一个匹配段）
///
/// 执行语义：
/// 1. 每个Demand按SegmentOrder从小到大遍历所有Segment
/// 2. 命中第一个满足MatchConditions的Segment后停止（First Match）
/// 3. 在该Segment内按SortFields依次排序
/// 4. 最后用StableTieBreakFields确保确定性
///
/// 冻结约束：
/// - 不支持任意C#表达式、SQL、动态脚本
/// - 只支持白名单业务字段 + 简单比较符（EQ/IN/LT/LTE/GT/GTE）
/// </summary>
public sealed class PrioritySegmentConfig
{
    /// <summary>
    /// 计算层（越小越优先，通常用于区分紧急订单/普通订单等大层级）
    /// </summary>
    public int CalculationLayer { get; init; }

    /// <summary>
    /// 段顺序（同CalculationLayer内的执行顺序，越小越先匹配）
    /// </summary>
    public int SegmentOrder { get; init; }

    /// <summary>
    /// 是否启用该段
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// 匹配条件列表（AND关系，全部满足才进入该段）
    /// </summary>
    public IReadOnlyList<MatchCondition> MatchConditions { get; init; } = [];

    /// <summary>
    /// 排序字段列表（依次排序，第一个字段优先级最高）
    /// </summary>
    public IReadOnlyList<SortField> SortFields { get; init; } = [];

    /// <summary>
    /// 稳定性Tie-break字段（当SortFields完全相同时，按这些字段确保确定性）
    /// 最终兜底：DemandKey ASC
    /// </summary>
    public IReadOnlyList<string> StableTieBreakFields { get; init; } = [];
}
