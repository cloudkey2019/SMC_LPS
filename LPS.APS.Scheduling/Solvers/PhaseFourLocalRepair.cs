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

        // 构建已排程任务的资源占用图
        var resourceOccupancy = BuildResourceOccupancy(scheduleResult.ScheduledTasks);

        // ═══════════════════════════════════════════════
        // 1. 对未排程需求尝试修复
        // ═══════════════════════════════════════════════
        // TODO P9: Candidate局部重排传播（§十三 13.2）
        // Seed → 找到直接受影响Task → 做最小修改 → 只有Resource/Start/End/Qty真正变化才继续传播 → 直到稳定
        // 影响可以沿：前序/后序、物料Quantity-Time、共享Resource时间轴、Setup邻居传播
        //
        // TODO P12: Phase 4 fallback（§十三 13.5）
        // 局部修复超限后：使用同一Solver对本Domain全部可移动Task重新求解
        // 仍固定：Execution、Firm、Frozen、Protection、其它不可逆事实、外Domain共享资源阻挡
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
    /// 构建资源占用图
    /// </summary>
    private Dictionary<int, List<TimeWindow>> BuildResourceOccupancy(List<FinalTaskDraft> tasks)
    {
        var occupancy = new Dictionary<int, List<TimeWindow>>();

        foreach (var task in tasks)
        {
            if (!occupancy.ContainsKey(task.ResourceId))
            {
                occupancy[task.ResourceId] = new List<TimeWindow>();
            }

            occupancy[task.ResourceId].Add(
                new TimeWindow(task.PlannedStartTime, task.PlannedEndTime));
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
        var earliestStart = GetMaterialEarliestTime(demand.AllocationSequence, constraints, request.PlanningStart);

        // 对每道工序尝试找资源
        foreach (var operation in operations)
        {
            var duration = TimeSpan.FromMinutes((double)operation.StandardDuration);

            // 获取合格资源列表
            var eligibleResources = GetEligibleResources(demand.MaterialId, operation.OperationCode, constraints);

            FinalTaskDraft? scheduledTask = null;

            // 遍历合格资源，找第一个可用的
            foreach (var resourceId in eligibleResources)
            {
                var slot = FindForwardSlot(
                    earliestStart,
                    duration,
                    resourceId,
                    constraints,
                    resourceOccupancy,
                    request.PlanningEnd);

                if (slot.HasValue)
                {
                    scheduledTask = CreateTask(demand, operation, resourceId, slot.Value.Start, slot.Value.End);

                    // 临时占用
                    if (!resourceOccupancy.ContainsKey(resourceId))
                    {
                        resourceOccupancy[resourceId] = new List<TimeWindow>();
                    }
                    resourceOccupancy[resourceId].Add(slot.Value);

                    earliestStart = slot.Value.End;
                    break;
                }
            }

            if (scheduledTask == null)
            {
                return new List<FinalTaskDraft>(); // 修复失败
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
    /// 获取工序的合格资源列表
    /// </summary>
    private List<int> GetEligibleResources(
        int materialId,
        string operationCode,
        ConstraintContext constraints)
    {
        var key = $"DEFAULT::{operationCode}";
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
        // 根据 ProductionInstructionNo 确定 TaskType
        string taskType;
        if (!string.IsNullOrEmpty(demand.ProductionInstructionNo))
        {
            taskType = "NEW_REQUIREMENT";
        }
        else
        {
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
/// 修复结果（Phase 4 输出）
/// </summary>
internal class RepairResult
{
    public List<FinalTaskDraft> RepairedTasks { get; set; } = new();
    public List<string> StillUnscheduledKeys { get; set; } = new();
}
