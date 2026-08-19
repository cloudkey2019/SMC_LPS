namespace LPS.APS.Shared.Models;

/// <summary>
/// 时间窗口值类型（1号位核心数据结构）
/// </summary>
public readonly struct TimeWindow
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }

    public TimeWindow(DateTime start, DateTime end)
    {
        Start = start;
        End = end;
    }

    /// <summary>
    /// 检查两个时间窗口是否重叠
    /// </summary>
    public bool Overlaps(TimeWindow other)
    {
        return Start < other.End && other.Start < End;
    }

    /// <summary>
    /// 获取时间窗口的持续时间
    /// </summary>
    public TimeSpan Duration => End - Start;
}
