namespace LPS.APS.Application.Services.Query.Dto;

/// <summary>
/// 计划版本摘要（用于前端版本下拉选择）
/// </summary>
public class PlanVersionSummaryDto
{
    public int Id { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public string VersionCategory { get; set; } = string.Empty;
    public DateTime PlanHorizonStart { get; set; }
    public DateTime PlanHorizonEnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ComputedAt { get; set; }
    public int? TotalTasks { get; set; }
    public DateTime CreatedAt { get; set; }
}
