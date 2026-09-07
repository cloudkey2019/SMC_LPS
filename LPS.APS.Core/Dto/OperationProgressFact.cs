namespace LPS.APS.Core.Dto;

/// <summary>
/// 工序进度事实（如需要更细粒度的进度数据）
/// </summary>
public sealed class OperationProgressFact
{
    /// <summary>
    /// 工序代码
    /// </summary>
    public string OperationCode { get; init; } = string.Empty;

    /// <summary>
    /// 所属Stage代码
    /// </summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>
    /// 累计完成数量
    /// </summary>
    public decimal CumulativeCompletedQty { get; init; }

    /// <summary>
    /// 工序序号
    /// </summary>
    public int OperationSequence { get; init; }

    /// <summary>
    /// 数据来源快照ID
    /// </summary>
    public long? SnapshotId { get; init; }

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}
