namespace LPS.APS.Core.Enum;

/// <summary>
/// 冻结原因类型
/// 对应文档：步骤2.3 中的冻结区机制
/// </summary>
public enum FrozenReasonType
{
    /// <summary>
    /// MES 已下发
    /// 任务已下发到 MES，不可重新排产
    /// </summary>
    MES_DISPATCHED = 1,

    /// <summary>
    /// 手工锁定
    /// 计划员手工锁定的任务
    /// </summary>
    MANUAL_LOCK = 2,

    /// <summary>
    /// 约束固定
    /// 由于硬约束（如模具、关键资源）固定的任务
    /// </summary>
    CONSTRAINT_FIXED = 3,

    /// <summary>
    /// 在执行中
    /// 任务已开工，正在执行
    /// </summary>
    IN_EXECUTION = 4,

    /// <summary>
    /// 客户承诺
    /// 已向客户承诺的交期，不可变更
    /// </summary>
    CUSTOMER_COMMITMENT = 5
}
