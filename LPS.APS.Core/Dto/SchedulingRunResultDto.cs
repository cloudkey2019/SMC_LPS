namespace LPS.APS.Core.Dto;

/// <summary>
/// 排程执行结果摘要
/// </summary>
public class SchedulingRunResult
{
    /// <summary>计划版本ID</summary>
    public int PlanVersionId { get; set; }

    /// <summary>计划版本编码</summary>
    public string VersionCode { get; set; } = string.Empty;

    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>已排程任务数</summary>
    public int ScheduledCount { get; set; }

    /// <summary>未排程任务数</summary>
    public int UnscheduledCount { get; set; }

    /// <summary>排程耗时（毫秒）</summary>
    public long ElapsedMs { get; set; }

    /// <summary>错误信息（成功时为null）</summary>
    public string? ErrorMessage { get; set; }
}
