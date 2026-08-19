namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 数据范围策略表
/// 对应 APS_Auth.DataScopePolicy
/// </summary>
public class DataScopePolicy
{
    public int Id { get; set; }
    public string PolicyCode { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string? ScopeValue { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
