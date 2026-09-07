namespace LPS.APS.Core.Dto;

/// <summary>
/// Overview - 当前ACTIVE计划版本信息
/// </summary>
public sealed class OverviewActivePlanDto
{
    /// <summary>PlanVersionId</summary>
    public int PlanVersionId { get; init; }

    /// <summary>版本号</summary>
    public string VersionCode { get; init; } = string.Empty;

    /// <summary>DomainKey</summary>
    public string? DomainKey { get; init; }

    /// <summary>计划范围开始</summary>
    public DateTime PlanHorizonStart { get; init; }

    /// <summary>计划范围结束</summary>
    public DateTime PlanHorizonEnd { get; init; }

    /// <summary>激活时间</summary>
    public DateTime? ActivatedAt { get; init; }

    /// <summary>来源ScheduleRunId</summary>
    public int? SourceScheduleRunId { get; init; }

    /// <summary>总任务数</summary>
    public int TotalTasks { get; init; }

    /// <summary>总订单数</summary>
    public int TotalOrders { get; init; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Overview - 任务状态摘要
/// </summary>
public sealed class OverviewTaskSummaryDto
{
    /// <summary>PlanVersionId</summary>
    public int PlanVersionId { get; init; }

    /// <summary>总任务数</summary>
    public int TotalTasks { get; init; }

    /// <summary>On-time（按时完成）</summary>
    public int OnTimeCount { get; init; }

    /// <summary>Delayed（延期）</summary>
    public int DelayedCount { get; init; }

    /// <summary>Risk（风险）</summary>
    public int RiskCount { get; init; }

    /// <summary>Unscheduled（未排程）</summary>
    public int UnscheduledCount { get; init; }

    /// <summary>Estimated-only（仅估算，非正式承诺）</summary>
    public int EstimatedOnlyCount { get; init; }

    /// <summary>其他状态</summary>
    public int OtherCount { get; init; }
}

/// <summary>
/// Overview - 资源负荷/Bottleneck
/// </summary>
public sealed class OverviewResourceBottleneckDto
{
    /// <summary>资源ID</summary>
    public int ResourceId { get; init; }

    /// <summary>资源编码</summary>
    public string ResourceCode { get; init; } = string.Empty;

    /// <summary>资源名称</summary>
    public string ResourceName { get; init; } = string.Empty;

    /// <summary>资源类型</summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>分配任务数</summary>
    public int TaskCount { get; init; }

    /// <summary>总计划工时（小时）</summary>
    public decimal TotalPlannedHours { get; init; }

    /// <summary>负荷率（实际工时/可用工时，>1为过载）</summary>
    public decimal? UtilizationRate { get; init; }

    /// <summary>是否Bottleneck</summary>
    public bool IsBottleneck { get; init; }
}

/// <summary>
/// Overview - Candidate待处理摘要
/// </summary>
public sealed class OverviewCandidateSummaryDto
{
    /// <summary>待处理Candidate数量</summary>
    public int PendingCount { get; init; }

    /// <summary>Candidate列表</summary>
    public List<CandidateBriefDto> Candidates { get; init; } = new();
}

/// <summary>
/// Candidate简要信息
/// </summary>
public sealed class CandidateBriefDto
{
    /// <summary>PlanVersionId</summary>
    public int PlanVersionId { get; init; }

    /// <summary>版本号</summary>
    public string VersionCode { get; init; } = string.Empty;

    /// <summary>DomainKey</summary>
    public string? DomainKey { get; init; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>创建人</summary>
    public string? CreatedByUserName { get; init; }
}
