using LPS.APS.Core.DTOs.Scope;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// 2号位从 ScheduleContext 裁剪后传给1号位的纯内存请求。
/// 不含 SupplyPool、BOM 原始快照、Ledger、PSA 或任何数据库对象。
/// 符合1↔2接口冻结文档 v1.0_20260814 §2.3 九类输入要求
/// </summary>
public sealed class DomainSolveRequest
{
    public long? ScheduleRunId { get; init; }
    public int PlanVersionId { get; init; }
    public string DomainKey { get; init; } = string.Empty;
    public DateTime? DataCutoffTime { get; init; }
    public DateTime PlanningStart { get; init; }
    public DateTime PlanningEnd { get; init; }

    public IReadOnlyList<LogicalProductionDemand> LogicalProductionDemands { get; init; }
        = Array.Empty<LogicalProductionDemand>();

    /// <summary>
    /// 多层 BOM「任务喂任务」血缘输入（PM 2026-09-10 裁决 R2）：父 LogicalProductionDemand → 子 LogicalProductionDemand
    /// 运行时关系。1号位据此 + 拆批/合批 + Routing 生成真实 TaskDependency（FinalTaskPeggingDraft）。
    /// 子件全库存 / 采购占位（0910 §二十 Case B）时仍产 link 以保留「父需求→子需求」真相，ProducerLogicalDemandKey 为 null；1号位 据此不产该边的 TaskDependency。
    /// </summary>
    public IReadOnlyList<MaterialRequirementLink> MaterialRequirementLinks { get; init; }
        = Array.Empty<MaterialRequirementLink>();

    public IReadOnlyList<AllocationLineage> AllocationLineage { get; init; }
        = Array.Empty<AllocationLineage>();

    public IReadOnlyList<RoutingOperation> RoutingOperations { get; init; }
        = Array.Empty<RoutingOperation>();

    public IReadOnlyList<RoutingDependency> RoutingDependencies { get; init; }
        = Array.Empty<RoutingDependency>();

    public IReadOnlyList<OperationResourceEligibility> OperationResourceEligibility { get; init; }
        = Array.Empty<OperationResourceEligibility>();

    /// <summary>
    /// 物料×阶段→默认生产部门 上下文（PM 裁定：最小 B）。
    /// 2号位裁剪当前 Domain 涉及的 (MaterialId, StageCode) 传入；1号位按 (MaterialId, StageCode)
    /// 锁 ProductionDepartmentId 后过滤 Routing 三件套，不得重新推导部门。
    /// </summary>
    public IReadOnlyList<MaterialStageDepartmentContextDto> MaterialStageDepartmentContexts { get; init; }
        = Array.Empty<MaterialStageDepartmentContextDto>();

    /// <summary>
    /// 无 Routing 阶段的提前期（PM《无Routing Stage统一处理建议》§九/§十，2026-09-28：
    /// **2号位 装载 → 1号位 消费**）。
    ///
    /// 语义：`StagePath 决定阶段是否存在及顺序；Routing 决定该 Stage 内部有无小工序；
    /// Routing 不存在 ≠ Stage 不存在。` 对「有效 Stage 但无 RoutingOperation」的 (物料, 阶段)，
    /// 2号位 按三级命中顺序（部门级 `DEPT_EXACT` → 工厂级 `FACTORY_STAGE_DEFAULT` → 全局级 `GLOBAL_STAGE_DEFAULT`）
    /// 解析 `StageLeadTimeParam` 后给出提前期；**走完三级仍未命中**则记 `STAGE_LEADTIME_MISSING` 并**按 3 天兜底**
    /// （PM 2026-09-29 裁决）——即本列表对每个缺口 (物料,阶段) **都有 Fact**，1号位 不必自备兜底值。
    /// ⚠️ **2026-09-29 用户更正**：本节此前写的「「命中到参数行但折算值 ≤ 0」**同样视同未命中**、同样给 72h 兜底」
    /// **已作废**。**命中就是命中** —— 折算值为 0（或负）**按命中产出**：`LeadTimeHours = 0`、
    /// `MatchLevel` = 实际命中级（实测 4 行 `*_FINAL` 的 `LeadTimeDays = 0.00` ⇒ 值 0 + `FACTORY_STAGE_DEFAULT`）。
    /// ⇒ 消费侧认 `MatchLevel`，但**不要**假定「值 0 ⇒ 一定是兜底」：值为 0 也可能是一条真实命中的参数。
    /// 1号位 据此为该阶段保留时间与前后依赖（不因无 Routing 而丢弃该阶段）。
    ///
    /// 注：Stage 存在性由 `MaterialStageDepartmentContexts`（含 (MaterialId, StageCode)）承载；
    /// **Stage 顺序由 `StageSequenceFacts` 承载**；本字段只补「无小工序时该阶段占多少时间」。
    /// </summary>
    public IReadOnlyList<StageLeadTimeFact> StageLeadTimes { get; init; }
        = Array.Empty<StageLeadTimeFact>();

