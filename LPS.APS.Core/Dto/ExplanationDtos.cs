namespace LPS.APS.Core.Dto;

/// <summary>
/// 排程原因解释DTO（Explanation：5号位提供给4号位）
///
/// 数据来源：ScheduleExplanationFact表（1号位产出，2号位落盘）
/// 5号位只读取展示，不重新裁决延期原因
/// </summary>
public sealed class ExplanationDto
{
    public long Id { get; init; }
    public int PlanVersionId { get; init; }
    public int? ScheduleRunId { get; init; }

    /// <summary>对象类型：ORDER / TASK / RESOURCE / STAGE / DOMAIN</summary>
    public string ObjectType { get; init; } = string.Empty;

    /// <summary>关联订单ID</summary>
    public long? OrderId { get; init; }

    /// <summary>关联任务ID</summary>
    public long? TaskId { get; init; }

    /// <summary>关联资源ID</summary>
    public int? ResourceId { get; init; }

    /// <summary>关联Stage代码</summary>
    public string? StageCode { get; init; }

    /// <summary>结构化原因码</summary>
    public string ReasonCode { get; init; } = string.Empty;

    /// <summary>严重级别：INFO / WARN / ERROR</summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>影响工时（小时）</summary>
    public decimal? ImpactHours { get; init; }

    /// <summary>证据JSON（结构化详情）</summary>
    public string? EvidenceJson { get; init; }

    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Explanation查询汇总DTO
/// </summary>
public sealed class ExplanationSummaryDto
{
    /// <summary>PlanVersionId</summary>
    public int PlanVersionId { get; init; }

    /// <summary>总Explanation数</summary>
    public int TotalCount { get; init; }

    /// <summary>按严重级别统计</summary>
    public int ErrorCount { get; init; }
    public int WarnCount { get; init; }
    public int InfoCount { get; init; }

    /// <summary>按ReasonCode分组统计</summary>
    public List<ReasonCodeCountDto> ReasonCodeCounts { get; init; } = new();

    /// <summary>Explanation列表</summary>
    public List<ExplanationDto> Items { get; init; } = new();
}

/// <summary>
/// ReasonCode统计
/// </summary>
public sealed class ReasonCodeCountDto
{
    public string ReasonCode { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal? TotalImpactHours { get; init; }
}
