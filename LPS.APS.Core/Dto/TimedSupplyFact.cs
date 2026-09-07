namespace LPS.APS.Core.Dto;

/// <summary>
/// Timed Supply标准事实DTO（5号位→2号位冻结接口）
/// 用于表达采购/VMI/到厂未入库等时间相关的供给源
/// 字段严格遵循2↔5接口冻结标准，不随意扩张
/// </summary>
public sealed class TimedSupplyFact
{
    /// <summary>
    /// 供给类型（冻结值域：PURCHASE_IN_TRANSIT / OPEN_PO_REMAINING / ARRIVED_NOT_RECEIVED / VMI_ONSITE 等）
    /// </summary>
    public string SupplyType { get; init; } = string.Empty;

    /// <summary>
    /// 物理来源键（PO号/VMI仓库号/Transit单号等，用于去重与追溯）
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
    /// 仓库编码
    /// </summary>
    public string WarehouseCode { get; init; } = string.Empty;

    /// <summary>
    /// 剩余可用数量
    /// </summary>
    public decimal RemainingQty { get; init; }

    /// <summary>
    /// ETA（ERP原始预计到达时间，不是Effective ETA）
    ///
    /// 【2026-08-26新基线】5号位输出ERP原始ETA，可能为null
    /// Effective ETA计算（ManualEta ?? ErpEta ?? ReleaseDate+DefaultLT）由2号位负责
    /// </summary>
    public DateTime? Eta { get; init; }

    /// <summary>
    /// PO发行/下发日期（用于ETA兜底计算：ReleaseDate + DefaultLT）
    ///
    /// 【2026-08-26新基线】冻结接口必需字段，供2号位F15兜底逻辑使用
    /// 参考：复审报告P0-02
    /// </summary>
    public DateTime? ReleaseDate { get; init; }

    /// <summary>
    /// 排程可用时间（由2号位计算Effective ETA + ArrivalToUsableOffset后填充）
    ///
    /// 【2026-08-26新基线】5号位输出阶段为null，由2号位计算后使用
    /// 参考：复审报告P1-03
    /// </summary>
    public DateTime? AvailableTime { get; init; }

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
    /// 来源数据更新时间（用于数据新鲜度判断）
    /// </summary>
    public DateTime SourceUpdatedAt { get; init; }
}
