namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 工厂表
/// 对应 APS_Production.Factory
/// </summary>
public class Factory
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string TimeZone { get; set; } = "China Standard Time";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
