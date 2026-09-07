namespace LPS.APS.Core.Dto;

/// <summary>
/// 匹配条件（用于Demand排序Segment的筛选）
///
/// 冻结约束：
/// - 只支持白名单业务字段
/// - 只支持简单比较符（EQ/IN/LT/LTE/GT/GTE）
/// - 不支持任意C#表达式、SQL、复杂脚本、动态表达式
///
/// 白名单业务字段示例：
/// - OrderType: 订单类型
/// - DelayStatus: 延迟状态
/// - CustomerTier: 客户等级
/// - DueDate: 到期日
/// - RemainingTime: 剩余时间
/// - IssueDate: 下单日期
/// - ProtectionStatus: 保护状态
/// </summary>
public sealed class MatchCondition
{
    /// <summary>
    /// 字段名（必须是白名单业务字段）
    /// </summary>
    public string FieldName { get; init; } = default!;

    /// <summary>
    /// 操作符
    /// EQ: 等于
    /// IN: 包含于（值列表）
    /// LT: 小于
    /// LTE: 小于等于
    /// GT: 大于
    /// GTE: 大于等于
    /// </summary>
    public string Operator { get; init; } = default!;

    /// <summary>
    /// 比较值（单值或多值，视Operator而定）
    /// </summary>
    public string Value { get; init; } = default!;
}