    /// <summary>
    /// Stage 顺序事实（PM《BOM取用_Pegging_Stage_Routing完整链路说明》§七，2026-09-28：
    /// `StageSeq` 是排程大工艺顺序的唯一权威来源）。
    ///
    /// 【为什么需要】1号位 消费 `LogicalProductionDemand.RequiredStageCode`（供给阈值 Stage）时，
    ///   「做到该阶段为止」须判断哪些 Stage 在该阶段**之前** ⇒ **必须有顺序**；
    ///   `StageLeadTimes`（无 Routing 阶段保留前后依赖）同理。
    ///   此前 `MaterialStageDepartmentContexts` 只有 (MaterialId, StageCode, Dept) 三字段、**无任何顺序载体**，
    ///   致上述两个能力无处落地。
    ///
    /// 【形态说明】本字段给的是**每个物料一条完整有序链**（`StageSequenceChain`，每步带 `StageSeq` 数值），
    ///   不预判消费侧建图形态（StageDependency / 有序列表均可）—— 满足 1号位 2026-09-28 回执 §二 的撤回前提。
    /// 【粒度】同一物料在本次 BOM 里 ROOT ∪ EDGE 取并集，同一 StageCode 取最小 StageSeq。
    /// 【空值语义】空 = 无 StagePath 数据（数据缺口 / 采购件），1号位 按无顺序信息处理。
    /// </summary>
    public IReadOnlyList<StageSequenceChain> StageSequenceChains { get; init; }
        = Array.Empty<StageSequenceChain>();

    public IReadOnlyList<MaterialAvailabilitySlice> MaterialConstraints { get; init; }
        = Array.Empty<MaterialAvailabilitySlice>();

    public IReadOnlyList<ResourceDefinition> Resources { get; init; }
        = Array.Empty<ResourceDefinition>();

    public IReadOnlyList<ResourceCalendarSlot> CalendarSlots { get; init; }
        = Array.Empty<ResourceCalendarSlot>();

    public IReadOnlyList<ResourceEligibilityDefinition> ResourceEligibility { get; init; }
        = Array.Empty<ResourceEligibilityDefinition>();

    public IReadOnlyList<ExecutionConstraint> ExecutionConstraints { get; init; }
        = Array.Empty<ExecutionConstraint>();

    public SolverStrategySnapshot StrategySnapshot { get; init; } = new();

    public CandidateContext? CandidateContext { get; init; }

    /// <summary>
    /// 局部重排范围投影（白天候选 Run 专用；null = FULL 语义）。
    /// 2号位 由 ScheduleRun.ScopeJson 反序列化后按本块键体系投影；
    /// OrderCanonicalId / TaskId 已转 1号位 内存键，long 官方 Id 不进 Solver 契约。
    /// InScopeLogicalDemandKeys 按冻结 v1.7 §69 留空：影响范围由 1号位 从变化 Seed 动态传播推导，
    /// 2号位 只转 ChangeSeed/TaskTarget/ChangedResourceIds（不预填可移动集合 / Scope 外 Anchor·Block）。
    /// </summary>
    public RunScope? RunScope { get; init; }

    /// <summary>
    /// 前序 Domain 成功后的共享 Resource 占用块（FULL §9）。
    /// 1号位将其作为不可用时间窗阻挡后续 Domain 在真实共享 Resource 上重叠占用。
    /// Candidate 的对应物是 CandidateContext.ExternalDomainResourceBlocks（§11）；本字段 FULL 专用、Candidate 时为 null。
    /// </summary>
    public IReadOnlyList<ResourceBlock> UpstreamDomainResourceBlocks { get; init; }
        = Array.Empty<ResourceBlock>();
}

