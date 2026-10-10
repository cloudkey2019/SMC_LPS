using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 4: 有界局部修复
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md》§六 Phase 4
///
/// 职责：
/// - 资源切换（在合格资源列表中尝试其他资源）
/// - 邻近时间槽（微调开始时间寻找空隙）
/// - 有限拆分（允许时拆成小批次）
/// - 局部重排（调整低优先级任务为高优先级让路）
/// </summary>
internal class PhaseFourLocalRepair
{
    /// <summary>
    /// 执行局部修复
    /// 文档：§六 Phase 4、§十一 Setup
    /// </summary>
    public RepairResult Repair(
        DomainSolveRequest request,
        InitialScheduleResult scheduleResult,
        DiagnosticsResult diagnostics,
        ConstraintContext constraints)
    {
        var result = new RepairResult();

        // 构建已排程任务的资源占用图（P0-06修复：包含ExternalDomain ResourceBlocks）
        var resourceOccupancy = BuildResourceOccupancy(scheduleResult.ScheduledTasks, constraints);

        // ═══════════════════════════════════════════════
        // P9完整实现：Candidate影响传播（§十三 Candidate局部重排）
        // ═══════════════════════════════════════════════
        // 0号位第8轮审核要求：
        // 1. 变化种子能关联已排任务（不只是未排程需求）
        // 2. 传播影响检测：任务变化 → 检查前后工序/同资源邻近/换型邻居/物料依赖
        // 3. 无真实变化时停止传播
        // 4. 固定不可移动约束：Execution/Firm/Frozen/Protection/外Domain阻挡
        // 5. Fallback兜底：局部修复超限 → 本Domain全部可移动任务重排

        // M5 第一批：Task 软目标（RunScope.TaskTargetOverrides）也触发传播修复——目标 Task 优先重排（软偏好贴近 TargetTime）。
        bool hasTaskTargets = request.RunScope?.TaskTargetOverrides is { Count: > 0 };
        // A 项（§69 明定的三个 Seed 之一）：资源变化（停机/加班/维修）也是传播起点——
        // 仅 ChangeSeedKeys/TaskTargetOverrides 无法承载「某资源可用性变了 → 其上已排 Task 需重评估」。
        // 口径（与 2号位 双向确认，见《白天候选配合事项 回执 v1.0》§一）：
        // ChangedResourceIds 只作传播起点，不得当事实源（§13.4）—— 不改变资源可用性判定，不新增机制。
        bool hasChangedResources = request.RunScope?.ChangedResourceIds is { Count: > 0 };
        if ((request.CandidateContext?.ChangeSeedKeys != null && request.CandidateContext.ChangeSeedKeys.Count > 0) || hasTaskTargets || hasChangedResources)
        {
            // Candidate模式：影响传播
            return RepairWithPropagation(request, scheduleResult, diagnostics, constraints, resourceOccupancy);
        }
        else
        {
            // Base/FULL模式：传统修复未排程需求
            return RepairUnscheduledDemands(request, scheduleResult, constraints, resourceOccupancy);
        }
    }

    /// <summary>
    /// Candidate模式：变化种子影响传播修复（P9完整实现）
    /// 文档：§十三 Candidate局部重排
    /// </summary>
    private RepairResult RepairWithPropagation(
        DomainSolveRequest request,
        InitialScheduleResult scheduleResult,
        DiagnosticsResult diagnostics,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy)
    {
        var result = new RepairResult();

        // 1. 识别受ChangeSeed直接影响的任务和需求
        var affectedTasks = new HashSet<string>(); // FinalDraftId
        var affectedDemands = new HashSet<string>(); // LogicalDemandKey

        foreach (var seedKey in request.CandidateContext?.ChangeSeedKeys ?? Array.Empty<string>())
        {
            // 关联已排任务：按DemandKey/AllocationSequence找到对应的已排Task
            foreach (var task in scheduleResult.ScheduledTasks)
            {
                var demand = request.LogicalProductionDemands
                    .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);

                if (demand != null &&
                    (demand.DemandKey == seedKey ||
                     demand.AllocationSequence.ToString() == seedKey ||
                     demand.LogicalDemandKey == seedKey))
                {
                    affectedTasks.Add(task.FinalDraftId);
                }
            }

            // 关联未排需求
            var matchedDemands = request.LogicalProductionDemands
                .Where(d => scheduleResult.UnscheduledDemandKeys.Contains(d.LogicalDemandKey) &&
                           (d.DemandKey == seedKey ||
                            d.AllocationSequence.ToString() == seedKey ||
                            d.LogicalDemandKey == seedKey))
                .Select(d => d.LogicalDemandKey);

            foreach (var key in matchedDemands)
            {
                affectedDemands.Add(key);
            }
        }

        // M5 第一批：Task 软目标 → 目标 Task 进影响集（传播优先处理，软偏好贴近 TargetTime；
        // 匹配键 (DraftId=SourceDraftId, OperationCode)）。
        //
        // ⛔ 「未达 TargetTime 的决策说明」产出点**此处暂不落码**（2026-09-28 复核后确认），三个前置未满足：
        //   ① 载体虽已落（2号位 r13494 `DomainSolveResult.SolveTraceNotes`），但 1号位 2026-09-24
        //      《白天候选配合事项 回执 v1.1》§2.3/§2.4 已向 2号位 提请 3 项并声明「三小项一并明确后…同批落两处产出点」：
        //      `ReasonCode` 位是否改名（撞域，1号位 倾向改名）/ `Level` 取值域 / `Key` 语义（失踪键 vs 产出点标识）。**2号位 尚未答复**。
        //   ② 「未达」判定口径在契约中**不存在**：`TaskTargetOverride`（DomainSolveRequest.cs:311）仅定义
        //      「(DraftId, OperationCode) → TargetTime」，未规定 TargetTime 对应 PlannedStartTime 还是 PlannedEndTime，
        //      亦无容差定义。自创即违反「要求 1号位 按某键判定时该键必须是契约既有字段」通则 → 投结论不投判据。
        //   ③ 产出点位置须在 Phase5 之后：Phase5 `CompactGaps`/`OptimizeSetupSequences` 会继续移动与重排序 Task，
        //      在 Phase4 记录「未达」会立即过时。
        //   ⇒ 三项齐备后，落点应在 PhaseFiveCompression 压实与序列优化之后、导出 `SolveTraceNotes` 之前。
        if (request.RunScope?.TaskTargetOverrides != null)
        {
            foreach (var target in request.RunScope.TaskTargetOverrides)
            {
                // ⚠ 键域说明（0号位 2026-09-29 §5.3 后复核）：契约 `TaskTargetOverride` 的键就是
                //   **(DraftId, OperationCode)**，契约内**没有 StageCode** ⇒ 1号位不得自行加维
                //   （通则：按某键判定时该键必须是契约既有字段；否则投结论不投判据）。
                //   同码跨 Stage 时会多收另一 Stage 的同名 Task ⇒ 影响集**偏大**（方向保守：只扩大重排范围，
                //   不会漏收、不产生错误计划）；代价是可能更早触及 §13.4 的 30% 警戒线而走 Fallback。
                //   若后续要精确化，须由 2号位 在契约层补 StageCode，不在 1号位 自决。
                foreach (var task in scheduleResult.ScheduledTasks.Where(t =>
                             t.SourceDraftId == target.DraftId && t.OperationCode == target.OperationCode))
                {
                    affectedTasks.Add(task.FinalDraftId);
                }
            }
        }

        // A 项（§69 明定的 Seed 之一）：ChangedResourceIds → 该资源上的已排 Task 进影响集。
        // 匹配键：RunScope.ChangedResourceIds（int，与 ResourceDefinition.ResourceId 同域，2号位 原样透传）
        //         ↔ FinalTaskDraft.ResourceId（int?，非资源工序 UNCONSTRAINED/WAIT_ONLY 为 NULL，自然不命中）。
        // ⚠ 规模提示：资源维度的影响集 = 该资源上「全部」已排 Task，单点即可能逼近 §13.4 的 30% 警戒线
        //   （maxAffectedTasks）→ 超线走既有 Fallback（本 Domain 可移动任务全量重排）；
        //   代价是性能而非正确性，已在《06 拆批两域自检与优化性拆批成本缺口》§六 登记（与实施包 §19 同源风险）。
        if (request.RunScope?.ChangedResourceIds is { Count: > 0 } changedResourceIds)
        {
            var changedResourceIdSet = new HashSet<int>(changedResourceIds);
            foreach (var task in scheduleResult.ScheduledTasks)
            {
                if (task.ResourceId is int taskResourceId && changedResourceIdSet.Contains(taskResourceId))
                {
                    affectedTasks.Add(task.FinalDraftId);
                }
            }
        }

        // 2. 传播Guardrail（文档§十三 13.4）
        // 第8轮P0-02修复：30%是警戒线而非硬截断，修复小规模场景Bug
        // P1-02：轮数/警戒线改读冻结参数（B 组整块透传，零行为变化：默认 10 / 30%）
        int maxPropagationRounds = request.StrategySnapshot.Parameters.MaxPropagationRounds;
        decimal maxAffectedRatio = request.StrategySnapshot.Parameters.ImpactedTaskWarningPercent / 100m; // 30%警戒线
        int totalScheduledTasks = scheduleResult.ScheduledTasks.Count;

        // 修复小规模场景Bug：至少允许1个任务受影响，避免0阈值导致传播无法进入
        int maxAffectedTasks = Math.Max(1, (int)(totalScheduledTasks * maxAffectedRatio));

        bool shouldTriggerFallback = false; // 超警戒线标志

        // 3. 传播循环：识别受影响任务，做最小修改，只在真实变化时继续传播
        var propagationRound = 0;
        var taskSnapshots = new Dictionary<string, TaskSnapshot>(); // FinalDraftId -> 快照
        var immovableTasks = IdentifyImmovableTasks(request, scheduleResult.ScheduledTasks);

