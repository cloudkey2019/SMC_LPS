namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 需求-供给硬锁定实体（V1.2新增，§8）
///
/// 支持两种Lock类型：
/// 1. STRICT_BINDING - 严格绑定：1对1强绑定，其他需求完全不可用
/// 2. DEMAND_PROTECTION - 需求保护：1对N保护，保护组内需求可用，组外不可用
///
/// Execution Lock不持久化到此表，仅在运行时内存中校验
/// </summary>
public class DemandSupplyHardLock
{
    /// <summary>
    /// 主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Lock类型：STRICT_BINDING | DEMAND_PROTECTION
    /// </summary>
    public string LockType { get; set; } = string.Empty;

    /// <summary>
    /// 需求类型（ORDER | WORKSET | PI_STAGE_DEMAND）
    /// </summary>
    public string DemandType { get; set; } = string.Empty;

    /// <summary>
    /// 需求唯一键（如：ORDER_12345_MAT001_F01）
    /// </summary>
    public string DemandKey { get; set; } = string.Empty;

    /// <summary>
    /// 供给类型（INVENTORY | WIP | PIPELINE | PI | PURCHASE_ORDER | VMI）
    /// </summary>
    public string SupplyType { get; set; } = string.Empty;

    /// <summary>
    /// 供给唯一键（如：INVENTORY_MAT001_F01_WH01）
    /// </summary>
    public string SupplyKey { get; set; } = string.Empty;

    /// <summary>
    /// 锁定数量
    /// </summary>
    public decimal LockedQty { get; set; }

    /// <summary>
    /// 来源计划版本ID（记录Lock首次建立的PlanVersion）
    /// </summary>
    public int? SourcePlanVersionId { get; set; }

    /// <summary>
    /// 来源分配序号（记录Lock首次建立的AllocationSequence）
    /// </summary>
    public long? SourceAllocationSequence { get; set; }

    /// <summary>
    /// Lock状态：ACTIVE | RELEASED | BROKEN
    /// </summary>
    public string Status { get; set; } = "ACTIVE";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 创建人
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 释放时间
    /// </summary>
    public DateTime? ReleasedAt { get; set; }

    /// <summary>
    /// 释放人
    /// </summary>
    public string? ReleasedBy { get; set; }

    /// <summary>
    /// 释放原因
    /// </summary>
    public string? ReleaseReason { get; set; }
}