/// <summary>
/// Pegging Allocation到FinalTask的追溯信息（接口冻结§2.3第3类）
/// 不等同于PeggingSupplyAllocation持久化表
/// </summary>
public sealed class AllocationLineage
{
    public long AllocationSequence { get; init; }
    public string DemandKey { get; init; } = string.Empty;
    public int MaterialId { get; init; }
    public string SupplyType { get; init; } = string.Empty;
    public string SupplyKey { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public DateTime? AvailableTime { get; init; }
}

/// <summary>
/// 某个逻辑生产需求的材料，在什么时间有多少数量真正可用（接口冻结§2.3第6类）
/// 必须支持多段Quantity-Time：40件15日+60件17日，不能压成100件17日
/// </summary>
public sealed class MaterialAvailabilitySlice
{
    public long AllocationSequence { get; init; }
    public int MaterialId { get; init; }
    public int FactoryId { get; init; }
    public decimal Quantity { get; init; }
    public DateTime AvailableTime { get; init; }
    public string? SourceType { get; init; }
    public string? SourceKey { get; init; }
    public string? Commitment { get; init; }
    public string? Confidence { get; init; }
}

/// <summary>
/// 一次ScheduleRun冻结给1号位使用的Solver参数包（接口冻结§2.3第7类）
/// 1号位不需要Demand排序、库存规则等，那些已由2号位执行完成
/// </summary>
public sealed class SolverStrategySnapshot
{
    public long? StrategyProfileVersionId { get; init; }
    public long? ParameterSetVersionId { get; init; }

    /// <summary>已激活消费点用的裁剪参数（执行用子集：AllowSplit/AllowMerge/SchedulingDirection + P1-02 B 组）。</summary>
    public FiniteCapacityParameters Parameters { get; init; } = new();

    // ── P1-02：⑤⑥ 全字段整块透传（PM/2号位 2026-09-14 拍板：整块引用，避免同 ⑤⑥ 逐批平铺）──
    /// <summary>P1-02：⑤ Solver 策略整块（Mode/Bottleneck/OnTimeTarget/Split/Setup/StageOverlap/AllowMerge）。<br/>
    /// 1号位从整块读取正式参数（瓶颈阈值 Bottleneck/OnTimeTarget/Split原始值/Setup/StageOverlap），
    /// 替换换不了的硬编码。强类型零漂移，2号位不再逐批投影。</summary>
    public SolverStrategyBlock SolverStrategy { get; init; } = new();

    /// <summary>P1-02：⑥ Candidate 技术 Guardrail 整块（限时/传播/警告/TopN/拆分候选）。<br/>
    /// 1号位从整块读取（NormalMs/SoftMs/LocalHardMs/MaxRepairAttempts/MaxPropagationRounds/ResourceTopN/WarnOnlyOnMaxImpacted）。
    /// 强类型零漂移。</summary>
    public CandidateGuardrailBlock CandidateGuardrail { get; init; } = new();

    /// <summary>⑦ 产品转换换型规则（v1.2 §九/§十）。2号位 按本 Run RuleSetVersionId 从 SetupTransitionRule 表装载，
    /// 筛 IsActive 投影后按 Domain（ProductionDepartmentId + StageCode）裁剪；1号位 SetupOptimizer.BuildRuleLookups 消费
    /// （EXACT/DEFAULT 命中，无规则 0 分钟，禁止 RoutingOperation.SetupTime 兜底）。</summary>
    public IReadOnlyList<SetupTransitionRuleSnapshot> SetupTransitionRules { get; init; }
        = Array.Empty<SetupTransitionRuleSnapshot>();

    /// <summary>⑧ 批量策略（Batch Policy）规则（0号位 2026-10-07《未命名的Markdown文件 (7).md》§四/§十/§十一）。
    /// 粒度 = <c>Material + ProductionDepartment</c>（冻结 B-001；<c>ProductionDepartmentId</c> 可空 = Material 级默认）。
    /// 2号位 按本 Run 从 `TaskSplitRuleConfig` 装载并投影；1号位 `PhaseOneConstraintBuilder` 收进
    /// <c>ConstraintContext.ExecutionBatchPolicies</c>，由 <c>PhaseTwoInitialScheduler</c> 做 C 桶 Batch Decision。
    /// 空 ⇒ 每需求恒 1 批（不拆）；**1号位 不得自造全局默认策略**（§十一 第 3 条）。</summary>
    public IReadOnlyList<BatchPolicyRuleSnapshot> BatchPolicies { get; init; }
        = Array.Empty<BatchPolicyRuleSnapshot>();
}

/// <summary>
/// Candidate Run专用上下文（接口冻结§2.3第9类）
/// FULL Run时为null
/// </summary>
public sealed class CandidateContext
{
    /// <summary>
    /// Base 稳定锚点 = ScheduleRun.BasePlanVersionId（创建 Run 时由 3号位冻结的当前 ACTIVE PlanVersion）。
    /// PM 2026-09-07 P0-04 2.1：Candidate 前后比较基线，2号位运行期必须始终用此值，不得中途再查「此刻最新 ACTIVE」。
    /// </summary>
    public int? BasePlanVersionId { get; init; }

