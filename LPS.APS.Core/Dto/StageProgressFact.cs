namespace LPS.APS.Core.Dto;

/// <summary>
/// Stage进度事实（2号位提供给5号位的输入数据）
/// </summary>
public sealed class StageProgressFact
{
    /// <summary>
    /// Stage代码
    /// </summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>
    /// 累计完成数量
    /// </summary>
    public decimal CumulativeCompletedQty { get; init; }

    /// <summary>
    /// Stage序号（用于排序）
    /// </summary>
    public int StageSequence { get; init; }

    /// <summary>
    /// 数据来源快照ID（用于追溯）
    /// </summary>
    public long? SnapshotId { get; init; }

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public DateTime? UpdatedAt { get; init; }
}
