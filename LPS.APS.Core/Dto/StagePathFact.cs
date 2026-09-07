namespace LPS.APS.Core.Dto;

/// <summary>
/// Stage路径事实（定义PI的加工路径）
/// </summary>
public sealed class StagePathFact
{
    /// <summary>
    /// Stage代码
    /// </summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>
    /// Stage序号（定义路径顺序）
    /// </summary>
    public int StageSequence { get; init; }

    /// <summary>
    /// 是否为起始Stage
    /// </summary>
    public bool IsStartStage { get; init; }

    /// <summary>
    /// 是否为结束Stage
    /// </summary>
    public bool IsEndStage { get; init; }
}
