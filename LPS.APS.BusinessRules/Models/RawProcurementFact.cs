namespace LPS.APS.BusinessRules.Models;

/// <summary>
/// 原始采购事实（Calculator内部输入模型）
/// 包含ETA优先级计算所需的全部原始字段
/// 不暴露到2↔5接口，仅用于5号位内部事实标准化计算
/// </summary>
public sealed class RawProcurementFact
{
    /// <summary>
    /// 供给类型（PURCHASE_IN_TRANSIT / OPEN_PO_REMAINING / ARRIVED_NOT_RECEIVED / VMI_ONSITE等）
    /// </summary>
    public string SupplyType { get; init; } = string.Empty;

    /// <summary>
    /// 物理来源键（PO号/VMI仓库号等）
    /// </summary>
    public string PhysicalSourceKey { get; init; } = string.Empty;

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 物料编码
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 接收工厂ID
    /// </summary>
    public int FactoryId { get; init; }

    /// <summary>
    /// 工厂编码
    /// </summary>
    public string FactoryCode { get; init; } = string.Empty;

    /// <summary>
    /// 仓库编码（对应ODS的StorageCode）
    /// </summary>
    public string StorageCode { get; init; } = string.Empty;

    /// <summary>
    /// 剩余数量
    /// </summary>
    public decimal RemainingQty { get; init; }

    /// <summary>
    /// 人工ETA（最高优先级，F13）
    /// APS人工维护，不要求ODS提供
    /// null表示未设置或已取消（V1简化方案）
    /// </summary>
    public DateTime? ManualEta { get; init; }

    /// <summary>
    /// ERP ETA（次优先级，F13）
    /// 对应ODS的ETA字段（ERP原始预计到达时间）
    /// </summary>
    public DateTime? Eta { get; init; }

    /// <summary>
    /// PO Release/Issue Date（F15基准日期）
    /// 用于计算默认ETA = ReleaseDate + DefaultPurchaseLt
    /// </summary>
    public DateTime? ReleaseDate { get; init; }

    /// <summary>
    /// 承诺状态（V1表示正式供应事实承诺状态）
    /// </summary>
    public string CommitmentStatus { get; init; } = string.Empty;

    /// <summary>
    /// 可信度
    /// </summary>
    public string Confidence { get; init; } = string.Empty;

    /// <summary>
    /// 来源单据号
    /// </summary>
    public string SourceDocumentNo { get; init; } = string.Empty;

    /// <summary>
    /// 来源单据行号
    /// </summary>
    public string SourceDocumentLineNo { get; init; } = string.Empty;

    /// <summary>
    /// 来源数据更新时间
    /// </summary>
    public DateTime SourceUpdatedAt { get; init; }
}
