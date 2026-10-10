using System.Diagnostics.CodeAnalysis;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 5: 压缩空隙与最终评价
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md》§六 Phase 5
///
/// 职责：
/// - 在不破坏高优先级交期的情况下：
///   * 减少不必要等待
///   * 减少 WIP
///   * 减少 Setup
///   * 提升利用率
///   * 避免过早生产
///   * 尽量保持计划稳定
/// </summary>
internal class PhaseFiveCompression
{
    /// <summary>
    /// 执行空隙压缩与最终评价
    /// 文档：§六 Phase 5
    /// 职责：在不破坏高优先级交期的情况下压缩空隙
    /// </summary>
    public DomainSolveResult Compress(
        DomainSolveRequest request,
        InitialScheduleResult scheduleResult,
        RepairResult repairResult,
        DiagnosticsResult diagnostics,
        ConstraintContext constraints)
    {
        // 合并所有已排程任务
        var allScheduledTasks = new List<FinalTaskDraft>();
        allScheduledTasks.AddRange(scheduleResult.ScheduledTasks);
        allScheduledTasks.AddRange(repairResult.RepairedTasks);

        // P1-05：Gap Compaction（§六 Phase 5 最小次级优化，Level 3——不破坏 Level 0 硬约束/Level 1 履约/Level 2 交期）。
        // 保序前向压实：把 Task 拉进更早的日历可用空档（减少不必要等待/减少 WIP/提升利用率）；
        // 仅严格增益才移动、绝不重排序（尽量保持计划稳定）；floor 含物料/Routing前序/跨物料子件/同PI连续份额/
        // 同资源前序占用（避免过早生产）；Setup 顺序不因保序压实恶化（FromMaterial 链不变），
        // 真正的 Setup 序列优化 = item1 夜间 FULL 有界搜索（待 2号位 规则通道）。
        // BACKWARD/MIXED 倒排 JIT 锚点即「避免过早生产」语义本身，V1 不做前向拉早。
        // 必须在 Shares/Dependency/硬校验/诊断之前执行，让下游看到压实后的最终时间。
        CompactGaps(allScheduledTasks, request, constraints);

        // P1-02 item1（夜间 FULL，v1.2 §14-§16）：单 Resource × 生产日窗口 × 固定锚点间可移动段的
        // 有界序列优化——Level 0-2 不恶化前提下减少段内 Setup 总时间（Level 3）。
        // 门控：仅 FULL Run（§17：白天 Candidate 局部优先，不做全天重排）且 FORWARD（与压实同口径）。
        OptimizeSetupSequences(allScheduledTasks, request, constraints);

        // 生成 AllocationTaskShare（追溯 Allocation → Task 的份额）
        // P0-04修复：传入 Phase2 的 Merge 份额血缘，保证合并批次的 Allocation→Task 份额闭合
        var allocationShares = GenerateAllocationShares(allScheduledTasks, request, constraints, scheduleResult.AllocationTaskShare);

        // 第5轮修复：TaskDependency必须在硬校验之前生成，以便校验Dependency约束
        // P0-13修复：生成 TaskDependency（基于 Routing 工序依赖关系）
        var taskDependencies = GenerateTaskDependencies(allScheduledTasks, request, constraints);

        // P0-04（0号位 2026-10-08 §七）：未排程需求集合（Phase2 未排 ∪ Phase4 未修）——
        //   数量闭合校验据此区分「已完全排定（必须**严格**闭合）」与「部分执行批失败（允许小于，但**绝不允许放大**）」。
        var unscheduledDemandKeysForValidation = new HashSet<string>(
            scheduleResult.UnscheduledDemandKeys, StringComparer.Ordinal);
        unscheduledDemandKeysForValidation.UnionWith(repairResult.StillUnscheduledKeys);

        // 第4轮Item 10：Phase5最终硬约束校验（§十一）
        var validationResult = ValidateHardResult(
            allScheduledTasks, allocationShares, taskDependencies, request, constraints,
            unscheduledDemandKeysForValidation, scheduleResult.AllocationTaskShare);
        if (!validationResult.IsValid)
        {
            return new DomainSolveResult
            {
                Success = false,
                ErrorMessage = $"Phase5硬约束校验失败: {validationResult.ErrorMessage}",
                IsRoughCut = false,
                FinalTasks = Array.Empty<FinalTaskDraft>(),
                AllocationShares = Array.Empty<AllocationTaskShare>(),
                UnscheduledTasks = Array.Empty<UnscheduledTaskResult>(),
                PhysicalPeggingDrafts = Array.Empty<FinalTaskPeggingDraft>(),
                ExplanationFacts = Array.Empty<ScheduleExplanationFact>(),
                // B.2：硬约束校验失败路径同样导出已收集的追溯（失败原因本身即求解过程上下文）
                SolveTraceNotes = constraints.TraceNotes,
                Summary = new SolveSummary
                {
                    TotalDrafts = 0,
                    ScheduledCount = 0,
                    UnscheduledCount = 0,
                    ElapsedMs = 0,
                    IssueCount = 0,
                    UsedRoughCut = false
                }
            };
        }

        // 收集未排程需求
        var unscheduledTasks = new List<UnscheduledTaskResult>();

        // P0-03（0号位 2026-10-08 §六）：同一需求可能**同时**出现在 Phase2 未排程表与 Phase4 未修复表
        //   ⇒ 出口必须**去重**，否则一个需求被报两次（错误的最终结果）。
        var reportedUnscheduled = new HashSet<string>(StringComparer.Ordinal);
        void ReportUnscheduled(string demandKey, string reason)
        {
            if (reportedUnscheduled.Add(demandKey))
            {
                unscheduledTasks.Add(new UnscheduledTaskResult { DraftId = demandKey, Reason = reason });
            }
        }

        // Phase 2 未排程的需求
        foreach (var demandKey in scheduleResult.UnscheduledDemandKeys)
        {
            if (!repairResult.RepairedTasks.Any(t => t.SourceDraftId == demandKey))
            {
                // ── P0-01 / P0-02（0号位 2026-10-08 §四 / §五）：硬业务失败优先归属 ──
                //   `BATCH_POLICY_MISSING` / `BATCH_POLICY_CONFLICT` 是**硬失败类别**，
                //   不得被降格成「Phase 2 初始排程失败」这类泛化原因（§十二 要求 Reason 逐字可判）。
                if (scheduleResult.BatchPolicyHardFailures.TryGetValue(demandKey, out var hardFailureReason))
                {
                    ReportUnscheduled(demandKey, hardFailureReason);
                    continue;
                }

                // 最小B：缺失生产部门 Context 的需求，Reason 单独标识
                var missingDept = IsMissingDepartmentContext(demandKey, request, constraints);
                // B-1（0号位 2026-09-29 裁决 §2.3）：StageSeq 全序冲突的需求，Reason 单独标识
                var stageSeqConflict = IsStageSequenceConflict(demandKey, request, constraints);
                // C-1（0号位 2026-09-28 裁决 §11.3）：真实资源日历覆盖不足 ⇒ Reason 单独标识。
                // 排在两个「配置类」根因之后：缺部门 / StageSeq 冲突是更具体的根因，优先归属。
                var calendarCoverageInsufficient = IsCalendarCoverageInsufficient(demandKey, request, constraints);
                ReportUnscheduled(demandKey, missingDept
                    ? "MISSING_PRODUCTION_DEPARTMENT_CONTEXT"
                    : stageSeqConflict
                        ? "STAGE_SEQUENCE_CONFLICT"
                        : calendarCoverageInsufficient
                            ? "CALENDAR_COVERAGE_INSUFFICIENT"
                            : "Phase 2 初始排程失败，Phase 4 修复未成功");
            }
        }

        // Phase 4 仍未排程的需求
        foreach (var demandKey in repairResult.StillUnscheduledKeys)
        {
            string reason;
            if (scheduleResult.BatchPolicyHardFailures.TryGetValue(demandKey, out var hardFailureReason))
            {
                // 硬失败（缺策略 / 无合法切分）—— Phase4 已按 §五 拒绝修复，出口仍报硬失败类别。
                reason = hardFailureReason;
            }
            else if (allScheduledTasks.Any(t => t.SourceDraftId == demandKey))
            {
                // P0-03（§六）：**部分**执行批已落定（Phase2 成功的批仍在最终集合里）、其余仍未排下
                //   —— 必须区别于「完全没排下」，否则「失败批没排出来」会被「需求已修复」掩盖。
                //   判据取**最终任务集合**而非 `RepairedTasks`：Phase2 成功的批不在 `RepairedTasks` 里，
                //   只看 `RepairedTasks` 会把「Phase2 落了一批 / Phase4 一批没修成」误报成「完全没排下」。
                reason = "Phase 4 局部修复后仍有执行批未排下";
            }
            else
            {
                reason = "Phase 4 局部修复后仍无法排程";
            }

            ReportUnscheduled(demandKey, reason);
        }

        // P0-03+P0-15修复：区分技术失败与业务Unscheduled
        // 技术失败：Routing非法、数量闭合错误、硬资源约束破坏 → Success=false
        // 业务结果：产能不足、物料太晚、DueDate无法满足 → Success=true + Unscheduled
        bool technicalFailure = scheduleResult.TechnicalFailure;
        string? errorMessage = technicalFailure ? scheduleResult.TechnicalFailureReason : null;

        // P1-03修复：Phase4 可能改变 Resource/Start/End，Phase3 的旧诊断结果已过时。
        // 在最终任务集合上重跑 Phase3 诊断，保证 ExplanationFacts 与最终修复结果一致。
        var finalDiagnostics = new PhaseThreeDiagnostics().Diagnose(
            request,
            new InitialScheduleResult { ScheduledTasks = allScheduledTasks },
            constraints);

        return new DomainSolveResult
        {
            Success = !technicalFailure,
            ErrorMessage = errorMessage,
            IsRoughCut = false,
            FinalTasks = allScheduledTasks,
            AllocationShares = allocationShares,
            UnscheduledTasks = unscheduledTasks,
            PhysicalPeggingDrafts = taskDependencies,
            ExplanationFacts = finalDiagnostics.ExplanationFacts,
            // B.2：求解过程追溯（非排程结果）—— 由 Phase2/4 经 ConstraintContext.TraceNotes 收集，此处导出给 2号位 落库/透传
            SolveTraceNotes = constraints.TraceNotes,
            // ④⑤⑨（0号位 2026-09-29 裁决 §十四第 4/5/9 项）：末端/整条无 Routing Stage 的纯内存时间节点。
            // 载体在 Core（2号位 落），值由 1号位 填 —— 见 StageTimingNodeBuilder 类注释。
            // ⚠ 这些节点**不进 FinalTasks**：结构上就没有 Task 载体，不生成 TaskNo/MES工单。
            DemandCompletions = StageTimingNodeBuilder.Build(request, constraints, allScheduledTasks),
            Summary = new SolveSummary
            {
                TotalDrafts = allScheduledTasks.Count + unscheduledTasks.Count,
                ScheduledCount = allScheduledTasks.Count,
                UnscheduledCount = unscheduledTasks.Count,
                ElapsedMs = 0, // 由 FiniteCapacitySolver 填充
                IssueCount = finalDiagnostics.ExplanationFacts.Count,
                UsedRoughCut = false
            }
        };
    }

    /// <summary>
    /// 判断某 Demand 是否因缺失生产部门 Context 而被排除（最小B）。
    /// 部门锁定在 Phase 1 完成，缺失 Context 的 Material 记入 constraints.MissingDepartmentContextMaterialIds；
    /// 此处按 LogicalDemandKey → MaterialId 反查，给 Unscheduled 结果补正确的 Reason。
    /// </summary>
    private bool IsMissingDepartmentContext(
        string demandKey,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var demand = request.LogicalProductionDemands
            .FirstOrDefault(d => d.LogicalDemandKey == demandKey);
        if (demand == null)
        {
            return false;
        }
        return constraints.MissingDepartmentContextMaterialIds.Contains(demand.MaterialId);
    }

    /// <summary>
    /// 判断某 Demand 是否因 StageSeq 全序冲突而被拒绝出计划（B-1，0号位 2026-09-29 裁决 §2.3）。
    /// 冲突在 Phase 1 <c>ValidateStageSequences</c> 登记（按 MaterialId），此处按
    /// LogicalDemandKey → MaterialId 反查，给 Unscheduled 结果补 Reason = STAGE_SEQUENCE_CONFLICT。
    /// </summary>
    private bool IsStageSequenceConflict(
        string demandKey,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var demand = request.LogicalProductionDemands
            .FirstOrDefault(d => d.LogicalDemandKey == demandKey);
        if (demand == null)
        {
            return false;
        }
        return constraints.StageSequenceConflictMaterialIds.Contains(demand.MaterialId);
    }

