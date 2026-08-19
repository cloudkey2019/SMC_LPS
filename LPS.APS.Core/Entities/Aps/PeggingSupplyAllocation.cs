namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 非 Task 类型的供应分配记录（v5.1.2）
/// 记录库存、在途、Received等非Task供给的分配结果
/// 对应文档：APS_数据库字段说明文档_v5.1.2 § PeggingSupplyAllocation
/// </summary>
public class PeggingSupplyAllocation
{
    /// <summary>
    /// 主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 计划版本ID
    /// </summary>
    public int PlanVersionId { get; set; }

    /// <summary>
    /// 排程运行ID
    /// </summary>
    public int ScheduleRunId { get; set; }

    /// <summary>
    /// 分配序号（Demand/Supply原子扣减成功时生成）
    /// </summary>
    public long AllocationSequence { get; set; }

    /// <summary>
    /// 数据批次号
    /// </summary>
    public string? BatchNo { get; set; }

    /// <summary>
    /// 根订单ID（顶层需求）
    /// </summary>
    public long? RootOrderId { get; set; }

    /// <summary>
    /// 根订单号
    /// </summary>
    public string? RootOrderNo { get; set; }

    /// <summary>
    /// 当前单据ID（当前生产指示/厂间出荷指示）
    /// </summary>
    public long? CurrentOrderId { get; set; }

    /// <summary>
    /// 当前单据号
    /// </summary>
    public string? CurrentOrderNo { get; set; }

    /// <summary>
    /// 订单类型（SALES_ORDER/PRODUCTION_INSTRUCTION）
    /// </summary>
    public string? OrderType { get; set; }

    /// <summary>
    /// Workset行ID（BOM边）
    /// </summary>
    public long? WorksetId { get; set; }

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; set; }

    /// <summary>
    /// 物料编码
    /// </summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// 需求工厂代码
    /// </summary>
    public string? DemandFactoryCode { get; set; }

    /// <summary>
    /// 需求阶段代码
    /// </summary>
    public string? DemandStageCode { get; set; }

    /// <summary>
    /// 需求数量
    /// </summary>
    public decimal DemandQty { get; set; }

    /// <summary>
    /// 分配数量（本条供给覆盖数量）
    /// </summary>
    public decimal AllocatedQty { get; set; }

    /// <summary>
    /// 供给类型
    /// INVENTORY / PRODUCTION_INSTRUCTION / SHIPPING_INSTRUCTION /
    /// INTERPLANT_IN_TRANSIT / ARRIVED_NOT_RECEIVED / PURCHASE_IN_TRANSIT /
    /// OPEN_PO_REMAINING / VMI_ONSITE
    /// </summary>
    public string SupplyType { get; set; } = string.Empty;

    /// <summary>
    /// 供给所在工厂代码
    /// </summary>
    public string? SupplyFactoryCode { get; set; }

    /// <summary>
    /// 供给仓库代码
    /// </summary>
    public string? SupplyWarehouseCode { get; set; }

    /// <summary>
    /// ERP仓库属性（M/XC/ZP/BP）
    /// </summary>
    public string? ERPProperty { get; set; }

    /// <summary>
    /// 挂接大工艺
    /// </summary>
    public string? AttachStageCode { get; set; }

    /// <summary>
    /// 已完成大工艺
    /// </summary>
    public string? CompletedStageCode { get; set; }

    /// <summary>
    /// 下一步大工艺
    /// </summary>
    public string? NextRequiredStageCode { get; set; }

    /// <summary>
    /// 剩余大工艺路径JSON
    /// </summary>
    public string? RemainingStagePathJson { get; set; }

    /// <summary>
    /// 跨厂供给方式
    /// STAGE_HANDOFF / INTER_FACTORY_ORDER / PURCHASE_IN_TRANSIT / NULL
    /// </summary>
    public string? SupplyMode { get; set; }

    /// <summary>
    /// 跨厂边ID
    /// </summary>
    public long? CrossFactoryEdgeId { get; set; }

    /// <summary>
    /// 运输提前期（小时）
    /// </summary>
    public int? TransportLeadTimeHours { get; set; }

    /// <summary>
    /// 预计到达时间
    /// </summary>
    public DateTime? ETA { get; set; }

    /// <summary>
    /// 本次分配可用时间
    /// </summary>
    public DateTime? KnownAvailableTime { get; set; }

    /// <summary>
    /// 供给承诺状态
    /// COMMITTED / CONFIRMED / ESTIMATED / NOT_COMMITTED
    /// </summary>
    public string? CommitmentStatus { get; set; }

    /// <summary>
    /// 供给单据类型
    /// STOCK / SHIPPING_INSTRUCTION / PURCHASE_ORDER / PRODUCTION_INSTRUCTION / PIPELINE
    /// </summary>
    public string? SupplyDocumentType { get; set; }

    /// <summary>
    /// 供给单据号
    /// </summary>
    public string? SupplyDocumentNo { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
