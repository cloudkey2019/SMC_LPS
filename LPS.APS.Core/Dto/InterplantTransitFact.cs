namespace LPS.APS.Core.Dto;

/// <summary>
/// 厂间在途事实
/// </summary>
public sealed class InterplantTransitFact
{
    /// <summary>
    /// 在途单据号（= 生产指示号 PI No，PM 0910 归属键）
    /// </summary>
    public string TransitDocumentNo { get; init; } = string.Empty;

    /// <summary>
    /// 物料代码（PM 0910 一致性校验条件，非归属键）
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 源工厂代码
    /// </summary>
    public string SourceFactoryCode { get; init; } = string.Empty;

    /// <summary>
    /// 目标工厂代码
    /// </summary>
    public string TargetFactoryCode { get; init; } = string.Empty;

    /// <summary>
    /// 在途数量
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// 预计到达时间
    /// </summary>
    public DateTime? EstimatedArrivalTime { get; init; }

    /// <summary>
    /// 来源单据
    /// </summary>
    public string? SourceDocument { get; init; }

    /// <summary>
    /// 发货时间
    /// </summary>
    public DateTime? ShippedAt { get; init; }
}
