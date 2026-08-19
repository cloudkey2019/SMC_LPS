namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 产品族配置表
/// 对应 APS_Production.ProductFamily
/// </summary>
public class ProductFamily
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
