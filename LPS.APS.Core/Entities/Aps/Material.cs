namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 物料主数据表
/// 对应 APS_Production.Material
/// </summary>
public class Material
{
    public int Id { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public int? ProductFamilyId { get; set; }
    public string MaterialType { get; set; } = string.Empty;
    public string UOM { get; set; } = string.Empty;
    public int LeadTimeDays { get; set; }
    public decimal SafetyStock { get; set; }
    public int? LowLevelCode { get; set; }
    public bool IsPurchased { get; set; }
    public bool IsSimpleItem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
