namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// APS 统一库存余额表（L4：规则筛选后的排程唯一真相）
/// 对应 APS_Production.InventoryBalance（DDL v2.8，2026-03-25）
/// 
/// 五层架构定位：
///   L1 InventoryFact_ERP / InventoryFact_MES（物理主键快照）
///   L2 InventorySupplyCandidate（统一 MaterialCode）
///   L3 ProductFamilyInventoryScope + InventorySourceRule（规则筛选）
///   L4 InventoryBalance ← 本表（排程唯一真相，带产品族上下文）
///   L5 SchedulingContext.InventorySupplies（内存消费层）
/// 
/// 业务键：MaterialCode + ProductFamilyId + FactoryId（UQ_Inventory_Balance）
/// 注意：AvailableQty 是数据库计算列（OnHandQty - AllocatedQty），C# 侧只读
/// </summary>
public class InventoryBalance
{
    public long Id { get; set; }

    /// <summary>物料编码（APS 统一业务键）</summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>产品族ID（v2.8 新增：带产品族上下文，避免跨产品族抢库存）</summary>
    public int ProductFamilyId { get; set; }

    /// <summary>工厂ID</summary>
    public int FactoryId { get; set; }

    /// <summary>现有量（筛选后汇总）</summary>
    public decimal OnHandQty { get; set; }

    /// <summary>已分配量（由 5 号位 Pegging 规则扣减后回写）</summary>
    public decimal AllocatedQty { get; set; }

    /// <summary>可用量（数据库 PERSISTED 计算列，= OnHandQty - AllocatedQty，只读）</summary>
    public decimal AvailableQty { get; set; }

    /// <summary>来源：ERP / MES / BOTH（双源合并后的标记）</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>计划批次号（可空，关联 nightly-batch 的 BatchNo）</summary>
    public string? BatchNo { get; set; }

    public DateTime LastUpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
