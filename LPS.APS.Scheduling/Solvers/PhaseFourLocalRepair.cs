using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 4: 有界局部修复
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》§六 Phase 4
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

        if (request.CandidateContext?.ChangeSeedKeys != null && request.CandidateContext.ChangeSeedKeys.Count > 0)
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

        foreach (var seedKey in request.CandidateContext!.ChangeSeedKeys)
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

        // 2. 传播Guardrail（文档§十三 13.4）
        // 第8轮P0-02修复：30%是警戒线而非硬截断，修复小规模场景Bug
        int maxPropagationRounds = 10;
        decimal maxAffectedRatio = 0.3m; // 30%警戒线
        int totalScheduledTasks = scheduleResult.ScheduledTasks.Count;

        // 修复小规模场景Bug：至少允许1个任务受影响，避免0阈值导致传播无法进入
        int maxAffectedTasks = Math.Max(1, (int)(totalScheduledTasks * maxAffectedRatio));

        bool shouldTriggerFallback = false; // 超警戒线标志

        // 3. 传播循环：识别受影响任务，做最小修改，只在真实变化时继续传播
        var propagationRound = 0;
        var taskSnapshots = new Dictionary<string, TaskSnapshot>(); // FinalDraftId -> 快照
        var immovableTasks = IdentifyImmovableTasks(request, scheduleResult.ScheduledTasks);

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

            // 第9轮P0-02.1修复：先尝试重排受影响Task，让它们真实变化
            foreach (var affectedTaskId in currentRoundAffected)
            {
                var task = scheduleResult.ScheduledTasks.FirstOrDefault(t => t.FinalDraftId == affectedTaskId);
                if (task == null) continue;

                // 核心修复：真正重新安排Task位置
                // 1. 从resourceOccupancy中移除当前Task占用
                if (resourceOccupancy.TryGetValue(task.ResourceId, out var occupiedWindows))
                {
                    occupiedWindows.RemoveAll(w =>
                        w.Start == task.PlannedStartTime && w.End == task.PlannedEndTime);
                }

                // 2. 计算该Task的工序约束时间窗
                var demand = request.LogicalProductionDemands
                    .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);

                DateTime earliestStart = task.PlannedStartTime; // 默认保持原位置
                DateTime latestEnd = task.PlannedEndTime;

                if (demand != null && constraints.RoutingGraphs.TryGetValue(demand.MaterialId, out var routeGraphs))
                {
                    if (routeGraphs.TryGetValue("DEFAULT", out var graph))
                    {
                        // 检查前驱约束：必须在所有前驱完成后开始
                        if (graph.Dependencies.TryGetValue(task.OperationCode, out var predecessors))
                        {
                            foreach (var pred in predecessors)
                            {
                                var predecessorTask = scheduleResult.ScheduledTasks
                                    .FirstOrDefault(t => t.SourceDraftId == task.SourceDraftId &&
                                                        t.OperationCode == pred.FromOperationCode);
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
                            .Where(kvp => kvp.Value.Any(dep => dep.FromOperationCode == task.OperationCode))
                            .SelectMany(kvp => kvp.Value.Where(dep => dep.FromOperationCode == task.OperationCode)
                                .Select(dep => new { ToOpCode = kvp.Key, LagMinutes = dep.LagTime }));

                        foreach (var succ in successors)
                        {
                            var successorTask = scheduleResult.ScheduledTasks
                                .FirstOrDefault(t => t.SourceDraftId == task.SourceDraftId &&
                                                    t.OperationCode == succ.ToOpCode);
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

                // 3. 在资源上寻找可用时间窗（简化实现：尝试向前或向后移动）
                var duration = task.PlannedEndTime - task.PlannedStartTime;
                DateTime newStart = earliestStart;
                DateTime newEnd = earliestStart + duration;

                // 检查资源占用冲突，尝试找到无冲突的时间段
                bool foundSlot = false;
                if (resourceOccupancy.TryGetValue(task.ResourceId, out var windows))
                {
                    var sortedWindows = windows.OrderBy(w => w.Start).ToList();

                    // 尝试在现有占用窗口之间插入
                    for (int i = 0; i <= sortedWindows.Count; i++)
                    {
                        DateTime slotStart = (i == 0) ? earliestStart : sortedWindows[i - 1].End;
                        DateTime slotEnd = (i == sortedWindows.Count) ? latestEnd : sortedWindows[i].Start;

                        if (slotStart + duration <= slotEnd && slotStart + duration <= latestEnd)
                        {
                            newStart = slotStart;
                            newEnd = slotStart + duration;
                            foundSlot = true;
                            break;
                        }
                    }
                }
                else
                {
                    foundSlot = true; // 资源空闲
                }

                // 4. 如果找到新位置且与原位置不同，创建新FinalTaskDraft替换
                if (foundSlot && (newStart != task.PlannedStartTime || newEnd != task.PlannedEndTime))
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
                        SetupTime = task.SetupTime,
                        Priority = task.Priority,
                        IsVirtual = task.IsVirtual,
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

                    // 5. 更新resourceOccupancy
                    if (!resourceOccupancy.ContainsKey(task.ResourceId))
                    {
                        resourceOccupancy[task.ResourceId] = new List<TimeWindow>();
                    }
                    resourceOccupancy[task.ResourceId].Add(new TimeWindow(newStart, newEnd));
                }
                else if (foundSlot)
                {
                    // 位置未变，但需要重新加回resourceOccupancy
                    if (!resourceOccupancy.ContainsKey(task.ResourceId))
                    {
                        resourceOccupancy[task.ResourceId] = new List<TimeWindow>();
                    }
                    resourceOccupancy[task.ResourceId].Add(new TimeWindow(task.PlannedStartTime, task.PlannedEndTime));
                }


                // 检查1: 工艺前后工序依赖
                // 找到该任务对应的Demand和Routing
                var taskDemand = request.LogicalProductionDemands
                    .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);
                if (taskDemand != null && constraints.RoutingGraphs.TryGetValue(taskDemand.MaterialId, out var taskRouteGraphs))
                {
                    if (taskRouteGraphs.TryGetValue("DEFAULT", out var graph))
                    {
                        // 1.1 前序传播：找当前工序的前驱
                        if (graph.Dependencies.TryGetValue(task.OperationCode, out var predecessors))
                        {
                            foreach (var pred in predecessors)
                            {
                                var predecessorTask = scheduleResult.ScheduledTasks
                                    .FirstOrDefault(t => t.SourceDraftId == task.SourceDraftId &&
                                                        t.OperationCode == pred.FromOperationCode);
                                if (predecessorTask != null && !immovableTasks.Contains(predecessorTask.FinalDraftId))
                                {
                                    nextRoundAffected.Add(predecessorTask.FinalDraftId);
                                }
                            }
                        }

                        // 1.2 后序传播：找到以当前Operation作为FromOperationCode的所有后继
                        var successors = graph.Dependencies
                            .Where(kvp => kvp.Value.Any(dep => dep.FromOperationCode == task.OperationCode))
                            .SelectMany(kvp => scheduleResult.ScheduledTasks
                                .Where(t => t.SourceDraftId == task.SourceDraftId &&
                                           t.OperationCode == kvp.Key &&
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
                        if (constraints.RoutingGraphs.TryGetValue(currentTaskForMaterial.MaterialId, out var materialRouteGraphs))
                        {
                            if (materialRouteGraphs.TryGetValue("DEFAULT", out var materialGraph))
                            {
                                // 检查consumer是否依赖当前Task的工序
                                if (materialGraph.Dependencies.TryGetValue(consumer.OperationCode, out var consumerPreds))
                                {
                                    if (consumerPreds.Any(dep => dep.FromOperationCode == currentTaskForMaterial.OperationCode))
                                    {
                                        nextRoundAffected.Add(consumer.FinalDraftId);
                                    }
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
        }

        // 第9轮P0-03.1修复：轮次耗尽时自动触发Fallback
        if (propagationRound >= maxPropagationRounds)
        {
            shouldTriggerFallback = true;
        }

        // 4. 第9轮P0-03修复：Fallback兜底 - 调用同一Solver对本Domain全部可移动任务重排
        if (shouldTriggerFallback || affectedTasks.Count > maxAffectedTasks)
        {
            // 构建Fallback重排请求：保留不可移动约束，重排全部可移动任务
            var fallbackScheduler = new PhaseTwoInitialScheduler();

            // 重新构建初始排程，此时constraints.LockedTasks已包含所有不可移动任务
            // ExternalDomainResourceBlocks也已在resourceOccupancy中预填充
            var fallbackResult = fallbackScheduler.Schedule(request, constraints);

            // 第9轮P0-03.2修复：Fallback结果替换语义，不追加到原计划
            // 从ScheduledTasks中移除所有可移动Task（保留immovableTasks）
            var immovableScheduledTasks = scheduleResult.ScheduledTasks
                .Where(t => immovableTasks.Contains(t.FinalDraftId))
                .ToList();

            // 清空原计划，保留不可移动部分
            scheduleResult.ScheduledTasks.Clear();
            scheduleResult.ScheduledTasks.AddRange(immovableScheduledTasks);

            // 用Fallback完整结果替换可移动部分
            scheduleResult.ScheduledTasks.AddRange(fallbackResult.ScheduledTasks);

            // 将Fallback结果同样体现在RepairResult中（Phase5会合并）
            result.RepairedTasks.AddRange(fallbackResult.ScheduledTasks);
            result.StillUnscheduledKeys.AddRange(fallbackResult.UnscheduledDemandKeys);

            return result;
        }

        // 当前简化实现：优先修复受影响的未排需求
        var orderedDemandKeys = affectedDemands
            .Concat(scheduleResult.UnscheduledDemandKeys.Except(affectedDemands))
            .ToList();

        foreach (var demandKey in orderedDemandKeys)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == demandKey);

            if (demand == null) continue;

            // 尝试资源切换
            var repairedTasks = TryResourceSwitch(
                demand,
                constraints,
                resourceOccupancy,
                request);

            if (repairedTasks.Count > 0)
            {
                result.RepairedTasks.AddRange(repairedTasks);

                // 更新资源占用
                foreach (var task in repairedTasks)
                {
                    if (!resourceOccupancy.ContainsKey(task.ResourceId))
                    {
                        resourceOccupancy[task.ResourceId] = new List<TimeWindow>();
                    }
                    resourceOccupancy[task.ResourceId].Add(
                        new TimeWindow(task.PlannedStartTime, task.PlannedEndTime));
                }
            }
            else
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

        foreach (var demandKey in scheduleResult.UnscheduledDemandKeys)
        {
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == demandKey);

            if (demand == null) continue;

            // 尝试资源切换
            var repairedTasks = TryResourceSwitch(
                demand,
                constraints,
                resourceOccupancy,
                request);

            if (repairedTasks.Count > 0)
            {
                result.RepairedTasks.AddRange(repairedTasks);

                // 更新资源占用
                foreach (var task in repairedTasks)
                {
                    if (!resourceOccupancy.ContainsKey(task.ResourceId))
                    {
                        resourceOccupancy[task.ResourceId] = new List<TimeWindow>();
                    }
                    resourceOccupancy[task.ResourceId].Add(
                        new TimeWindow(task.PlannedStartTime, task.PlannedEndTime));
                }

                // TODO P6: Setup邻接优化
                // 文档§十一：当Task插入/移动时，重新计算它与前后邻居的Setup
                // Setup属性可包括：模具、刀具、材质、颜色等冻结配置维度
            }
            else
            {
                result.StillUnscheduledKeys.Add(demandKey);
            }
        }

        return result;
    }

    /// <summary>
    /// 识别不可移动任务（Execution/Firm/Frozen/Protection）
    /// 文档：§四 4.8 ImmovableFacts
    /// </summary>
    private HashSet<string> IdentifyImmovableTasks(
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
    /// </summary>
    private Dictionary<int, List<TimeWindow>> BuildResourceOccupancy(
        List<FinalTaskDraft> tasks,
        ConstraintContext constraints)
    {
        var occupancy = new Dictionary<int, List<TimeWindow>>();

        foreach (var task in tasks)
        {
            if (!occupancy.ContainsKey(task.ResourceId))
            {
                occupancy[task.ResourceId] = new List<TimeWindow>();
            }

            // 第4轮Setup修复：PlannedStartTime是加工开始时间（Setup之后），资源占用需从Setup开始
            var occupancyStart = task.PlannedStartTime.AddMinutes(-(double)task.SetupTime);
            occupancy[task.ResourceId].Add(
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
    /// 尝试资源切换（换到其他合格资源）
    /// </summary>
    private List<FinalTaskDraft> TryResourceSwitch(
        LogicalProductionDemand demand,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DomainSolveRequest request)
    {
        // 获取工艺路线
        if (!constraints.RoutingGraphs.TryGetValue(demand.MaterialId, out var routeGraphs))
        {
            return new List<FinalTaskDraft>();
        }

        if (!routeGraphs.TryGetValue("DEFAULT", out var routingGraph))
        {
            return new List<FinalTaskDraft>();
        }

        var operations = routingGraph.Operations.Values
            .OrderBy(op => op.OperationCode)
            .ToList();

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

        // P0-17修复：改为for循环以便访问下一道工序，应用Routing LagTime
        for (int i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];

            // 获取合格资源列表
            var eligibleResources = GetEligibleResources(demand.MaterialId, operation.OperationCode, constraints);

            FinalTaskDraft? scheduledTask = null;

            // 遍历合格资源，找第一个可用的
            foreach (var resourceId in eligibleResources)
            {
                // P0-04修复：Duration = StandardDuration × PlannedProcessQty ÷ CapacityFactor
                // 第4轮Setup修复：加上SetupTime占用资源时间轴
                // 第5轮修复：CapacityFactor缺失或非法时跳过该资源
                var capacityFactor = GetCapacityFactor(demand.MaterialId, operation.OperationCode, resourceId, constraints);
                if (capacityFactor == null || capacityFactor <= 0)
                {
                    continue; // CapacityFactor缺失/非法，跳过该资源
                }
                var adjustedDuration = operation.StandardDuration * demand.PlannedProcessQty / capacityFactor.Value;
                var processDuration = TimeSpan.FromMinutes((double)adjustedDuration);
                var setupDuration = TimeSpan.FromMinutes((double)operation.SetupTime);
                var totalDuration = processDuration + setupDuration;

                var slot = FindForwardSlot(
                    earliestStart,
                    totalDuration,
                    resourceId,
                    constraints,
                    resourceOccupancy,
                    request.PlanningEnd);

                if (slot.HasValue)
                {
                    // Task的PlannedStartTime是加工开始时间（Setup之后）
                    var taskStart = slot.Value.Start + setupDuration;
                    scheduledTask = CreateTask(demand, operation, resourceId, taskStart, slot.Value.End);

                    // 临时占用：从Setup开始
                    if (!resourceOccupancy.ContainsKey(resourceId))
                    {
                        resourceOccupancy[resourceId] = new List<TimeWindow>();
                    }
                    resourceOccupancy[resourceId].Add(slot.Value);

                    // P0-17修复：应用Routing LagTime到下道工序的最早开始时间
                    earliestStart = slot.Value.End;
                    if (i < operations.Count - 1)
                    {
                        var nextOperation = operations[i + 1];
                        var lagTime = GetLagTime(operation.OperationCode, nextOperation.OperationCode, routingGraph);
                        earliestStart = earliestStart.AddMinutes((double)lagTime);
                    }
                    break;
                }
            }

            if (scheduledTask == null)
            {
                // 第4轮Split修复：当前工序无法在任何资源找到完整时间槽时，尝试有限Split
                if (request.StrategySnapshot.Parameters.AllowSplit && demand.PlannedProcessQty > 1.0m)
                {
                    var splitTasks = TrySplitOperation(
                        demand,
                        operation,
                        eligibleResources,
                        earliestStart,
                        constraints,
                        resourceOccupancy,
                        request.PlanningEnd);

                    if (splitTasks.Count > 0)
                    {
                        // Split成功：更新资源占用，推进下道工序最早开始时间
                        // 第5轮修复：Split Task的资源占用必须包含Setup时间
                        foreach (var splitTask in splitTasks)
                        {
                            if (!resourceOccupancy.ContainsKey(splitTask.ResourceId))
                            {
                                resourceOccupancy[splitTask.ResourceId] = new List<TimeWindow>();
                            }
                            // PlannedStartTime是Setup后的加工开始时间，资源占用要从Setup开始算
                            var setupDuration = TimeSpan.FromMinutes((double)splitTask.SetupTime);
                            var resourceStart = splitTask.PlannedStartTime - setupDuration;
                            resourceOccupancy[splitTask.ResourceId].Add(
                                new TimeWindow(resourceStart, splitTask.PlannedEndTime));
                        }

                        tasks.AddRange(splitTasks);

                        // 更新earliestStart为所有Split Task的最晚结束时间
                        earliestStart = splitTasks.Max(t => t.PlannedEndTime);
                        if (i < operations.Count - 1)
                        {
                            var nextOperation = operations[i + 1];
                            var lagTime = GetLagTime(operation.OperationCode, nextOperation.OperationCode, routingGraph);
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
    /// 获取物料最早可用时间
    /// 文档：§四 4.6、§十二 Stage overlap
    /// P0-05修复：支持多段Quantity-Time，根据所需数量确定可用时间，并验证总量是否足够
    /// </summary>
    private DateTime GetMaterialEarliestTime(
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
        string operationCode,
        ConstraintContext constraints)
    {
        // 第4轮C1修复：索引加入MaterialId
        var key = $"{materialId}::DEFAULT::{operationCode}";
        if (constraints.OperationResourceEligibility.TryGetValue(key, out var resources))
        {
            return resources;
        }
        return new List<int>();
    }

    /// <summary>
    /// 正排寻找时间槽
    /// </summary>
    private TimeWindow? FindForwardSlot(
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

        foreach (var calWindow in calendar.OrderBy(w => w.Start))
        {
            if (calWindow.End <= earliestStart) continue;
            if (calWindow.Start >= planningEnd) break;

            var windowStart = calWindow.Start > earliestStart ? calWindow.Start : earliestStart;
            var windowEnd = calWindow.End < planningEnd ? calWindow.End : planningEnd;

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
    /// </summary>
    private TimeWindow? FindFirstAvailableSlot(
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
        DateTime end)
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
            ResourceCode = string.Empty, // TODO: 从Resources查找ResourceCode
            RouteCode = null, // TODO: 从Routing获取RouteCode
            PathId = null, // TODO: 从Routing获取PathId
            Quantity = demand.NetOutputQty,
            PlannedProcessQty = demand.PlannedProcessQty,
            UOM = string.Empty,
            PlannedStartTime = start,
            PlannedEndTime = end,
            SetupTime = operation.SetupTime,
            Priority = demand.DemandSequence,
            IsVirtual = false
        };
    }

    /// <summary>
    /// P0-04修复：获取资源产能系数
    /// 第4轮C1修复：索引加入MaterialId
    /// </summary>
    private decimal? GetCapacityFactor(int materialId, string operationCode, int resourceId, ConstraintContext constraints)
    {
        var key = $"{materialId}::DEFAULT::{operationCode}";
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
    /// </summary>
    private decimal GetLagTime(string fromOperationCode, string toOperationCode, RoutingGraph routingGraph)
    {
        // Dependencies结构：Key=ToOperationCode, Value=该To的所有前驱边
        // 应查找toOperation的前驱边列表，找到FromOperationCode匹配的边
        if (routingGraph.Dependencies.TryGetValue(toOperationCode, out var edges))
        {
            // 找到从fromOperation来的边
            var edge = edges.FirstOrDefault(e => e.FromOperationCode == fromOperationCode);
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
        List<int> eligibleResources,
        DateTime earliestStart,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningEnd)
    {
        var splitTasks = new List<FinalTaskDraft>();

        // Guardrail：Split候选≤3，尝试2分和3分
        var splitCandidates = new[] { 2, 3 };

        foreach (var splitCount in splitCandidates)
        {
            // 计算每份数量（PlannedProcessQty）
            var qtyPerSplit = demand.PlannedProcessQty / splitCount;
            if (qtyPerSplit < 0.1m) continue; // 拆分后数量过小，跳过

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
                    // 第4轮Setup修复：Split任务也需要Setup时间
                    // 第5轮修复：CapacityFactor缺失或非法时跳过该资源
                    var capacityFactor = GetCapacityFactor(demand.MaterialId, operation.OperationCode, resourceId, constraints);
                    if (capacityFactor == null || capacityFactor <= 0)
                    {
                        continue; // CapacityFactor缺失/非法，跳过该资源
                    }
                    var adjustedDuration = operation.StandardDuration * qtyPerSplit / capacityFactor.Value;
                    var processDuration = TimeSpan.FromMinutes((double)adjustedDuration);
                    var setupDuration = TimeSpan.FromMinutes((double)operation.SetupTime);
                    var totalDuration = processDuration + setupDuration;

                    var slot = FindForwardSlot(
                        candidateStart,
                        totalDuration,
                        resourceId,
                        constraints,
                        resourceOccupancy,
                        planningEnd);

                    if (slot.HasValue)
                    {
                        // 创建Split Task：Quantity按比例拆分
                        // Task的PlannedStartTime是加工开始时间（Setup之后）
                        var taskStart = slot.Value.Start + setupDuration;
                        var splitQuantity = demand.NetOutputQty / splitCount;
                        splitTask = CreateSplitTask(demand, operation, resourceId, taskStart, slot.Value.End, qtyPerSplit, splitQuantity);

                        candidateTasks.Add(splitTask);

                        // 临时占用该槽（模拟，不真正修改resourceOccupancy）
                        candidateStart = slot.Value.End; // 下一个Split从当前结束时间开始
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
        decimal quantity)
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
            ResourceCode = string.Empty,
            RouteCode = null,
            PathId = null,
            Quantity = quantity, // Split后的净产出
            PlannedProcessQty = plannedProcessQty, // Split后的加工数量
            UOM = string.Empty,
            PlannedStartTime = start,
            PlannedEndTime = end,
            SetupTime = operation.SetupTime,
            Priority = demand.DemandSequence,
            IsVirtual = false
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
    public int ResourceId { get; set; }
    public DateTime PlannedStartTime { get; set; }
    public DateTime PlannedEndTime { get; set; }
    public decimal Quantity { get; set; }
}