    /// <summary>变化 Seed：Candidate Pegging 相对 Base ACTIVE 发生变化的逻辑生产需求键（DemandKey/AllocationSequence/LogicalDemandKey 之一）</summary>
    public IReadOnlyList<string> ChangeSeedKeys { get; init; } = Array.Empty<string>();

    /// <summary>其它 Domain 当前 ACTIVE 在共享 Resource 上的不可移动占用（PM 0907：不是 Quantity-Time）</summary>
    public IReadOnlyList<ResourceBlock> ExternalDomainResourceBlocks { get; init; } = Array.Empty<ResourceBlock>();
}

/// <summary>
/// 其它Domain ACTIVE共享资源占用的不可用时间窗
/// PM 2026-09-07 P0-04：Candidate 的外部 Domain 阻挡块必须携带来源域/版本/不可移动语义，
/// 与 FULL 的 UpstreamDomainResourceBlocks 共用本结构（FULL 时 SourceDomainKey/SourcePlanVersionId 可空）。
/// </summary>
public sealed class ResourceBlock
{
    public int ResourceId { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public string Reason { get; init; } = string.Empty;

    /// <summary>来源 DomainKey（外部 ACTIVE 阻挡块的归属域；FULL 前序 Domain 时也有值）</summary>
    public string? SourceDomainKey { get; init; }

    /// <summary>来源 PlanVersionId（外部 ACTIVE 阻挡块的归属版本）</summary>
    public int? SourcePlanVersionId { get; init; }

    /// <summary>不可移动标记（PM 0907：Candidate 外 Domain 阻挡块 Immutable=true，1号位不得挤动）</summary>
    public bool Immutable { get; init; } = true;
}

/// <summary>Task 间依赖意图（排程前保留，用于排程后生成 PhysicalPeggingDraft）</summary>
public sealed class TaskDependencyDraft
{
    public string FromDraftId { get; init; } = string.Empty;
    public string ToDraftId { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public long AllocationSequence { get; init; }
}

public sealed class ResourceDefinition
{
    public int ResourceId { get; init; }
    public string ResourceCode { get; init; } = string.Empty;
    public string FactoryCode { get; init; } = string.Empty;
    public decimal Capacity { get; init; }
}

public sealed class ResourceCalendarSlot
{
    public int ResourceId { get; init; }
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool IsAvailable { get; init; }
}

public sealed class ResourceEligibilityDefinition
{
    public int ResourceId { get; init; }
    public string OperationCode { get; init; } = string.Empty;
    public string RouteKey { get; init; } = string.Empty;
    public int Priority { get; init; }
}

public sealed class ExecutionConstraint
{
    public string DraftId { get; init; } = string.Empty;
    public int ResourceId { get; init; }
    public DateTime LockedStart { get; init; }
    public DateTime LockedEnd { get; init; }
    public string ConstraintType { get; init; } = string.Empty;

    // 第4轮Anchor补充：Stage/Operation信息，用于原地继承锁定Task
    public string? StageCode { get; init; }
    public string? OperationCode { get; init; }

    // 第4轮Anchor补充：锁定数量，原地继承该份额，只排剩余可移动份额
    public decimal? LockedQuantity { get; init; }

    // P1-01：净合格数量（锁定 Task 的净产出，YIELD 场景 != 产能加工数量）
    public decimal? LockedNetOutputQty { get; init; }

    // P1-01：产能加工数量（锁定 Task 的计划加工量）
    public decimal? LockedPlannedProcessQty { get; init; }

    // 第4轮Anchor补充：稳定TaskKey，用于跨轮次识别同一Task
    public string? TaskKey { get; init; }
}

public sealed class FiniteCapacityParameters
{
    public bool AllowSplit { get; init; } = false;
    public bool AllowMerge { get; init; } = false;
    public string SchedulingDirection { get; init; } = "BACKWARD";

