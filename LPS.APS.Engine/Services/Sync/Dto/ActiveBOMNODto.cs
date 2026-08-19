namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_GetActiveRootBOMNOs 返回的结果行映射
/// </summary>
public class ActiveBOMNODto
{
    public string BOMNO { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public DateTime EarliestDueDate { get; set; }
    public DateTime LatestDueDate { get; set; }
}
