namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 审计日志表
/// 对应 APS_Auth.AuditLog
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? UserCode { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