        // 锚点（Anchor）不可移动：ExecutionConstraints 标记的 Task 不进影响集——它们是「保持原位的真实约束」，
        // 既不重排也不参与传播（与 Phase5 压实跳过 immovable 的处置一致）。
        // 修复背景：初始集原未排除锚点，导致「既是锚点又是变化种子」的 Task 会被 relocation 主循环搬走
        //           （该主循环只排连带牺牲者，不查 immovableTasks 自身）。
        // 注：IdentifyImmovableTasks 只认 request.ExecutionConstraints；当前生产侧该字段恒空
        //     （PeggingOrchestrator.cs:210「当前锚点机制未建……故预留为空」），本保护在锚点通道接通后生效。
        affectedTasks.ExceptWith(immovableTasks);

        // 初始化所有已排任务的快照
        foreach (var task in scheduleResult.ScheduledTasks)
        {
            taskSnapshots[task.FinalDraftId] = new TaskSnapshot
            {
                ResourceId = task.ResourceId,
                PlannedStartTime = task.PlannedStartTime,
                PlannedEndTime = task.PlannedEndTime,
                Quantity = task.Quantity
            };
        }

        // 第8轮P0-02修复：传播循环改为内部检测警戒，不作为while硬条件
        // 传播循环：只要未超轮次且有新受影响任务，就继续传播
        var currentRoundAffected = new HashSet<string>(affectedTasks);

        while (propagationRound < maxPropagationRounds)
        {
            propagationRound++;

            // 检查警戒线：受影响任务超过30%，触发Fallback标志
            if (affectedTasks.Count > maxAffectedTasks)
            {
                shouldTriggerFallback = true;
                break; // 超警戒，退出传播，进入Fallback
            }

            var nextRoundAffected = new HashSet<string>();

            // 热点计时（0号位 2026-10-08 §九）：传播循环整体（含其内部全表扫描）。
            long swPropagation = SolverDiagnostics.HotspotStart();

            // 第9轮P0-02.1修复：先尝试重排受影响Task，让它们真实变化
            foreach (var affectedTaskId in currentRoundAffected)
            {
                // 性能计数（0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§九「Phase4 传播 Task 数」）：
                //   传播循环**实际访问**（含 `ScheduledTasks.FirstOrDefault` 全表扫描，见 §7.4）的 Task 次数。
                SolverDiagnostics.CountPhase4PropagatedTask();

                var task = scheduleResult.ScheduledTasks.FirstOrDefault(t => t.FinalDraftId == affectedTaskId);
                if (task == null) continue;

                // 核心修复：真正重新安排Task位置
                // P0-02修复：Task占用从Setup开始（与BuildResourceOccupancy口径一致），移除时也要算上Setup
                var taskSetup = TimeSpan.FromMinutes((double)task.SetupTime);
                var taskOccupancyStart = task.PlannedStartTime - taskSetup;

                // 1. 从resourceOccupancy中移除当前Task占用（非资源 Task ResourceId=NULL，无占用，跳过）
                if (task.ResourceId is int rid)
                {
                    if (resourceOccupancy.TryGetValue(rid, out var occupiedWindows))
                    {
                        occupiedWindows.RemoveAll(w =>
                            w.Start == taskOccupancyStart && w.End == task.PlannedEndTime);
                    }
                    // item1 接线（阶段二）：产品时间线同步摘除，移动/保持后按最终位置重新登记
                    constraints.ProductTimeline.Remove(rid, task.PlannedEndTime, task.MaterialId);
                }

                // 2. 计算该Task的工序约束时间窗
                var demand = request.LogicalProductionDemands
                    .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);

                // item1 接线（阶段二）：默认 floor 用占用起点（含 Setup）而非加工起点——
                // 旧口径（加工起点当占用 floor）会让槽查找系统性晚 Setup 量，传播每轮向后漂移直至轮次耗尽。
                // 前序约束（predEnd+lag）是加工口径，直接作占用 floor 属保守收紧（proc ≥ predEnd+lag+setup），
                // 永不违反硬约束；精确的 occ floor = procFloor - setupEstimate 留待夜间 FULL 精细化。
                DateTime earliestStart = taskOccupancyStart; // 默认保持原位置（占用口径）
                // P0-02修复：latestEnd 不再默认等于原结束时间（那会人为锁死Task位置），
                // 改为 DateTime.MaxValue，仅由后继约束（如有）收紧。
                DateTime latestEnd = DateTime.MaxValue;

                if (demand != null)
                {
                    // Path-aware 解析（0号位 2026-10-07 裁决 Q-3）：按**任务自身** (RouteCode, PathId) 取图，不串 Path。
                    if (constraints.TryGetRoutingGraph(task.MaterialId, task.RouteCode, task.PathId, out var graph))
                    {
                        // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)。
                        // 同码跨 Stage 时若只按 OperationCode 匹配，会把另一 Stage 的同名工序误认为前驱/后继。
                        var taskNodeKey = OperationNodeKey.Of(task.StageCode, task.OperationCode);

                        // 检查前驱约束：必须在所有前驱完成后开始
                        if (graph.Dependencies.TryGetValue(taskNodeKey, out var predecessors))
                        {
                            foreach (var pred in predecessors)
                            {
                                var predecessorTask = scheduleResult.ScheduledTasks
                                    .FirstOrDefault(t => t.SourceDraftId == task.SourceDraftId &&
                                                        t.StageCode == pred.From.StageCode &&
                                                        t.OperationCode == pred.From.OperationCode);
                                if (predecessorTask != null)
                                {
                                    var predEnd = predecessorTask.PlannedEndTime.AddMinutes((double)pred.LagTime);
                                    if (predEnd > earliestStart)
                                    {
                                        earliestStart = predEnd;
                                    }
                                }
                            }
                        }

                        // 检查后继约束：必须在所有后继开始前完成
                        var successors = graph.Dependencies
                            .Where(kvp => kvp.Value.Any(dep => dep.From == taskNodeKey))
                            .SelectMany(kvp => kvp.Value.Where(dep => dep.From == taskNodeKey)
                                .Select(dep => new { ToNode = kvp.Key, LagMinutes = dep.LagTime }));

                        foreach (var succ in successors)
                        {
                            var successorTask = scheduleResult.ScheduledTasks
                                .FirstOrDefault(t => t.SourceDraftId == task.SourceDraftId &&
                                                    t.StageCode == succ.ToNode.StageCode &&
                                                    t.OperationCode == succ.ToNode.OperationCode);
                            if (successorTask != null)
                            {
                                var succStart = successorTask.PlannedStartTime.AddMinutes(-(double)succ.LagMinutes);
                                if (succStart < latestEnd)
                                {
                                    latestEnd = succStart;
                                }
                            }
                        }
                    }
                }

                // 3. P0-02修复 + item1 接线（阶段二）：日历感知 + 动态 Setup 规则查找（含Setup占用）。
                // 移动后的 Task 以新位置前产品重解析 Setup（v1.2 §11.1：移动必须重算受影响邻接），
                // 不再读 RoutingOperation.SetupTime（§1.2/§20.3 废止）。
                var duration = task.PlannedEndTime - task.PlannedStartTime;

                // 非资源 Task（ResourceId=NULL）不占资源、无槽可找 → 跳过重排（found=null，走"保持原位置"分支）
                var found = task.ResourceId is int ridSlot
                    ? SetupOptimizer.FindSlotWithDynamicSetup(
                        earliestStart, duration, ridSlot, task.OperationCode, task.MaterialId,
                        constraints.ProductTimeline, constraints.SetupExactRules, constraints.SetupDefaultRules,
                        (f, total) => FindForwardSlot(f, total, ridSlot, constraints, resourceOccupancy, request.PlanningEnd))
                    : ((TimeWindow Slot, decimal SetupMinutes, SetupOptimizer.SetupResolution Resolution)?)null;

                bool foundSlot = found.HasValue && found.Value.Slot.End <= latestEnd;
                decimal newSetupMinutes = foundSlot ? found!.Value.SetupMinutes : task.SetupTime;
                var newSetup = TimeSpan.FromMinutes((double)newSetupMinutes);
                DateTime newStart = foundSlot ? found!.Value.Slot.Start + newSetup : task.PlannedStartTime;
                DateTime newEnd = foundSlot ? found!.Value.Slot.End : task.PlannedEndTime;
                // SetupSource 填充：新位置以新解析命中类型更新；无新槽时保留原来源（2号位 原样落库）
                string? newSetupSource = foundSlot
                    ? SetupOptimizer.SetupOutcomeToSource(found!.Value.Resolution.Outcome)
                    : task.SetupSource;

                // 4. 如果找到新位置且与原位置不同（或 Setup 解析值变化），创建新FinalTaskDraft替换
                if (foundSlot && (newStart != task.PlannedStartTime || newEnd != task.PlannedEndTime || newSetupMinutes != task.SetupTime))
                {
                    // 创建新FinalTaskDraft对象（init-only属性需要重新构造）
                    var updatedTask = new FinalTaskDraft
                    {
                        FinalDraftId = task.FinalDraftId,
                        SourceDraftId = task.SourceDraftId,
                        MaterialId = task.MaterialId,
                        FactoryId = task.FactoryId,
                        StageCode = task.StageCode,
                        OperationCode = task.OperationCode,
                        OperationSeq = task.OperationSeq,   // P1-05顺手修复：此前重建漏拷贝 OperationSeq（init-only 默认归零）
                        TaskType = task.TaskType,
                        ResourceId = task.ResourceId,
                        ResourceCode = task.ResourceCode,
                        RouteCode = task.RouteCode,
                        PathId = task.PathId,
                        Quantity = task.Quantity,
                        PlannedProcessQty = task.PlannedProcessQty,
                        UOM = task.UOM,
                        PlannedStartTime = newStart,  // 新位置
                        PlannedEndTime = newEnd,      // 新位置
                        SetupTime = newSetupMinutes,  // item1 接线：新位置规则解析值（§11.1 移动重算）
                        SetupSource = newSetupSource, // SetupSource 填充：新位置命中类型 / 保留原来源
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

                    // 在scheduleResult中替换原Task
                    var taskIndex = scheduleResult.ScheduledTasks.IndexOf(task);
                    if (taskIndex >= 0)
                    {
                        scheduleResult.ScheduledTasks[taskIndex] = updatedTask;
                    }

                    // 5. 更新resourceOccupancy（P0-02：占用窗口含Setup；found.Slot 即 [newStart-Setup, newEnd]）
                    // 非资源 Task（ResourceId=NULL）不占资源，跳过占用登记。
                    if (task.ResourceId is int ridRep)
                    {
                        if (!resourceOccupancy.ContainsKey(ridRep))
                        {
                            resourceOccupancy[ridRep] = new List<TimeWindow>();
                        }
                        resourceOccupancy[ridRep].Add(found!.Value.Slot);
                        constraints.ProductTimeline.Place(ridRep, newEnd, task.MaterialId);
                    }
                }
                else
                {
                    // 位置未变或无可行槽：原样加回 resourceOccupancy + 产品时间线。
                    // （item1 接线顺手修复：旧代码「找不到槽」时占用不加回，形成伪空洞 → 后续 Task 可能与其重叠。）
                    if (task.ResourceId is int ridKeep)
                    {
                        if (!resourceOccupancy.ContainsKey(ridKeep))
                        {
                            resourceOccupancy[ridKeep] = new List<TimeWindow>();
                        }
                        // P0-02修复：占用窗口含Setup
                        resourceOccupancy[ridKeep].Add(new TimeWindow(taskOccupancyStart, task.PlannedEndTime));
                        constraints.ProductTimeline.Place(ridKeep, task.PlannedEndTime, task.MaterialId);
                    }
                }


                // 检查1: 工艺前后工序依赖
                // 找到该任务对应的Demand和Routing
                var taskDemand = request.LogicalProductionDemands
                    .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);
                if (taskDemand != null)
                {
                    // Path-aware 解析（0号位 2026-10-07 裁决 Q-3）：按**任务自身** (RouteCode, PathId) 取图，不串 Path。
                    if (constraints.TryGetRoutingGraph(task.MaterialId, task.RouteCode, task.PathId, out var graph))
                    {
                        // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)
                        var taskNodeKey = OperationNodeKey.Of(task.StageCode, task.OperationCode);

                        // 1.1 前序传播：找当前工序的前驱
                        if (graph.Dependencies.TryGetValue(taskNodeKey, out var predecessors))
                        {
                            foreach (var pred in predecessors)
                            {
                                var predecessorTask = scheduleResult.ScheduledTasks
                                    .FirstOrDefault(t => t.SourceDraftId == task.SourceDraftId &&
                                                        t.StageCode == pred.From.StageCode &&
                                                        t.OperationCode == pred.From.OperationCode);
                                if (predecessorTask != null && !immovableTasks.Contains(predecessorTask.FinalDraftId))
                                {
                                    nextRoundAffected.Add(predecessorTask.FinalDraftId);
                                }
                            }
                        }

                        // 1.2 后序传播：找到以当前节点作为 From 的所有后继
                        var successors = graph.Dependencies
                            .Where(kvp => kvp.Value.Any(dep => dep.From == taskNodeKey))
                            .SelectMany(kvp => scheduleResult.ScheduledTasks
                                .Where(t => t.SourceDraftId == task.SourceDraftId &&
                                           t.StageCode == kvp.Key.StageCode &&
                                           t.OperationCode == kvp.Key.OperationCode &&
                                           !immovableTasks.Contains(t.FinalDraftId)));

                        foreach (var successorTask in successors)
                        {
                            nextRoundAffected.Add(successorTask.FinalDraftId);
                        }
                    }
                }

                // 检查2: 同资源时间轴邻近任务冲突
                var sameResourceTasks = scheduleResult.ScheduledTasks
                    .Where(t => t.ResourceId == task.ResourceId &&
                               t.FinalDraftId != affectedTaskId &&
                               !immovableTasks.Contains(t.FinalDraftId))
                    .ToList();

                foreach (var neighbor in sameResourceTasks)
                {
                    // 时间窗重叠检测
                    if (!(task.PlannedEndTime <= neighbor.PlannedStartTime ||
                          task.PlannedStartTime >= neighbor.PlannedEndTime))
                    {
                        nextRoundAffected.Add(neighbor.FinalDraftId);
                    }
                }

                // 检查3: Setup换型邻居传播（P0-02.3完整实现）
                // 当Task被重排到新位置时，需要检查：
                // - 原位置的前驱/后继（Setup邻居发生变化）
                // - 新位置的前驱/后继（需要重新计算Setup时间）
                if (taskSnapshots.TryGetValue(affectedTaskId, out var taskSnapshot))
                {
                    var currentTask = scheduleResult.ScheduledTasks.FirstOrDefault(t => t.FinalDraftId == affectedTaskId);
                    if (currentTask != null && currentTask.ResourceId == taskSnapshot.ResourceId)
                    {
                        // 如果Task在同一资源上移动了时间位置
                        if (currentTask.PlannedStartTime != taskSnapshot.PlannedStartTime)
                        {
                            var sameResourceNeighbors = scheduleResult.ScheduledTasks
                                .Where(t => t.ResourceId == currentTask.ResourceId &&
                                           t.FinalDraftId != affectedTaskId &&
                                           !immovableTasks.Contains(t.FinalDraftId))
                                .OrderBy(t => t.PlannedStartTime)
                                .ToList();

                            // 找到原时间位置的邻居
                            var oldPredecessor = sameResourceNeighbors
                                .LastOrDefault(t => t.PlannedEndTime <= taskSnapshot.PlannedStartTime);
                            var oldSuccessor = sameResourceNeighbors
                                .FirstOrDefault(t => t.PlannedStartTime >= taskSnapshot.PlannedEndTime);

                            // 找到新时间位置的邻居
                            var newPredecessor = sameResourceNeighbors
                                .LastOrDefault(t => t.PlannedEndTime <= currentTask.PlannedStartTime);
                            var newSuccessor = sameResourceNeighbors
                                .FirstOrDefault(t => t.PlannedStartTime >= currentTask.PlannedEndTime);

                            // 原邻居和新邻居的Setup时间可能需要重新计算
                            if (oldPredecessor != null && oldPredecessor.FinalDraftId != newPredecessor?.FinalDraftId)
                                nextRoundAffected.Add(oldPredecessor.FinalDraftId);
                            if (oldSuccessor != null && oldSuccessor.FinalDraftId != newSuccessor?.FinalDraftId)
                                nextRoundAffected.Add(oldSuccessor.FinalDraftId);
                            if (newPredecessor != null && newPredecessor.FinalDraftId != oldPredecessor?.FinalDraftId)
                                nextRoundAffected.Add(newPredecessor.FinalDraftId);
                            if (newSuccessor != null && newSuccessor.FinalDraftId != oldSuccessor?.FinalDraftId)
                                nextRoundAffected.Add(newSuccessor.FinalDraftId);
                        }
                    }
                }

                // 检查4: 物料Quantity-Time消费者传播（P0-02.4完整实现）
                // 当Task产出时间或数量变化，消费该物料的后续Task可能受影响
                var currentTaskForMaterial = scheduleResult.ScheduledTasks.FirstOrDefault(t => t.FinalDraftId == affectedTaskId);
                if (currentTaskForMaterial != null)
                {
                    // 找到所有可能消费该Task产出物料的后续Task
                    // 通过StageExecutionBatchDraftKey关联同一批次的后续工序
                    var materialConsumers = scheduleResult.ScheduledTasks
                        .Where(t => t.SourceDraftId == currentTaskForMaterial.SourceDraftId &&
                                   t.MaterialId == currentTaskForMaterial.MaterialId &&
                                   t.FinalDraftId != affectedTaskId &&
                                   !immovableTasks.Contains(t.FinalDraftId))
                        .ToList();

                    // 如果是同一物料的后续工序，且时间上有依赖关系
                    foreach (var consumer in materialConsumers)
                    {
                        // 检查是否存在工艺依赖关系
                        // Path-aware 解析（0号位 2026-10-07 裁决 Q-3）：按**任务自身** (RouteCode, PathId) 取图，不串 Path。
                        if (constraints.TryGetRoutingGraph(
                                currentTaskForMaterial.MaterialId,
                                currentTaskForMaterial.RouteCode,
                                currentTaskForMaterial.PathId,
                                out var materialGraph))
                        {
                            // 检查consumer是否依赖当前Task的工序
                            // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)
                            var consumerNodeKey = OperationNodeKey.Of(consumer.StageCode, consumer.OperationCode);
                            var currentTaskNodeKey = OperationNodeKey.Of(
                                currentTaskForMaterial.StageCode, currentTaskForMaterial.OperationCode);

                            if (materialGraph.Dependencies.TryGetValue(consumerNodeKey, out var consumerPreds))
                            {
                                if (consumerPreds.Any(dep => dep.From == currentTaskNodeKey))
                                {
                                    nextRoundAffected.Add(consumer.FinalDraftId);
                                }
                            }
                        }
                    }
                }
            }