    // ── P1-02 B 组 live 硬编码（3号位 已冻结、2号位 投影；字段先行、1号位 换读后逐项激活）──
    public int ImpactedTaskWarningPercent { get; init; } = 30;   // 传播警戒（PhaseFourLocalRepair maxAffectedRatio）
    public int MaxPropagationRounds { get; init; } = 10;          // 传播轮数（PhaseFourLocalRepair maxPropagationRounds）
    public int SplitAlternatives { get; init; } = 3;              // Split 候选（PhaseFourLocalRepair {2,3}）
    public decimal MinBatchQty { get; init; } = 0.1m;             // 拆分下限（PhaseFourLocalRepair qtyPerSplit<0.1）
}

/// <summary>
/// SolverStrategyMode ↔ SchedulingDirection 字符串固定映射（P1-02 §五-3：1↔2 契约正式化）。
/// 由 2号位 在投影处唯一使用；1号位 消费 SchedulingDirection 字符串（PhaseTwoInitialScheduler）。
/// 2026-10-07（0号位 裁决）：Mode += Auto，透传 "AUTO"——由 1号位 按当前求解上下文决定最终方向（不按 OrderType 硬编码）。
/// </summary>
public static class SolverStrategyModeMap
{
    public static string ToDirection(SolverStrategyMode mode) => mode switch
    {
        SolverStrategyMode.Forward  => "FORWARD",
        SolverStrategyMode.Backward => "BACKWARD",
        SolverStrategyMode.Mixed    => "MIXED",
        SolverStrategyMode.Auto     => "AUTO",         // 1号位 按求解上下文决定最终方向（0号位 2026-10-07 裁决）
        _                            => "BACKWARD"    // 防御未知枚举，等效 Backward（与历史行为一致）
    };
}

/// <summary>Run 级局部重排范围投影（2号位 → 1号位，强类型，零 string-JSON）。
/// 键体系已转 1号位 内存键；null ⇒ FULL Run 语义（向后兼容，缺省不改变现有行为）。</summary>
public sealed class RunScope
{
    public BusinessTriggerType Trigger { get; init; }

    /// <summary>优先级模式；DOMAIN_MANUAL_RESCHEDULE 为 null（走既有正式优先规则）。</summary>
    public PriorityMode? PriorityMode { get; init; }

    /// <summary>范围内逻辑生产需求键（LogicalDemandKey）；M5 详设前留空。</summary>
    public IReadOnlyList<string> InScopeLogicalDemandKeys { get; init; } = Array.Empty<string>();

    /// <summary>Run 级交期覆盖：LogicalDemandKey → ManualTargetDueDate（OrderCanonicalId 已按需求展开）。</summary>
    public IReadOnlyList<DueDateOverride> DueDateOverrides { get; init; } = Array.Empty<DueDateOverride>();

    /// <summary>Task 软目标：(DraftId, OperationCode) → TargetTime（TaskId 已投影为复合键）。</summary>
    public IReadOnlyList<TaskTargetOverride> TaskTargetOverrides { get; init; } = Array.Empty<TaskTargetOverride>();

    /// <summary>本次局部重排涉及资源 Id（int，与 ResourceDefinition.ResourceId 同域，原样透传）。</summary>
    public IReadOnlyList<int> ChangedResourceIds { get; init; } = Array.Empty<int>();

    /// <summary>本次 Candidate 操作允许影响的业务对象范围（权限框；0号位 §五/§七「谁可发起什么变化」）。
    /// 3号位 生成、2号位 纯透传不判权限、1号位 遵守；null = 未投影（历史载荷/非 Candidate）按无授权 fail-safe。</summary>
    public BusinessScopeDto? BusinessScope { get; init; }
}

/// <summary>Run 级交期覆盖：逻辑需求键 → 手工目标交期（仅 ManualTargetDueDate 非空行投影）。</summary>
public sealed class DueDateOverride
{
    public string LogicalDemandKey { get; init; } = string.Empty;
    public DateTime ManualTargetDueDate { get; init; }
}

/// <summary>Task 软目标：(DraftId, OperationCode) → TargetTime（与 ExecutionConstraint 同复合键口径）。</summary>
public sealed class TaskTargetOverride
{
    public string DraftId { get; init; } = string.Empty;
    public string OperationCode { get; init; } = string.Empty;
    public DateTime TargetTime { get; init; }
}
