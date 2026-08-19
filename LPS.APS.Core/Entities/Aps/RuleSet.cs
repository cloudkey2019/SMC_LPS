namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 规则集主表
/// 对应 APS_Production.RuleSet（冻结 DDL v5.1.2 §3.10.1）
/// </summary>
public class RuleSet
{
    public long Id { get; set; }
    public string RuleSetCode { get; set; } = string.Empty;
    public string RuleSetName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
