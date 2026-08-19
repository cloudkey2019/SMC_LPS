namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 审批流程表
/// 对应 APS_Auth.ApprovalFlow
/// </summary>
public class ApprovalFlow
{
    public int Id { get; set; }
    public string FlowCode { get; set; } = string.Empty;
    public string FlowName { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
