namespace LPS.APS.Core.Dto;

/// <summary>
/// XC（线边仓）事实
/// </summary>
public sealed class XcFact
{
    /// <summary>
    /// XC仓库代码
    /// </summary>
    public string XcWarehouseCode { get; init; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// 关联的Stage代码（XC通常关联某个Stage）
    /// </summary>
    public string? RelatedStageCode { get; init; }

    /// <summary>
    /// 可用时间
    /// </summary>
    public DateTime? AvailableTime { get; init; }

    /// <summary>
    /// 来源单据
    /// </summary>
    public string? SourceDocument { get; init; }
}
