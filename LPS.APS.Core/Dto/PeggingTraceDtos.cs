namespace LPS.APS.Core.Dto;

/// <summary>
/// Pegging供需追溯DTO（5号位提供给4号位）
///
/// 数据来源：PeggingSupplyAllocation表（2号位计算结果）
/// 5号位只读取展示，不重算Allocation
/// </summary>
public sealed class PeggingTraceDto
{
    public long Id { get; init; }
    public int PlanVersionId { get; init; }
    public int ScheduleRunId { get; init; }
    public long AllocationSequence { get; init; }

    // 需求侧
    public long? RootOrderId { get; init; }
    public string? RootOrderNo { get; init; }
    public long? CurrentOrderId { get; init; }
    public string? CurrentOrderNo { get; init; }
    public string? OrderType { get; init; }
    public string MaterialCode { get; init; } = string.Empty;
    public int MaterialId { get; init; }
    public string? DemandFactoryCode { get; init; }
    public string? DemandStageCode { get; init; }
    public decimal? DemandQty { get; init; }

    // 分配结果
    public decimal AllocatedQty { get; init; }

    // 供给侧
    public string SupplyType { get; init; } = string.Empty;
    public string? SupplyFactoryCode { get; init; }
    public string? SupplyWarehouseCode { get; init; }
    public string? ERPProperty { get; init; }
    public string? SupplyDocumentType { get; init; }
    public string? SupplyDocumentNo { get; init; }
    public string? SupplyMode { get; init; }
    public int? CrossFactoryEdgeId { get; init; }
    public int? TransportLeadTimeHours { get; init; }

    // 时间
    public DateTime? ETA { get; init; }
    public DateTime? KnownAvailableTime { get; init; }
    public string? CommitmentStatus { get; init; }

    // Stage路径
    public string? AttachStageCode { get; init; }
    public string? CompletedStageCode { get; init; }
    public string? NextRequiredStageCode { get; init; }

    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Pegging Trace查询汇总DTO
/// </summary>
public sealed class PeggingTraceSummaryDto
{
    /// <summary>PlanVersionId</summary>
    public int PlanVersionId { get; init; }

    /// <summary>总分配数</summary>
    public int TotalAllocations { get; init; }

    /// <summary>按SupplyType分组统计</summary>
    public List<SupplyTypeCountDto> SupplyTypeCounts { get; init; } = new();

    /// <summary>按CommitmentStatus分组统计</summary>
    public List<CommitmentStatusCountDto> CommitmentStatusCounts { get; init; } = new();

    /// <summary>分配列表</summary>
    public List<PeggingTraceDto> Items { get; init; } = new();
}

/// <summary>
/// SupplyType统计
/// </summary>
public sealed class SupplyTypeCountDto
{
    public string SupplyType { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal TotalAllocatedQty { get; init; }
}

/// <summary>
/// CommitmentStatus统计
/// </summary>
public sealed class CommitmentStatusCountDto
{
    public string CommitmentStatus { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal TotalAllocatedQty { get; init; }
}
