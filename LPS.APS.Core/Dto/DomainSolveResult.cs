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

    /// <summary>
    /// 求解过程追溯记录（非排程结果，仅决策说明）。
    /// 1号位 在求解过程中产出（如 SCOPE_REFERENCE_MISSING / SetupResolution 等），
    /// 2号位 原样落库/透传，不进 ReasonCode 体系（0号位 Q4）。
    /// </summary>
    public IReadOnlyList<SolveTraceNote> SolveTraceNotes { get; init; }
        = Array.Empty<SolveTraceNote>();

    /// <summary>
    /// Demand 级最终完成 / 供给可用时间（0号位 2026-09-29 裁决 §10.2/§10.3、§十四第9项）。
    ///
    /// 【为什么需要】末端无 Routing Stage 的时间（如 BJ_FINAL 完工 4h）**没有对应的真实 Operation Task**，
    ///   故不能由 <see cref="FinalTaskDraft"/> 表达；而 0号位 §9.2 明文**禁止**为此伪造
    ///   假 RoutingOperation / 假 Task.Id / 假 TaskNo / 假 MESWorkOrder。
    ///   ⇒ 该时间必须走本独立列表，**不得混入 <see cref="FinalTasks"/>**。
    ///
    /// 【口径】CompletionTime = 末端真实 Task.End + 其**之后**各无 Routing Stage 的 StageLeadTime 之和。
    ///   整条需求无任何真实 Operation Task 时 = 该 Stage 链自起点起的纯 LeadTime 累计。
    ///
    /// 【纯内存】2号位 消费后自行决定落库/透传形态；**不得**据 <see cref="DemandCompletionFact.StageTimings"/>
    ///   建 Task / TaskNo / MES工单 / Execution Batch。
    ///
    /// 【载体归属（2026-09-29 澄清）】本属性与下面两个类在 `LPS.APS.Core` —— 属 **2号位 辖区**；
    ///   1号位 边界只到 `LPS.APS.Scheduling/**`（不含 Core）⇒ **载体由 2号位 落，值由 1号位 填**。
    ///   1号位 提请件：《无RoutingStage时间出口字段_1号位致2号位_字段载体落码提请_v1.0_20260929》§三。
    /// </summary>
    public IReadOnlyList<DemandCompletionFact> DemandCompletions { get; init; }
        = Array.Empty<DemandCompletionFact>();

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

    /// <summary>
    /// 工序顺序号。DAG 路由（RoutingOperation/RoutingDependency v5.0）已废弃线性 OperationSeq，无客观"顺序号"；
    /// 排程顺序由 RoutingDependency 承载，与本字段无关。默认 0（落库 Task.OperationSeq=0 列兼容），
    /// 1号位可选透传 MES 源工序号，不强制填。
    /// </summary>
    public int OperationSeq { get; init; }
    public string TaskType { get; init; } = "NEW_REQUIREMENT";
    /// <summary>
    /// 排定资源（非资源工序 UNCONSTRAINED/WAIT_ONLY 产出的 Task 为 NULL；0号位 2026-09-22 裁决「非资源工序 Task 可有 Operation+Start/End、Resource 为空」）。
    /// </summary>
    public int? ResourceId { get; init; }
    public string ResourceCode { get; init; } = string.Empty;
    public string? RouteCode { get; init; }
    public long? PathId { get; init; }
    public decimal Quantity { get; init; }
    public decimal PlannedProcessQty { get; init; }
    public string UOM { get; init; } = string.Empty;
    public DateTime PlannedStartTime { get; init; }
    public DateTime PlannedEndTime { get; init; }
    public decimal SetupTime { get; init; }

    /// <summary>
    /// 换型来源（唯一落库值 = 4 态：INITIAL_SETUP_STATE / EXACT / DEFAULT / SETUP_RULE_MISSING_ZERO_FALLBACK，
    /// 0号位 2026-09-22 裁决替换旧 5 值 EXACT/DEFAULT/SAME_PRODUCT/NONE/INITIAL）。
    /// SetupTime=0 三语义与 SetupTime&gt;0 二语义无法由数值区分，必须显式来源字段，禁止查询层反推。
    /// 2号位 只承载 string + 原样落库，**不做映射**，SetupOutcome→4 态转换在 1号位 SetupOutcomeToSource。
    /// </summary>
    public string? SetupSource { get; init; }

    public int Priority { get; init; }
    public bool IsVirtual { get; init; }

    /// <summary>
    /// 执行批键（《APS_V1_1号位有限产能排程开发实施包 v1.6》§1号位新增/替换实施要求，0号位 2026-10-07 裁决 §六 授权补载体）：
    /// 「`ExecutionBatchDraftKey` 用于把多 Operation FinalTask 归为同一执行批；FinalTask 必须回传真实 RouteCode/PathId
    ///   并原样回传 ContinuationKey」。
    ///
    /// 【口径】同一执行批（同一 Routing 选择结果）下的多个 Operation FinalTask 共享同一 Key。
    ///   **生成权属 1号位**（0号位 2026-10-07 裁决 §七：本项「无前置生产职责」，1号位 今天即可完成）。
    ///   冻结要求「每个 Execution Batch 只允许一条完整 Path」⇒ 同一 Key 下所有 FinalTask 的
    ///   (RouteCode, PathId) 必须一致。
    ///
    /// ⚠ 与既有 <see cref="StageExecutionBatchDraftKey"/> **不是同一字段**（后者是 Stage 级、按 Stage 归并，
    ///   0号位 已明确二者不可互相顶替）。
    /// </summary>
    public string? ExecutionBatchDraftKey { get; init; }

    /// <summary>
    /// 连续份额身份键（原样回传，《APS_V1_1号位有限产能排程开发实施包 v1.6》Q3 + §1号位新增/替换实施要求：
    /// 「FinalTask 必须…原样回传 ContinuationKey」）。
    ///
    /// 【口径】1号位 **不生成、不解析**，从源 <c>LogicalProductionDemand.ContinuationKey</c> **逐字拷贝**。
    ///   null = 源需求无该身份。
    /// </summary>
    public string? ContinuationKey { get; init; }

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

