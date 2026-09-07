namespace LPS.APS.Core.Dto;

/// <summary>
/// 排序字段（用于Demand排序Segment内的排序规则）
///
/// 支持的字段示例：
/// - DueDate: 到期日
/// - IssueDate: 下单日期
/// - Priority: 优先级数值
/// - RemainingTime: 剩余时间
/// - CustomerTier: 客户等级
/// - DelayDays: 延迟天数
///
/// 排序方向：
/// - ASC: 升序（越小越优先）
/// - DESC: 降序（越大越优先）
/// </summary>
public sealed class SortField
{
    /// <summary>
    /// 字段名（必须是白名单业务字段）
    /// </summary>
    public string FieldName { get; init; } = default!;

    /// <summary>
    /// 排序方向（ASC/DESC）
    /// </summary>
    public string Direction { get; init; } = default!;
}
