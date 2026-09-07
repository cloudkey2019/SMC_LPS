namespace LPS.APS.Application.Services.Query.Dto;

/// <summary>
/// 计划版本摘要（用于前端版本下拉选择）
/// </summary>
public class PlanVersionSummaryDto
{
    public int Id { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public string VersionCategory { get; set; } = string.Empty;
    public string? DomainKey { get; set; }
    public DateTime PlanHorizonStart { get; set; }
    public DateTime PlanHorizonEnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ComputedAt { get; set; }
    public int? TotalTasks { get; set; }
    public int? SourceScheduleRunId { get; set; }
    public DateTime? ActivatedAt { get; set; }
    /// <summary>失败原因（G9 schema-gap：PlanVersion 表无 ErrorMessage 列，须 2号位 DDL 变更后填充；当前恒为 null，见 v1.1 规格 §五 G9）</summary>
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 基线版本号指向（v1.2 §四 G9；U13 确认可追溯）
    /// 取值规则：仅 CANDIDATE/ARCHIVED 可能有值；ACTIVE/BUILDING/FAILED 必为 null
    /// 推算逻辑：同 DomainKey、Status=ARCHIVED 且 ActivatedAt ≤ 当前版本 CreatedAt 的最新一条
    /// </summary>
    public int? BasePlanVersionId { get; set; }
}
