using LPS.APS.Scheduling.Algorithms;
using LPS.APS.Scheduling.DataStructures;
using LPS.APS.Scheduling.Models;
using LPS.APS.Core.Models.Scheduling;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// 有限产能排程求解器（1号位核心实现）
/// 实现文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》
///
/// 职责：
/// - 接收2号位传来的 LogicalProductionDemands
/// - 执行五阶段有限产能排程（Phase 1-5）
/// - 返回 FinalTaskDraft + AllocationTaskShare + ExplanationFacts
///
/// 架构红线：
/// - 纯内存计算，严禁任何I/O操作
/// - 不读库、不写库，只对内存对象排资源和时间
/// - 设备负荷率必须 ≤ 100%（这是算法正确性保证，不是业务校验）
/// </summary>
public class FiniteCapacitySolver : IFiniteCapacityScheduler
{
    private readonly TimeSlotFinder _timeSlotFinder;
    private readonly SetupOptimizer _setupOptimizer;

    public FiniteCapacitySolver()
    {
        _timeSlotFinder = new TimeSlotFinder();
        _setupOptimizer = new SetupOptimizer();
    }

    /// <summary>
    /// 执行单域有限产能排程（IFiniteCapacityScheduler接口实现）
    /// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》§六 五阶段流程
    /// </summary>
    public async Task<DomainSolveResult> SolveAsync(
        DomainSolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // ═══════════════════════════════════════════════
        // Phase 1: 硬约束构建
        // ═══════════════════════════════════════════════
        var phase1 = new PhaseOneConstraintBuilder();
        var constraints = phase1.BuildConstraints(request);

        // ═══════════════════════════════════════════════
        // Phase 2: 初始有限产能排程
        // ═══════════════════════════════════════════════
        var phase2 = new PhaseTwoInitialScheduler();
        var scheduleResult = phase2.Schedule(request, constraints);

        // ═══════════════════════════════════════════════
        // Phase 3: 可行性与延期诊断
        // ═══════════════════════════════════════════════
        var phase3 = new PhaseThreeDiagnostics();
        var diagnostics = phase3.Diagnose(request, scheduleResult, constraints);

        // ═══════════════════════════════════════════════
        // Phase 4: 有界局部修复
        // ═══════════════════════════════════════════════
        var phase4 = new PhaseFourLocalRepair();
        var repairResult = phase4.Repair(request, scheduleResult, diagnostics, constraints);

        // ═══════════════════════════════════════════════
        // Phase 5: 压缩空隙与最终评价
        // ═══════════════════════════════════════════════
        var phase5 = new PhaseFiveCompression();
        var finalResult = phase5.Compress(request, scheduleResult, repairResult, diagnostics, constraints);

        // 补充耗时统计
        var elapsed = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
        finalResult = new DomainSolveResult
        {
            Success = finalResult.Success,
            ErrorMessage = finalResult.ErrorMessage,
            IsRoughCut = finalResult.IsRoughCut,
            FinalTasks = finalResult.FinalTasks,
            AllocationShares = finalResult.AllocationShares,
            UnscheduledTasks = finalResult.UnscheduledTasks,
            PhysicalPeggingDrafts = finalResult.PhysicalPeggingDrafts,
            ExplanationFacts = finalResult.ExplanationFacts,
            Summary = new SolveSummary
            {
                TotalDrafts = finalResult.Summary.TotalDrafts,
                ScheduledCount = finalResult.Summary.ScheduledCount,
                UnscheduledCount = finalResult.Summary.UnscheduledCount,
                ElapsedMs = elapsed,
                IssueCount = finalResult.Summary.IssueCount,
                UsedRoughCut = finalResult.Summary.UsedRoughCut
            }
        };

        return await Task.FromResult(finalResult);
    }

    /// <summary>
    /// 执行有限产能排程
    /// </summary>
    /// <param name="context">排程沙盘上下文（由2号位在阶段1填充）</param>
    /// <param name="options">排程配置选项</param>
    /// <returns>排程结果</returns>
    public SchedulingResult Solve(SchedulingContext context, SchedulingOptions options)
    {
        var startTime = DateTime.UtcNow;
        var result = new SchedulingResult();

        // 步骤3.1 - 把context中所有Task按Priority降序装入优先级队列
        var taskQueue = BuildPriorityQueue(context);

        // 步骤3.2+3.3 - 主循环，逐个Task出队并寻址时间槽
        while (!taskQueue.IsEmpty)
        {
            var task = taskQueue.Dequeue();

            // 调用TimeSlotFinder为这个Task寻找可排时间槽
            // 内部逻辑：
            //   1. 检查前驱Task完成时间 + 物料AvailableTime → earliestStart
            //   2. 根据Strategy.Mode选倒排(Backward)/正排(Forward)/混合(BackwardThenForward)
            //   3. 倒排：从CustomerDueDate往前推DurationMinutes，检查冲突
            //   4. 正排/撞墙翻转：从earliestStart往后扫设备日历空闲槽，用IntervalTree加速
            //   5. 返回TimeWindow(Start, End)或null（找不到）
            var slot = _timeSlotFinder.FindSlot(task, context, options);

            if (slot.HasValue)
            {
                // 找到了 → 回填Task的计划开始/结束时间（这是1号位唯一允许写SchedulingTask的字段）
                task.PlannedStartTime = slot.Value.Start;
                task.PlannedEndTime = slot.Value.End;
                result.ScheduledCount++;
            }
            else
            {
                // 找不到 → 清空时间字段（标记为未排程状态）
                task.PlannedStartTime = null;
                task.PlannedEndTime = null;
                result.UnscheduledCount++;
                result.UnscheduledReasons.Add(
                    $"Task {task.TaskId}: 无法在计划期内找到可用时间槽");
            }
        }

        result.SolveDuration = DateTime.UtcNow - startTime;
        result.Success = result.UnscheduledCount == 0;
        return result;
    }

