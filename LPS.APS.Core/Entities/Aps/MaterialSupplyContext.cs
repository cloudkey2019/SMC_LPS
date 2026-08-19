namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 物料供给与责任上下文表（v2.7 新增 / v4.0 双源同构）
/// 对应 APS_Production.MaterialSupplyContext
///
/// 业务用途：记录物料在不同仓库/工厂下的供给方式、责任归属、计划参数
/// 核心理念：同一物料在不同仓库下，业务语义会变化（采购/自制、生产部门等）
/// 架构定位：承载"仓库级业务上下文"，而非"物料本体属性"
///
/// 数据来源：
///   - sp_SyncMasterData('ERP') - 每日 00:10
///   - sp_SyncMasterData('MES') - 每日 00:20
///
/// 同步机制：SCD Type 2 拉链（禁止全量删除重建）
///   - 供给属性变化 → 关闭旧版本（IsCurrent=0, ValidTo=@SyncTime）+ 插入新版本
///   - 源端消失的仓库 → 关闭对应记录
/// </summary>
public class MaterialSupplyContext
{
    public long Id { get; set; }

    // ─────────── 核心业务键 ───────────

    /// <summary>
    /// 物料编码（统一业务键）
    /// </summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// 仓库编码（关键维度，决定业务上下文）
    /// </summary>
    public string WarehouseCode { get; set; } = string.Empty;

    /// <summary>
    /// 工厂ID（可选，关联 Factory 表）
    /// </summary>
    public int? FactoryId { get; set; }

    // ─────────── 供给方式与责任归属 ───────────

    /// <summary>
    /// 供给方式：PURCHASE（采购）/ MAKE（自制）/ OUTSOURCE（委外）/ MIXED（混合）
    /// </summary>
    public string SupplyMode { get; set; } = string.Empty;

    /// <summary>
    /// 默认生产责任部门编码（SupplyMode=MAKE 时有效）
    /// </summary>
    public string? DefaultProductionDeptCode { get; set; }

    /// <summary>
    /// 默认生产责任部门ID（v5.0.16 FK，指向 ProductionDepartment.Id）
    /// 与 DefaultProductionDeptCode 双轨；sp_SyncMasterData 自动 JOIN 解析
    /// </summary>
    public int? DefaultProductionDepartmentId { get; set; }

    /// <summary>
    /// 采购责任部门编码（SupplyMode=PURCHASE 时有效，由 APS 维护）
    /// </summary>
    public string? ProcurementDeptCode { get; set; }

    /// <summary>
    /// 委外责任部门编码（SupplyMode=OUTSOURCE 时有效）
    /// </summary>
    public string? OutsourceDeptCode { get; set; }

    // ─────────── 计划参数（仓库级） ───────────

    /// <summary>
    /// 该上下文的提前期（天）
    /// ⚠️ 优先级高于 Material.LeadTimeDays（后者 v2.7 已废弃）
    /// </summary>
    public int? LeadTimeDays { get; set; }

    /// <summary>
    /// 该仓安全库存
    /// ⚠️ 优先级高于 Material.SafetyStock（后者 v2.7 已废弃）
    /// </summary>
    public decimal? SafetyStock { get; set; }

    /// <summary>
    /// 库存管理方式：STOCKED（有备货）/ NON_STOCKED（无备货）
    /// v4.0 新增
    /// </summary>
    public string? InventoryManagementMode { get; set; }

    // ─────────── 数据来源与版本控制（SCD Type 2） ───────────

    /// <summary>
    /// 数据来源系统：ERP / MES（v4.0 双源同构，无默认值）
    /// </summary>
    public string SourceSystem { get; set; } = string.Empty;

    /// <summary>
    /// 生效开始时间
    /// </summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>
    /// 生效结束时间（NULL 表示当前有效）
    /// </summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>
    /// 是否当前有效版本（1=当前，0=历史）
    /// </summary>
    public bool IsCurrent { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