    /// <summary>
    /// 判断某 Demand 是否因「真实资源日历覆盖不足」而排不下（C-1，0号位 2026-09-28 裁决 §11.3）。
    ///
    /// 裁定原文（`0号位代码审核意见/20260928/APS_V1_批量正倒排执行批_0号位对1_2_3号位评估回执统一解读与裁决回复_v1.0_20260928.md:509-533`）：
    ///   「如果直到正式 Calendar 末端仍找不到：返回 `CALENDAR_COVERAGE_INSUFFICIENT` 或现有等价 Issue。
    ///     其业务含义是：**日历覆盖范围不足，APS 无法证明更远未来的合法产能**。
    ///     这**不是**『真实产能一定不存在』，也**不是**『普通延期』。」
    /// 同裁决 §11.1 已明确 PlanningEnd 不是硬终止边界（正排只受日历窗 `calWindow.End` 约束，见
    /// `PhaseTwoInitialScheduler.FindForwardSlot`）；§11.2 禁止制造虚拟 7×24 日历 / 低置信虚拟 Capacity Slot
    /// 作为正式计划 ⇒ 本判据**只**看真实 <see cref="ConstraintContext.ResourceCalendars"/>
    /// （Phase1 `BuildResourceCalendars` 仅装载 `IsAvailable` 窗），不引入任何合成产能。
    ///
    /// 【判据 —— 2026-10-07 按 0号位 P1-04 整改为**逐工序**】该 Demand 的**每一个**必须执行的
    ///   `FINITE_RESOURCE` 工序，都存在「其合法资格资源 × 该资源真实日历窗」能容纳**该工序**时长。
    ///   · **旧实现的 Bug**（0号位 2026-10-07 审核 P1-04）：原判据为「**任一**工序能塞进**任一**窗」
    ///     即返回覆盖足够（`durations.Any(d =&gt; d &lt;= windowMinutes)`）。多工序需求下会**漏判** ——
    ///     例：OP10=30min 塞得进 60min 窗、OP20=180min 永远塞不进，旧码因 OP10 通过而判「覆盖足够」，
    ///     但 OP20 实无合法日历窗 ⇒ 该 Demand 仍会因日历覆盖不足排不下。三条旧单测**全是单工序**，故未暴露。
    ///   · 工序身份用 **(ProductionDepartmentId, OperationCode)**：与 <c>EligibilityLookupKey</c> 的部门维度同口径，
    ///     消解「同物料同工序码跨部门」的歧义（2号位 实测 117 物料）。
    ///   · 只取 `FINITE_RESOURCE` 工序：`UNCONSTRAINED` / `WAIT_ONLY` 不占正式资源日历
    ///     （Phase2 对二者跳过资源找槽、Task `ResourceId=NULL`），纳入会误报。
    ///   · 与 Phase2 <c>FindForwardSlot</c> 同源：该方法对「该资源无日历」与「窗长装不下」**均**返回 null ⇒ 需求 Unscheduled。
    ///   · 保守性：任一工序在任一资格资源的任一窗装下即视为该工序可排；**不抢**缺部门 / StageSeq 冲突 / 其它根因的归属。
    /// </summary>
    private bool IsCalendarCoverageInsufficient(
        string demandKey,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var demand = request.LogicalProductionDemands
            .FirstOrDefault(d => d.LogicalDemandKey == demandKey);
        if (demand == null)
        {
            return false;
        }

        // P1-04（0号位 2026-10-07 裁决 §3.4）**Path 隔离**：判据必须**只对该需求已选中的 Path** 判 ——
        //   未选中 Path 的工序混入会过度误报（另一条备选路径的长工序与本需求无关）。
        //   选中路径 = Phase2 登记 → 需求固定路径 → 唯一那条；多路径且未登记 ⇒ 不抢日历归属。
        if (!constraints.TryGetDemandRoutePath(
                demandKey, demand.MaterialId, demand.RouteCode, demand.PathId, out var chosenPath))
        {
            return false;
        }

        // 该物料的**逐工序**时长（含 Setup，与 Phase2 占资源窗口径一致），**限定选中 Path**。
        // 时长源仍取 RoutingOperation（OperationNode 已按 item1 删除静态 SetupTime 字段）。
        var operations = request.RoutingOperations
            .Where(op => op.MaterialId == demand.MaterialId
                         && string.Equals(op.RouteCode, chosenPath.RouteCode, StringComparison.Ordinal)
                         && op.PathId == chosenPath.PathId)
            .Where(op => string.Equals(op.OperationPlanningMode, "FINITE_RESOURCE", StringComparison.Ordinal))
            .GroupBy(op => (op.ProductionDepartmentId, op.OperationCode))
            .Select(g => (
                DepartmentId: g.Key.ProductionDepartmentId,
                OperationCode: g.Key.OperationCode,
                DurationMinutes: g.Max(op => (double)(op.StandardDuration + op.SetupTime))))
            .ToList();
        if (operations.Count == 0)
        {
            return false;   // 无有限资源工序 ⇒ 根因不是日历
        }

        // 逐工序校验：**每个**有限资源工序都必须 ∃「其资格资源 × 其真实日历窗」装得下。
        foreach (var op in operations)
        {
            // 该工序有资格的全部资源（按 MaterialId + 部门 + RouteCode + PathId + 工序码归集，
            // 与 EligibilityLookupKey 口径一致 —— P1-04 Path 隔离：不得跨 Path 混用资格）
            var eligibleResourceIds = constraints.OperationResourceEligibility
                .Where(kv => kv.Key.MaterialId == demand.MaterialId
                             && kv.Key.ProductionDepartmentId == op.DepartmentId
                             && string.Equals(kv.Key.RouteCode, chosenPath.RouteCode, StringComparison.Ordinal)
                             && kv.Key.PathId == chosenPath.PathId
                             && kv.Key.OperationCode == op.OperationCode)
                .SelectMany(kv => kv.Value)
                .Distinct()
                .ToList();
            if (eligibleResourceIds.Count == 0)
            {
                return false;   // 无资格资源 ⇒ 根因是工艺/资格，不是日历（保守：不抢归属）
            }

            var anyWindowFits = false;
            foreach (var resourceId in eligibleResourceIds)
            {
                if (!constraints.ResourceCalendars.TryGetValue(resourceId, out var windows) || windows.Count == 0)
                {
                    continue;
                }

                if (windows.Any(w => (w.End - w.Start).TotalMinutes >= op.DurationMinutes))
                {
                    anyWindowFits = true;
                    break;
                }
            }

            if (!anyWindowFits)
            {
                // 该工序在**所有**资格资源的真实日历中均装不下 ⇒ 日历覆盖不足（按 §11.2 判）
                return true;
            }
        }

        // 所有有限资源工序都至少有一个可容纳窗 ⇒ 覆盖足够（保守返回 false）
        return false;
    }

    /// <summary>
    /// P1 整改（0号位 2026-10-09《未命名的Markdown文件 (3)(1).md》§一 第 3 行）：本需求是否**按 FORWARD 处理**。
    ///
    /// 判据 = Phase2 登记于 <see cref="ConstraintContext.ResolvedDirections"/> 的**本需求自决方向**，
    ///   **不是** Run 级原始策略值 —— 整改前 Phase5 直接比较 `SchedulingDirection == "FORWARD"`，
    ///   Run 级为 `AUTO` 时恒为 false ⇒ 即使本需求自决为 FORWARD 也永不进入压实 / 序列优化。
    ///
    /// 零回归：Run 级 `FORWARD`/`BACKWARD` ⇒ Phase2 登记的即原值 ⇒ 本判据与整改前**逐字等价**。
    /// 未登记（旁路 / 早期 `continue` 的需求）⇒ 回落 Run 级原值（保守，与整改前一致）。
    /// </summary>
    private static bool IsForwardDemand(string demandKey, DomainSolveRequest request, ConstraintContext constraints)
    {
        if (constraints.ResolvedDirections.TryGetValue(demandKey, out var resolved))
        {
            return string.Equals(resolved, "FORWARD", StringComparison.Ordinal);
        }

        return string.Equals(
            request.StrategySnapshot.Parameters.SchedulingDirection, "FORWARD", StringComparison.Ordinal);
    }

    /// <summary>
    /// NEW-P1-01（0号位 2026-10-09《APS_V1_3_20261009.md》§三 / §六.2）：本 **Task** 是否**按 FORWARD 处理**。
    ///
    /// 复审判词：「`RunBatchPlan` 循环结束仅返回**一个** `Direction`（= 最后一个落定Batch的方向），
    ///   不能代表前面的Batch」；「Phase5 `IsForwardDemand()` 仅按**需求Key**读取方向」⇒ 同一需求多批不同方向时
    ///   Phase5 会用**错批的方向**决定**所有** Task 的可移动性（应保护的 JIT 片段可能被前推 / Setup 重排）。
    ///
    /// 消费身份**优先 `ExecutionBatchDraftKey`**（冻结模型：**每个执行批独立**做 Direction + Routing +
    ///   Resource + Calendar + Setup 联合求解 ⇒ 方向是**批级**属性）；消费顺序：
    ///     ① <c>task.ExecutionBatchDraftKey</c> 命中 <see cref="ConstraintContext.ResolvedBatchDirections"/> ⇒ 用该值；
    ///     ② 回落到需求级 <see cref="ConstraintContext.ResolvedDirections"/>（= 既有 <see cref="IsForwardDemand"/> 口径）；
    ///     ③ 再回落 Run 级原始策略值。
    ///
    /// 零回归：单批需求 ⇒ 批键方向 == 需求级方向 ⇒ 与整改前**逐字等价**；
    ///   `ExecutionBatchDraftKey == null`（V1 常态旁路 / 未归批 Task）⇒ 直接走 ②③，与整改前完全一致。
    /// </summary>
    private static bool IsForwardTask(FinalTaskDraft task, DomainSolveRequest request, ConstraintContext constraints)
    {
        if (!string.IsNullOrEmpty(task.ExecutionBatchDraftKey)
            && constraints.ResolvedBatchDirections.TryGetValue(task.ExecutionBatchDraftKey!, out var batchDirection))
        {
            return string.Equals(batchDirection, "FORWARD", StringComparison.Ordinal);
        }

        return IsForwardDemand(task.SourceDraftId, request, constraints);
    }

