namespace LPS.APS.Core.Enum;

/// <summary>
/// 供应来源类型（定稿口径，与 PeggingSupplyAllocation.SupplyType 字段对应）
///
/// 优先级顺序（跨厂 INTER_FACTORY_ORDER 场景）：
///   PIPELINE → PRODUCTION_INSTRUCTION（须匹配出荷指示号）→ NEW_REQUIREMENT（进入排产）
///
/// 注意：
///   ZP/BP Received 对应 PRODUCTION_INSTRUCTION，不可作为通用库存使用。
///   仅当 DocumentNo = 当前出荷指示号 且 DocumentType = SHIPPING_INSTRUCTION 且未完成时方可计入。
/// </summary>
public enum SupplySourceType
{
    /// <summary>
    /// 在库库存（ERP 仓库余量）
    /// </summary>
    INVENTORY = 1,

    /// <summary>
    /// 在制品（MES 在制，已投料但未入库）
    /// </summary>
    WIP = 2,

    /// <summary>
    /// 在途（已从上游工厂发出，尚未到货）
    /// INTER_FACTORY_ORDER 场景下第一优先级
    /// </summary>
    PIPELINE = 3,

    /// <summary>
    /// 跨厂订单供给（含 STAGE_HANDOFF 和 INTER_FACTORY_ORDER 两种子模式）
    /// </summary>
    INTER_FACTORY_ORDER = 4,

    /// <summary>
    /// 生产指示（ZP/BP Received）
    /// 仅匹配当前出荷指示号时有效，不可通用
    /// </summary>
    PRODUCTION_INSTRUCTION = 5,

    /// <summary>
    /// 采购订单（外部采购，含外协）
    /// </summary>
    PURCHASE_ORDER = 6,

    /// <summary>
    /// 新增生产需求（无现有供给，触发排产生成 Task）
    /// 此类型不写入 PeggingSupplyAllocation，而是进入物理 Pegging 表（Task-to-Task 血缘）
    /// </summary>
    NEW_REQUIREMENT = 7,

    /// <summary>
    /// 规划性采购占位（Planning-only Purchase Placeholder）
    /// 无任何正式供给时的缺口占位，仅内存，不生成采购单/Task
    /// 标记 ESTIMATED + NOT_COMMITTED，不可作为 CTP 承诺
    /// </summary>
    PLANNING_PURCHASE_PLACEHOLDER = 8
}
