namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 审批规则表
/// 对应 APS_Auth.ApprovalRule
/// </summary>
public class ApprovalRule
{
    public int Id { get; set; }
    public int FlowId { get; set; }
    public string RuleType { get; set; } = string.Empty;
    public string RuleCondition { get; set; } = string.Empty;
    public int TargetNodeId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
