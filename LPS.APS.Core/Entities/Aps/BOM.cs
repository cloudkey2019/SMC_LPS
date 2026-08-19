namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// BOM表
/// 对应 APS_Production.BOM
/// </summary>
public class BOM
{
    public long Id { get; set; }
    public int ParentMaterialId { get; set; }
    public int ChildMaterialId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ScrapRate { get; set; }
    public int LeadTimeOffset { get; set; }
    public int BOMLevel { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
