namespace LPS.APS.Core.Dto;

/// <summary>
/// 供应事实原始追溯DTO（5号位提供给4号位）
///
/// 用于页面查看Procurement/VMI/Received/Transit原始供应事实
/// 来源：SupplyFact_Pipeline（采购/Transit）+ ext_ERP_Received_ByDocument_View（Received）
/// </summary>
public sealed class SupplyFactTraceDto
{
    /// <summary>
    /// 来源类型：SUPPLY_PIPELINE / RECEIVED
    /// </summary>
    public string SourceType { get; init; } = string.Empty;

    /// <summary>
    /// 供应类型（PURCHASE_IN_TRANSIT / OPEN_PO_REMAINING / VMI_ONSITE / ARRIVED_NOT_RECEIVED / INTERPLANT_IN_TRANSIT / SHIPPING_INSTRUCTION / PRODUCTION_INSTRUCTION）
    /// </summary>
    public string SupplyType { get; init; } = string.Empty;

    /// <summary>
    /// 物料编码
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 工厂编码
    /// </summary>
    public string FactoryCode { get; init; } = string.Empty;

    /// <summary>
    /// 仓库编码
    /// </summary>
    public string? WarehouseCode { get; init; }

    /// <summary>
    /// 数量
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// ERP原始ETA
    /// </summary>
    public DateTime? Eta { get; init; }

    /// <summary>
    /// PO发行日期
    /// </summary>
    public DateTime? ReleaseDate { get; init; }

    /// <summary>
    /// APS统一可用时间
    /// </summary>
    public DateTime? AvailableTime { get; init; }

    /// <summary>
    /// 承诺状态
    /// </summary>
    public string? CommitmentStatus { get; init; }

    /// <summary>
    /// 源单据号
    /// </summary>
    public string? SourceDocumentNo { get; init; }

    /// <summary>
    /// 源单据行号
    /// </summary>
    public string? SourceDocumentLineNo { get; init; }

    /// <summary>
    /// 来源系统
    /// </summary>
    public string? SourceSystem { get; init; }

    /// <summary>
    /// 源更新时间
    /// </summary>
    public DateTime? SourceUpdatedAt { get; init; }

    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// 同步时间
    /// </summary>
    public DateTime? SyncedAt { get; init; }

    /// <summary>
    /// Received特有：单据类型（SHIPPING_INSTRUCTION / PRODUCTION_INSTRUCTION）
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Received特有：最后收货时间
    /// </summary>
    public DateTime? LastReceivedAt { get; init; }
}
