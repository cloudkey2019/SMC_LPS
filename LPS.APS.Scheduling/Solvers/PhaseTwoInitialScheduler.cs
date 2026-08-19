using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 2: 初始有限产能排程
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》§六 Phase 2
///
/// 职责：
/// - 按冻结策略执行初始排程（Forward/Backward/Mixed）
/// - 优先形成一版可行计划
/// - 为每个 LogicalProductionDemand 生成初步的 FinalTaskDraft
/// </summary>
internal class PhaseTwoInitialScheduler
{
    /// <summary>
    /// 执行初始有限产能排程
    /// </summary>
    public InitialScheduleResult Schedule(
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var result = new InitialScheduleResult();

        // 获取排程方向参数
        var direction = request.StrategySnapshot.Parameters.SchedulingDirection;

        // 按 DemandSequence 排序（2号位已排好序）
        var sortedDemands = request.LogicalProductionDemands
            .OrderBy(d => d.DemandSequence)
            .ToList();

        // 资源占用追踪：ResourceId → 已占用时间窗列表
        var resourceOccupancy = InitializeResourceOccupancy(request.Resources, constraints);

        // 逐个需求排程
        // TODO P5: 实现合批逻辑 - 检测相同 Material/Operation/工艺的多个 Demand 是否可以合并成一个 Task
        // 文档§十 10.3: 不同Demand可以合并成一个FinalTask，只要 Material/Operation/工艺相容、Resource能力允许、交期不被破坏
        foreach (var demand in sortedDemands)
        {
            // 获取该需求的工艺路线
            if (!constraints.RoutingGraphs.TryGetValue(demand.MaterialId, out var routeGraphs))
            {
                // 无工艺路线 → 无法排程
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
                continue;
            }

            // V1 固定使用 DEFAULT 路径
            if (!routeGraphs.TryGetValue("DEFAULT", out var routingGraph))
            {
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
                continue;
            }

            // 从 StartStageCode 开始的工序列表
            var operationsToSchedule = GetOperationsFromStage(
                demand.StartStageCode,
                routingGraph,
                constraints);

            if (operationsToSchedule.Count == 0)
            {
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
                continue;
            }

            // 排程该需求的所有工序
            var demandTasks = ScheduleDemandOperations(
                demand,
                operationsToSchedule,
                direction,
                constraints,
                resourceOccupancy,
                request.PlanningStart,
                request.PlanningEnd);

            if (demandTasks.Count == 0)
            {
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
            }
            else
            {
                result.ScheduledTasks.AddRange(demandTasks);
            }
        }

        return result;
    }

    /// <summary>
    /// 初始化资源占用追踪
    /// 文档：§四 4.8、§六 Phase 1
    /// 预填充 Execution/Firm/Frozen 锁定任务的资源占用
    /// </summary>
    private Dictionary<int, List<TimeWindow>> InitializeResourceOccupancy(
        IReadOnlyList<ResourceDefinition> resources,
        ConstraintContext constraints)
    {
        var occupancy = new Dictionary<int, List<TimeWindow>>();
        foreach (var resource in resources)
        {
            occupancy[resource.ResourceId] = new List<TimeWindow>();
        }

        // 预填充锁定任务的资源占用
        foreach (var lockedTask in constraints.LockedTasks.Values)
        {
            if (occupancy.ContainsKey(lockedTask.ResourceId))
            {
                occupancy[lockedTask.ResourceId].Add(
                    new TimeWindow(lockedTask.LockedStart, lockedTask.LockedEnd));
            }
        }

        return occupancy;
    }