            // 第8轮P0-02.2修复：TaskSnapshot前后比较，只在真实变化时继续传播
            var hasRealChanges = false;
            foreach (var taskId in nextRoundAffected)
            {
                var task = scheduleResult.ScheduledTasks.FirstOrDefault(t => t.FinalDraftId == taskId);
                if (task == null) continue;

                if (taskSnapshots.TryGetValue(taskId, out var oldSnapshot))
                {
                    // 比较Resource/Start/End/Qty是否真实变化
                    if (task.ResourceId != oldSnapshot.ResourceId ||
                        task.PlannedStartTime != oldSnapshot.PlannedStartTime ||
                        task.PlannedEndTime != oldSnapshot.PlannedEndTime ||
                        task.Quantity != oldSnapshot.Quantity)
                    {
                        hasRealChanges = true;

                        // 更新快照
                        taskSnapshots[taskId] = new TaskSnapshot
                        {
                            ResourceId = task.ResourceId,
                            PlannedStartTime = task.PlannedStartTime,
                            PlannedEndTime = task.PlannedEndTime,
                            Quantity = task.Quantity
                        };
                    }
                }
            }

            // 无真实变化时停止传播
            if (!hasRealChanges || nextRoundAffected.Count == 0)
            {
                SolverDiagnostics.HotspotEnd(swPropagation, SolverDiagnostics.Hotspot.Phase4Propagation);
                break;
            }

            // 第8轮P0-02.3修复：immovableTasks限制移动
            // 已在上面检查时过滤掉不可移动任务

            // 将下一轮受影响任务加入总集合
            foreach (var taskId in nextRoundAffected)
            {
                affectedTasks.Add(taskId);
            }

