using LPS.APS.Core.Dto;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 3: 可行性与延期诊断
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》§六 Phase 3
///
/// 职责：
/// - 识别哪些 Demand 未满足
/// - 识别哪些 Task 晚于 RequiredAvailableTime
/// - 识别真实瓶颈
/// - 诊断物料/资源/前序/锁约束
/// - 生成 ScheduleExplanationFact（根因诊断）
/// </summary>
internal class PhaseThreeDiagnostics
{
    /// <summary>
    /// 执行可行性诊断
    /// </summary>
    public DiagnosticsResult Diagnose(
        DomainSolveRequest request,
        InitialScheduleResult scheduleResult,
        ConstraintContext constraints)
    {
        var result = new DiagnosticsResult();

        // P0-12修复：构建已排程任务的索引，使用ToLookup支持多工序（一个Demand生成多个Task）
        var scheduledTasksLookup = scheduleResult.ScheduledTasks
            .ToLookup(t => t.SourceDraftId);

        // ═══════════════════════════════════════════════
        // 1. 延期识别：PlannedEndTime > RequiredAvailableTime
        // ═══════════════════════════════════════════════
        foreach (var demand in request.LogicalProductionDemands)
        {
            // P0-12修复：使用Lookup支持多工序
            if (!scheduledTasksLookup.Contains(demand.LogicalDemandKey))
            {
                // 未排程需求
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
                continue;
            }

            // 找到该需求的最后一道工序
            var demandTasks = scheduledTasksLookup[demand.LogicalDemandKey]
                .OrderBy(t => t.PlannedEndTime)
                .ToList();

            if (demandTasks.Count == 0) continue;

            var lastTask = demandTasks.Last();
            var delay = lastTask.PlannedEndTime - demand.RequiredAvailableTime;

            if (delay > TimeSpan.Zero)
            {
                // 延期
                result.DelayedTaskIds.Add(lastTask.FinalDraftId);

                // 诊断延期原因
                var reasonCode = DiagnoseDelayReason(
                    demand,
                    demandTasks,
                    constraints,
                    request);

                result.ExplanationFacts.Add(new ScheduleExplanationFact
                {
                    FinalDraftId = lastTask.FinalDraftId,
                    ObjectType = "DEMAND",
                    OrderId = demand.OrderId,
                    StageCode = lastTask.StageCode,
                    ReasonCode = reasonCode,
                    Severity = "HIGH",
                    ImpactHours = (decimal)delay.TotalHours,
                    EvidenceJson = $"{{\"RequiredTime\":\"{demand.RequiredAvailableTime:O}\",\"ActualTime\":\"{lastTask.PlannedEndTime:O}\"}}"
                });
            }
        }

        // ═══════════════════════════════════════════════
        // 2. 识别瓶颈资源（Load / AvailableCapacity > 阈值）
        // ═══════════════════════════════════════════════
        var resourceUtilization = CalculateResourceUtilization(
            scheduleResult.ScheduledTasks,
            constraints,
            request.PlanningStart,
            request.PlanningEnd);

        foreach (var (resourceId, utilization) in resourceUtilization)
        {
            if (utilization > 0.85m) // 85% 以上视为瓶颈
            {
                result.BottleneckResourceIds.Add(resourceId);

                result.ExplanationFacts.Add(new ScheduleExplanationFact
                {
                    FinalDraftId = string.Empty,
                    ObjectType = "RESOURCE",
                    ResourceId = resourceId,
                    ReasonCode = "RESOURCE_CAPACITY_SHORTAGE",
                    Severity = "HIGH",
                    ImpactHours = null,
                    EvidenceJson = $"{{\"Utilization\":{utilization:F2}}}"
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 诊断延期原因
    /// </summary>
    private string DiagnoseDelayReason(
        LogicalProductionDemand demand,
        List<FinalTaskDraft> demandTasks,
        ConstraintContext constraints,
        DomainSolveRequest request)
    {
        // 检查物料可用时间
        if (constraints.MaterialAvailability.TryGetValue(demand.AllocationSequence, out var segments))
        {
            var earliestMaterialTime = segments.Min(s => s.AvailableTime);
            var firstTaskStart = demandTasks.Min(t => t.PlannedStartTime);

            if (firstTaskStart < earliestMaterialTime)
            {
                return "MATERIAL_NOT_AVAILABLE";
            }
        }

        // 检查资源容量不足
        var resourceIds = demandTasks.Select(t => t.ResourceId).Distinct().ToList();
        var resourceUtilization = CalculateResourceUtilization(
            demandTasks,
            constraints,
            request.PlanningStart,
            request.PlanningEnd);

        if (resourceIds.Any(rid => resourceUtilization.ContainsKey(rid) && resourceUtilization[rid] > 0.90m))
        {
            return "RESOURCE_CAPACITY_SHORTAGE";
        }

        // 默认原因：前序延期或其他约束
        return "PREDECESSOR_DELAY";
    }

    /// <summary>
    /// 计算资源利用率
    /// </summary>
    private Dictionary<int, decimal> CalculateResourceUtilization(
        List<FinalTaskDraft> tasks,
        ConstraintContext constraints,
        DateTime planningStart,
        DateTime planningEnd)
    {
        var utilization = new Dictionary<int, decimal>();

        var tasksByResource = tasks.GroupBy(t => t.ResourceId);

        foreach (var group in tasksByResource)
        {
            var resourceId = group.Key;

            // 计算总占用时间
            var totalOccupiedMinutes = group
                .Sum(t => (t.PlannedEndTime - t.PlannedStartTime).TotalMinutes);

            // 计算资源可用时间
            var availableMinutes = 0.0;
            if (constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar))
            {
                availableMinutes = calendar
                    .Where(c => c.Start >= planningStart && c.End <= planningEnd)
                    .Sum(c => (c.End - c.Start).TotalMinutes);
            }

            if (availableMinutes > 0)
            {
                utilization[resourceId] = (decimal)(totalOccupiedMinutes / availableMinutes);
            }
        }

        return utilization;
    }
}

/// <summary>
/// 诊断结果（Phase 3 输出）
/// </summary>
internal class DiagnosticsResult
{
    public List<ScheduleExplanationFact> ExplanationFacts { get; set; } = new();
    public List<string> DelayedTaskIds { get; set; } = new();
    public List<string> UnscheduledDemandKeys { get; set; } = new();
    public List<int> BottleneckResourceIds { get; set; } = new();
}
