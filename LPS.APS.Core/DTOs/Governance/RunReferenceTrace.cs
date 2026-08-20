namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// Run 引用追溯结果（P0-08）：ScheduleRun → 策略包版本 → 规则/参数集版本 → PlanVersion。
/// 与 RunStrategyProfileTrace（P0-06 版本维）互补：本 DTO 补齐 ScheduleRun 维，
/// 含冻结的 ExpectedDomainKeysJson 与关联 PlanVersion 状态。
/// </summary>
public sealed class RunReferenceTrace
{
    /// <summary>ScheduleRun.Id</summary>
    public int ScheduleRunId { get; set; }
    /// <summary>ScheduleRun.RunType（FULL_SCHEDULE / MANUAL_RESCHEDULE / ...）</summary>
    public string RunType { get; set; } = string.Empty;
    /// <summary>运行状态：RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>绑定的策略包版本（可为 null：无绑定）</summary>
    public long? StrategyProfileVersionId { get; set; }
    public string? StrategyProfileVersionCode { get; set; }
    public long? RuleSetVersionId { get; set; }
    public string? RuleSetVersionCode { get; set; }
    public long? ParameterSetVersionId { get; set; }
    public string? ParameterSetVersionCode { get; set; }

    /// <summary>冻结的预期 Domain 集合（JSON 数组原样返回；终态判定唯一权威来源）</summary>
    public string? ExpectedDomainKeysJson { get; set; }

    /// <summary>关联结果版本 Id（无则 0）</summary>
    public int PlanVersionId { get; set; }
    /// <summary>版本状态：BUILDING / CANDIDATE / ACTIVE / ARCHIVED / FAILED</summary>
    public string? PlanVersionStatus { get; set; }

    /// <summary>数据切片边界</summary>
    public DateTime? DataCutoffTime { get; set; }
    public DateTime? StartedAt { get; set; }
    /// <summary>所有终态（COMPLETED / PARTIAL_SUCCESS / FAILED）均写入</summary>
    public DateTime? CompletedAt { get; set; }
    /// <summary>失败错误信息（仅 FAILED）</summary>
    public string? ErrorMessage { get; set; }
}
