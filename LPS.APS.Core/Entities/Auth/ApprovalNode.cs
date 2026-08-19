namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 审批节点表
/// 对应 APS_Auth.ApprovalNode
/// </summary>
public class ApprovalNode
{
    public int Id { get; set; }
    public int FlowId { get; set; }
    public int NodeSeq { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public int? ApproverRoleId { get; set; }
    public int? ApproverUserId { get; set; }
    public bool IsParallel { get; set; }
    public bool IsOptional { get; set; }
    public int TimeoutHours { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
