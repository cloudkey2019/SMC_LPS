namespace LPS.APS.Core.Enum;

/// <summary>
/// 需求类型枚举
/// 用于DemandBalance内存账本，标识需求来源
/// </summary>
public enum DemandType
{
    /// <summary>
    /// 销售订单需求
    /// </summary>
    ORDER,

    /// <summary>
    /// 积压订单需求
    /// </summary>
    BACKLOG,

    /// <summary>
    /// 预测需求
    /// </summary>
    FORECAST,

    /// <summary>
    /// 组件需求（BOM展开产生的半成品需求）
    /// </summary>
    COMPONENT
}
