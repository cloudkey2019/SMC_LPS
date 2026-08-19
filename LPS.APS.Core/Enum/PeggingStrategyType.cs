namespace LPS.APS.Core.Enum;

/// <summary>
/// Pegging 策略类型
/// 对应文档：步骤2.1 中的供应分配策略
/// </summary>
public enum PeggingStrategyType
{
    /// <summary>
    /// 先进先出（First In First Out）
    /// 优先分配最早入库的供应
    /// </summary>
    FIFO = 1,

    /// <summary>
    /// 先到期先出（First Expired First Out）
    /// 优先分配最接近到期日的供应
    /// </summary>
    FEFO = 2,

    /// <summary>
    /// 最近工段优先
    /// 优先分配最接近当前工段的供应（减少转运成本）
    /// </summary>
    NEAREST_STAGE = 3,

    /// <summary>
    /// 跨工厂调配
    /// 允许跨工厂的供应分配
    /// </summary>
    CROSS_FACTORY = 4,

    /// <summary>
    /// 同批次优先
    /// 优先分配相同批次的供应（质量一致性）
    /// </summary>
    SAME_BATCH = 5,

    /// <summary>
    /// 最小提前期
    /// 优先分配提前期最短的供应
    /// </summary>
    MIN_LEAD_TIME = 6,

    /// <summary>
    /// 成本最优
    /// 优先分配单位成本最低的供应
    /// </summary>
    LOWEST_COST = 7
}
