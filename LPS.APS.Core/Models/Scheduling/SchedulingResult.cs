namespace LPS.APS.Core.Models.Scheduling;

/// <summary>
/// 排程结果
/// </summary>
public class SchedulingResult
{
    /// <summary>
    /// 是否全部排程成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 已排程任务数
    /// </summary>
    public int ScheduledCount { get; set; }

    /// <summary>
    /// 未排程任务数
    /// </summary>
    public int UnscheduledCount { get; set; }

    /// <summary>
    /// 未排程原因列表
    /// </summary>
    public List<string> UnscheduledReasons { get; set; } = new();

    /// <summary>
    /// 求解耗时
    /// </summary>
    public TimeSpan SolveDuration { get; set; }

    /// <summary>
    /// 总结信息
    /// </summary>
    public string Summary =>
        $"排程完成：成功 {ScheduledCount} 个，未排 {UnscheduledCount} 个，耗时 {SolveDuration.TotalSeconds:F1}s";
}
