namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// ScheduleRun 治理只读模型（P0-08，3号位）
/// 仅承载 3号位生命周期治理所需的冻结列；不替代 2号位 ScheduleRun 执行模型。
/// 边界：ExpectedDomainKeysJson 为运行启动时冻结的预期 Domain 集合（JSON 数组），创建后不可修改，终态判定唯一权威来源。
/// DDL 依据：冻结 DDL v5.1.2（§3.1 ScheduleRun）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class ScheduleRunGov
{
    /// <summary>ScheduleRun.Id</summary>
    public int Id { get; set; }

    /// <summary>运行类型：FULL_SCHEDULE / MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / SIMULATION / INSERT_ORDER_WHATIF</summary>
    public string RunType { get; set; } = string.Empty;

    /// <summary>运行状态：RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>触发来源：'Hangfire' / UserId / 'API' / 'Agent'</summary>
    public string? TriggeredBy { get; set; }

    /// <summary>本次运行统一数据切片边界</summary>
    public DateTime DataCutoffTime { get; set; }

    /// <summary>绑定的策略包版本（冻结基线；可为 null：无绑定）</summary>
    public long? StrategyProfileVersionId { get; set; }

    /// <summary>白天 Candidate 所基于的当前 ACTIVE 计划版本（逻辑引用；创建服务校验版本存在/状态/Domain 一致性）</summary>
    public int? BasePlanVersionId { get; set; }

    /// <summary>运行启动时冻结的预期 Domain 集合（JSON 数组；创建后不可修改）</summary>
    public string? ExpectedDomainKeysJson { get; set; }

    /// <summary>局部重排范围 JSON（v5.1.4 起不再承载 ExpectedDomainKeys，仅保留只读）</summary>
    public string? ScopeJson { get; set; }

    /// <summary>运行启动时间</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>终态完成时间（COMPLETED / PARTIAL_SUCCESS / FAILED 均写入）</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>失败错误信息（仅 FAILED）</summary>
    public string? ErrorMessage { get; set; }
}