            currentRoundAffected = nextRoundAffected;
            SolverDiagnostics.HotspotEnd(swPropagation, SolverDiagnostics.Hotspot.Phase4Propagation);
        }

        // 第9轮P0-03.1修复：轮次耗尽时自动触发Fallback
        if (propagationRound >= maxPropagationRounds)
        {
            shouldTriggerFallback = true;
        }

        // 4. 第9轮P0-03修复：Fallback兜底 - 调用同一Solver对本Domain全部可移动任务重排
        if (shouldTriggerFallback || affectedTasks.Count > maxAffectedTasks)
        {
            // 构建Fallback重排请求：Phase2 Schedule 会重新继承所有 LockedTasks（原地保留不可移动部分）
            var fallbackScheduler = new PhaseTwoInitialScheduler();
            var fallbackResult = fallbackScheduler.Schedule(request, constraints);

            // P0-03修复：Fallback结果直接替换语义，避免同一批Task进入 final set 两次。
            // fallbackResult.ScheduledTasks 已含 LockedTasks 继承（Phase2 Schedule 内部处理），
            // 因此无需再手动保留 immovableTasks；result.RepairedTasks 保持为空，
            // Phase5 只合并 scheduleResult.ScheduledTasks 一次。
            scheduleResult.ScheduledTasks = fallbackResult.ScheduledTasks;
            scheduleResult.UnscheduledDemandKeys = fallbackResult.UnscheduledDemandKeys;
            scheduleResult.AllocationTaskShare = fallbackResult.AllocationTaskShare;

            return result;
        }

        // 当前简化实现：优先修复受影响的未排需求
        var orderedDemandKeys = affectedDemands
            .Concat(scheduleResult.UnscheduledDemandKeys.Except(affectedDemands))
            .ToList();

        // 块4（任务喂任务）：修复路径遵守子件完成下界。
        var demandCompletion = scheduleResult.ScheduledTasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.PlannedEndTime));

        // P0-08：从已排 Task 反推各 PI 连续份额的完成时间，供修复路径的自由份额做下界。
        var continuityCompletionByPI = BuildContinuityCompletionByPI(request.LogicalProductionDemands, scheduleResult.ScheduledTasks);

        foreach (var demandKey in orderedDemandKeys)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == demandKey);

            if (demand == null) continue;

            // ── P0-02（0号位 2026-10-08 §五 / §十一-1）：硬业务失败禁止普通修复 ──
            if (scheduleResult.IsBatchPolicyHardFailure(demandKey))
            {
                result.StillUnscheduledKeys.Add(demandKey);
                continue;
            }

            // 块4：父件缺料（子件未排成）→ 保持 Unscheduled，不得被修复绕过。
            var dynamicMaterialFloor = GetDynamicMaterialFloor(
                demandKey, constraints, demandCompletion, out bool childUnavailable);
            if (childUnavailable)
            {
                result.StillUnscheduledKeys.Add(demandKey);
                continue;
            }

            // P0-08：同 PI 自由份额不得排到连续份额之前（也不得与其时间重叠）。
            if (!demand.IsContinuation && !string.IsNullOrEmpty(demand.ProductionInstructionNo)
                && continuityCompletionByPI.TryGetValue(demand.ProductionInstructionNo!, out var contEnd)
                && contEnd > dynamicMaterialFloor)
            {
                dynamicMaterialFloor = contEnd;
            }

            // P0-03：**逐批**修复 —— 局部修复的基本单位 = **执行批**（0号位 (7).md §六），
            //   每批带本批键 + 本批数量；不得用整份需求数量重建，不得把 Batch-002 写回 Batch-001 的键。
            //   P0-03 整改：单元 = **未落定执行批**（已成功批不展开）。
            var repairUnits = ExpandRepairUnits(demand, scheduleResult, constraints);
            var repairedBatchCount = 0;

            foreach (var (unitBatchKey, unitDemand) in repairUnits)
            {
                var repairedTasks = TryResourceSwitch(
                    unitDemand,
                    unitBatchKey,
                    constraints,
                    resourceOccupancy,
                    request,
                    dynamicMaterialFloor);

                if (repairedTasks.Count == 0)
                {
                    continue;
                }

                repairedBatchCount++;
                result.RepairedTasks.AddRange(repairedTasks);

                // ── NEW-P1-01（0号位 2026-10-09《APS_V1_4_20261009.md》§三）：**Phase4 修复批亦须留批级方向痕** ──
                //   复审判词（成立）：Phase2 只在**有胜出候选**时登记 `ResolvedBatchDirections`；未落定批
                //   （所有 Routing 试排均不成功）无方向留痕，Phase4 修复出的 Task 虽带批键却查不到批级方向
                //   ⇒ Phase5 回落需求级/Run 级 ⇒ 可能误动或误锁该批。
                //   **不变量（代码可证）**：Phase4 的**全部**放置路径都是**正排** —— 两处逐批修复循环
                //   （`RepairWithPropagation` / `RepairUnscheduledDemands`）均经 `TryResourceSwitch` 以
                //   `earliestStart` 为下界、只调 `FindForwardSlot`（本文件放置调用 `:319`/`:1105`/`:1569` 全为
                //   `FindForwardSlot`，**无任何倒排/交期锚定路径**）⇒ 修复批的**实际落定方向唯一且恒为
                //   `FORWARD`**。故此处登记**本批实际方向**（可追溯到本批键），而非回落猜测。
                //   已落定批不在修复单元内（`ExpandRepairUnits` 只取 `FailedExecutionBatches`）⇒ `TryAdd`
                //   保证不覆盖 Phase2 的既有登记（零回归）。
                //   ⚠ 不得改变冻结的 Route/Path/批身份：取图走 `TryGetBatchRoutingGraph(unitBatchKey, ...)`
                //      （批键 → 本批路径 → 需求固定路径 → 唯一那条），本处只登记方向、不动任何身份字段。
                constraints.ResolvedBatchDirections.TryAdd(unitBatchKey, SchedulingDirectionResolver.Forward);

                // P0-08：修复出连续份额时登记完成时间，供后续同 PI 自由份额做下界。
                if (unitDemand.IsContinuation && !string.IsNullOrEmpty(unitDemand.ProductionInstructionNo))
                {
                    var repairEnd = repairedTasks.Max(t => t.PlannedEndTime);
                    if (!continuityCompletionByPI.TryGetValue(unitDemand.ProductionInstructionNo!, out var existingEnd) || repairEnd > existingEnd)
                        continuityCompletionByPI[unitDemand.ProductionInstructionNo!] = repairEnd;
                }

                // 更新资源占用
                foreach (var task in repairedTasks)
                {
                    // 非资源 Task（ResourceId=NULL）不占资源，跳过登记
                    if (task.ResourceId is not int ridRep) continue;
                    if (!resourceOccupancy.ContainsKey(ridRep))
                    {
                        resourceOccupancy[ridRep] = new List<TimeWindow>();
                    }
                    // P0-02 口径：Task 占用从 Setup 起点算起（与 BuildResourceOccupancy 及移除口径一致）。
                    // 旧写法用 PlannedStartTime（加工起点）会漏登 Setup 那一段，导致后续 Task 被排进本 Task 的 Setup 窗口。
                    var occStart = task.PlannedStartTime.AddMinutes(-(double)task.SetupTime);
                    resourceOccupancy[ridRep].Add(new TimeWindow(occStart, task.PlannedEndTime));
                }
            }

            // ── P0-03（0号位 2026-10-08 §六 / §十一-2）：需求级成功条件 = **所有未落定批全部修复成功** ──
            //   旧形态 `repairedAnyBatch`（任一批修出即算需求已修）会同时造成三个错误结果：
            //     ① 已成功批被重复生产；② 失败批没排出来；③ Demand 却不再标记 Unscheduled。
            if (repairedBatchCount < repairUnits.Count)
            {
                result.StillUnscheduledKeys.Add(demandKey);
            }
        }

        return result;
    }

    /// <summary>
    /// Base/FULL模式：传统修复未排程需求
    /// </summary>
    private RepairResult RepairUnscheduledDemands(
        DomainSolveRequest request,
        InitialScheduleResult scheduleResult,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy)
    {
        var result = new RepairResult();

        // 块4（任务喂任务）：从已排 Task 反推每个 demand 的完成时间，修复路径据此遵守子件完成下界。
        var demandCompletion = scheduleResult.ScheduledTasks
            .GroupBy(t => t.SourceDraftId)
            .ToDictionary(g => g.Key, g => g.Max(t => t.PlannedEndTime));

        // P0-08：从已排 Task 反推各 PI 连续份额的完成时间，供修复路径的自由份额做下界。
        var continuityCompletionByPI = BuildContinuityCompletionByPI(request.LogicalProductionDemands, scheduleResult.ScheduledTasks);

        foreach (var demandKey in scheduleResult.UnscheduledDemandKeys)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == demandKey);

            if (demand == null) continue;

            // ── P0-02（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§五 / §十一-1）──
            //   硬业务失败（`BATCH_POLICY_MISSING` / `BATCH_POLICY_CONFLICT`）⇒ **NonRepairable**：
            //   **禁止**进入普通 Local Repair。否则 Phase4 会把「Qty10/Min6/Max6」当成普通「资源/日历没排下」
            //   重新 Resource Switch / Split / Local Repair，最终产出绕过 Min/Max 硬约束的非法 FinalTask。
            if (scheduleResult.IsBatchPolicyHardFailure(demandKey))
            {
                result.StillUnscheduledKeys.Add(demandKey);
                continue;
            }

            // 块4：父件缺料（子件未排成）→ 保持 Unscheduled，不得被修复绕过。
            var dynamicMaterialFloor = GetDynamicMaterialFloor(
                demandKey, constraints, demandCompletion, out bool childUnavailable);
            if (childUnavailable)
            {
                result.StillUnscheduledKeys.Add(demandKey);
                continue;
            }

            // P0-08：同 PI 自由份额不得排到连续份额之前（也不得与其时间重叠）。
            if (!demand.IsContinuation && !string.IsNullOrEmpty(demand.ProductionInstructionNo)
                && continuityCompletionByPI.TryGetValue(demand.ProductionInstructionNo!, out var contEnd)
                && contEnd > dynamicMaterialFloor)
            {
                dynamicMaterialFloor = contEnd;
            }

            // P0-03：**逐批**修复（同上方 Base 路径；基本单位 = 执行批）。
            //   P0-03 整改：单元 = **未落定执行批**（已成功批不展开）。
            var repairUnits = ExpandRepairUnits(demand, scheduleResult, constraints);
            var repairedBatchCount = 0;

            foreach (var (unitBatchKey, unitDemand) in repairUnits)
            {
                var repairedTasks = TryResourceSwitch(
                    unitDemand,
                    unitBatchKey,
                    constraints,
                    resourceOccupancy,
                    request,
                    dynamicMaterialFloor);

                if (repairedTasks.Count == 0)
                {
                    continue;
                }

                repairedBatchCount++;
                result.RepairedTasks.AddRange(repairedTasks);

                // ── NEW-P1-01（0号位 2026-10-09《APS_V1_4_20261009.md》§三）：**Phase4 修复批亦须留批级方向痕** ──
                //   复审判词（成立）：Phase2 只在**有胜出候选**时登记 `ResolvedBatchDirections`；未落定批
                //   （所有 Routing 试排均不成功）无方向留痕，Phase4 修复出的 Task 虽带批键却查不到批级方向
                //   ⇒ Phase5 回落需求级/Run 级 ⇒ 可能误动或误锁该批。
                //   **不变量（代码可证）**：Phase4 的**全部**放置路径都是**正排** —— 两处逐批修复循环
                //   （`RepairWithPropagation` / `RepairUnscheduledDemands`）均经 `TryResourceSwitch` 以
                //   `earliestStart` 为下界、只调 `FindForwardSlot`（本文件放置调用 `:319`/`:1105`/`:1569` 全为
                //   `FindForwardSlot`，**无任何倒排/交期锚定路径**）⇒ 修复批的**实际落定方向唯一且恒为
                //   `FORWARD`**。故此处登记**本批实际方向**（可追溯到本批键），而非回落猜测。
                //   已落定批不在修复单元内（`ExpandRepairUnits` 只取 `FailedExecutionBatches`）⇒ `TryAdd`
                //   保证不覆盖 Phase2 的既有登记（零回归）。
                //   ⚠ 不得改变冻结的 Route/Path/批身份：取图走 `TryGetBatchRoutingGraph(unitBatchKey, ...)`
                //      （批键 → 本批路径 → 需求固定路径 → 唯一那条），本处只登记方向、不动任何身份字段。
                constraints.ResolvedBatchDirections.TryAdd(unitBatchKey, SchedulingDirectionResolver.Forward);

                // P0-08：修复出连续份额时登记完成时间，供后续同 PI 自由份额做下界。
                if (unitDemand.IsContinuation && !string.IsNullOrEmpty(unitDemand.ProductionInstructionNo))
                {
                    var repairEnd = repairedTasks.Max(t => t.PlannedEndTime);
                    if (!continuityCompletionByPI.TryGetValue(unitDemand.ProductionInstructionNo!, out var existingEnd) || repairEnd > existingEnd)
                        continuityCompletionByPI[unitDemand.ProductionInstructionNo!] = repairEnd;
                }

                // 更新资源占用
                foreach (var task in repairedTasks)
                {
                    // 非资源 Task（ResourceId=NULL）不占资源，跳过登记
                    if (task.ResourceId is not int ridRep) continue;
                    if (!resourceOccupancy.ContainsKey(ridRep))
                    {
                        resourceOccupancy[ridRep] = new List<TimeWindow>();
                    }
                    // P0-02 口径：Task 占用从 Setup 起点算起（与 BuildResourceOccupancy 及移除口径一致）。
                    // 旧写法用 PlannedStartTime（加工起点）会漏登 Setup 那一段，导致后续 Task 被排进本 Task 的 Setup 窗口。
                    var occStart = task.PlannedStartTime.AddMinutes(-(double)task.SetupTime);
                    resourceOccupancy[ridRep].Add(new TimeWindow(occStart, task.PlannedEndTime));
                }

                // 待办（原 TODO P6，描述已按 v1.2 更新）：本修复路径的 Setup 邻接关系重算。
                // 现状：Setup 由 TryResourceSwitch → FindSlotWithDynamicSetup 在放置时解析（含时间线邻接前产品的规则命中）；
                //       占用登记口径见上方 P0-02 修复（从 Setup 起点算起）。
                // 未覆盖：Task 插入/移动后若时间线上前后邻居的产品关系变化，邻居自身的 Setup 未随之重算
                //       （属有界序列优化范畴；Phase5 夜间 FULL 的 OptimizeSetupSequences 已覆盖 FORWARD 方向）。
                // 注：旧描述「模具/刀具/材质/颜色」属 v1.2 已废止的 SetupAttribute 口径，勿再引用。
            }

            // P0-03：需求级成功条件 = **所有未落定批全部修复成功**（不是「任一批修出」）。
            if (repairedBatchCount < repairUnits.Count)
            {
                result.StillUnscheduledKeys.Add(demandKey);
            }
        }

        return result;
    }

    /// <summary>
    /// 识别不可移动任务（Execution/Firm/Frozen/Protection）
    /// 文档：§四 4.8 ImmovableFacts
    /// P1-05：internal static 供 Phase5 Gap Compaction 复用（同一套不可移动语义，不另建）。
    /// </summary>
    internal static HashSet<string> IdentifyImmovableTasks(
        DomainSolveRequest request,
        List<FinalTaskDraft> scheduledTasks)
    {
        var immovable = new HashSet<string>();

        // 从ExecutionConstraints识别不可移动任务
        foreach (var constraint in request.ExecutionConstraints)
        {
            // ExecutionConstraint包含：已执行、Firm、Frozen、锁定资源/时间
            // 当前简化实现：所有ExecutionConstraint标记的任务都视为不可移动
            // 第13轮P0-02修复：immovable集合统一存放FinalDraftId，不混放DraftId
            // DraftId 负责关联 SourceDraft/Demand（即 FinalTaskDraft.SourceDraftId）
            // TaskKey 负责稳定Task键（跨轮次识别同一Task，若等于 FinalDraftId 则精确命中）

            // 1) DraftId → SourceDraftId → FinalDraftId（该Demand对应的全部已排Task）
            if (!string.IsNullOrEmpty(constraint.DraftId))
            {
                foreach (var task in scheduledTasks.Where(t => t.SourceDraftId == constraint.DraftId))
                {
                    immovable.Add(task.FinalDraftId);
                }
            }

            // 2) TaskKey → 精确匹配 FinalDraftId
            if (!string.IsNullOrEmpty(constraint.TaskKey))
            {
                var matchedByTaskKey = scheduledTasks.FirstOrDefault(t =>
                    t.FinalDraftId == constraint.TaskKey);

                if (matchedByTaskKey != null)
                {
                    immovable.Add(matchedByTaskKey.FinalDraftId);
                }
            }
        }

        return immovable;
    }

    /// <summary>
    /// 构建资源占用图
    /// P0-06修复：重建occupancy时也要加入ExternalDomain ResourceBlocks
    /// P1-05：internal static 供 Phase5 Gap Compaction 复用。
    /// </summary>
    internal static Dictionary<int, List<TimeWindow>> BuildResourceOccupancy(
        List<FinalTaskDraft> tasks,
        ConstraintContext constraints)
    {
        var occupancy = new Dictionary<int, List<TimeWindow>>();

        foreach (var task in tasks)
        {
            // 非资源 Task（ResourceId=NULL）不占资源，跳过
            if (task.ResourceId is not int ridOcc) continue;
            if (!occupancy.ContainsKey(ridOcc))
            {
                occupancy[ridOcc] = new List<TimeWindow>();
            }

            // 第4轮Setup修复：PlannedStartTime是加工开始时间（Setup之后），资源占用需从Setup开始
            var occupancyStart = task.PlannedStartTime.AddMinutes(-(double)task.SetupTime);
            occupancy[ridOcc].Add(
                new TimeWindow(occupancyStart, task.PlannedEndTime));
        }

        // P0-06修复：加入ExternalDomain ResourceBlocks阻挡
        foreach (var kvp in constraints.ResourceBlocks)
        {
            int resourceId = kvp.Key;
            if (!occupancy.ContainsKey(resourceId))
            {
                occupancy[resourceId] = new List<TimeWindow>();
            }

            foreach (var block in kvp.Value)
            {
                occupancy[resourceId].Add(new TimeWindow(block.StartTime, block.EndTime));
            }
        }

        return occupancy;
    }

    /// <summary>
    /// P0-03：把需求展开成**执行批修复单元**（本批键 + 批级需求）。
    ///
    /// 局部修复的基本单位 = **执行批**（0号位 (7).md §六）：Phase2 登记了几批就修几批，
    ///   每批带**本批数量**（<see cref="PhaseTwoInitialScheduler.CloneDemandWithBatchQty"/>，其余字段全量逐字拷贝）
    ///   与**本批键** —— 不得用整份需求数量重建，不得把 Batch-002 写回 Batch-001 的键。
    ///
    /// **P0-03 整改（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§六 / §十一-2）**：
    ///   修复单元**只取「未落定执行批」**（<see cref="InitialScheduleResult.FailedExecutionBatches"/>）——
    ///   Phase2 已成功落定的前序批**禁止**重新展开（否则同一批键产出第二套完整链 ⇒ 重复生产）。
    ///   未登记失败批（Phase2 未及形成该需求 / 单批回落 / 非批失败类未排）⇒ 回落既有口径，与升维前逐字一致。
    /// </summary>
    private static IReadOnlyList<(string BatchKey, LogicalProductionDemand Demand)> ExpandRepairUnits(
        LogicalProductionDemand demand,
        InitialScheduleResult scheduleResult,
        ConstraintContext constraints)
    {
        // P0-03：优先按「未落定执行批」展开 —— 已成功批**不在此表**，因此不会被重复修复。
        var unsettled = scheduleResult.FailedExecutionBatches
            .Where(f => string.Equals(f.LogicalDemandKey, demand.LogicalDemandKey, StringComparison.Ordinal))
            .OrderBy(f => f.Ordinal)
            .ToList();

        if (unsettled.Count > 0)
        {
            return unsettled
                .Select(f => (f.ExecutionBatchDraftKey,
                    PhaseTwoInitialScheduler.CloneDemandWithBatchQty(
                        demand, f.BatchQty, f.BatchPlannedProcessQty)))
                .ToList();
        }

        if (constraints.ExecutionBatchPlans.TryGetValue(demand.LogicalDemandKey, out var plans) && plans.Count > 0)
        {
            if (plans.Count == 1)
            {
                // 单批：直接用原实例（零回归）。
                return new[] { (plans[0].BatchKey, demand) };
            }

            return plans
                .Select(p => (p.BatchKey,
                    PhaseTwoInitialScheduler.CloneDemandWithBatchQty(demand, p.NetOutputQty, p.PlannedProcessQty)))
                .ToList();
        }

        return new[] { (PhaseTwoInitialScheduler.ExecutionBatchKey(demand.LogicalDemandKey), demand) };
    }

    /// <summary>
    /// 尝试资源切换（换到其他合格资源）
    /// </summary>
    private List<FinalTaskDraft> TryResourceSwitch(
        LogicalProductionDemand demand,
        string batchDraftKey,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DomainSolveRequest request,
        DateTime dynamicMaterialFloor)
    {
        // **批级**取图（P0-03；RT-002「局部修复不得换路径」）：本批选中路径 → 需求固定路径 → 唯一那条。
        //   多批时各批可能走不同 Path ⇒ 必须按**批键**取，不能按需求键取（否则串批）。
        if (!constraints.TryGetBatchRoutingGraph(
                batchDraftKey, demand.MaterialId, demand.RouteCode, demand.PathId, out var routingGraph))
        {
            return new List<FinalTaskDraft>();
        }

        // P0-01修复：复用 Phase2 的 GetOperationsFromStage，
        // 保证 Phase4 与 Phase2 使用同一套 Kahn 拓扑排序 + StartOperationCode/StartStageCode 裁剪逻辑，
        // 修复此前按 OperationCode 字典序（非拓扑序）遍历导致 StartOperationCode 裁剪被绕过的问题。
        var operations = PhaseTwoInitialScheduler.GetOperationsFromStage(
            demand.StartStageCode,
            demand.StartOperationCode,
            routingGraph,
            constraints,
            out var startFailureReason);

        if (operations.Count == 0)
        {
            // StartStageCode/StartOperationCode 非法，或 Routing 有环，无法修复
            return new List<FinalTaskDraft>();
        }

        var tasks = new List<FinalTaskDraft>();
        // P0-05修复：传入所需数量，根据累计可用量确定启动时间，并验证总量是否足够
        var earliestStart = GetMaterialEarliestTime(
            demand.AllocationSequence,
            demand.NetOutputQty,
            constraints,
            request.PlanningStart,
            out bool isMaterialSufficient);

        // P0-05修复：物料总量不足时，标记为业务Unscheduled（不是技术失败）
        if (!isMaterialSufficient)
        {
            return new List<FinalTaskDraft>(); // 物料总量不足
        }

        // 块4（任务喂任务）：修复路径也必须遵守子件完成时间下界，父件不得早于子件完成。
        if (dynamicMaterialFloor > earliestStart)
        {
            earliestStart = dynamicMaterialFloor;
        }

        // P0-17修复：改为for循环以便访问下一道工序，应用Routing LagTime
        for (int i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];

            // 获取合格资源列表（P1-01：统一走 Phase2 纯函数 —— 在此前 Phase4 自带旧版**只认 Id、完全不看
            // PreferredResourceCode**，导致 Phase4 局部修复会静默丢弃 Code 软偏好；现与 Phase2 同源。）
            var eligibleResources = PhaseTwoInitialScheduler.OrderResourcesByPreference(
                demand,
                GetEligibleResources(demand.MaterialId, operation, constraints),
                constraints,
                operation.OperationCode,
                operation.StageCode);   // P1-02：软偏好仅作用于当前承接工序（身份 = StageCode + OperationCode）

            FinalTaskDraft? scheduledTask = null;

            // 遍历合格资源，找第一个可用的
            foreach (var resourceId in eligibleResources)
            {
                // P0-04修复：Duration = StandardDuration × PlannedProcessQty ÷ CapacityFactor
                // 第4轮Setup修复：加上SetupTime占用资源时间轴
                // 第5轮修复：CapacityFactor缺失或非法时跳过该资源
                var capacityFactor = GetCapacityFactor(demand.MaterialId, operation, resourceId, constraints);
                if (capacityFactor == null || capacityFactor <= 0)
                {
                    continue; // CapacityFactor缺失/非法，跳过该资源
                }
                var adjustedDuration = operation.StandardDuration * demand.PlannedProcessQty / capacityFactor.Value;
                var processDuration = TimeSpan.FromMinutes((double)adjustedDuration);

                // item1 接线（阶段二）：Setup = 规则查找「当前工序+当前设备+前产品→当前产品」（v1.2 §2/§5），
                // 不再读 RoutingOperation.SetupTime（§1.2/§20.3 废止）；§12 候选评价即含 Setup。
                var found = SetupOptimizer.FindSlotWithDynamicSetup(
                    earliestStart, processDuration, resourceId, operation.OperationCode, demand.MaterialId,
                    constraints.ProductTimeline, constraints.SetupExactRules, constraints.SetupDefaultRules,
                    (f, total) => FindForwardSlot(f, total, resourceId, constraints, resourceOccupancy, request.PlanningEnd));

                if (found.HasValue)
                {
                    var (occSlot, setupMinutes, setupResolution) = found.Value;
                    // Task的PlannedStartTime是加工开始时间（Setup之后）
                    var taskStart = occSlot.Start + TimeSpan.FromMinutes((double)setupMinutes);
                    // SetupSource 填充：解析命中类型 → 大写 5 值（5号位 值契约统一 20260921）
                    var taskSetupSource = SetupOptimizer.SetupOutcomeToSource(setupResolution.Outcome);
                    scheduledTask = CreateTask(demand, operation, resourceId, taskStart, occSlot.End, setupMinutes, constraints, batchDraftKey, taskSetupSource);

                    // 临时占用：从Setup开始
                    if (!resourceOccupancy.ContainsKey(resourceId))
                    {
                        resourceOccupancy[resourceId] = new List<TimeWindow>();
                    }
                    resourceOccupancy[resourceId].Add(occSlot);
                    constraints.ProductTimeline.Place(resourceId, occSlot.End, demand.MaterialId);

                    // P0-17修复：应用Routing LagTime到下道工序的最早开始时间
                    earliestStart = occSlot.End;
                    if (i < operations.Count - 1)
                    {
                        var nextOperation = operations[i + 1];
                        var lagTime = GetLagTime(
                            OperationNodeKey.Of(operation.StageCode, operation.OperationCode),
                            OperationNodeKey.Of(nextOperation.StageCode, nextOperation.OperationCode),
                            routingGraph);
                        earliestStart = earliestStart.AddMinutes((double)lagTime);
                    }
                    break;
                }
            }

            if (scheduledTask == null)
            {
                // 第4轮Split修复：当前工序无法在任何资源找到完整时间槽时，尝试有限Split
                // P0-07：连续份额不可被普通 Split 破坏逐工单身份，禁止拆分。
                // NoSplitMerge（不拆不合）显式落实（0号位 2026-10-07 裁决 `:246`／职责表 `:357`）：
                //   与 Phase2 合批守卫同一口径，显式读该冻结字段，不再只靠 IsContinuation 间接覆盖。
                if (request.StrategySnapshot.Parameters.AllowSplit && !demand.IsContinuation
                    && !demand.NoSplitMerge && demand.PlannedProcessQty > 1.0m)
                {
                    var splitTasks = TrySplitOperation(
                        demand,
                        operation,
                        batchDraftKey,
                        eligibleResources,
                        earliestStart,
                        constraints,
                        resourceOccupancy,
                        request.PlanningEnd,
                        request.StrategySnapshot.Parameters.SplitAlternatives,
                        request.StrategySnapshot.Parameters.MinBatchQty);

                    if (splitTasks.Count > 0)
                    {
                        // Split成功：更新资源占用，推进下道工序最早开始时间
                        // 第5轮修复：Split Task的资源占用必须包含Setup时间
                        foreach (var splitTask in splitTasks)
                        {
                            // 非资源 Task（ResourceId=NULL）不占资源，跳过登记
                            if (splitTask.ResourceId is not int ridSplit) continue;
                            if (!resourceOccupancy.ContainsKey(ridSplit))
                            {
                                resourceOccupancy[ridSplit] = new List<TimeWindow>();
                            }
                            // PlannedStartTime是Setup后的加工开始时间，资源占用要从Setup开始算
                            var setupDuration = TimeSpan.FromMinutes((double)splitTask.SetupTime);
                            var resourceStart = splitTask.PlannedStartTime - setupDuration;
                            resourceOccupancy[ridSplit].Add(
                                new TimeWindow(resourceStart, splitTask.PlannedEndTime));
                            // item1 接线（阶段二）：拆分件逐个登记产品时间线（首件前产品=时间线邻接，后续件=前件同产品）
                            constraints.ProductTimeline.Place(ridSplit, splitTask.PlannedEndTime, splitTask.MaterialId);
                        }

                        tasks.AddRange(splitTasks);

                        // 更新earliestStart为所有Split Task的最晚结束时间
                        earliestStart = splitTasks.Max(t => t.PlannedEndTime);
                        if (i < operations.Count - 1)
                        {
                            var nextOperation = operations[i + 1];
                            var lagTime = GetLagTime(
                            OperationNodeKey.Of(operation.StageCode, operation.OperationCode),
                            OperationNodeKey.Of(nextOperation.StageCode, nextOperation.OperationCode),
                            routingGraph);
                            earliestStart = earliestStart.AddMinutes((double)lagTime);
                        }
                        continue; // Split成功，继续下一道工序
                    }
                }

                // Split失败或不允许Split：修复失败
                return new List<FinalTaskDraft>();
            }

            tasks.Add(scheduledTask);
        }

        return tasks;
    }

    /// <summary>
    /// P0-08：从已排 Task 反推各 PI 连续份额的完成时间，供修复路径的自由份额做「不得早于连续份额」的时间下界。
    /// </summary>
    private static Dictionary<string, DateTime> BuildContinuityCompletionByPI(
        IReadOnlyList<LogicalProductionDemand> demands,
        List<FinalTaskDraft> scheduledTasks)
    {
        var map = new Dictionary<string, DateTime>();
        var demandByKey = demands.ToDictionary(d => d.LogicalDemandKey);
        foreach (var task in scheduledTasks)
        {
            if (demandByKey.TryGetValue(task.SourceDraftId, out var demand)
                && demand.IsContinuation
                && !string.IsNullOrEmpty(demand.ProductionInstructionNo))
            {
                var pi = demand.ProductionInstructionNo!;
                if (!map.TryGetValue(pi, out var existing) || task.PlannedEndTime > existing)
                    map[pi] = task.PlannedEndTime;
            }
        }
        return map;
    }

    /// <summary>
    /// 块4（任务喂任务）：计算某需求的「子件完成时间」动态物料下界。
    /// 与 Phase2.GetDynamicMaterialFloor 口径一致：取所有直接子件已排完成时间的最大值。
    /// 任一子件未成功排程（缺料）时 childUnavailable=true，调用方应保持该需求 Unscheduled。
    /// P1-05：internal static 供 Phase5 Gap Compaction 复用。
    /// </summary>
    internal static DateTime GetDynamicMaterialFloor(
        string demandKey,
        ConstraintContext constraints,
        Dictionary<string, DateTime> demandCompletion,
        out bool childUnavailable)
    {
        childUnavailable = false;

        if (!constraints.CrossMaterialParentToChildren.TryGetValue(demandKey, out var children)
            || children.Count == 0)
        {
            return DateTime.MinValue;
        }

        DateTime floor = DateTime.MinValue;
        foreach (var childKey in children)
        {
            if (!demandCompletion.TryGetValue(childKey, out var childEnd))
            {
                childUnavailable = true;
                continue;
            }
            // P1-12：父件消费相对子件完工的滞后时间（分钟），累加到子件完成时间上。
            if (constraints.CrossMaterialLagMinutes.TryGetValue((demandKey, childKey), out var lagMinutes))
            {
                childEnd = childEnd.AddMinutes((double)lagMinutes);
            }
            if (childEnd > floor) floor = childEnd;
        }

        return floor;
    }

    /// <summary>
    /// 获取物料最早可用时间
    /// 文档：§四 4.6、§十二 Stage overlap
    /// P0-05修复：支持多段Quantity-Time，根据所需数量确定可用时间，并验证总量是否足够
    /// P1-05：internal static 供 Phase5 Gap Compaction 复用。
    /// </summary>
    internal static DateTime GetMaterialEarliestTime(
        long allocationSequence,
        decimal requiredQuantity,
        ConstraintContext constraints,
        DateTime planningStart,
        out bool isSufficient)
    {
        isSufficient = true;

        if (constraints.MaterialAvailability.TryGetValue(allocationSequence, out var segments) && segments.Count > 0)
        {
            // P0-05修复：累计可用数量，找到满足需求数量的最早时间
            decimal accumulated = 0m;
            foreach (var segment in segments.OrderBy(s => s.AvailableTime))
            {
                accumulated += segment.Quantity;
                if (accumulated >= requiredQuantity)
                {
                    // 累计数量满足需求，返回该段时间
                    return segment.AvailableTime;
                }
            }

            // P0-05修复：所有段累计仍不足需求量，标记不足并返回最后一段时间
            isSufficient = false;
            return segments.Max(s => s.AvailableTime);
        }
        return planningStart;
    }

    /// <summary>
    /// 获取工序的合格资源列表
    /// </summary>
    private List<int> GetEligibleResources(
        int materialId,
        OperationNode operation,
        ConstraintContext constraints)
    {
        // 第4轮C1修复：索引加入MaterialId
        // 0号位 2026-09-29 裁决 §5.3 落实：键升为强类型 EligibilityLookupKey，**补上 ProductionDepartmentId**
        // （旧键写死 "DEFAULT" 且无部门 ⇒ 两个部门的同名工序资格被合并 ⇒ 跨部门串资源）。
        // ⚠ 契约 OperationResourceEligibility **无 StageCode 字段**，无法再细到 Stage（残留见键类型注释）。
        var key = new EligibilityLookupKey(
            materialId, operation.ProductionDepartmentId, operation.RouteCode, operation.PathId, operation.OperationCode);

        if (constraints.OperationResourceEligibility.TryGetValue(key, out var resources))
        {
            return resources;
        }
        return new List<int>();
    }

    /// <summary>
    /// P1-01：旧版 Phase4 专用「只认 Id」的软偏好排序已**移除**，统一改调
    /// <see cref="PhaseTwoInitialScheduler.OrderResourcesByPreference"/>（同时消费 Code + Id + P1-02 作用域）。
    /// </summary>

    /// <summary>
    /// 正排寻找时间槽
    /// P1-05：internal static 供 Phase5 Gap Compaction 复用（同一套日历感知槽查找，不另建）。
    /// </summary>
    internal static TimeWindow? FindForwardSlot(
        DateTime earliestStart,
        TimeSpan duration,
        int resourceId,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningEnd)
    {
        if (!constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar) || calendar.Count == 0)
        {
            return null;
        }

        // 0号位裁决（2026-09-12）：无末期限制 —— 正排不再被 planningEnd 截断，
        // 只受资源日历窗口约束。与 Phase2.FindForwardSlot 口径一致。
        foreach (var calWindow in calendar.OrderBy(w => w.Start))
        {
            if (calWindow.End <= earliestStart) continue;

            var windowStart = calWindow.Start > earliestStart ? calWindow.Start : earliestStart;
            var windowEnd = calWindow.End;

            if (windowEnd - windowStart < duration) continue;

            var slot = FindFirstAvailableSlot(windowStart, duration, resourceId, resourceOccupancy);
            if (slot.HasValue && slot.Value.End <= windowEnd)
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// 在窗口内找第一个空闲槽
    /// P1-05：internal static 供 FindForwardSlot 静态化后继续调用。
    /// </summary>
    private static TimeWindow? FindFirstAvailableSlot(
        DateTime windowStart,
        TimeSpan duration,
        int resourceId,
        Dictionary<int, List<TimeWindow>> resourceOccupancy)
    {
        if (!resourceOccupancy.ContainsKey(resourceId))
        {
            return new TimeWindow(windowStart, windowStart + duration);
        }

        var occupied = resourceOccupancy[resourceId].OrderBy(w => w.Start).ToList();
        var cursor = windowStart;

        foreach (var occ in occupied)
        {
            if (occ.Start >= cursor + duration)
            {
                return new TimeWindow(cursor, cursor + duration);
            }
            cursor = occ.End > cursor ? occ.End : cursor;
        }

        return new TimeWindow(cursor, cursor + duration);
    }

    /// <summary>
    /// 创建 FinalTaskDraft
    /// 文档：§四 4.2、§五 5.1、§十六 Firm/Frozen/Execution 继承
    /// Task.Quantity = NetOutputQty（净合格产出）
    /// TaskType 继承 Demand 的 Firm/Frozen/Execution 标记
    /// </summary>
    private FinalTaskDraft CreateTask(
        LogicalProductionDemand demand,
        OperationNode operation,
        int resourceId,
        DateTime start,
        DateTime end,
        decimal setupMinutes,     // item1 接线（阶段二）：规则解析出的换型分钟
        ConstraintContext constraints,
        string batchDraftKey,     // P0-03：**本批真实归批键**（调用方逐批传入，不由 LogicalDemandKey 反推）
        string? setupSource = null)  // SetupSource 填充（v1.2/5号位 值契约）：SetupOutcome → 大写 5 值
    {
        // P0-16修复：V1新生成的都是生产Task，统一使用PRODUCTION
        // UNLOCATED、无PI等作为独立标识/来源事实，不增加新TaskType
        string taskType = "PRODUCTION";

        // P0-04修复：补齐FinalTaskDraft必需字段，Duration已使用 StandardDuration × PlannedProcessQty ÷ CapacityFactor

        return new FinalTaskDraft
        {
            FinalDraftId = Guid.NewGuid().ToString(),
            SourceDraftId = demand.LogicalDemandKey,
            MaterialId = demand.MaterialId,
            FactoryId = demand.FactoryId,
            StageCode = operation.StageCode ?? string.Empty,
            OperationCode = operation.OperationCode,
            TaskType = taskType,
            ResourceId = resourceId,
            ResourceCode = GetResourceCode(resourceId, constraints),
            RouteCode = operation.RouteCode,
            PathId = operation.PathId,
            Quantity = demand.NetOutputQty,
            PlannedProcessQty = demand.PlannedProcessQty,
            UOM = demand.UOM ?? string.Empty,
            PlannedStartTime = start,
            PlannedEndTime = end,
            SetupTime = setupMinutes,   // item1 接线：规则值（RoutingOperation.SetupTime 已废止，v1.2 §1.2）
            SetupSource = setupSource,  // SetupSource 填充：SetupOutcome → 大写 5 值（2号位 原样落库）
            Priority = demand.DemandSequence,
            IsVirtual = false,
            // v1.6 §1：FinalTask 必须原样回传 ContinuationKey + 归批键（局部修复/拆分新建的 Task 同样要带）。
            // P0-02：批键**不由 Route/Path 派生**（键域 = (需求键, 批序号)）——
            //   重建的 Task 属**同一执行批**，必须复用 Phase2 为该批发出的键（否则同一批被劈成两个键）。
            ContinuationKey = demand.ContinuationKey,
            // P0-03：批键**逐批由调用方传入**（本批真实键）——不再从 LogicalDemandKey 反推首批键
            //   （多批时反推会把 Batch-002 的修复结果写回 Batch-001 的键）。
            ExecutionBatchDraftKey = batchDraftKey
        };
    }

    /// <summary>
    /// P1-08修复：资源编码回填（ResourceId → ResourceCode）。
    /// 查不到时返回空串，不抛异常（资源定义缺省时 FinalTaskDraft.ResourceCode 留空，2号位落库兜底）。
    /// </summary>
    private static string GetResourceCode(int resourceId, ConstraintContext constraints)
        => constraints.ResourceCodes.TryGetValue(resourceId, out var code) ? code : string.Empty;

    /// <summary>
    /// P0-04修复：获取资源产能系数
    /// 第4轮C1修复：索引加入MaterialId
    /// </summary>
    private decimal? GetCapacityFactor(int materialId, OperationNode operation, int resourceId, ConstraintContext constraints)
    {
        // 0号位 2026-09-29 裁决 §5.3 落实：与 GetEligibleResources 同步升维（键补 ProductionDepartmentId）
        var key = new EligibilityLookupKey(
            materialId, operation.ProductionDepartmentId, operation.RouteCode, operation.PathId, operation.OperationCode);
        if (constraints.ResourceCapacityFactors.TryGetValue(key, out var resourceFactors))
        {
            if (resourceFactors.TryGetValue(resourceId, out var capacityFactor))
            {
                return capacityFactor;
            }
        }
        // 第5轮修复：CapacityFactor查不到时返回null，不静默使用1.0
        return null;
    }

    /// <summary>
    /// P0-17修复：获取工序间的Lag时间（分钟）
    /// 应用Routing LagTime到工序间时间依赖
    /// 第4轮审核修正：Dependencies按ToOperationCode存储，应查toOperationCode
    /// 0号位 2026-09-29 裁决 §5.3：节点身份升维为 (StageCode, OperationCode)，
    ///   出入参由 string operationCode 改为 OperationNodeKey，避免同码跨 Stage 时取到错误边的 LagTime。
    /// </summary>
    private decimal GetLagTime(OperationNodeKey fromNode, OperationNodeKey toNode, RoutingGraph routingGraph)
    {
        // Dependencies结构：Key=To节点, Value=该To的所有前驱边
        // 应查找toNode的前驱边列表，找到From匹配的边
        if (routingGraph.Dependencies.TryGetValue(toNode, out var edges))
        {
            // 找到从fromNode来的边
            var edge = edges.FirstOrDefault(e => e.From == fromNode);
            if (edge != null)
            {
                return edge.LagTime;
            }
        }
        return 0m; // 默认无延迟
    }

    /// <summary>
    /// 第4轮Split修复：尝试将工序拆分成多个小批次
    /// 文档§十三 13.3第6项：有限Split，Guardrail参数Split候选≤3
    /// </summary>
    private List<FinalTaskDraft> TrySplitOperation(
        LogicalProductionDemand demand,
        OperationNode operation,
        string batchDraftKey,
        List<int> eligibleResources,
        DateTime earliestStart,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningEnd,
        int splitAlternatives,
        decimal minBatchQty)
    {
        var splitTasks = new List<FinalTaskDraft>();

        // Guardrail：Split候选上限（P1-02：读冻结 SplitAlternatives，默认 3 → 尝试 2 分 / 3 分）
        // 拆分下限（P1-02：读冻结 MinBatchQty，默认 0.1）
        var splitCandidates = Enumerable.Range(2, Math.Max(0, splitAlternatives - 1)).ToArray();

        foreach (var splitCount in splitCandidates)
        {
            // 计算每份数量（PlannedProcessQty）
            var qtyPerSplit = demand.PlannedProcessQty / splitCount;
            if (qtyPerSplit < minBatchQty) continue; // 拆分后数量过小，跳过

            var candidateTasks = new List<FinalTaskDraft>();
            var candidateStart = earliestStart;
            bool allSplitsScheduled = true;

            // 尝试为每个Split找到时间槽
            for (int i = 0; i < splitCount; i++)
            {
                FinalTaskDraft? splitTask = null;

                // 遍历合格资源
                foreach (var resourceId in eligibleResources)
                {
                    // 计算Split Task的Duration
                    // 第5轮修复：CapacityFactor缺失或非法时跳过该资源
                    var capacityFactor = GetCapacityFactor(demand.MaterialId, operation, resourceId, constraints);
                    if (capacityFactor == null || capacityFactor <= 0)
                    {
                        continue; // CapacityFactor缺失/非法，跳过该资源
                    }
                    var adjustedDuration = operation.StandardDuration * qtyPerSplit / capacityFactor.Value;
                    var processDuration = TimeSpan.FromMinutes((double)adjustedDuration);

                    // item1 接线（阶段二）：拆分件 Setup 走规则查找——首件前产品 = 资源时间线邻接；
                    // 后续件前产品 = 前一拆分件（同产品 → v1.2 §六 A→A 显式规则或 0）。
                    // 拆分件为模拟放置（成功前不入真实时间线/占用，成功后由调用方统一登记）；
                    // 固定前产品下解析值不随槽位变化，有界收敛（≤3 轮，实际 ≤2）。
                    int? fromMaterial = i == 0
                        ? constraints.ProductTimeline.GetPrevMaterial(resourceId, candidateStart)
                        : demand.MaterialId;
                    decimal setupMinutes = 0m;
                    TimeWindow? pieceSlot = null;
                    SetupOptimizer.SetupResolution? convergedResolution = null;   // SetupSource 填充：记录收敛解析结果
                    for (int iter = 0; iter < 3; iter++)
                    {
                        var totalDuration = processDuration + TimeSpan.FromMinutes((double)setupMinutes);
                        var slot = FindForwardSlot(
                            candidateStart,
                            totalDuration,
                            resourceId,
                            constraints,
                            resourceOccupancy,
                            planningEnd);
                        if (!slot.HasValue)
                        {
                            pieceSlot = null;
                            break;
                        }

                        var resolved = SetupOptimizer.ResolveSetupCore(
                            operation.OperationCode, resourceId, fromMaterial, demand.MaterialId,
                            constraints.SetupExactRules, constraints.SetupDefaultRules);
                        if (resolved.SetupMinutes == setupMinutes)
                        {
                            pieceSlot = slot;
                            convergedResolution = resolved;   // SetupSource 填充：收敛值即实际命中类型
                            break;
                        }
                        setupMinutes = resolved.SetupMinutes;
                    }

                    if (pieceSlot.HasValue)
                    {
                        // 创建Split Task：Quantity按比例拆分
                        // Task的PlannedStartTime是加工开始时间（Setup之后）
                        var taskStart = pieceSlot.Value.Start + TimeSpan.FromMinutes((double)setupMinutes);
                        var splitQuantity = demand.NetOutputQty / splitCount;
                        var splitSetupSource = convergedResolution.HasValue
                            ? SetupOptimizer.SetupOutcomeToSource(convergedResolution.Value.Outcome)
                            : null;
                        splitTask = CreateSplitTask(demand, operation, resourceId, taskStart, pieceSlot.Value.End, qtyPerSplit, splitQuantity, setupMinutes, constraints, batchDraftKey, splitSetupSource);

                        candidateTasks.Add(splitTask);

                        // 临时占用该槽（模拟，不真正修改resourceOccupancy）
                        candidateStart = pieceSlot.Value.End; // 下一个Split从当前结束时间开始
                        break;
                    }
                }

                if (splitTask == null)
                {
                    // 某个Split无法调度，该splitCount方案失败
                    allSplitsScheduled = false;
                    break;
                }
            }

            // 如果所有Split都成功调度，返回该方案
            if (allSplitsScheduled && candidateTasks.Count == splitCount)
            {
                return candidateTasks;
            }
        }

        // 所有Split方案都失败
        return new List<FinalTaskDraft>();
    }

    /// <summary>
    /// 第4轮Split修复：创建Split子任务
    /// </summary>
    private FinalTaskDraft CreateSplitTask(
        LogicalProductionDemand demand,
        OperationNode operation,
        int resourceId,
        DateTime start,
        DateTime end,
        decimal plannedProcessQty,
        decimal quantity,
        decimal setupMinutes,     // item1 接线（阶段二）：规则解析出的换型分钟
        ConstraintContext constraints,
        string batchDraftKey,     // P0-03：**本批真实归批键**（拆分是**批内**有限拆分 ⇒ 与本批同键）
        string? setupSource = null)  // SetupSource 填充（v1.2/5号位 值契约）：SetupOutcome → 大写 5 值
    {
        // P0-16修复：V1新生成的都是生产Task，统一使用PRODUCTION
        string taskType = "PRODUCTION";

        return new FinalTaskDraft
        {
            FinalDraftId = Guid.NewGuid().ToString(),
            SourceDraftId = demand.LogicalDemandKey,
            MaterialId = demand.MaterialId,
            FactoryId = demand.FactoryId,
            StageCode = operation.StageCode ?? string.Empty,
            OperationCode = operation.OperationCode,
            TaskType = taskType,
            ResourceId = resourceId,
            ResourceCode = GetResourceCode(resourceId, constraints),
            RouteCode = operation.RouteCode,
            PathId = operation.PathId,
            Quantity = quantity, // Split后的净产出
            PlannedProcessQty = plannedProcessQty, // Split后的加工数量
            UOM = demand.UOM ?? string.Empty,
            PlannedStartTime = start,
            PlannedEndTime = end,
            SetupTime = setupMinutes,   // item1 接线：规则值（RoutingOperation.SetupTime 已废止，v1.2 §1.2）
            SetupSource = setupSource,  // SetupSource 填充：SetupOutcome → 大写 5 值（2号位 原样落库）
            Priority = demand.DemandSequence,
            IsVirtual = false,
            // v1.6 §1：FinalTask 必须原样回传 ContinuationKey + 归批键（局部修复/拆分新建的 Task 同样要带）。
            // P0-02：批键**不由 Route/Path 派生**（键域 = (需求键, 批序号)）——
            //   重建的 Task 属**同一执行批**，必须复用 Phase2 为该批发出的键（否则同一批被劈成两个键）。
            ContinuationKey = demand.ContinuationKey,
            // P0-03：批内有限拆分**保持本批键**（拆分不产生新执行批）。
            ExecutionBatchDraftKey = batchDraftKey
        };
    }
}

/// <summary>
/// 修复结果（Phase 4 输出）
/// </summary>
internal class RepairResult
{
    public List<FinalTaskDraft> RepairedTasks { get; set; } = new();
    public List<string> StillUnscheduledKeys { get; set; } = new();
}

/// <summary>
/// 任务快照：用于Candidate传播检测真实变化
/// </summary>
internal class TaskSnapshot
{
    public int? ResourceId { get; set; }   // 非资源工序 Task（UNCONSTRAINED/WAIT_ONLY）为 NULL
    public DateTime PlannedStartTime { get; set; }
    public DateTime PlannedEndTime { get; set; }
    public decimal Quantity { get; set; }
}
