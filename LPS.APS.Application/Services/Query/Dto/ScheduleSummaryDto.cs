namespace LPS.APS.Application.Services.Query.Dto;

/// <summary>
/// 排程概要 KPI（V1 最小集，供前端顶部面板展示）
/// </summary>
public class ScheduleSummaryDto
{
    public int PlanVersionId { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public int TotalTasks { get; set; }
    public int ScheduledTasks { get; set; }
    public int UnscheduledTasks { get; set; }
    public int DelayedTasks { get; set; }

    public int TotalOrders { get; set; }
    public int DelayedOrders { get; set; }

    public DateTime? FirstTaskStart { get; set; }
    public DateTime? LastTaskEnd { get; set; }

    public int? ComputeDurationSeconds { get; set; }
}