    /// <summary>
    /// P1-05：Gap Compaction —— 保序前向压实（V1 最小实现，Level 3 次级优化）。
    /// 仅 FORWARD 方向执行；把 Task 拉进更早的日历可用空档，全程满足：
    /// - Level 0 硬约束：Calendar/占用含 Setup（复用 Phase4.FindForwardSlot 唯一实现）/不换资源（Eligibility 不变）/
    ///   Routing 依赖含 Lag（Split 多前序取最大）/物料多段 Quantity-Time/跨物料子件完成（块4）/
    ///   Execution-Firm-Frozen 不可移动（复用 Phase4.IdentifyImmovableTasks）/ExternalDomain 阻挡（BuildResourceOccupancy 已含）；
    /// - Level 1/2：只提前、不推迟 → Demand 履约与交期不可能恶化；
    /// - 稳定性：仅严格增益（新加工开始 &lt; 旧加工开始）才移动；同资源前序占用末端/后继占用起点作序界，绝不重排序；
    /// - 避免过早生产：floor 含物料可用时间与全部依赖下界；BACKWARD/MIXED JIT 锚点整体跳过不前拉。
    /// Setup 减少：保序压实不改变资源上的产品先后关系（FromMaterial 链不变），Setup 不恶化；
    /// 真正的 Setup 序列优化属 item1 夜间 FULL 有界搜索（待 2号位 规则通道接线）。
    /// </summary>
    private static void CompactGaps(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        // 避免过早生产：BACKWARD/MIXED 的 JIT 倒排锚点不前拉（V1 最小口径，只做 FORWARD 压实）。
        // P1 整改（0号位 2026-10-09《未命名的Markdown文件 (3)(1).md》§一 第 3 行）：判据由 **Run 级原始策略值**
        //   改为 **本需求自决方向**（Phase2 登记于 `constraints.ResolvedDirections`）。
        //   Run 级 FORWARD/BACKWARD ⇒ 全量同向 ⇒ 与整改前逐字一致；Run 级 AUTO ⇒ 逐需求判定 ——
        //   自决为 FORWARD 的需求参与压实，自决为 BACKWARD/MIXED 的（JIT 倒排锚点）仍整体跳过、绝不前拉。
        // NEW-P1-01（0号位 2026-10-09《APS_V1_3_20261009.md》§六.2）：门控**必须批级可见** ——
        //   同一需求多执行批可有不同方向；若只按**需求级**单值判，则「最后落定批为 BACKWARD」会把
        //   该需求**全部** Task（含 FORWARD 批）挡在压实之外 ⇒ 与「优化FORWARD批」要求不符。
        //   故本门控 = 需求级原有条件 **或** 任一 Task 按批级判定为 FORWARD（**并集**）：
        //   需求级为真时必然放行（对既有单批/无批键输入 ⇒ 与整改前逐字一致，零回归）。
        if (!request.LogicalProductionDemands.Any(d => IsForwardDemand(d.LogicalDemandKey, request, constraints))
            && !tasks.Any(t => IsForwardTask(t, request, constraints)))
        {
            return;
        }
        SolverDiagnostics.CountPhase5CompactionRun();

        var occupancy = PhaseFourLocalRepair.BuildResourceOccupancy(tasks, constraints);
        var immovable = PhaseFourLocalRepair.IdentifyImmovableTasks(request, tasks);
        var demandByKey = request.LogicalProductionDemands.ToDictionary(d => d.LogicalDemandKey);

        // P1 整改：非 FORWARD 需求（BACKWARD/MIXED 的 JIT 倒排锚点）**整体按不可移动处理** ⇒
        //   只作同资源序界，绝不前拉（避免过早生产）。这正是既有注释「BACKWARD/MIXED 锚点整体跳过」的
        //   落实处：整改前靠「整 Run 门控」达成，整改后靠「逐需求门控」达成
        //   ⇒ AUTO 下自决为 FORWARD 的需求得以压实，而非 FORWARD 需求仍被完整保护。
        foreach (var t in tasks)
        {
            // NEW-P1-01：**逐 Task** 按批级方向判定（`ExecutionBatchDraftKey` → 需求级 → Run 级）。
            //   ⇒ 同一需求下 BACKWARD 批的 JIT 锚点被完整保护，FORWARD 批仍可参与优化。
            if (!IsForwardTask(t, request, constraints))
            {
                immovable.Add(t.FinalDraftId);
            }
        }

        // P1-02 性能加固：三重索引消除每 Task 全表扫描（O(n²)→O(n·k)），10万 Task 标定前置。
        var index = TaskIndex.Build(tasks, demandByKey);

        // demand → 当前完成时间（跨物料动态 floor 用），随压实推进刷新
        var completionByDemand = tasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.PlannedEndTime));

        // 全局按加工开始时间升序处理：Routing 前序/跨物料子件/同 PI 连续份额先于当前 Task 处理，
        // 它们的 floor 用压实后的最新位置（只提前 → 后继 floor 只会更低，安全）。
        var order = tasks
            .Where(t => !immovable.Contains(t.FinalDraftId))
            .OrderBy(t => t.PlannedStartTime)
            .ToList();

        foreach (var task in order)
        {
            if (!demandByKey.TryGetValue(task.SourceDraftId, out var demand))
                continue;

            // 非资源工序 Task（UNCONSTRAINED/WAIT_ONLY，ResourceId=NULL，0号位 2026-09-22 裁决）不占资源：
            // 压实本质是「减少资源空档等待」，非资源 Task 无资源日历/无占用，不参与资源压实 → 时间保持不变（保守）。
            if (task.ResourceId is not int resourceId)
                continue;

            var setup = TimeSpan.FromMinutes((double)task.SetupTime);
            var totalDuration = (task.PlannedEndTime - task.PlannedStartTime) + setup;
            var myOccStart = task.PlannedStartTime - setup;

            // ── floor（占用开始时间口径，与 Phase2/Phase4 的 FindForwardSlot 用法一致）──
            var floor = request.PlanningStart;

            // 1) 物料可用时间（多段 Quantity-Time）；总量不足 → 保持原位不动
            var materialFloor = PhaseFourLocalRepair.GetMaterialEarliestTime(
                demand.AllocationSequence, demand.NetOutputQty, constraints, request.PlanningStart, out var sufficient);
            if (!sufficient) continue;
            if (materialFloor > floor) floor = materialFloor;

            // 2) Routing 前序（同 Demand，含 Lag；Split 多前序 Task 取最大完成）
            // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)
            if (constraints.TryGetRoutingGraph(task.MaterialId, task.RouteCode, task.PathId, out var graph) &&
                graph.Dependencies.TryGetValue(
                    OperationNodeKey.Of(task.StageCode, task.OperationCode), out var preds) &&
                index.BySource.TryGetValue(task.SourceDraftId, out var sameTasks))
            {
                foreach (var pred in preds)
                {
                    foreach (var predTask in sameTasks)
                    {
                        if (predTask.StageCode != pred.From.StageCode) continue;
                        if (predTask.OperationCode != pred.From.OperationCode) continue;
                        var predEnd = predTask.PlannedEndTime.AddMinutes((double)pred.LagTime);
                        if (predEnd > floor) floor = predEnd;
                    }
                }
            }

            // 3) 跨物料子件完成时间（块4：任务喂任务）；子件未排 → 保持原位不动
            var dynFloor = PhaseFourLocalRepair.GetDynamicMaterialFloor(
                task.SourceDraftId, constraints, completionByDemand, out var childUnavailable);
            if (childUnavailable) continue;
            if (dynFloor > floor) floor = dynFloor;

            // 4) P0-08：同 PI 自由份额不得早于连续份额完成（用压实后的最新完成时间；索引直达）
            if (!demand.IsContinuation && !string.IsNullOrEmpty(demand.ProductionInstructionNo) &&
                index.ContinuationByPi.TryGetValue(demand.ProductionInstructionNo!, out var contTasks))
            {
                foreach (var contTask in contTasks)
                {
                    if (contTask.SourceDraftId == task.SourceDraftId) continue;
                    if (contTask.PlannedEndTime > floor) floor = contTask.PlannedEndTime;
                }
            }

            // 5) 保序界：同资源不得越过前序 Task 占用末端（floor）/后继 Task 占用起点（ceil）——绝不重排序
            var orderCeil = DateTime.MaxValue;
            if (index.ByResource.TryGetValue(resourceId, out var sameRes))
            {
                foreach (var t in sameRes)
                {
                    if (ReferenceEquals(t, task)) continue;
                    var tOccStart = t.PlannedStartTime - TimeSpan.FromMinutes((double)t.SetupTime);
                    if (tOccStart < myOccStart)
                    {
                        // 前序：占用末端（= 加工结束，占用窗 [start-setup, end]）作 floor
                        if (t.PlannedEndTime > floor) floor = t.PlannedEndTime;
                    }
                    else if (tOccStart < orderCeil)
                    {
                        // 后继：占用起点（含其 Setup）作 ceil
                        orderCeil = tOccStart;
                    }
                }
            }

            // floor 已不早于当前位置 → 无前拉空间
            if (floor >= task.PlannedStartTime) continue;

            // ── 日历感知槽查找（复用 Phase4 唯一实现，含 Setup 占用）──
            // 先把自身窗口从 occupancy 摘除：自身当前窗会挡住「与自己部分重叠的更早空档」，
            // 导致合法前拉被误判无槽；拒绝移动时原样加回。
            List<TimeWindow>? windows = null;
            if (occupancy.TryGetValue(resourceId, out windows))
            {
                windows.RemoveAll(w => w.Start == myOccStart && w.End == task.PlannedEndTime);
            }

            var slot = PhaseFourLocalRepair.FindForwardSlot(
                floor, totalDuration, resourceId, constraints, occupancy, request.PlanningEnd);

            if (slot == null)
            {
                windows?.Add(new TimeWindow(myOccStart, task.PlannedEndTime));
                continue;
            }

            var newStart = slot.Value.Start + setup;   // slot.Start = 占用开始（含 Setup）
            var newEnd = slot.Value.End;

            // 仅严格增益 + 不越后继序界才移动（稳定性红线）
            if (newStart >= task.PlannedStartTime || newEnd > orderCeil)
            {
                windows?.Add(new TimeWindow(myOccStart, task.PlannedEndTime));
                continue;
            }

            // ── 落地移动：替换 Task（init-only → 重建）、刷新占用/索引/完成时间 ──
            var idx = tasks.IndexOf(task);
            if (idx < 0)
            {
                windows?.Add(new TimeWindow(myOccStart, task.PlannedEndTime));
                continue;
            }
            var movedTask = WithTimes(task, newStart, newEnd);
            tasks[idx] = movedTask;
            index.Replace(task, movedTask);   // FinalTaskDraft 不可变 → 索引同步换引用，防后续读到旧位置

            if (windows != null)
            {
                windows.Add(new TimeWindow(newStart - setup, newEnd));
            }
            else
            {
                occupancy[resourceId] = new List<TimeWindow> { new TimeWindow(newStart - setup, newEnd) };
            }

            completionByDemand[task.SourceDraftId] = index.BySource[task.SourceDraftId].Max(t => t.PlannedEndTime);
        }
    }

    /// <summary>P1-05：FinalTaskDraft 为 init-only，移动时重建实例（除时间外全字段原样保留，含 OperationSeq）。</summary>
    private static FinalTaskDraft WithTimes(FinalTaskDraft task, DateTime newStart, DateTime newEnd)
        => new FinalTaskDraft
        {
            FinalDraftId = task.FinalDraftId,
            SourceDraftId = task.SourceDraftId,
            MaterialId = task.MaterialId,
            FactoryId = task.FactoryId,
            StageCode = task.StageCode,
            OperationCode = task.OperationCode,
            OperationSeq = task.OperationSeq,
            TaskType = task.TaskType,
            ResourceId = task.ResourceId,
            ResourceCode = task.ResourceCode,
            RouteCode = task.RouteCode,
            PathId = task.PathId,
            Quantity = task.Quantity,
            PlannedProcessQty = task.PlannedProcessQty,
            UOM = task.UOM,
            PlannedStartTime = newStart,
            PlannedEndTime = newEnd,
            SetupTime = task.SetupTime,
            SetupSource = task.SetupSource,   // SetupSource 填充：压实仅移时间不重算 Setup → 透传原来源（2号位 原样落库）
            Priority = task.Priority,
            IsVirtual = task.IsVirtual,
            // v1.6 §1：重建 Task 必须逐字带走身份键（漏拷 = 下游静默丢执行批/连续份额身份）
            ExecutionBatchDraftKey = task.ExecutionBatchDraftKey,
            ContinuationKey = task.ContinuationKey,
            StageExecutionBatchDraftKey = task.StageExecutionBatchDraftKey,
            StageExecutionBatchQty = task.StageExecutionBatchQty,
            ExistingMESPlanReleaseId = task.ExistingMESPlanReleaseId,
            ExecutionLockId = task.ExecutionLockId
        };

    // ═══════════════════════════════════════════════════════════
    // P1-02 item1（夜间 FULL）：Setup 有界序列优化（v1.2 §14-§16）
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Setup 序列优化全局时钟守护（秒）——实现细节兜底，不进治理参数
    /// （0号位 红线「Setup 搜索不得破坏夜间总体性能目标」；相对约 15 分钟总目标留足余量。
    /// 若需治理可调，按 1号位 四件套流程另行提请，不自造治理字段）。
    /// </summary>
    private const int SetupSearchGlobalTimeLimitSeconds = 120;

    /// <summary>段内 Task 的预计算上下文（floor/deadline 全部冻结为原计划口径，保证段外消费者零重排）。</summary>
    private sealed class SegmentTaskInfo
    {
        public FinalTaskDraft Task = null!;
        public LogicalProductionDemand Demand = null!;
        public TimeSpan ProcessDuration;
        /// <summary>占用开始下界 = max(物料, 跨物料子件, 同PI连续份额, 段外Routing前序+Lag, 窗口起点, 段下界)</summary>
        public DateTime Floor;
        /// <summary>占用结束上界 = min(Routing后继原start-Lag, 跨物料父件原start-Lag, 同PI自由份额原start)；无消费者 = MaxValue</summary>
        public DateTime Deadline;
    }

    /// <summary>段序列模拟结果。</summary>
    private sealed record SegmentSimulation(
        bool Feasible,
        decimal TotalSetup,
        int NewDelayCount,          // 相对基线新增延期的 Task 数（Level 1/2 不恶化红线）
        double TotalDelayMinutes,   // 段内总延期分钟（不得高于基线）
        DateTime[] Ends,            // 按模拟顺序的占用结束时间
        decimal[] Setups,           // 按模拟顺序的 Setup 分钟
        SetupOptimizer.SetupOutcome[] Outcomes,   // SetupSource 填充：与 Setups 对齐的命中类型（序列优化回写用）
        decimal AnchorSetup,                      // 后锚点新 Setup（§15「段内最后→固定F」；无锚点=0）
        SetupOptimizer.SetupOutcome AnchorOutcome)  // SetupSource 填充：后锚点重算命中类型
    {
        public static readonly SegmentSimulation Infeasible =
            new(false, 0m, int.MaxValue, double.MaxValue, Array.Empty<DateTime>(), Array.Empty<decimal>(),
                Array.Empty<SetupOptimizer.SetupOutcome>(), 0m, SetupOptimizer.SetupOutcome.RuleMissing);
    }

    private static void OptimizeSetupSequences(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        // §17：白天 Candidate 局部优先，不做全天序列重排
        if (request.CandidateContext != null) return;
        // ── P1-02（0号位 2026-10-09《APS_V1_2_20261009.md》§三）：**撤销「全 Run 一票否决」** ──
        //   复审判词：Setup V1 要求在「**资源 × 连续生产窗口 × 固定锚点间可移动段**」做**有界局部**优化；
        //   别的资源上的倒排锚点**不应当然剥夺本资源独立 FORWARD 段**的优化机会；未排下、无 Task 的需求
        //   也不应禁止所有可行资源段优化。此前「任一非 FORWARD 需求 ⇒ 整 Run 返回」**超出冻结资源窗口范围**。
        //   整改：改**分段保护** —— 非 FORWARD 需求（JIT 倒排锚点）的 Task 一律并入 `immovable`
        //   ⇒ 它们成为**段边界锚点**（下方 `OptimizeSegment` 不跨锚点重排、锚点本身不动），
        //     本资源上的独立 FORWARD 段照常参与有界局部优化。判据与 `CompactGaps` 同源（`IsForwardDemand`）。
        // 计数口径 = **实际进入优化体**（有 ≥2 个 Task 可重排）；< 2 时无序列可优化 ⇒ 不计（不是「进入优化体」）。
        if (tasks.Count < 2) return;
        SolverDiagnostics.CountPhase5SetupOptimizationRun();

        // CompactGaps 可能已移动 Task（压实只维护自身占用图、未同步产品时间线）→
        // 从当前任务集合重建时间线，保证段首前产品查询基于最新位置。
        constraints.ProductTimeline = ResourceProductTimeline.FromTasks(tasks);

        var setupParams = request.StrategySnapshot.SolverStrategy.Setup;
        int searchBudget = setupParams.SetupSearchBudget > 0 ? setupParams.SetupSearchBudget : 500;
        int maxStaleTries = setupParams.SetupMaxNeighborhoodTries > 0 ? setupParams.SetupMaxNeighborhoodTries : 50;

        // 可重放：种子由 Run/版本派生（同一 Run 重放 → 同一搜索轨迹 → 同一结果）
        var rng = new DeterministicRandom(request.ScheduleRunId ?? request.PlanVersionId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var immovable = PhaseFourLocalRepair.IdentifyImmovableTasks(request, tasks);

        // P1-02：非 FORWARD 需求（JIT 倒排锚点）整体登记为不可移动 ⇒ 作**段边界锚点**，绝不跨段重排 / 不前拉。
        //   （撤销全 Run 一票否决后，局部保护改由本行 + 既有「段按锚点切分」共同达成。）
        foreach (var t in tasks)
        {
            // NEW-P1-01：**逐 Task** 按批级方向判定（`ExecutionBatchDraftKey` → 需求级 → Run 级）。
            //   ⇒ 同一需求下 BACKWARD 批的 JIT 锚点被完整保护，FORWARD 批仍可参与优化。
            if (!IsForwardTask(t, request, constraints))
            {
                immovable.Add(t.FinalDraftId);
            }
        }

        var demandByKey = request.LogicalProductionDemands.ToDictionary(d => d.LogicalDemandKey);
        var occupancy = PhaseFourLocalRepair.BuildResourceOccupancy(tasks, constraints);
        // P1-02 性能加固：段预计算的 deadline/floor 查询走三重索引（消除每 Task 全表扫描）
        var index = TaskIndex.Build(tasks, demandByKey);
        var completionByDemand = tasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.PlannedEndTime));

        // 跨物料 子→父 反向索引（deadline 用）
        var parentsByChild = new Dictionary<string, List<string>>();
        foreach (var kvp in constraints.CrossMaterialParentToChildren)
        {
            foreach (var child in kvp.Value)
            {
                if (!parentsByChild.TryGetValue(child, out var list))
                    parentsByChild[child] = list = new List<string>();
                list.Add(kvp.Key);
            }
        }

        foreach (var resourceGroup in tasks.GroupBy(t => t.ResourceId).ToList())
        {
            // 非资源工序 Task（ResourceId=NULL，不占资源）无资源日历、无 Setup 序列语义 → 整组跳过（保守：不动其时间）
            if (resourceGroup.Key is not int resourceId) continue;
            if (!constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar)) continue;

            // 生产日窗口（0号位 Q1 裁决：最大连续可用生产窗口；窗口只是搜索边界，不重置产品状态）
            var windows = SetupOptimizer.BuildProductionWindows(
                calendar.Select(w => (w.Start, w.End, true)));

            var resTasks = resourceGroup
                .OrderBy(t => t.PlannedStartTime - TimeSpan.FromMinutes((double)t.SetupTime))
                .ToList();

            foreach (var window in windows)
            {
                if (stopwatch.Elapsed.TotalSeconds > SetupSearchGlobalTimeLimitSeconds) return;

                var windowTasks = resTasks.Where(t =>
                {
                    var occStart = t.PlannedStartTime - TimeSpan.FromMinutes((double)t.SetupTime);
                    return occStart < window.End && t.PlannedEndTime > window.Start;
                }).ToList();
                if (windowTasks.Count < 2) continue;

                // §15：固定锚点（已执行/Frozen/Firm/其它不可移动）切割可移动段，不跨锚点重排
                FinalTaskDraft? prevAnchor = null;
                var current = new List<FinalTaskDraft>();
                foreach (var t in windowTasks)
                {
                    if (immovable.Contains(t.FinalDraftId))
                    {
                        if (current.Count >= 2)
                            OptimizeSegment(current, prevAnchor, t, window, resourceId, tasks, request,
                                constraints, demandByKey, completionByDemand, parentsByChild, occupancy, index,
                                searchBudget, maxStaleTries, rng, stopwatch);
                        current = new List<FinalTaskDraft>();
                        prevAnchor = t;
                    }
                    else
                    {
                        current.Add(t);
                    }
                }
                if (current.Count >= 2)
                    OptimizeSegment(current, prevAnchor, null, window, resourceId, tasks, request,
                        constraints, demandByKey, completionByDemand, parentsByChild, occupancy, index,
                        searchBudget, maxStaleTries, rng, stopwatch);
            }
        }
    }

    private static void OptimizeSegment(
        List<FinalTaskDraft> segment,
        FinalTaskDraft? prevAnchor,
        FinalTaskDraft? nextAnchor,
        (DateTime Start, DateTime End) window,
        int resourceId,
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        Dictionary<string, DateTime> completionByDemand,
        Dictionary<string, List<string>> parentsByChild,
        Dictionary<int, List<TimeWindow>> occupancy,
        TaskIndex index,
        int searchBudget,
        int maxStaleTries,
        DeterministicRandom rng,
        System.Diagnostics.Stopwatch stopwatch)
    {
        var segmentIds = segment.Select(t => t.FinalDraftId).ToHashSet();

        // P1-02 验收观测（0号位 2026-10-09《APS_V1_2_20261009.md》§五.5）：本段**确实进入**有界局部优化体。
        //   段由调用方保证「≥2 个 Task 且全部可移动（不含锚点）」⇒ 混合方向下 FORWARD 独立段可达此处；
        //   BACKWARD/MIXED 需求整体在 `immovable` 内 ⇒ 只会作**锚点**切分，不会形成本调用。纯观测，不参与判定。
        SolverDiagnostics.CountPhase5SetupSegmentOptimized();
        var segmentLowerBound = prevAnchor != null ? prevAnchor.PlannedEndTime : window.Start;

        // ── 预计算段内 Task 的 floor/deadline（全部冻结为原计划口径）──
        var infos = new List<SegmentTaskInfo>(segment.Count);
        foreach (var task in segment)
        {
            if (!demandByKey.TryGetValue(task.SourceDraftId, out var demand)) return;  // 追溯不到需求 → 保守不优化该段

            var taskOccStart = task.PlannedStartTime - TimeSpan.FromMinutes((double)task.SetupTime);

            // floor：物料（不足则不早于原位）
            var floor = PhaseFourLocalRepair.GetMaterialEarliestTime(
                demand.AllocationSequence, demand.NetOutputQty, constraints, request.PlanningStart, out var sufficient);
            if (!sufficient) floor = taskOccStart;

            // floor：跨物料子件完成（子件未排则不早于原位）
            var crossFloor = PhaseFourLocalRepair.GetDynamicMaterialFloor(
                task.SourceDraftId, constraints, completionByDemand, out var childUnavailable);
            if (childUnavailable) crossFloor = taskOccStart;
            if (crossFloor > floor) floor = crossFloor;

            // floor：同 PI 连续份额完成（P0-08，原位置口径；索引直达）
            if (!demand.IsContinuation && !string.IsNullOrEmpty(demand.ProductionInstructionNo) &&
                index.ContinuationByPi.TryGetValue(demand.ProductionInstructionNo!, out var contTasks))
            {
                foreach (var contTask in contTasks)
                {
                    if (segmentIds.Contains(contTask.FinalDraftId)) continue;
                    if (contTask.SourceDraftId == task.SourceDraftId) continue;
                    if (contTask.PlannedEndTime > floor) floor = contTask.PlannedEndTime;
                }
            }

            // floor：段外 Routing 前序（段内前序由模拟链处理）
            // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)
            var taskNodeKey = OperationNodeKey.Of(task.StageCode, task.OperationCode);
            RoutingGraph? graph = null;
            constraints.TryGetRoutingGraph(task.MaterialId, task.RouteCode, task.PathId, out graph);
            if (graph != null && graph.Dependencies.TryGetValue(taskNodeKey, out var preds) &&
                index.BySource.TryGetValue(task.SourceDraftId, out var sameTasks))
            {
                foreach (var pred in preds)
                {
                    foreach (var predTask in sameTasks)
                    {
                        if (predTask.StageCode != pred.From.StageCode) continue;
                        if (predTask.OperationCode != pred.From.OperationCode) continue;
                        if (segmentIds.Contains(predTask.FinalDraftId)) continue;
                        var predEnd = predTask.PlannedEndTime.AddMinutes((double)pred.LagTime);
                        if (predEnd > floor) floor = predEnd;
                    }
                }
            }

            if (segmentLowerBound > floor) floor = segmentLowerBound;
            if (window.Start > floor) floor = window.Start;

            // deadline：所有以本 Task 为 floor 源的段外消费者（原位置口径 → 段外零重排即安全；索引直达）
            var deadline = DateTime.MaxValue;
            if (graph != null && index.BySource.TryGetValue(task.SourceDraftId, out var sameTasksForSucc))
            {
                foreach (var kvp in graph.Dependencies)
                {
                    foreach (var dep in kvp.Value.Where(d => d.From == taskNodeKey))
                    {
                        foreach (var succTask in sameTasksForSucc)
                        {
                            if (succTask.StageCode != kvp.Key.StageCode) continue;
                            if (succTask.OperationCode != kvp.Key.OperationCode) continue;
                            if (segmentIds.Contains(succTask.FinalDraftId)) continue;
                            var latest = succTask.PlannedStartTime.AddMinutes(-(double)dep.LagTime);
                            if (latest < deadline) deadline = latest;
                        }
                    }
                }
            }
            if (parentsByChild.TryGetValue(task.SourceDraftId, out var parents))
            {
                foreach (var parentKey in parents)
                {
                    constraints.CrossMaterialLagMinutes.TryGetValue((parentKey, task.SourceDraftId), out var crossLag);
                    if (!index.BySource.TryGetValue(parentKey, out var parentTasks)) continue;
                    foreach (var parentTask in parentTasks)
                    {
                        if (segmentIds.Contains(parentTask.FinalDraftId)) continue;
                        var latest = parentTask.PlannedStartTime.AddMinutes(-(double)crossLag);
                        if (latest < deadline) deadline = latest;
                    }
                }
            }
            if (demand.IsContinuation && !string.IsNullOrEmpty(demand.ProductionInstructionNo) &&
                index.FreeByPi.TryGetValue(demand.ProductionInstructionNo!, out var freeTasks))
            {
                foreach (var freeTask in freeTasks)
                {
                    if (segmentIds.Contains(freeTask.FinalDraftId)) continue;
                    if (freeTask.SourceDraftId == task.SourceDraftId) continue;
                    if (freeTask.PlannedStartTime < deadline) deadline = freeTask.PlannedStartTime;
                }
            }

            infos.Add(new SegmentTaskInfo
            {
                Task = task,
                Demand = demand,
                ProcessDuration = task.PlannedEndTime - task.PlannedStartTime,
                Floor = floor,
                Deadline = deadline
            });
        }

        // ── 摘除段内 Task + 后锚点的时间线登记，取段首前产品（跨窗口可追溯，§14.3 窗口不重置产品状态）──
        foreach (var info in infos)
            constraints.ProductTimeline.Remove(resourceId, info.Task.PlannedEndTime, info.Task.MaterialId);
        if (nextAnchor != null)
            constraints.ProductTimeline.Remove(resourceId, nextAnchor.PlannedEndTime, nextAnchor.MaterialId);

        var firstPrevProduct = constraints.ProductTimeline.GetPrevMaterial(resourceId, segmentLowerBound);

        // 重登记辅助（拒绝/结束时按给定位置放回）
        void RestoreTimeline(List<SegmentTaskInfo> order, DateTime[]? ends, decimal anchorSetup)
        {
            for (int i = 0; i < order.Count; i++)
            {
                var end = ends != null ? ends[i] : order[i].Task.PlannedEndTime;
                constraints.ProductTimeline.Place(resourceId, end, order[i].Task.MaterialId);
            }
            if (nextAnchor != null)
                constraints.ProductTimeline.Place(resourceId, nextAnchor.PlannedEndTime, nextAnchor.MaterialId);
        }

        // ── 基线模拟（原顺序必须可行，否则数据噪声/口径偏差 → 保守跳过该段）──
        var baseSim = SimulateSegment(infos, resourceId, nextAnchor, window.End, constraints,
            segmentLowerBound, firstPrevProduct, null);
        if (!baseSim.Feasible)
        {
            RestoreTimeline(infos, null, 0m);
            return;
        }

        var baselineEnds = new Dictionary<string, DateTime>();
        for (int i = 0; i < infos.Count; i++)
            baselineEnds[infos[i].Task.FinalDraftId] = baseSim.Ends[i];

        // ── 有界邻域搜索（§14.2：swap/insertion，不枚举全排列；预算/停滞双上限 + 全局时钟守护）──
        var bestOrder = infos;
        var bestSim = baseSim;
        int tries = 0, stale = 0;
        while (tries < searchBudget && stale < maxStaleTries &&
               stopwatch.Elapsed.TotalSeconds <= SetupSearchGlobalTimeLimitSeconds)
        {
            tries++;
            var candidate = new List<SegmentTaskInfo>(bestOrder);

            if (rng.Next(2) == 0)
            {
                // swap 相邻性无关的任意两件
                int a = rng.Next(candidate.Count), b = rng.Next(candidate.Count);
                if (a == b) { stale++; continue; }
                (candidate[a], candidate[b]) = (candidate[b], candidate[a]);
            }
            else
            {
                // insertion：取一件插到另一位置
                int a = rng.Next(candidate.Count);
                var item = candidate[a];
                candidate.RemoveAt(a);
                int pos = rng.Next(candidate.Count + 1);
                candidate.Insert(pos, item);
            }

            if (candidate.SequenceEqual(bestOrder, ReferenceEqualityComparer.Instance)) { stale++; continue; }

            var sim = SimulateSegment(candidate, resourceId, nextAnchor, window.End, constraints,
                segmentLowerBound, firstPrevProduct, baselineEnds);

            // §16 第六步：硬约束全过 + 不新增延期 + 总延期不升 + Setup 严格下降才接受（同等情况保持稳定）
            if (sim.Feasible &&
                sim.NewDelayCount == 0 &&
                sim.TotalDelayMinutes <= baseSim.TotalDelayMinutes + 1e-9 &&
                sim.TotalSetup < bestSim.TotalSetup)
            {
                bestOrder = candidate;
                bestSim = sim;
                stale = 0;
            }
            else
            {
                stale++;
            }
        }

        // ── 回写：序列变化或锚点 Setup 变化才动（稳定性红线）──
        bool orderChanged = !bestOrder.SequenceEqual(infos, ReferenceEqualityComparer.Instance);
        bool anchorSetupChanged = nextAnchor != null && bestSim.AnchorSetup != nextAnchor.SetupTime;

        if (!orderChanged && !anchorSetupChanged)
        {
            RestoreTimeline(infos, null, 0m);
            return;
        }

        if (occupancy.TryGetValue(resourceId, out var windows))
        {
            foreach (var info in infos)
            {
                var oldOccStart = info.Task.PlannedStartTime - TimeSpan.FromMinutes((double)info.Task.SetupTime);
                windows.RemoveAll(w => w.Start == oldOccStart && w.End == info.Task.PlannedEndTime);
            }
            if (nextAnchor != null)
            {
                var anchorOccStart = nextAnchor.PlannedStartTime - TimeSpan.FromMinutes((double)nextAnchor.SetupTime);
                windows.RemoveAll(w => w.Start == anchorOccStart && w.End == nextAnchor.PlannedEndTime);
            }
        }

        for (int i = 0; i < bestOrder.Count; i++)
        {
            var info = bestOrder[i];
            var newEnd = bestSim.Ends[i];
            var newSetup = bestSim.Setups[i];
            var newStart = newEnd - info.ProcessDuration;          // 加工开始
            var occStart = newStart - TimeSpan.FromMinutes((double)newSetup);

            var idx = tasks.IndexOf(info.Task);
            if (idx >= 0)
            {
                var rebuilt = WithSetupAndTimes(info.Task, newStart, newEnd, newSetup,
                    SetupOptimizer.SetupOutcomeToSource(bestSim.Outcomes[i]));   // SetupSource 填充：段内命中类型
                tasks[idx] = rebuilt;
                index.Replace(info.Task, rebuilt);   // 索引同步（后续段/窗口读到新位置）
            }

            if (occupancy.TryGetValue(resourceId, out var w))
                w.Add(new TimeWindow(occStart, newEnd));
            constraints.ProductTimeline.Place(resourceId, newEnd, info.Task.MaterialId);
        }

        if (nextAnchor != null)
        {
            // §11.1/§15：锚点位置不动，但前产品变化 → Setup 重算回写（占用起点随之变化）
            var rebuiltAnchor = WithSetupAndTimes(nextAnchor, nextAnchor.PlannedStartTime, nextAnchor.PlannedEndTime, bestSim.AnchorSetup,
                SetupOptimizer.SetupOutcomeToSource(bestSim.AnchorOutcome));   // SetupSource 填充：锚点重算命中类型
            var anchorIdx = tasks.IndexOf(nextAnchor);
            if (anchorIdx >= 0) tasks[anchorIdx] = rebuiltAnchor;
            index.Replace(nextAnchor, rebuiltAnchor);

            var newAnchorOccStart = nextAnchor.PlannedStartTime - TimeSpan.FromMinutes((double)bestSim.AnchorSetup);
            if (occupancy.TryGetValue(resourceId, out var w))
                w.Add(new TimeWindow(newAnchorOccStart, nextAnchor.PlannedEndTime));
            constraints.ProductTimeline.Place(resourceId, nextAnchor.PlannedEndTime, nextAnchor.MaterialId);
        }
    }

    /// <summary>
    /// 段序列前向模拟：按给定顺序在 [Floor, Deadline] 与日历窗内紧致放置，逐件解析 Setup（前产品=序列邻接）。
    /// baselineEnds=null 表示基线运行（记录绝对延期）；否则统计「新增延期」数（相对基线）。
    /// </summary>
    private static SegmentSimulation SimulateSegment(
        List<SegmentTaskInfo> order,
        int resourceId,
        FinalTaskDraft? nextAnchor,
        DateTime windowEnd,
        ConstraintContext constraints,
        DateTime segmentLowerBound,
        int? firstPrevProduct,
        Dictionary<string, DateTime>? baselineEnds)
    {
        int n = order.Count;
        var ends = new DateTime[n];
        var setups = new decimal[n];
        var outcomes = new SetupOptimizer.SetupOutcome[n];   // SetupSource 填充：与 setups 对齐的命中类型
        decimal totalSetup = 0m;
        int newDelayCount = 0;
        double totalDelayMinutes = 0;

        int? prevProduct = firstPrevProduct;
        var prevOccEnd = segmentLowerBound;
        // 0号位 2026-09-29 裁决 §5.3：段内键同样升维 —— 原 (SourceDraftId, OperationCode) 在
        // 同码跨 Stage 时会把两个 Stage 的同名工序当成同一节点（局部字典，编译器不报错 ⇒ 「半升级」陷阱）。
        var simEndsByKey = new Dictionary<(string SourceDraftId, string StageCode, string OperationCode), DateTime>();   // 段内依赖链
        var segmentKeys = order.Select(x => (x.Task.SourceDraftId, x.Task.StageCode, x.Task.OperationCode)).ToHashSet();

        for (int i = 0; i < n; i++)
        {
            var info = order[i];

            var setupRes = SetupOptimizer.ResolveSetupCore(
                info.Task.OperationCode, resourceId, prevProduct, info.Task.MaterialId,
                constraints.SetupExactRules, constraints.SetupDefaultRules);
            var setup = setupRes.SetupMinutes;

            var occStart = info.Floor > prevOccEnd ? info.Floor : prevOccEnd;

            // 段内 Routing 前序链：前序在段内必须位于序列更早处（否则违反工艺顺序 → 不可行）
            // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)
            if (constraints.TryGetRoutingGraph(
                    info.Task.MaterialId, info.Task.RouteCode, info.Task.PathId, out var graph) &&
                graph.Dependencies.TryGetValue(
                    OperationNodeKey.Of(info.Task.StageCode, info.Task.OperationCode), out var preds))
            {
                foreach (var pred in preds)
                {
                    var key = (info.Task.SourceDraftId, pred.From.StageCode, pred.From.OperationCode);
                    if (simEndsByKey.TryGetValue(key, out var predEnd))
                    {
                        var floorFromPred = predEnd.AddMinutes((double)pred.LagTime);
                        if (floorFromPred > occStart) occStart = floorFromPred;
                    }
                    else if (segmentKeys.Contains(key))
                    {
                        return SegmentSimulation.Infeasible;   // 前序被排到了后面 → 工艺顺序破坏
                    }
                }
            }

            var procStart = occStart.AddMinutes((double)setup);
            var end = procStart + info.ProcessDuration;

            // 硬界：日历窗口末端 / 自身 deadline（下游消费者原位置）
            if (end > windowEnd || end > info.Deadline)
                return SegmentSimulation.Infeasible;

            ends[i] = end;
            setups[i] = setup;
            outcomes[i] = setupRes.Outcome;   // SetupSource 填充：记录命中类型（回写时映射为大写 5 值）
            totalSetup += setup;
            simEndsByKey[(info.Task.SourceDraftId, info.Task.StageCode, info.Task.OperationCode)] = end;

            // 延期检查（Level 1/2 不恶化；M5 第一批：覆盖交期口径）
            var due = constraints.EffectiveDue(info.Demand);
            if (end > due)
            {
                totalDelayMinutes += (end - due).TotalMinutes;
                if (baselineEnds != null &&
                    baselineEnds.TryGetValue(info.Task.FinalDraftId, out var baseEnd) &&
                    baseEnd <= due)
                {
                    newDelayCount++;
                }
            }

            prevProduct = info.Task.MaterialId;
            prevOccEnd = end;
        }

        // §15：后锚点 Setup 随段末产品重算（位置不动，占用起点前伸不得被段尾侵入）
        decimal anchorSetup = 0m;
        SetupOptimizer.SetupOutcome anchorOutcome = SetupOptimizer.SetupOutcome.RuleMissing;   // SetupSource 填充：默认值
        if (nextAnchor != null)
        {
            var anchorRes = SetupOptimizer.ResolveSetupCore(
                nextAnchor.OperationCode, resourceId, prevProduct, nextAnchor.MaterialId,
                constraints.SetupExactRules, constraints.SetupDefaultRules);
            anchorSetup = anchorRes.SetupMinutes;
            anchorOutcome = anchorRes.Outcome;   // SetupSource 填充：记录锚点重算命中类型
            totalSetup += anchorSetup;

            var anchorOccStart = nextAnchor.PlannedStartTime - TimeSpan.FromMinutes((double)anchorSetup);
            if (prevOccEnd > anchorOccStart)
                return SegmentSimulation.Infeasible;
        }

        return new SegmentSimulation(true, totalSetup, newDelayCount, totalDelayMinutes, ends, setups, outcomes, anchorSetup, anchorOutcome);
    }

    /// <summary>序列优化回写：重建 FinalTaskDraft（新时间 + 新 Setup，其余字段原样保留）。</summary>
    private static FinalTaskDraft WithSetupAndTimes(FinalTaskDraft task, DateTime newStart, DateTime newEnd, decimal setupMinutes, string? setupSource)
        => new FinalTaskDraft
        {
            FinalDraftId = task.FinalDraftId,
            SourceDraftId = task.SourceDraftId,
            MaterialId = task.MaterialId,
            FactoryId = task.FactoryId,
            StageCode = task.StageCode,
            OperationCode = task.OperationCode,
            OperationSeq = task.OperationSeq,
            TaskType = task.TaskType,
            ResourceId = task.ResourceId,
            ResourceCode = task.ResourceCode,
            RouteCode = task.RouteCode,
            PathId = task.PathId,
            Quantity = task.Quantity,
            PlannedProcessQty = task.PlannedProcessQty,
            UOM = task.UOM,
            PlannedStartTime = newStart,
            PlannedEndTime = newEnd,
            SetupTime = setupMinutes,
            SetupSource = setupSource,   // SetupSource 填充：序列优化重算命中类型 → 大写 5 值（2号位 原样落库）
            Priority = task.Priority,
            IsVirtual = task.IsVirtual,
            // v1.6 §1：重建 Task 必须逐字带走身份键（漏拷 = 下游静默丢执行批/连续份额身份）
            ExecutionBatchDraftKey = task.ExecutionBatchDraftKey,
            ContinuationKey = task.ContinuationKey,
            StageExecutionBatchDraftKey = task.StageExecutionBatchDraftKey,
            StageExecutionBatchQty = task.StageExecutionBatchQty,
            ExistingMESPlanReleaseId = task.ExistingMESPlanReleaseId,
            ExecutionLockId = task.ExecutionLockId
        };

    /// <summary>
    /// P1-02 性能加固：Phase5 任务集合三重索引（按需求键 / 按资源 / 按同 PI 连续-自由分组），
    /// 消除 CompactGaps 与 OptimizeSegment 中每 Task 的全表扫描（O(n²) → O(n·k)，10万 Task 标定前置）。
    /// FinalTaskDraft 为 init-only 不可变——移动重建后必须调 <see cref="Replace"/> 同步索引引用，
    /// 否则后续段/后续 Task 读到旧位置（正确性红线）。PI 归属经 FinalDraftId 反查（移动不改变该 Id）。
    /// </summary>
    private sealed class TaskIndex
    {
        /// <summary>SourceDraftId（需求键）→ 该需求全部 Task（多工序/Split 多件）。</summary>
        public Dictionary<string, List<FinalTaskDraft>> BySource { get; } = new();

        /// <summary>ResourceId → 该资源全部 Task（保序界/deadline 扫描用）。
        /// 仅登记非空资源 Task：非资源工序 Task（ResourceId=NULL）不占资源，不入资源桶（0号位 2026-09-22 裁决）。</summary>
        public Dictionary<int, List<FinalTaskDraft>> ByResource { get; } = new();

        /// <summary>PI → 连续份额 Task（P0-08 自由份额 floor 用）。</summary>
        public Dictionary<string, List<FinalTaskDraft>> ContinuationByPi { get; } = new();

        /// <summary>PI → 自由份额 Task（连续份额 deadline 用）。</summary>
        public Dictionary<string, List<FinalTaskDraft>> FreeByPi { get; } = new();

        // FinalDraftId → PI / 是否连续（Replace 时 O(1) 定位 PI 桶，避免全桶扫描）
        private readonly Dictionary<string, (string Pi, bool IsContinuation)> _piByDraftId = new();

        public static TaskIndex Build(List<FinalTaskDraft> tasks, Dictionary<string, LogicalProductionDemand> demandByKey)
        {
            var index = new TaskIndex();
            foreach (var t in tasks)
            {
                if (!index.BySource.TryGetValue(t.SourceDraftId, out var bySource))
                    index.BySource[t.SourceDraftId] = bySource = new List<FinalTaskDraft>();
                bySource.Add(t);

                // 非资源工序 Task（ResourceId=NULL）不占资源 → 不进资源桶（资源桶只承载真实资源占用语义）
                if (t.ResourceId is int rid)
                {
                    if (!index.ByResource.TryGetValue(rid, out var byResource))
                        index.ByResource[rid] = byResource = new List<FinalTaskDraft>();
                    byResource.Add(t);
                }

                if (demandByKey.TryGetValue(t.SourceDraftId, out var d) &&
                    !string.IsNullOrEmpty(d.ProductionInstructionNo))
                {
                    var pi = d.ProductionInstructionNo!;
                    var bucket = d.IsContinuation ? index.ContinuationByPi : index.FreeByPi;
                    if (!bucket.TryGetValue(pi, out var list))
                        bucket[pi] = list = new List<FinalTaskDraft>();
                    list.Add(t);
                    index._piByDraftId[t.FinalDraftId] = (pi, d.IsContinuation);
                }
            }
            return index;
        }

        /// <summary>Task 重建后同步换引用（FinalDraftId 不变，资源/需求/PI 归属均不变）。</summary>
        public void Replace(FinalTaskDraft oldTask, FinalTaskDraft newTask)
        {
            ReplaceIn(BySource, oldTask.SourceDraftId, oldTask, newTask);
            // 资源桶只含非空资源 Task（NULL 未登记 → 无桶可换引用）
            if (oldTask.ResourceId is int rid)
                ReplaceIn(ByResource, rid, oldTask, newTask);
            if (_piByDraftId.TryGetValue(oldTask.FinalDraftId, out var piInfo))
            {
                ReplaceIn(piInfo.IsContinuation ? ContinuationByPi : FreeByPi, piInfo.Pi, oldTask, newTask);
            }
        }

        private static void ReplaceIn<TKey>(Dictionary<TKey, List<FinalTaskDraft>> dict, TKey key,
            FinalTaskDraft oldTask, FinalTaskDraft newTask) where TKey : notnull
        {
            if (!dict.TryGetValue(key, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], oldTask))
                {
                    list[i] = newTask;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 生成 AllocationTaskShare（追溯机制）
    /// 文档：§五 5.2
    /// P0-14修复（0号位严重Bug反馈）：只有末端Task记录净产出份额，串行前序通过TaskDependency追溯
    /// 闭合检查：Σ ShareQty = 该Allocation需制造的NetOutputQty
    /// P0-04/P0-05修复：接收 merge 血缘，闭合目标改为该 Allocation 下所有 Demand 的 NetOutputQty 之和，
    /// 并按末端Task的真实 Demand 构成归并（修复合批M:N与多切片闭合错误）。
    /// </summary>
    private List<AllocationTaskShare> GenerateAllocationShares(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> mergeLineage)
    {
        var shares = new List<AllocationTaskShare>();

        // P0-04/P0-05修复：按 LogicalDemandKey 建索引，供 merge 血缘展开
        var demandByKey = request.LogicalProductionDemands
            .ToDictionary(d => d.LogicalDemandKey);

        // 构建TaskDependency，用于识别末端Task
        var downstreamTasks = new HashSet<string>();
        foreach (var demandGroup in tasks.GroupBy(t => t.SourceDraftId))
        {
            if (!demandByKey.TryGetValue(demandGroup.Key, out var demand))
                continue;

            // P0-04（反证 ⑧）：末端判定同源**逐批** —— 多批时按批键分区 + 按**批键**取图，
            //   否则需求级图（多批时 `ChosenRoutePaths` 未登记，会回落需求声明路径）可能与本批实际路径不符
            //   ⇒ 末端集合算错 ⇒ AllocationTaskShare 归错 Task。
            foreach (var batchTasks in PartitionByExecutionBatch(demandGroup))
            {
                if (!TryGetGraphForBatch(constraints, FirstBatchKey(batchTasks), demand, out var routingGraph))
                    continue;

                // 标记所有有downstream的Task（非末端）。
                // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)。
                // 同一物料的不同 Stage 可能出现**相同 OperationCode**（2号位 实测 117 物料）。
                // 边端点升维后 dep.From.StageCode 直接可用 ⇒ **取消**原先「Stage 不可得则退回单键匹配」
                // 的兜底：那个兜底正是单键任取（会把别的 Stage 的同名 Task 误标为非末端 ⇒ AllocationTaskShare 归错 Task）。
                foreach (var depList in routingGraph.Dependencies.Values)
                {
                    foreach (var dep in depList)
                    {
                        var upstreamTask = batchTasks.FirstOrDefault(t =>
                            t.OperationCode == dep.From.OperationCode &&
                            string.Equals(t.StageCode, dep.From.StageCode, StringComparison.Ordinal));

                        if (upstreamTask != null)
                        {
                            downstreamTasks.Add(upstreamTask.FinalDraftId);
                        }
                    }
                }
            }
        }

        // P0-05修复：闭合目标 = 每个 Allocation 下所有 Demand 的 NetOutputQty 之和
        var allocationTotalQty = request.LogicalProductionDemands
            .GroupBy(d => d.AllocationSequence)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.NetOutputQty));

        // P0-05修复：展开每个末端Task的真实 (Demand, Qty) 构成（含 merge 血缘），按 Allocation 归并贡献
        // ── 2026-10-08 可重放性修复：**确定性任务排序键** ──
        //   背景（实测发现，非推测）：`FinalDraftId = Guid.NewGuid().ToString()`（Phase2/Phase4 共 5 处）
        //   ⇒ **每次运行都不同**。而下方份额分摊用 `OrderBy(x => x.TaskId)`（TaskId = FinalDraftId）
        //   决定「**哪一个 Task 吸收 3 位小数残差**」⇒ **同输入跑两次，份额落到的 Task 不同**。
        //   实测：同参两次求解，`T/S/U` 与 `ΣShare/ΣQty` 全部相同，但逐任务份额与业务签名 hash 不同。
        //   这与既有冻结预期冲突：`DeterministicRandom` 明示「同一 Run 重放 → 同一结果」；
        //   且 0号位 §十五 第 7 项要求「优化前后同输入、同输出业务结果一致性对照」——
        //   **不确定就无从对照**。故此处改用稳定业务复合键排序（**不改分摊公式、不改数量、不改 ΣShare**）。
        //   注：本修复只改变「残差归属哪个 Task」（量级 ≤ 0.001），不改变任何数量的总和。
        var taskSortKeys = tasks.ToDictionary(
            t => t.FinalDraftId,
            t => $"{t.SourceDraftId}|{t.StageCode}|{t.OperationCode}|{t.RouteCode}|{t.PathId}"
               + $"|{t.ExecutionBatchDraftKey}|{t.PlannedStartTime:O}|{t.PlannedEndTime:O}",
            StringComparer.Ordinal);

        var contributions = tasks
            .Where(t => !downstreamTasks.Contains(t.FinalDraftId))
            .SelectMany(t => GetTaskDemandComposition(t, demandByKey, mergeLineage)
                .Select(c => new { TaskId = t.FinalDraftId, AllocationSeq = c.Demand.AllocationSequence, Qty = c.Qty }))
            .GroupBy(c => c.AllocationSeq);

        foreach (var allocGroup in contributions)
        {
            var allocationSeq = allocGroup.Key;
            if (!allocationTotalQty.TryGetValue(allocationSeq, out var expectedQty))
            {
                continue;
            }

            var taskQtys = allocGroup
                .GroupBy(c => c.TaskId)
                .Select(g => new { TaskId = g.Key, ContributionQty = g.Sum(c => c.Qty) })
                // 可重放性修复：按**稳定业务键**排序（原为随机 Guid 的 `TaskId`）。键缺失时回落 TaskId。
                .OrderBy(x => taskSortKeys.TryGetValue(x.TaskId, out var sortKey) ? sortKey : x.TaskId,
                         StringComparer.Ordinal)
                .ToList();

            if (taskQtys.Count == 0) continue;

            decimal totalContribution = taskQtys.Sum(x => x.ContributionQty);

            // ── 2026-10-08 复审 NEW-P0-01 整改：**撤销**「份额闭合目标 = `min(声明量, 落定量)`」──
            //   0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§二 / §十二 + 冻结基线原文：
            //     · `APS_有限产能排产…v1.7_20261005_Batch_Direction_AB_多Routing冻结回写版.md:1052`
            //       `AllocationQty = Σ AllocationTaskShare中的预计合格产出份额`
            //     · 同文 `:1026-1034`「有限产能硬错误，例如…**TaskShare数量不闭合**…必须使Domain失败」；
            //       `APS_Pegging…v1.6_20261005…md:748/:759/:790` 同义（「必须使Domain失败，不能Warning后发布」）。
            //   ⇒ **`AllocationQty`（声明数量）是权威**，份额的职责是「把该 Allocation 的数量**分摊**到已落定 Task 上」，
            //     故闭合目标**恒为 `expectedQty`**，**不随落定量变化**（改成 `min(声明, 落定)` 等于把 AllocationQty
            //     偷偷重定义成「已排出的数量」⇒ 冻结等式被架空 ⇒ 0号位 §二 判 NEW-P0-01）。
            //   这正是 0号位 §二 要的失败出口：**部分落定**时把整份声明数量分摊到仅存 Task 上
            //     ⇒ `ΣShare > Task.Quantity` ⇒ 下方 `ValidateHardResult` **第 3 项**判失败
            //     （实测报文：`Task … 的 ΣShare=10 超过 Quantity=5`）⇒ **Domain 失败**。
            //   绝不：把落定量当新目标 / 静默跳过该 Allocation（§二.4 明列禁止项）。
            var closureTarget = expectedQty;

            for (int i = 0; i < taskQtys.Count; i++)
            {
                var c = taskQtys[i];
                decimal shareQty;

                if (i == taskQtys.Count - 1)
                {
                    // 最后一个：补差，使 Σ 恰等于 `closureTarget`（消除 3 位小数舍入漂移）
                    var alreadyAllocated = shares
                        .Where(s => s.AllocationSequence == allocationSeq)
                        .Sum(s => s.ComponentQty);
                    shareQty = closureTarget - alreadyAllocated;
                }
                else if (totalContribution > 0)
                {
                    // 按**真实贡献占比**分摊 `closureTarget`（= 声明数量），与旧实现逐字一致
                    shareQty = Math.Round(closureTarget * c.ContributionQty / totalContribution, 3);
                }
                else
                {
                    shareQty = closureTarget / taskQtys.Count;
                }

                shares.Add(new AllocationTaskShare
                {
                    FinalDraftId = c.TaskId,
                    AllocationSequence = allocationSeq,
                    ComponentQty = shareQty
                });
            }
        }

        return shares;
    }

    /// <summary>
    /// P0-05修复：展开一个末端Task承载的真实 (Demand, Qty) 构成。
    /// 合并任务（M:N）会承载多个 Demand：merge 血缘里记录的份额 + SourceDraftId 锚点 Demand 的剩余份额。
    /// </summary>
    private List<(LogicalProductionDemand Demand, decimal Qty)> GetTaskDemandComposition(
        FinalTaskDraft task,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> mergeLineage)
    {
        var composition = new List<(LogicalProductionDemand, decimal)>();
        decimal mergedTotal = 0m;

        // 合并进来的 Demand（merge 血缘）
        if (mergeLineage.TryGetValue(task.FinalDraftId, out var merged))
        {
            foreach (var (demandKey, shareQty) in merged)
            {
                if (demandByKey.TryGetValue(demandKey, out var mergedDemand))
                {
                    composition.Add((mergedDemand, shareQty));
                    mergedTotal += shareQty;
                }
            }
        }

        // 锚点 Demand（SourceDraftId）承担剩余份额
        if (demandByKey.TryGetValue(task.SourceDraftId, out var anchorDemand))
        {
            var anchorQty = task.Quantity - mergedTotal;
            if (anchorQty > 0)
            {
                composition.Add((anchorDemand, anchorQty));
            }
        }

        return composition;
    }

    /// <summary>
    /// 生成 TaskDependency（基于 Routing 工序依赖关系）
    /// 文档：§五 5.3
    /// P0-13修复：根据工艺路线生成工序间的物理依赖关系
    /// </summary>
    private List<FinalTaskPeggingDraft> GenerateTaskDependencies(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var dependencies = new List<FinalTaskPeggingDraft>();

        // 按 SourceDraftId（需求）分组任务
        var tasksByDemand = tasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.PlannedStartTime).ToList());

        // 为每个需求生成工序依赖
        foreach (var demandGroup in tasksByDemand)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == demandGroup.Key);
            if (demand == null) continue;

            // ── P0-04（0号位 2026-10-07《未命名的Markdown文件 (7).md》§六 / §十三 反证 ⑧）：**逐批**建边 ──
            //   旧实现按需求整体取 `demandGroup.Value` ⇒ 同一需求的两批 Task 落在同一集合，
            //   数量不等时走下方「均摊」分支 ⇒ 给**全部上游 × 全部下游**建边 ⇒ **批间交叉**
            //   （Batch-001 的 OP10 连到 Batch-002 的 OP20）。分区后每批只在本批 Task 内配对，永不跨批。
            foreach (var batchTasks in PartitionByExecutionBatch(demandGroup.Value))
            {
                // 取图（RT-002：复用 Phase2 选中路径，不重选、不串 Path）：
                // 多批 ⇒ 按**批键**取本批选中路径（不串批）；无批键 ⇒ 回落需求级取图。
                if (!TryGetGraphForBatch(constraints, FirstBatchKey(batchTasks), demand, out var routingGraph))
                    continue;

                // 遍历工艺路线中的依赖关系
                // 第5轮修复：Split场景下，一个Operation可能对应多个Task，必须为所有组合建立Dependency
                foreach (var depList in routingGraph.Dependencies.Values)
                {
                    foreach (var dep in depList)
                    {
                        // 找到对应的所有上游Task和下游Task
                        // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)，
                        // 同码跨 Stage 时单键会把另一 Stage 的同名 Task 也算进来（血缘错接）。
                        var upstreamTasks = batchTasks
                            .Where(t => t.OperationCode == dep.From.OperationCode &&
                                        string.Equals(t.StageCode, dep.From.StageCode, StringComparison.Ordinal))
                            .ToList();
                        var downstreamTasks = batchTasks
                            .Where(t => t.OperationCode == dep.To.OperationCode &&
                                        string.Equals(t.StageCode, dep.To.StageCode, StringComparison.Ordinal))
                            .ToList();

                        // P0-06修复：Split场景不再做全量交叉积（那会把整条需求数量重复算到每条边）。
                        // 等数量时按下标一一配对（每条边取下游Task真实数量）；
                        // 数量不等时，把每个下游Task的数量按上游个数均摊，避免数量重复累计。
                        if (upstreamTasks.Count == 0 || downstreamTasks.Count == 0)
                        {
                            continue;
                        }

                        if (upstreamTasks.Count == downstreamTasks.Count)
                        {
                            for (int i = 0; i < upstreamTasks.Count; i++)
                            {
                                dependencies.Add(new FinalTaskPeggingDraft
                                {
                                    UpstreamFinalDraftId = upstreamTasks[i].FinalDraftId,
                                    DownstreamFinalDraftId = downstreamTasks[i].FinalDraftId,
                                    UpstreamMaterialId = demand.MaterialId,
                                    DownstreamMaterialId = demand.MaterialId,
                                    Quantity = downstreamTasks[i].Quantity,
                                    UOM = string.Empty,
                                    InheritedPriority = demand.DemandSequence,
                                    DependencyType = dep.DependencyType,
                                    LagTime = dep.LagTime
                                });
                            }
                        }
                        else
                        {
                            foreach (var downstreamTask in downstreamTasks)
                            {
                                decimal edgeQty = Math.Round(downstreamTask.Quantity / upstreamTasks.Count, 3);
                                foreach (var upstreamTask in upstreamTasks)
                                {
                                    dependencies.Add(new FinalTaskPeggingDraft
                                    {
                                        UpstreamFinalDraftId = upstreamTask.FinalDraftId,
                                        DownstreamFinalDraftId = downstreamTask.FinalDraftId,
                                        UpstreamMaterialId = demand.MaterialId,
                                        DownstreamMaterialId = demand.MaterialId,
                                        Quantity = edgeQty,
                                        UOM = string.Empty,
                                        InheritedPriority = demand.DemandSequence,
                                        DependencyType = dep.DependencyType,
                                        LagTime = dep.LagTime
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }

        // 跨物料 Task-to-Task 血缘（0号位 2026-09-10 裁决 §十七 明文：TaskDependency 必须由 1号位 生成）。
        // 上方循环只产「同物料工序间」边（输入 RoutingDependencies）；本行补「跨物料父-子」边。
        dependencies.AddRange(GenerateCrossMaterialDependencies(tasks, request, constraints));

        return dependencies;
    }

    /// <summary>
    /// P0-04（0号位 2026-10-07《未命名的Markdown文件 (7).md》§六 / §十三 反证 ⑧）：把一个需求名下的 Task
    /// 按**执行批**分区 —— 批内自成一条完整链，**批间不得交叉**（TaskDependency / 末端判定同源）。
    ///
    /// 分区规则（保守、零回归）：
    ///   · 该需求名下**至多一个**非空 `ExecutionBatchDraftKey` ⇒ **不分区**（单批 / 全无批键 ⇒ 与旧行为逐字一致）。
    ///     锚点继承任务在路径身份不可解时 `ExecutionBatchDraftKey = null`（`PhaseTwoInitialScheduler.cs:165`），
    ///     与同需求的单批新任务同组 ⇒ **不丢边**。
    ///   · 出现 **≥2 个**不同非空批键 ⇒ 按批键分区；`null` 批键单独成区 —— **不猜批身份**
    ///     （0号位 §六 明令不得用 `LogicalDemandKey` 反推批身份；无批键的 Task 本就不构成「一条完整 Path 的执行批」）。
    ///
    /// 分区内**保持原顺序**（`GroupBy` 保序）⇒ 下游「等数量按下标一一配对」仍按时间序配对，行为不变。
    /// </summary>
    private static List<List<FinalTaskDraft>> PartitionByExecutionBatch(IEnumerable<FinalTaskDraft> demandTasks)
    {
        var all = demandTasks.ToList();

        var distinctKeys = all
            .Select(t => t.ExecutionBatchDraftKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinctKeys.Count <= 1)
        {
            return new List<List<FinalTaskDraft>> { all };
        }

        return all
            .GroupBy(t => t.ExecutionBatchDraftKey ?? string.Empty, StringComparer.Ordinal)
            .Select(g => g.ToList())
            .ToList();
    }

    /// <summary>取该分区内的执行批键（分区内最多一个非空键；全空 ⇒ null）。</summary>
    private static string? FirstBatchKey(IEnumerable<FinalTaskDraft> batchTasks)
        => batchTasks.Select(t => t.ExecutionBatchDraftKey)
            .FirstOrDefault(k => !string.IsNullOrEmpty(k));

    /// <summary>
    /// 取本**批**的 Routing 图（RT-002：复用 Phase2 选中路径，不重选、不串 Path）：
    ///   有批键 ⇒ <see cref="ConstraintContext.TryGetBatchRoutingGraph"/>（批键未登记时其内部同样回落
    ///   「需求固定路径 → 唯一那条」，与需求级取图**逐字同源**，故锚点继承批零回归）；
    ///   无批键 ⇒ 需求级 <see cref="ConstraintContext.TryGetDemandRoutingGraph"/>。
    /// </summary>
    private static bool TryGetGraphForBatch(
        ConstraintContext constraints,
        string? batchKey,
        LogicalProductionDemand demand,
        [NotNullWhen(true)] out RoutingGraph? routingGraph)
    {
        if (!string.IsNullOrEmpty(batchKey))
        {
            return constraints.TryGetBatchRoutingGraph(
                batchKey, demand.MaterialId, demand.RouteCode, demand.PathId, out routingGraph);
        }

        return constraints.TryGetDemandRoutingGraph(
            demand.LogicalDemandKey, demand.MaterialId, demand.RouteCode, demand.PathId, out routingGraph);
    }

    /// <summary>
    /// 跨物料 Task-to-Task 血缘边（父件 ← 子件），消费 request.MaterialRequirementLinks。
    ///
    /// 口径依据（0号位《多层BOM任务喂任务血缘闭环正式裁决 v1.0 20260910》）：
    ///   • 归属（§十七 + §二十三-3）：必须由 1号位 生成，2号位 不得提前产 TaskId→TaskId。
    ///   • 端点（§十五 / §二十二）：上游 = 子件「达到供给阈值」完成的 Task。
    ///     V1 `ChildRequiredStageCode` 尚未上 1↔2 契约 → 按 §二十二 明文默认**保守**取
    ///     「子件完整 Routing 完成」= 子件该需求下最晚完成的 Task
    ///     （与 Phase2 demandCompletion 同口径，见 PhaseTwoInitialScheduler.cs:309）。
    ///     下游 = 父件该需求下最早开始的 Task（V1 保守：首道即需料）。
    ///     ⚠ 父件多工序时的「消费工序」口径待 2号位 明确（1号位 20260924 回执 §七）。
    ///   • 数量（§十六 / §十九-2.4）= **生产 Task 实际提供份额** = 子件生产需求 NetOutputQty；
    ///     **非** `RequiredQty`（= 父层对子件的完整需求量，如 A→B=200），否则会把库存/PI 承接
    ///     的份额误记为生产血缘（§二十三-9 禁止）。
    ///   • 判别（R4）：跨物料靠 `UpstreamMaterialId != DownstreamMaterialId`，不新增枚举。
    ///   • 子件或父件未排成 → 无血缘可产（父件本就 Unscheduled），跳过。
    /// </summary>
    private List<FinalTaskPeggingDraft> GenerateCrossMaterialDependencies(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var dependencies = new List<FinalTaskPeggingDraft>();

        var links = request.MaterialRequirementLinks;
        if (links == null || links.Count == 0)
        {
            return dependencies;
        }

        var tasksByDemand = tasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var demandByKey = request.LogicalProductionDemands
            .GroupBy(d => d.LogicalDemandKey)
            .ToDictionary(g => g.Key, g => g.First());

        // 同一条 (上游Task, 下游Task) 不重复产出（BOM 可能有多条同边）
        var emitted = new HashSet<(string Up, string Down)>();

        foreach (var link in links)
        {
            // 0910 §二十 Case B：子件全库存 / 采购占位 → ProducerLogicalDemandKey = null
            //（2号位 20260924 落码方案 (a)：link 仍产，以保留「父需求→子需求」真相）。
            // 此时无子件生产身份 → 无供给 Task → 按 §二十「不生成 B 生产 Task → A TaskDependency」跳过。
            // ⚠ 必须先判 null 再查字典：Dictionary.TryGetValue(null) 抛 ArgumentNullException
            //   （注意 HashSet<T>.Contains(null) 只是返回 false，两者语义不同，勿混用）。
            var producerKey = link.ProducerLogicalDemandKey;
            if (producerKey is null)
            {
                continue;
            }

            if (!tasksByDemand.TryGetValue(producerKey, out var producerTasks)
                || producerTasks.Count == 0)
            {
                continue;   // 子件未排成：无供给 Task
            }

            if (!tasksByDemand.TryGetValue(link.ConsumerLogicalDemandKey, out var consumerTasks)
                || consumerTasks.Count == 0)
            {
                continue;   // 父件未排成：无消费 Task
            }

            var upstreamTask = producerTasks.OrderByDescending(t => t.PlannedEndTime).First();
            var downstreamTask = consumerTasks.OrderBy(t => t.PlannedStartTime).First();

            // R4 判别式：同物料边由上方 RoutingDependency 循环承载，此处不得重复产出。
            if (upstreamTask.MaterialId == downstreamTask.MaterialId)
            {
                continue;
            }

            if (!emitted.Add((upstreamTask.FinalDraftId, downstreamTask.FinalDraftId)))
            {
                continue;
            }

            // §十六：数量只记由生产 Task 实际提供的新增生产份额。
            // 生产需求缺失（异常数据）时退化为完整需求量，仅作防御。
            var quantity = demandByKey.TryGetValue(producerKey, out var producerDemand)
                ? producerDemand.NetOutputQty
                : link.RequiredQty;

            // P1-12 跨厂滞后（分钟）。当前 2号位 血缘边恒同厂 → 恒 0（1号位 20260924 回执 §六 已对齐）。
            decimal lagMinutes = 0m;
            if (constraints.CrossMaterialLagMinutes.TryGetValue(
                    (link.ConsumerLogicalDemandKey, producerKey), out var lag))
            {
                lagMinutes = lag;
            }

            var inheritedPriority = demandByKey.TryGetValue(link.ConsumerLogicalDemandKey, out var consumerDemand)
                ? consumerDemand.DemandSequence
                : 0;

            dependencies.Add(new FinalTaskPeggingDraft
            {
                UpstreamFinalDraftId   = upstreamTask.FinalDraftId,
                DownstreamFinalDraftId = downstreamTask.FinalDraftId,
                UpstreamMaterialId     = upstreamTask.MaterialId,
                DownstreamMaterialId   = downstreamTask.MaterialId,
                Quantity               = quantity,
                UOM                    = string.Empty,   // 与同物料工序边同口径；如需子件 UOM 请 2号位 指明
                InheritedPriority      = inheritedPriority,
                DependencyType         = "ES",           // 0911 口径基线表：V1 只用 ES
                LagTime                = lagMinutes
            });
        }

        return dependencies;
    }

    /// <summary>
    /// 第4轮Item 10：Phase5最终硬约束校验（§十一）
    /// 验证FinalTask结果是否违反硬约束
    /// 第5轮修复：增加TaskDependency校验
    /// </summary>
    private ValidationResult ValidateHardResult(
        List<FinalTaskDraft> tasks,
        List<AllocationTaskShare> allocationShares,
        List<FinalTaskPeggingDraft> taskDependencies,
        DomainSolveRequest request,
        ConstraintContext constraints,
        HashSet<string> unscheduledDemandKeys,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> mergeLineage)
    {
        // 1. P0-05修复：验证每个Allocation的ΣShareQty == 该Allocation下所有Demand的NetOutputQty之和
        var allocationTotalNetOutput = request.LogicalProductionDemands
            .GroupBy(d => d.AllocationSequence)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.NetOutputQty));

        var sharesByAllocation = allocationShares
            .GroupBy(s => s.AllocationSequence)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── 2026-10-08 复审 NEW-P0-01 整改：**撤销**「含未排定需求的 Allocation 跳过本项闭合」的改法 ──
        //   0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§二.4 明列禁止项：「**静默跳过该Allocation**」；
        //   §十二 并指出「无法按客户DueDate完成不是Solver失败」与「Allocation只排出一部分数量且TaskShare不闭合
        //   也可以正式成功」**不是一个概念**（v1.7:1052 + :1026-1034 要求 `TaskShare数量不闭合` 必须使 Domain 失败）。
        //   ⇒ 本项**无条件**对所有**已产出份额**的 Allocation 执行。
        //   注：整条 Allocation 的所有需求都未排定时，它不产出任何 Task ⇒ 不产生份额行 ⇒ 不出现在 `sharesByAllocation`
        //   ⇒ 本项**天然不适用**（这不是跳过，而是「无份额可闭合」）⇒ 「产能不足 ⇒ Success=true + Unscheduled」的
        //   冻结口径（v1.7 §41）**不受影响**；只有「**部分**落定」（ΣShare < 声明 AllocationQty）才判失败。
        foreach (var allocKvp in sharesByAllocation)
        {
            var allocationSeq = allocKvp.Key;
            var shares = allocKvp.Value;

            if (!allocationTotalNetOutput.TryGetValue(allocationSeq, out var expectedQty))
            {
                // 该 Allocation 无 Demand 定义（异常数据），跳过闭合校验
                continue;
            }

            decimal totalShare = shares.Sum(s => s.ComponentQty);
            if (Math.Abs(totalShare - expectedQty) > 0.001m)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Allocation {allocationSeq} 数量闭合失败: ΣShareQty={totalShare}, 期望NetOutputQty={expectedQty}"
                };
            }

            // 2. 验证每个ShareQty > 0
            foreach (var share in shares)
            {
                if (share.ComponentQty <= 0)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Task {share.FinalDraftId} 的 ShareQty <= 0: {share.ComponentQty}"
                    };
                }
            }
        }

        // 3. 验证每个FinalTask的ΣShare不超过Task.Quantity
        var sharesByTask = allocationShares
            .GroupBy(s => s.FinalDraftId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.ComponentQty));

        foreach (var task in tasks)
        {
            if (sharesByTask.TryGetValue(task.FinalDraftId, out var totalTaskShare))
            {
                if (totalTaskShare > task.Quantity + 0.001m)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Task {task.FinalDraftId} 的 ΣShare={totalTaskShare} 超过 Quantity={task.Quantity}"
                    };
                }
            }
        }

        // 4. 验证FinalTask Resource硬互斥（同一资源上的时间段不重叠）
        // 非资源工序 Task（ResourceId=NULL）不占资源 → 不参与资源互斥校验（0号位 2026-09-22 裁决）
        var tasksByResource = tasks
            .Where(t => t.ResourceId is not null)
            .GroupBy(t => t.ResourceId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.PlannedStartTime).ToList());

        foreach (var resourceGroup in tasksByResource.Values)
        {
            for (int i = 0; i < resourceGroup.Count - 1; i++)
            {
                var current = resourceGroup[i];
                var next = resourceGroup[i + 1];

                // 当前Task的结束时间（包括Setup）必须 <= 下一个Task的开始时间（Setup之前）
                var currentOccupancyStart = current.PlannedStartTime.AddMinutes(-(double)current.SetupTime);
                var nextOccupancyStart = next.PlannedStartTime.AddMinutes(-(double)next.SetupTime);

                if (current.PlannedEndTime > nextOccupancyStart)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Resource {current.ResourceId} 时间冲突: Task {current.FinalDraftId} [{currentOccupancyStart:HH:mm:ss}-{current.PlannedEndTime:HH:mm:ss}] 与 Task {next.FinalDraftId} [{nextOccupancyStart:HH:mm:ss}-{next.PlannedEndTime:HH:mm:ss}] 重叠"
                    };
                }
            }
        }

        // 5. 验证Task不早于Material AvailableTime
        // 第5轮修复：必须按累计数量达到Task所需Qty，取真正Material Ready Time
        foreach (var task in tasks)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);
            if (demand == null) continue;

            if (constraints.MaterialAvailability.TryGetValue(demand.AllocationSequence, out var segments) && segments.Count > 0)
            {
                // 按时间排序Segments，累计数量直到满足Task需求
                var sortedSegments = segments.OrderBy(s => s.AvailableTime).ToList();
                decimal cumulativeQty = 0m;
                DateTime? materialReadyTime = null;

                // Task所需的物料数量（取PlannedProcessQty，因为这是实际加工需要的数量）
                var requiredQty = task.PlannedProcessQty;

                foreach (var segment in sortedSegments)
                {
                    cumulativeQty += segment.Quantity;
                    if (cumulativeQty >= requiredQty)
                    {
                        materialReadyTime = segment.AvailableTime;
                        break;
                    }
                }

                // 如果累计数量仍不足，取最后一个Segment的时间（物料始终不足）
                if (materialReadyTime == null && sortedSegments.Count > 0)
                {
                    materialReadyTime = sortedSegments.Last().AvailableTime;
                }

                if (materialReadyTime.HasValue && task.PlannedStartTime < materialReadyTime.Value)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Task {task.FinalDraftId} 开始时间 {task.PlannedStartTime:yyyy-MM-dd HH:mm:ss} 早于物料累计可用时间 {materialReadyTime.Value:yyyy-MM-dd HH:mm:ss}"
                    };
                }
            }
        }

        // 6. 验证Locked Anchor是否保持原地
        foreach (var lockedTask in constraints.LockedTasks.Values)
        {
            var correspondingTask = tasks
                .FirstOrDefault(t => t.SourceDraftId == lockedTask.DraftId &&
                                    t.ResourceId == lockedTask.ResourceId);

            if (correspondingTask != null)
            {
                // 验证时间是否与锁定时间一致
                if (Math.Abs((correspondingTask.PlannedStartTime - lockedTask.LockedStart).TotalSeconds) > 1 ||
                    Math.Abs((correspondingTask.PlannedEndTime - lockedTask.LockedEnd).TotalSeconds) > 1)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"Locked Task {lockedTask.DraftId} 时间未保持原地: 期望[{lockedTask.LockedStart:HH:mm:ss}-{lockedTask.LockedEnd:HH:mm:ss}], 实际[{correspondingTask.PlannedStartTime:HH:mm:ss}-{correspondingTask.PlannedEndTime:HH:mm:ss}]"
                    };
                }
            }
        }

        // 7. 第5轮修复：验证TaskDependency硬约束
        var taskDict = tasks.ToDictionary(t => t.FinalDraftId);
        foreach (var dep in taskDependencies)
        {
            // 检查上游Task是否存在
            if (!taskDict.TryGetValue(dep.UpstreamFinalDraftId, out var upstreamTask))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"TaskDependency引用的上游Task {dep.UpstreamFinalDraftId} 不存在"
                };
            }

            // 检查下游Task是否存在
            if (!taskDict.TryGetValue(dep.DownstreamFinalDraftId, out var downstreamTask))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"TaskDependency引用的下游Task {dep.DownstreamFinalDraftId} 不存在"
                };
            }

            // 检查时间约束：下游开始时间必须 >= 上游结束时间 + Lag
            var lagTime = TimeSpan.FromMinutes((double)dep.LagTime);
            var earliestDownstreamStart = upstreamTask.PlannedEndTime + lagTime;
            if (downstreamTask.PlannedStartTime < earliestDownstreamStart)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"TaskDependency违反时间约束: 下游Task {downstreamTask.FinalDraftId} 开始时间 {downstreamTask.PlannedStartTime:yyyy-MM-dd HH:mm:ss} 早于上游Task {upstreamTask.FinalDraftId} 结束时间 {upstreamTask.PlannedEndTime:yyyy-MM-dd HH:mm:ss} + Lag {dep.LagTime}分钟"
                };
            }
        }

        // 8. P0-04（0号位 2026-10-08 §七）：**最终物理数量闭合**（Final Quantity Closure 链）。
        //
        //    §七 原文：「对于每个 Execution Batch 及其工序链：不存在重复批身份下的第二套完整链；
        //    并且对同一需求：每个业务工序层面，Σ各 Execution Batch Quantity 必须与该 Demand 对应数量一致」。
        //
        //    实现要点：
        //    · 以 Task 的**真实需求构成**归集数量（`GetTaskDemandComposition`，含 merge 血缘），
        //      **不是** `Task.Quantity` —— 合批 Task 的 Quantity 是多个需求之和，直接相加会误判。
        //    · 逐「需求 × 工序（StageCode/OperationCode）」归集：`Task.Quantity` 在**同批各工序**上
        //      恒等于该批净产出 ⇒ 逐工序 Σ 应严格等于该需求 NetOutputQty。
        //    · 已完全排定的需求 ⇒ **严格闭合**（|Σ − NetOutputQty| ≤ 1e-3）。
        //      未排定（Phase2 未排 ∪ Phase4 未修）的需求 ⇒ 允许**小于**（部分执行批失败是合法业务结果，
        //      其未排程状态由 `UnscheduledTasks` 出口表达），但**绝不允许大于**
        //      —— 这正是 P0-03 重复生产（Batch-001 被 Phase2 与 Phase4 各生成一套）的数值指纹：
        //      10 件需求产出 15 件 ⇒ 逐工序 Σ=15 > 10 ⇒ 本条硬拒。
        //    · **刻意不做**「(需求, 批键, 工序) 唯一性」检查：Phase4 的有限 Split（`TrySplitOperation`）
        //      会在同一 (需求, 批键, 工序) 下**合法**产生多个部分 Task，按键唯一会误杀合法拆分。
        //      §七 的「第二套完整链」在本条下必然表现为**数量超额** ⇒ 由本条拦下，无需另设唯一性判据。
        var demandByKeyForClosure = request.LogicalProductionDemands
            .GroupBy(d => d.LogicalDemandKey)
            .ToDictionary(g => g.Key, g => g.First());

        var compositionByDemandOp =
            new Dictionary<(string DemandKey, string StageCode, string OperationCode), decimal>();

        foreach (var task in tasks)
        {
            if (task.IsVirtual) continue;   // 虚拟节点（StageTimingNode）非生产载体，不参与数量闭合

            foreach (var (compositionDemand, qty) in GetTaskDemandComposition(task, demandByKeyForClosure, mergeLineage))
            {
                var opKey = (compositionDemand.LogicalDemandKey, task.StageCode, task.OperationCode);
                compositionByDemandOp.TryGetValue(opKey, out var accumulated);
                compositionByDemandOp[opKey] = accumulated + qty;
            }
        }

        foreach (var demandGroup in compositionByDemandOp.GroupBy(kvp => kvp.Key.DemandKey))
        {
            if (!demandByKeyForClosure.TryGetValue(demandGroup.Key, out var closureDemand))
            {
                continue;   // 构成里出现请求外的需求键（异常数据）⇒ 跳过闭合校验
            }

            var expectedQty = closureDemand.NetOutputQty;
            var demandIsUnscheduled = unscheduledDemandKeys.Contains(demandGroup.Key);

            foreach (var opKvp in demandGroup)
            {
                var opQty = opKvp.Value;

                if (demandIsUnscheduled)
                {
                    if (opQty > expectedQty + 0.001m)
                    {
                        return new ValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"需求 {demandGroup.Key} 未排定却产出超额 FinalTask: "
                                + $"工序 ({opKvp.Key.StageCode}/{opKvp.Key.OperationCode}) Σ数量={opQty} > 需求 NetOutputQty={expectedQty}"
                                + "（重复执行批链 ⇒ 物理生产数量被放大）"
                        };
                    }
                }
                else if (Math.Abs(opQty - expectedQty) > 0.001m)
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"需求 {demandGroup.Key} 工序 ({opKvp.Key.StageCode}/{opKvp.Key.OperationCode}) 数量未闭合: "
                            + $"Σ各 Execution Batch Quantity={opQty}, 需求 NetOutputQty={expectedQty}"
                    };
                }
            }
        }

        return new ValidationResult { IsValid = true };
    }

    /// <summary>
    /// **P0-04 测试入口**（0号位 2026-10-08 §十二 第 8 行：「重复Batch ⇒ 必须被最终硬校验拒绝」）。
    ///
    /// 为什么要这个入口：P0-03 修好之后，「重复执行批链」在**正式 SolveAsync 路径上已不可达**
    ///   （Phase4 只修失败批 ⇒ 不会再生成已成功批的第二套链）。因此「硬校验确实会拒绝」这一性质
    ///   **只能**用**手工构造的重复 Task 集合**直接驱动校验器来证明 —— 否则该守卫永不被触发、也就永不被验证。
    ///   这不是「绕过正式路径」，而是「给守卫本身造一次真实输入」（§十二 另 8 条仍全部走 `SolveAsync`）。
    ///
    /// 仅供 `LPS.APS.Tests`（`InternalsVisibleTo`）使用；生产路径仍只经 <see cref="Compress"/>。
    /// </summary>
    internal static (bool IsValid, string ErrorMessage) ValidateHardResultForTest(
        List<FinalTaskDraft> tasks,
        List<AllocationTaskShare> allocationShares,
        List<FinalTaskPeggingDraft> taskDependencies,
        DomainSolveRequest request,
        ConstraintContext constraints,
        HashSet<string> unscheduledDemandKeys,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> mergeLineage)
    {
        var validation = new PhaseFiveCompression().ValidateHardResult(
            tasks, allocationShares, taskDependencies, request, constraints,
            unscheduledDemandKeys, mergeLineage);

        return (validation.IsValid, validation.ErrorMessage);
    }

    /// <summary>
    /// 验证结果
    /// </summary>
    private class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
