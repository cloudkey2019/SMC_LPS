namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 参数集主表
/// 对应 APS_Production.ParameterSet（冻结 DDL v5.1.2 §3.10.3）
/// </summary>
public class ParameterSet
{
    public long Id { get; set; }
    public string ParameterSetCode { get; set; } = string.Empty;
    public string ParameterSetName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
