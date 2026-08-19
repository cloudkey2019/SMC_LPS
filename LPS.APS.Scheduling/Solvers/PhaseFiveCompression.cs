using LPS.APS.Core.Dto;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 5: 压缩空隙与最终评价
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》§六 Phase 5
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

        // TODO P13: 实现真实的空隙压缩优化（§六 Phase 5）
        // 在不破坏高优先级交期的情况下：
        // - 减少不必要等待
        // - 减少 WIP
        // - 减少 Setup
        // - 提升利用率
        // - 避免过早生产
        // - 尽量保持计划稳定

        // 生成 AllocationTaskShare（追溯 Allocation → Task 的份额）
        var allocationShares = GenerateAllocationShares(allScheduledTasks, request);

        // 收集未排程需求
        var unscheduledTasks = new List<UnscheduledTaskResult>();

        // Phase 2 未排程的需求
        foreach (var demandKey in scheduleResult.UnscheduledDemandKeys)
        {
            if (!repairResult.RepairedTasks.Any(t => t.SourceDraftId == demandKey))
            {
                unscheduledTasks.Add(new UnscheduledTaskResult
                {
                    DraftId = demandKey,
                    Reason = "Phase 2 初始排程失败，Phase 4 修复未成功"
                });
            }
        }

        // Phase 4 仍未排程的需求
        foreach (var demandKey in repairResult.StillUnscheduledKeys)
        {
            unscheduledTasks.Add(new UnscheduledTaskResult
            {
                DraftId = demandKey,
                Reason = "Phase 4 局部修复后仍无法排程"
            });
        }

        // 构建最终结果
        return new DomainSolveResult
        {
            Success = unscheduledTasks.Count == 0,
            ErrorMessage = unscheduledTasks.Count > 0
                ? $"{unscheduledTasks.Count} 个需求无法排程"
                : null,
            IsRoughCut = false,
            FinalTasks = allScheduledTasks,
            AllocationShares = allocationShares,
            UnscheduledTasks = unscheduledTasks,
            PhysicalPeggingDrafts = Array.Empty<FinalTaskPeggingDraft>(),
            ExplanationFacts = diagnostics.ExplanationFacts,
            Summary = new SolveSummary
            {
                TotalDrafts = allScheduledTasks.Count + unscheduledTasks.Count,
                ScheduledCount = allScheduledTasks.Count,
                UnscheduledCount = unscheduledTasks.Count,
                ElapsedMs = 0, // 由 FiniteCapacitySolver 填充
                IssueCount = diagnostics.ExplanationFacts.Count,
                UsedRoughCut = false
            }
        };
    }

    /// <summary>
    /// 生成 AllocationTaskShare（追溯机制）
    /// 文档：§五 5.2
    /// 闭合检查：Σ ShareQty = 该Allocation需制造的NetOutputQty
    /// </summary>
    private List<AllocationTaskShare> GenerateAllocationShares(
        List<FinalTaskDraft> tasks,
        DomainSolveRequest request)
    {
        var shares = new List<AllocationTaskShare>();

        // 按 AllocationSequence 分组任务
        var tasksByAllocation = tasks
            .Select(task =>
            {
                var demand = request.LogicalProductionDemands
                    .FirstOrDefault(d => d.LogicalDemandKey == task.SourceDraftId);
                return new { Task = task, Demand = demand };
            })
            .Where(x => x.Demand != null)
            .GroupBy(x => x.Demand!.AllocationSequence);

        // 为每个 Allocation 生成 TaskShare
        foreach (var group in tasksByAllocation)
        {
            var allocationSeq = group.Key;
            var totalShareQty = 0m;

            foreach (var item in group)
            {
                var task = item.Task;
                var demand = item.Demand!;

                // 生成 AllocationTaskShare
                shares.Add(new AllocationTaskShare
                {
                    FinalDraftId = task.FinalDraftId,
                    AllocationSequence = allocationSeq,
                    ComponentQty = task.Quantity
                });

                totalShareQty += task.Quantity;
            }

            // 闭合检查：Σ ShareQty 应该等于 NetOutputQty
            var expectedQty = group.First().Demand!.NetOutputQty;
            if (Math.Abs(totalShareQty - expectedQty) > 0.001m)
            {
                // 数量不闭合，记录警告（实际应用中可能需要更严格处理）
                // TODO: 考虑是否需要调整最后一个 Task 的 Quantity 来闭合
            }
        }

        return shares;
    }
}
