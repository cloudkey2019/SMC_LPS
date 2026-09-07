namespace LPS.APS.Core.Dto;

/// <summary>
/// 强事实（Received等有明确单据支撑的事实）
/// </summary>
public sealed class ReceivedFact
{
    /// <summary>
    /// 单据号
    /// </summary>
    public string DocumentNo { get; init; } = string.Empty;

    /// <summary>
    /// 单据类型
    /// </summary>
    public string DocumentType { get; init; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// 到货时间
    /// </summary>
    public DateTime ReceivedAt { get; init; }

    /// <summary>
    /// 仓库代码
    /// </summary>
    public string WarehouseCode { get; init; } = string.Empty;

    /// <summary>
    /// 关联Stage（如果该Received证明已越过某个Stage）
    /// </summary>
    public string? RelatedStageCode { get; init; }
}
