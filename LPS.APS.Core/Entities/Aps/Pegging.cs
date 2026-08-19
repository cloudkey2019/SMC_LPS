namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// Pegging表（分区表）
/// 对应 APS_Production.Pegging
/// </summary>
public class Pegging
{
    public long Id { get; set; }
    public int PlanVersionId { get; set; }
    public long UpstreamTaskId { get; set; }
    public long DownstreamTaskId { get; set; }
    public int UpstreamMaterialId { get; set; }
    public int DownstreamMaterialId { get; set; }
    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public string PeggingType { get; set; } = string.Empty;
    public int LeadTimeDays { get; set; }
    public bool IsCrossDomain { get; set; }
    public decimal? AllocatedQuantity { get; set; }
    public int? InheritedPriority { get; set; }
    public string? AllocationReason { get; set; }
    public DateTime CreatedAt { get; set; }
}
