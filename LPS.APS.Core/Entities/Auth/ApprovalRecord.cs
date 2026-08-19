namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 审批记录表
/// 对应 APS_Auth.ApprovalRecord
/// </summary>
public class ApprovalRecord
{
    public long Id { get; set; }
    public int FlowId { get; set; }
    public string BusinessType { get; set; } = string.Empty;
    public string BusinessId { get; set; } = string.Empty;
    public int CurrentNodeId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int InitiatorUserId { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Remarks { get; set; }
}