/// <summary>
/// 求解过程追溯记录（非排程结果，仅决策说明）。
/// 1号位 产出载体，用于承载 SCOPE_REFERENCE_MISSING / SetupResolution / 目标未达 等求解过程决策上下文。
/// 不进 ReasonCode 体系（0号位 Q4 裁决）。
/// </summary>
public sealed class SolveTraceNote
{
    /// <summary>追溯键（如 LogicalDemandKey / DraftId / 资源Id）</summary>
    public string Key { get; init; } = string.Empty;
    /// <summary>原因码（如 SCOPE_REFERENCE_MISSING / TARGET_MISSED / SETUP_RESOLUTION）</summary>
    public string ReasonCode { get; init; } = string.Empty;
    /// <summary>可读说明</summary>
    public string? Message { get; init; }
    /// <summary>级别：Info / Warning / Error</summary>
    public string Level { get; init; } = "Info";
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

/// <summary>
/// 一个 Demand 的最终时间事实（0号位 2026-09-29 裁决 §10.2/§10.3）。
/// 与 <see cref="AllocationTaskShare"/> 的区别：Share 是**数量份额**（落真实末端 Task），
/// 本类是**时间结果**（可含无 Routing Stage 的纯时间，无对应 Task）。
///
/// 【载体归属】本类在 `LPS.APS.Core` = **2号位 辖区**（1号位 边界不含 Core）⇒ 2号位 落载体、1号位 填值。
/// </summary>
public sealed class DemandCompletionFact
{
    public string LogicalDemandKey { get; init; } = string.Empty;
    public int MaterialId { get; init; }
    public long AllocationSequence { get; init; }

    /// <summary>供给阈值 Stage（`LogicalProductionDemand.RequiredStageCode`）；无阈值语义时为 null。</summary>
    public string? RequiredStageCode { get; init; }

    /// <summary>
    /// 最终完成时间 = 末端真实 Task.End + 其**之后**各无 Routing Stage 的 LeadTime 之和。
    /// 整条需求无任何真实 Operation Task 时 = 该 Stage 链自起点起的纯 LeadTime 累计。
    /// </summary>
    public DateTime CompletionTime { get; init; }

    /// <summary>供给可用时间。V1 与 <see cref="CompletionTime"/> 同值（已对齐）。</summary>
    public DateTime AvailableTime { get; init; }

    /// <summary>
    /// 做到 `RequiredStageCode` 为止的完成时间（0号位 §10.3「Demand / RequiredStage」两个口径都给，已对齐）。
    /// `RequiredStageCode` 为 null、或该 Stage 不在本物料 Stage 链中时为 null。
    /// </summary>
    public DateTime? RequiredStageCompletionTime { get; init; }

    /// <summary>
    /// 承载 <see cref="CompletionTime"/> 的**末端真实生产 Task** 的 `FinalDraftId`。
    /// 整条需求无真实 Operation Task 时为 **null**（此时不生成 AllocationTaskShare，符合既有闭合不变式）。
    /// </summary>
    public string? TerminalFinalDraftId { get; init; }

    /// <summary>
    /// 被计入本时间的 Stage 级时间节点（**纯内存，非 Task**，对应 0号位 §9.3 的 `StageTimingNode`）。
    /// </summary>
    public IReadOnlyList<StageTimingSegmentFact> StageTimings { get; init; }
        = Array.Empty<StageTimingSegmentFact>();
}

/// <summary>
/// 一段 Stage 级时间（0号位 2026-09-29 裁决 §9.3 的 `StageTimingNode`）。
/// **显式标记 <see cref="NodeType"/>，避免日后被误持久化为真实 Operation Task。**
/// </summary>
public sealed class StageTimingSegmentFact
{
    /// <summary>
    /// 恒为 `"STAGE_TIMING"`（0号位 §9.3 建议的显式标记；**2号位 写库口的判据** ——
    /// §9.2 禁止把无 Routing Stage 伪装成真实 Task，2号位 在 `INSERT INTO [Task]` 前据此拒绝）。
    /// </summary>
    public string NodeType { get; init; } = "STAGE_TIMING";

    public string StageCode { get; init; } = string.Empty;

    /// <summary>本段占用的提前期（小时）。</summary>
    public decimal LeadTimeHours { get; init; }

    /// <summary>
    /// 命中层级（审计用）。**V1 合法值域 = 恰好 4 个**（与 <see cref="StageLeadTimeFact.MatchLevel"/> 同一契约）：
    /// `DEPT_EXACT` / `FACTORY_STAGE_DEFAULT` / `GLOBAL_STAGE_DEFAULT` / `STAGE_LEADTIME_MISSING`。
    /// 4 个之外的值（含旧名 `MATERIAL` / `FAMILY`）⇒ **契约违例**。
    /// </summary>
    public string MatchLevel { get; init; } = string.Empty;

    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
}
