namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 计划版本表
/// 对应 APS_Production.PlanVersion
/// </summary>
public class PlanVersion
{
    public int Id { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public string VersionCategory { get; set; } = string.Empty;
    public string? DomainKey { get; set; }
    public DateTime PlanHorizonStart { get; set; }
    public DateTime PlanHorizonEnd { get; set; }
    public string ComputeMode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ComputedAt { get; set; }
    public int? SourceScheduleRunId { get; set; }
    public int? SourceSimulationRunId { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string? ActivatedBy { get; set; }
    public int? TotalOrders { get; set; }
    public int? TotalTasks { get; set; }
    public string? BatchNo { get; set; }
    public string? SnapshotFilePath { get; set; }
    public long? SnapshotFileSize { get; set; }
    public string? SnapshotFileHash { get; set; }
    public long? SnapshotCompressedSize { get; set; }
    public DateTime? SnapshotCreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
