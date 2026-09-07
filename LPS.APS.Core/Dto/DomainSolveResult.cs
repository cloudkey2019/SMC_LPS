namespace LPS.APS.Core.Dto;

/// <summary>
/// 1号位排程结果（纯内存，不含正式 TaskId）。
/// 2号位收到后在统一事务中将 FinalTaskDraft 实例化为正式 [Task]。
/// </summary>
public sealed class DomainSolveResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsRoughCut { get; init; }

    public IReadOnlyList<FinalTaskDraft> FinalTasks { get; init; }
        = Array.Empty<FinalTaskDraft>();

    public IReadOnlyList<AllocationTaskShare> AllocationShares { get; init; }
        = Array.Empty<AllocationTaskShare>();

    public IReadOnlyList<UnscheduledTaskResult> UnscheduledTasks { get; init; }
        = Array.Empty<UnscheduledTaskResult>();

    /// <summary>Task-to-Task 血缘，使用 FinalDraftId 键，由1号位在排程后返回</summary>
    public IReadOnlyList<FinalTaskPeggingDraft> PhysicalPeggingDrafts { get; init; }
        = Array.Empty<FinalTaskPeggingDraft>();

    /// <summary>排程决策解释事实（材料何时到、设备负荷、为何延期等）</summary>
    public IReadOnlyList<ScheduleExplanationFact> ExplanationFacts { get; init; }
        = Array.Empty<ScheduleExplanationFact>();

    public SolveSummary Summary { get; init; } = new();
}

/// <summary>
/// 1号位排定后的内存草稿（含资源、实际时间、合并拆分后数量）。
/// 仍不是数据库正式 Task。
/// </summary>
public sealed class FinalTaskDraft
{
    public string FinalDraftId { get; init; } = Guid.NewGuid().ToString();
    public string SourceDraftId { get; init; } = string.Empty;
    public int MaterialId { get; init; }
    public int FactoryId { get; init; }
    public string StageCode { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public string TaskType { get; init; } = "NEW_REQUIREMENT";
    public int ResourceId { get; init; }
    public string ResourceCode { get; init; } = string.Empty;
    public string? RouteCode { get; init; }
    public long? PathId { get; init; }
    public decimal Quantity { get; init; }
    public decimal PlannedProcessQty { get; init; }
    public string UOM { get; init; } = string.Empty;
    public DateTime PlannedStartTime { get; init; }
    public DateTime PlannedEndTime { get; init; }
    public decimal SetupTime { get; init; }
    public int Priority { get; init; }
    public bool IsVirtual { get; init; }
    public string? StageExecutionBatchDraftKey { get; init; }
    public decimal? StageExecutionBatchQty { get; init; }
    public long? ExistingMESPlanReleaseId { get; init; }
    public long? ExecutionLockId { get; init; }
}

/// <summary>
/// Task-to-Task 物理血缘草稿，使用 FinalDraftId 键（C1修复：替代基于原始DraftId的PhysicalPeggingDraft）
/// P0-13修复：补充DependencyType和LagTime语义
/// </summary>
public sealed class FinalTaskPeggingDraft
{
    public string UpstreamFinalDraftId { get; init; } = string.Empty;
    public string DownstreamFinalDraftId { get; init; } = string.Empty;
    public int UpstreamMaterialId { get; init; }
    public int DownstreamMaterialId { get; init; }
    public decimal Quantity { get; init; }
    public string UOM { get; init; } = string.Empty;
    public int InheritedPriority { get; init; }

    /// <summary>
    /// 依赖类型：ES=结束-开始（默认）, SS=开始-开始, FF=结束-结束
    /// V1先只实现ES
    /// </summary>
    public string DependencyType { get; init; } = "ES";

    /// <summary>
    /// 延迟时间（分钟，0=紧跟前驱完成）
    /// </summary>
    public decimal LagTime { get; init; }
}

/// <summary>
/// AllocationSequence 在最终 Task 中的数量份额（供2号位写物理 Pegging 用）
/// </summary>
public sealed class AllocationTaskShare
{
    public string FinalDraftId { get; init; } = string.Empty;
    public long AllocationSequence { get; init; }
    public decimal ComponentQty { get; init; }
}

public sealed class UnscheduledTaskResult
{
    public string DraftId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// 排程决策解释事实（材料何时到、设备负荷、为何延期等）
/// 1号位必须返回真正原因，不能只给时间结果
/// </summary>
public sealed class ScheduleExplanationFact
{
    public string FinalDraftId { get; init; } = string.Empty;
    public string ObjectType { get; init; } = string.Empty;
    public long? OrderId { get; init; }
    public int? ResourceId { get; init; }
    public string? StageCode { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string? Severity { get; init; }
    public decimal? ImpactHours { get; init; }
    public string? EvidenceJson { get; init; }
}

public sealed class SolveSummary
{
    public int TotalDrafts { get; init; }
    public int ScheduledCount { get; init; }
    public int UnscheduledCount { get; init; }
    public long ElapsedMs { get; init; }
    public int IssueCount { get; init; }
    public bool UsedRoughCut { get; init; }
}
