using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 库存候选供给池（L2：首次统一到 MaterialCode）
/// 对应 APS_Production.InventorySupplyCandidate（DDL v2.8）
/// 
/// 架构定位：
///   InventoryFact_ERP/MES（物理主键） 
///     → 通过 MaterialMapping(SourceID→MaterialCode) 统一身份
///       → InventorySupplyCandidate（本表，候选池）
///         → 经 ProductFamilyInventoryScope + InventorySourceRule 筛选
///           → InventoryBalance（L4 排程唯一真相）
/// 
/// 职责：这是库存链路中第一次正式形成统一 MaterialCode 的承接层，
///       保留物理追溯字段（ERP_MasterID / MES_ID）便于回溯到事实层。
/// </summary>
[Table("InventorySupplyCandidate")]
public class InventorySupplyCandidate
{
    public long Id { get; set; }

    /// <summary>物料编码（APS 统一业务键）</summary>
    public string MaterialCode { get; set; } = string.Empty;

    public int FactoryId { get; set; }

    /// <summary>来源系统：ERP / MES</summary>
    public string SourceSystem { get; set; } = string.Empty;

    /// <summary>仓库/库位代码（ERP 的 Warehouse / MES 的 Location 或 Warehouse）</summary>
    public string StorageCode { get; set; } = string.Empty;

    /// <summary>候选库存数量</summary>
    public decimal Quantity { get; set; }

    /// <summary>ERP 来源时有值（物理追溯）</summary>
    public int? ERP_MasterID { get; set; }

    /// <summary>MES 来源时有值（物理追溯）</summary>
    public int? MES_ID { get; set; }

    /// <summary>规则筛选后是否可用</summary>
    public bool IsEligible { get; set; } = true;

    /// <summary>被剔除的原因（IsEligible=false 时填写）</summary>
    public string? RejectReason { get; set; }

    public DateTime SyncedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
