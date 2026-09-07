namespace LPS.APS.Core.Dto;

/// <summary>
/// 订单查询列表DTO
/// </summary>
public sealed class OrderListItemDto
{
    public long Id { get; init; }
    public int PlanVersionId { get; init; }
    public string OrderNo { get; init; } = string.Empty;
    public string OrderType { get; init; } = string.Empty;
    public string MaterialCode { get; init; } = string.Empty;
    public int MaterialId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerSegment { get; init; }
    public string? FactoryCode { get; init; }
    public int FactoryId { get; init; }
    public string? ProductFamilyCode { get; init; }
    public string? DomainKey { get; init; }
    public decimal Quantity { get; init; }
    public string UOM { get; init; } = string.Empty;
    public DateTime CustomerDueDate { get; init; }
    public DateTime? PromisedDate { get; init; }
    public int Priority { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? DelayStatus { get; init; }
    public string? DemandMaturityStatus { get; init; }
    public string? MTS_InstructionNo { get; init; }
    public string? BOMNO { get; init; }
}

/// <summary>
/// 订单详情DTO（含Pegging和生产计划）
/// </summary>
public sealed class OrderDetailDto
{
    /// <summary>订单基本信息</summary>
    public OrderListItemDto Order { get; init; } = new();

    /// <summary>Pegging承接列表</summary>
    public List<OrderPeggingDto> Pegging { get; init; } = new();

    /// <summary>生产计划（FinalTask）列表</summary>
    public List<OrderTaskDto> Tasks { get; init; } = new();
}

/// <summary>
/// 订单Pegging承接DTO
/// </summary>
public sealed class OrderPeggingDto
{
    public long AllocationSequence { get; init; }
    public string MaterialCode { get; init; } = string.Empty;
    public decimal AllocatedQty { get; init; }
    public string SupplyType { get; init; } = string.Empty;
    public string? SupplyFactoryCode { get; init; }
    public string? SupplyWarehouseCode { get; init; }
    public string? ERPProperty { get; init; }
    public string? SupplyDocumentNo { get; init; }
    public string? SupplyDocumentType { get; init; }
    public DateTime? ETA { get; init; }
    public DateTime? KnownAvailableTime { get; init; }
    public string? CommitmentStatus { get; init; }
    public string? SupplyMode { get; init; }
}

/// <summary>
/// 订单生产计划（FinalTask）DTO
/// </summary>
public sealed class OrderTaskDto
{
    public long TaskId { get; init; }
    public string TaskNo { get; init; } = string.Empty;
    public string? OperationCode { get; init; }
    public int OperationSeq { get; init; }
    public string? ResourceCode { get; init; }
    public string? ResourceName { get; init; }
    public decimal Quantity { get; init; }
    public decimal? PlannedProcessQty { get; init; }
    public DateTime? PlannedStartTime { get; init; }
    public DateTime? PlannedEndTime { get; init; }
    public decimal? Duration { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsCriticalPath { get; init; }
    public bool IsLocked { get; init; }
    public string? MTS_InstructionNo { get; init; }
}