    /// <summary>
    /// 获取从指定阶段开始的工序列表（拓扑排序）
    /// 文档：§四 4.4 RoutingDependency，§七 Level 0硬约束
    /// </summary>
    private List<OperationNode> GetOperationsFromStage(
        string startStageCode,
        RoutingGraph routingGraph,
        ConstraintContext constraints)
    {
        // 构建邻接表和入度表
        var adjacency = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();

        foreach (var op in routingGraph.Operations.Values)
        {
            adjacency[op.OperationCode] = new List<string>();
            inDegree[op.OperationCode] = 0;
        }

        // 根据 RoutingDependency 构建图
        foreach (var dep in routingGraph.Dependencies.Values.SelectMany(list => list))
        {
            if (adjacency.ContainsKey(dep.FromOperationCode) &&
                adjacency.ContainsKey(dep.ToOperationCode))
            {
                adjacency[dep.FromOperationCode].Add(dep.ToOperationCode);
                inDegree[dep.ToOperationCode]++;
            }
        }

        // Kahn 算法：拓扑排序
        var queue = new Queue<string>();
        var result = new List<OperationNode>();

        // 将入度为0的工序加入队列
        foreach (var op in routingGraph.Operations.Values)
        {
            if (inDegree[op.OperationCode] == 0)
            {
                queue.Enqueue(op.OperationCode);
            }
        }

        // BFS 拓扑排序
        while (queue.Count > 0)
        {
            var currentCode = queue.Dequeue();
            var currentOp = routingGraph.Operations[currentCode];
            result.Add(currentOp);

            // 减少后继工序的入度
            foreach (var successor in adjacency[currentCode])
            {
                inDegree[successor]--;
                if (inDegree[successor] == 0)
                {
                    queue.Enqueue(successor);
                }
            }
        }

        // 如果排序结果数量少于总工序数，说明存在环
        if (result.Count < routingGraph.Operations.Count)
        {
            // 回退到按工序名排序（避免完全失败）
            return routingGraph.Operations.Values
                .OrderBy(op => op.OperationCode)
                .ToList();
        }

        return result;
    }

    /// <summary>
    /// 排程单个需求的所有工序
    /// 文档：§八 Forward/Backward/Mixed
    /// </summary>
    private List<FinalTaskDraft> ScheduleDemandOperations(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        string direction,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart,
        DateTime planningEnd)
    {
        var tasks = new List<FinalTaskDraft>();

        // 根据排程方向选择策略
        if (direction == "BACKWARD")
        {
            tasks = ScheduleBackward(demand, operations, constraints, resourceOccupancy, planningStart, planningEnd);
        }
        else if (direction == "FORWARD")
        {
            tasks = ScheduleForward(demand, operations, constraints, resourceOccupancy, planningStart, planningEnd);
        }
        else // MIXED 或其他
        {
            // 先尝试倒排，失败则转正排（§八 8.3 Mixed模式）
            tasks = ScheduleBackward(demand, operations, constraints, resourceOccupancy, planningStart, planningEnd);
            if (tasks.Count == 0)
            {
                tasks = ScheduleForward(demand, operations, constraints, resourceOccupancy, planningStart, planningEnd);
            }
        }

        return tasks;
    }

    /// <summary>
    /// 倒排：从 RequiredAvailableTime 往前推
    /// </summary>
    private List<FinalTaskDraft> ScheduleBackward(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart,
        DateTime planningEnd)
    {
        var tasks = new List<FinalTaskDraft>();
        var currentEndTime = demand.RequiredAvailableTime;

        // 从最后一道工序往前倒推
        for (int i = operations.Count - 1; i >= 0; i--)
        {
            var operation = operations[i];
            var duration = TimeSpan.FromMinutes((double)operation.StandardDuration);

            // 计算候选开始时间
            var candidateStart = currentEndTime - duration;

            // 检查是否早于计划期起点
            if (candidateStart < planningStart)
            {
                return new List<FinalTaskDraft>(); // 倒排失败
            }

            // 查找合格资源
            var eligibleResources = GetEligibleResources(demand.MaterialId, operation.OperationCode, constraints);
            if (eligibleResources.Count == 0)
            {
                return new List<FinalTaskDraft>(); // 无合格资源
            }

            // 尝试在合格资源上找时间槽
            FinalTaskDraft? scheduledTask = null;
            foreach (var resourceId in eligibleResources)
            {
                var slot = FindBackwardSlot(
                    candidateStart,
                    currentEndTime,
                    resourceId,
                    constraints,
                    resourceOccupancy,
                    planningStart);

                if (slot.HasValue)
                {
                    // 找到可用槽 → 生成任务
                    scheduledTask = CreateTask(demand, operation, resourceId, slot.Value.Start, slot.Value.End);

                    // 更新资源占用
                    resourceOccupancy[resourceId].Add(new TimeWindow(slot.Value.Start, slot.Value.End));

                    // 更新前驱工序的结束时间
                    currentEndTime = slot.Value.Start;
                    break;
                }
            }

            if (scheduledTask == null)
            {
                return new List<FinalTaskDraft>(); // 排程失败
            }

            tasks.Insert(0, scheduledTask); // 倒序插入
        }

        return tasks;
    }

