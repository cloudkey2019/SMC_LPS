namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 审计日志表（DDL v1.3 统一审计：收敛治理/Candidate/运行/RBAC 审计）
/// 对应 APS_Auth.AuditLog
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserCode { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string? Module { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? VersionCode { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Result { get; set; } = "Success";
    public DateTime OccurredAt { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? Remark { get; set; }
    public int? PlanVersionId { get; set; }
    public string? BatchNo { get; set; }
    public int? ApprovalId { get; set; }
    public string? RequestData { get; set; }
    public string? ResponseData { get; set; }
    public string? ErrorMessage { get; set; }
}