    // TODO: 分域排程功能（文档一要求）
    // 当前暂时注释，等待2号位在 SchedulingTask 中添加 DomainId 字段后启用
    //
    // public SchedulingResult SolveByDomain(SchedulingContext context, SchedulingOptions options, int domainId)
    // {
    //     var startTime = DateTime.UtcNow;
    //     var result = new SchedulingResult();
    //     var domainTasks = context.Tasks.Where(t => t.DomainId == domainId).ToList();
    //     if (domainTasks.Count == 0)
    //     {
    //         result.Success = true;
    //         result.SolveDuration = DateTime.UtcNow - startTime;
    //         return result;
    //     }
    //     var taskQueue = new PriorityTaskQueue<SchedulingTask>();
    //     taskQueue.EnqueueRange(domainTasks.Select(t => (t, (double)t.Priority)));
    //     while (!taskQueue.IsEmpty)
    //     {
    //         var task = taskQueue.Dequeue();
    //         var slot = _timeSlotFinder.FindSlot(task, context, options);
    //         if (slot.HasValue)
    //         {
    //             task.PlannedStartTime = slot.Value.Start;
    //             task.PlannedEndTime = slot.Value.End;
    //             result.ScheduledCount++;
    //         }
    //         else
    //         {
    //             task.PlannedStartTime = null;
    //             task.PlannedEndTime = null;
    //             result.UnscheduledCount++;
    //             result.UnscheduledReasons.Add(
    //                 $"Task {task.TaskId} (Domain {domainId}): 无法在计划期内找到可用时间槽");
    //         }
    //     }
    //     result.SolveDuration = DateTime.UtcNow - startTime;
    //     result.Success = result.UnscheduledCount == 0;
    //     return result;
    // }

    /// <summary>
    /// 执行局部重排（场景6步骤6.3，文档二 LOCAL_RESCHEDULE 模式）
    /// 锁定的Task作为时间锚点不移动，只对范围内可移动的Task重新寻址
    /// 典型场景：插单/急单到达，需要重排未开工的Task，但已在制Task不能动
    /// </summary>
    /// <param name="context">排程沙盘上下文</param>
    /// <param name="options">排程配置选项</param>
    /// <param name="scope">范围约束（冻结Task、可移动Task、允许的资源）</param>
    /// <returns>重排结果</returns>
    public SchedulingResult Reschedule(SchedulingContext context, SchedulingOptions options, ScopeConstraint scope)
    {
        var startTime = DateTime.UtcNow;
        var result = new SchedulingResult();

        // 1. 遍历所有Task，清空范围内可移动Task的时间（冻结Task保持原时间不动）
        foreach (var task in context.Tasks)
        {
            if (scope.IsFrozen(task.TaskId))
            {
                // 冻结Task：保持原时间，跳过重排
                continue;
            }

            if (!scope.IsMovable(task.TaskId))
            {
                // 不在可移动列表中：保持原时间
                continue;
            }

            // 可移动Task：清空时间，等待重新寻址
            task.PlannedStartTime = null;
            task.PlannedEndTime = null;
        }

        // 2. 构建优先级队列（只包含需要重排的Task）
        var taskQueue = new PriorityTaskQueue<SchedulingTask>();
        taskQueue.EnqueueRange(
            context.Tasks
                .Where(t => !scope.IsFrozen(t.TaskId) && scope.IsMovable(t.TaskId))
                .Select(t => (t, (double)t.Priority))
        );

        // 3. 主循环：重新寻址
        while (!taskQueue.IsEmpty)
        {
            var task = taskQueue.Dequeue();
            var slot = _timeSlotFinder.FindSlot(task, context, options);

            if (slot.HasValue)
            {
                task.PlannedStartTime = slot.Value.Start;
                task.PlannedEndTime = slot.Value.End;
                result.ScheduledCount++;
            }
            else
            {
                task.PlannedStartTime = null;
                task.PlannedEndTime = null;
                result.UnscheduledCount++;
                result.UnscheduledReasons.Add(
                    $"Task {task.TaskId}: 局部重排无法在计划期内找到可用时间槽");
            }
        }

        result.SolveDuration = DateTime.UtcNow - startTime;
        result.Success = result.UnscheduledCount == 0;
        return result;
    }

    /// <summary>
    /// 构建优先级队列（Priority降序）
    /// </summary>
    /// <param name="context">排程沙盘上下文</param>
    /// <returns>按Priority DESC排序的任务队列</returns>
    private PriorityTaskQueue<SchedulingTask> BuildPriorityQueue(SchedulingContext context)
    {
        var queue = new PriorityTaskQueue<SchedulingTask>();
        queue.EnqueueRange(context.Tasks.Select(t => (t, (double)t.Priority)));
        return queue;
    }
}