    /// <summary>
    /// 正排：从物料可用时间往后推
    /// </summary>
    private List<FinalTaskDraft> ScheduleForward(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart,
        DateTime planningEnd)
    {
        var tasks = new List<FinalTaskDraft>();

        // 获取物料最早可用时间
        var earliestStart = GetMaterialEarliestTime(demand.AllocationSequence, constraints, planningStart);

        // 从第一道工序往后推
        // TODO P7: Stage overlap / 阈值启动
        // 文档§十二：如果配置了阈值数量，上游完成达到阈值后下游即可开始，无需等待整批全部完成
        // 这是保留 40件/60件 分段 AvailableTime 的重要原因
        foreach (var operation in operations)
        {
            var duration = TimeSpan.FromMinutes((double)operation.StandardDuration);

            // 查找合格资源
            var eligibleResources = GetEligibleResources(demand.MaterialId, operation.OperationCode, constraints);
            if (eligibleResources.Count == 0)
            {
                return new List<FinalTaskDraft>(); // 无合格资源
            }

            // 尝试在合格资源上找时间槽
            FinalTaskDraft? scheduledTask = null;
            foreach (var resourceId in eligibleResources)
            {
                var slot = FindForwardSlot(
                    earliestStart,
                    duration,
                    resourceId,
                    constraints,
                    resourceOccupancy,
                    planningEnd);

                if (slot.HasValue)
                {
                    scheduledTask = CreateTask(demand, operation, resourceId, slot.Value.Start, slot.Value.End);
                    resourceOccupancy[resourceId].Add(new TimeWindow(slot.Value.Start, slot.Value.End));
                    earliestStart = slot.Value.End; // 下道工序从此时间开始
                    break;
                }
            }

            if (scheduledTask == null)
            {
                return new List<FinalTaskDraft>(); // 排程失败
            }

            tasks.Add(scheduledTask);
        }

        return tasks;
    }

    /// <summary>
    /// 获取物料最早可用时间
    /// 文档：§四 4.6、§十二 Stage overlap
    /// 支持多段Quantity-Time，不能压平为单一时间
    /// </summary>
    private DateTime GetMaterialEarliestTime(
        long allocationSequence,
        ConstraintContext constraints,
        DateTime planningStart)
    {
        if (constraints.MaterialAvailability.TryGetValue(allocationSequence, out var segments) && segments.Count > 0)
        {
            // 返回最早的一段可用时间（不压平多段）
            return segments.Min(s => s.AvailableTime);
        }
        return planningStart;
    }

    /// <summary>
    /// 获取工序的合格资源列表（按优先级排序）
    /// </summary>
    private List<int> GetEligibleResources(
        int materialId,
        string operationCode,
        ConstraintContext constraints)
    {
        var key = $"DEFAULT::{operationCode}"; // V1 固定 DEFAULT 路径
        if (constraints.OperationResourceEligibility.TryGetValue(key, out var resources))
        {
            return resources;
        }
        return new List<int>();
    }

    /// <summary>
    /// 倒排寻找时间槽
    /// </summary>
    private TimeWindow? FindBackwardSlot(
        DateTime candidateStart,
        DateTime candidateEnd,
        int resourceId,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart)
    {
        var candidate = new TimeWindow(candidateStart, candidateEnd);

        // 检查日历约束
        if (!IsWithinCalendar(candidate, resourceId, constraints))
        {
            return null;
        }

        // 检查资源占用冲突
        if (HasConflict(candidate, resourceId, resourceOccupancy))
        {
            return null;
        }

        return candidate;
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
        // 获取资源日历
        if (!constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar) || calendar.Count == 0)
        {
            return null;
        }

