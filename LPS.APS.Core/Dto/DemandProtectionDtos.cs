namespace LPS.APS.Core.Dto;

/// <summary>
/// Demand Protection查看DTO（5号位提供给4号位）
///
/// 数据来源：DemandSupplyHardLock表（LockType='DEMAND_PROTECTION'）
/// 5号位只读取展示，释放操作必须通过2号位Application Service
/// </summary>
public sealed class DemandProtectionDto
{
    public long Id { get; init; }

    /// <summary>锁定类型：STRICT_BINDING / DEMAND_PROTECTION</summary>
    public string LockType { get; init; } = string.Empty;

    /// <summary>需求类型</summary>
    public string DemandType { get; init; } = string.Empty;

    /// <summary>需求键</summary>
    public string DemandKey { get; init; } = string.Empty;

    /// <summary>供应类型</summary>
    public string SupplyType { get; init; } = string.Empty;

    /// <summary>供应键</summary>
    public string SupplyKey { get; init; } = string.Empty;

    /// <summary>锁定数量</summary>
    public decimal LockedQty { get; init; }

    /// <summary>来源PlanVersionId</summary>
    public int? SourcePlanVersionId { get; init; }

    /// <summary>来源分配序列</summary>
    public long? SourceAllocationSequence { get; init; }

    /// <summary>状态：ACTIVE / RELEASED / BROKEN</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>创建人</summary>
    public string? CreatedBy { get; init; }

    /// <summary>释放时间</summary>
    public DateTime? ReleasedAt { get; init; }

    /// <summary>释放人</summary>
    public string? ReleasedBy { get; init; }

    /// <summary>释放原因</summary>
    public string? ReleaseReason { get; init; }

    /// <summary>是否允许人工释放（ACTIVE且非STRICT_BINDING）</summary>
    public bool CanRelease => Status == "ACTIVE" && LockType == "DEMAND_PROTECTION";
}

/// <summary>
/// Demand Protection查询汇总DTO
/// </summary>
public sealed class DemandProtectionSummaryDto
{
    /// <summary>总记录数</summary>
    public int TotalCount { get; init; }

    /// <summary>ACTIVE数量</summary>
    public int ActiveCount { get; init; }

    /// <summary>RELEASED数量</summary>
    public int ReleasedCount { get; init; }

    /// <summary>BROKEN数量</summary>
    public int BrokenCount { get; init; }

    /// <summary>总锁定数量</summary>
    public decimal TotalLockedQty { get; init; }

    /// <summary>记录列表</summary>
    public List<DemandProtectionDto> Items { get; init; } = new();
}
