using LPS.APS.Core.DTOs.Scope;
using LPS.APS.Core.Enum;

namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 白天候选局部重排范围载荷（ScopeJsonV2；冻结 §十九 / Delta §4.5）
/// 承载 ScheduleRun.ScopeJson 的 v2 结构化载荷：业务触发类型 + 优先级模式 + 订单/任务目标 + 变更资源。
/// 边界（§四 附加边界）：BasePlanVersionId / ExpectedDomainKeysJson 不进本载荷，继续用 ScheduleRun 独立物理列；
/// 本类型是「ScopeJson 载荷」（纯范围语义），不是「创建入参 DTO」（创建入参见 <see cref="CandidateRunCreateSpec"/>）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class ScopeJsonV2
{
    /// <summary>载荷 SchemaVersion（0号位 2026-09-22 裁决：ScopeJson 采用 SchemaVersion 演进，允许版本化扩展）
    /// 实例属性（非常量）：需随载荷序列化为 JSON 供反序列化读取判断版本；默认 2 = 本 v2 五块字段结构；
    /// 历史缺 SchemaVersion 的旧 v1 载荷反序列化时取默认 2 兜底（实施包 §15.2「历史缺失按旧 v1 兼容读取」对齐）。</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>业务触发类型（八码；冻结 §十九 / Delta §4.3~4.4）</summary>
    public BusinessTriggerType Trigger { get; set; }

    /// <summary>优先级模式；DOMAIN_MANUAL_RESCHEDULE 走既有正式优先规则应为 null（非 NORMAL/EXPEDITE）</summary>
    public PriorityMode? PriorityMode { get; set; }

    /// <summary>订单目标（结构化：OrderCanonicalId + 本次 Run 的手工目标交期）；单一真相，OrderCanonicalId 须去重唯一</summary>
    public IReadOnlyList<OrderTarget> OrderTargets { get; set; } = [];

    /// <summary>任务目标（结构化：TaskId + 软目标时间）</summary>
    public IReadOnlyList<TaskTarget> TaskTargets { get; set; } = [];

    /// <summary>本次局部重排涉及的资源 Id 集合（int 数组，如 [12, 18, 33]）</summary>
    public IReadOnlyList<int> ChangedResourceIds { get; set; } = [];

    /// <summary>本次 Candidate 操作允许影响的业务对象范围（BusinessScope，0号位 §五/§七；v2 版本化扩展可选块，历史载荷缺省 null=按无授权 fail-safe）。</summary>
    public BusinessScopeDto? BusinessScope { get; set; }
}

/// <summary>
/// 订单目标（Delta §4.5）：ManualTargetDueDate 只属本次 Run、不改 ERP/Order_Canonical 正式 DueDate，
/// NORMAL 下不得偷换为 Demand 排序字段。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class OrderTarget
{
    /// <summary>订单规范 Id（OrderCanonical.Id，long）</summary>
    public long OrderCanonicalId { get; set; }

    /// <summary>本次 Run 的手工目标交期（可选；不改正式 DueDate）</summary>
    public DateTime? ManualTargetDueDate { get; set; }
}

/// <summary>
/// 任务目标（Delta §4.5）：TargetTime 为软目标，Solver 可返回其它可行时间并给 Explanation。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class TaskTarget
{
    /// <summary>排程任务 Id（长期自增，long）</summary>
    public long TaskId { get; set; }

    /// <summary>软目标时间（Solver 可返回其它可行时间并附说明）</summary>
    public DateTime TargetTime { get; set; }
}