        // 遍历日历窗口
        foreach (var calWindow in calendar.OrderBy(w => w.Start))
        {
            if (calWindow.End <= earliestStart) continue;
            if (calWindow.Start >= planningEnd) break;

            var windowStart = calWindow.Start > earliestStart ? calWindow.Start : earliestStart;
            var windowEnd = calWindow.End < planningEnd ? calWindow.End : planningEnd;

            if (windowEnd - windowStart < duration) continue;

            // 在窗口内寻找空闲槽
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
    /// TODO P15: 性能优化（§十九）
    /// 当前 O(N) 扫描，目标 10万Task / 15分钟需要：
    /// - Interval Timeline（不用分钟Grid）
    /// - Resource时间轴内存索引
    /// - 避免 O(N²) 全Task扫描
    /// - Setup只局部更新
    /// - Candidate只传播实际变化
    /// </summary>
    private TimeWindow? FindFirstAvailableSlot(
        DateTime windowStart,
        TimeSpan duration,
        int resourceId,
        Dictionary<int, List<TimeWindow>> resourceOccupancy)
    {
        var occupied = resourceOccupancy[resourceId].OrderBy(w => w.Start).ToList();
        var cursor = windowStart;

        foreach (var occ in occupied)
        {
            if (occ.Start >= cursor + duration)
            {
                // 找到间隙
                return new TimeWindow(cursor, cursor + duration);
            }
            cursor = occ.End > cursor ? occ.End : cursor;
        }

        // 最后一个占用槽之后的空间
        return new TimeWindow(cursor, cursor + duration);
    }

    /// <summary>
    /// 检查候选窗口是否在日历内
    /// </summary>
    private bool IsWithinCalendar(
        TimeWindow candidate,
        int resourceId,
        ConstraintContext constraints)
    {
        if (!constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar))
        {
            return false;
        }

        return calendar.Any(c => c.Start <= candidate.Start && c.End >= candidate.End);
    }

    /// <summary>
    /// 检查是否与已占用时间冲突
    /// </summary>
    private bool HasConflict(
        TimeWindow candidate,
        int resourceId,
        Dictionary<int, List<TimeWindow>> resourceOccupancy)
    {
        var occupied = resourceOccupancy[resourceId];
        return occupied.Any(o => Overlaps(candidate, o));
    }

    /// <summary>
    /// 检查两个时间窗是否重叠
    /// </summary>
    private bool Overlaps(TimeWindow a, TimeWindow b)
    {
        return a.Start < b.End && a.End > b.Start;
    }

    /// <summary>
    /// 创建 FinalTaskDraft
    /// 文档：§四 4.2、§五 5.1、§十六 Firm/Frozen/Execution 继承
    /// Task.Quantity = NetOutputQty（净合格产出）
    /// Task.PlannedProcessQty = 计划加工量（用于资源能力占用计算）
    /// TaskType 继承 Demand 的 Firm/Frozen/Execution 标记
    /// </summary>
    private FinalTaskDraft CreateTask(
        LogicalProductionDemand demand,
        OperationNode operation,
        int resourceId,
        DateTime start,
        DateTime end)
    {
        // 根据 ProductionInstructionNo 确定 TaskType
        string taskType;
        if (!string.IsNullOrEmpty(demand.ProductionInstructionNo))
        {
            // 有 PI 的任务，可能是 Firm/Frozen/Execution
            // 这里简化处理，实际应该根据 PI 状态判断
            taskType = "NEW_REQUIREMENT";
        }
        else
        {
            // 无 PI 的任务（规划性生产或 Unlocated）
            taskType = demand.IsUnlocated ? "UNLOCATED" : "PLANNING_ONLY";
        }

        return new FinalTaskDraft
        {
            FinalDraftId = Guid.NewGuid().ToString(),
            SourceDraftId = demand.LogicalDemandKey,
            MaterialId = demand.MaterialId,
            StageCode = operation.StageCode ?? string.Empty,
            OperationCode = operation.OperationCode,
            TaskType = taskType,
            ResourceId = resourceId,
            ResourceCode = string.Empty,
            Quantity = demand.NetOutputQty,
            UOM = string.Empty,
            PlannedStartTime = start,
            PlannedEndTime = end,
            Priority = demand.DemandSequence,
            IsVirtual = false
        };
    }
}

/// <summary>
/// 初始排程结果（Phase 2 输出）
/// </summary>
internal class InitialScheduleResult
{
    public List<FinalTaskDraft> ScheduledTasks { get; set; } = new();
    public List<string> UnscheduledDemandKeys { get; set; } = new();
}
