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

/// <summary>
/// Demand Protection释放请求
/// </summary>
public sealed class DemandProtectionReleaseRequest
{
    /// <summary>要释放的Lock ID列表</summary>
    public List<long> LockIds { get; init; } = new();

    /// <summary>操作人</summary>
    public string ReleasedBy { get; init; } = string.Empty;

    /// <summary>释放原因（必填）</summary>
    public string ReleaseReason { get; init; } = string.Empty;
}

/// <summary>
/// Demand Protection 释放结果（逐 Lock 返回，与请求 lockIds 一一对应）
///
/// 每个 lockId 独立返回一条状态，便于前端逐条提示。
/// Status：RELEASED=已释放 / FAILED=未释放（FailureReason 说明原因）。
/// </summary>
public sealed class DemandProtectionReleaseResult
{
    /// <summary>锁定 Id</summary>
    public long LockId { get; init; }

    /// <summary>需求标识（Lock 不存在时为空）</summary>
    public string DemandKey { get; init; } = string.Empty;

    /// <summary>状态：RELEASED / FAILED</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>失败原因（仅 Status=FAILED 时非空）</summary>
    public string? FailureReason { get; init; }
}
