namespace LPS.APS.Core.Enum;

/// <summary>
/// 跨工厂模式
/// 对应文档：步骤2.8 中的跨工厂供应模式
/// </summary>
public enum CrossFactoryMode
{
    /// <summary>
    /// 工段交接模式
    /// 上游工厂完成某工段后，半成品转移到下游工厂继续加工
    /// 特点：上游工厂冻结，下游工厂可重排
    /// </summary>
    STAGE_HANDOFF = 1,

    /// <summary>
    /// 跨厂订单模式
    /// 各工厂独立接单、独立排产，通过订单关联
    /// 特点：各工厂独立冻结区
    /// </summary>
    INTER_FACTORY_ORDER = 2,

    /// <summary>
    /// 虚拟工厂模式
    /// 多工厂作为一个整体进行排产（共享资源池）
    /// 特点：统一冻结区
    /// </summary>
    VIRTUAL_FACTORY = 3
}
