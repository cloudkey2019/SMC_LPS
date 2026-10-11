using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Dto;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// 有限产能排程求解器（1号位核心实现）
/// 实现文档：《APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md》
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
///
/// 【生产入口】<see cref="SolveAsync"/> 是唯一排程入口（Phase 1-5 五阶段流程）。
/// 早期单方法版本 Solve() / Reschedule() 及其依赖（TimeSlotFinder / IntervalTree / PriorityTaskQueue /
/// ScopeConstraint / SetupOptimizer 实例字段）已随死代码清理批次删除；需回溯请查 SVN 历史。
/// </summary>
public class FiniteCapacitySolver : IFiniteCapacityScheduler
{
    /// <summary>
    /// 执行单域有限产能排程（IFiniteCapacityScheduler接口实现）
    /// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md》§六 五阶段流程
    /// </summary>
    public async Task<DomainSolveResult> SolveAsync(
        DomainSolveRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── V1_4 NEW-06：**未开计数作用域时自动建立** ──
        //   复审 NEW-06 判词（本号位核对：**成立**）：V1_3 的预算快照只写 `SolverDiagnostics.Current`
        //   （`AsyncLocal`），而正常生产 Run **从未** `BeginScope()` ⇒ 全部记录静默丢弃 ⇒
        //   预算诊断**不是**生产 Run 的正式快照。
        //   ⇒ 入口自动建作用域（调用方**已开**则沿用其作用域，压测工装读计数的语义不变），
        //     使 Phase 计时 / 计数器 / 预算快照在**任何** Run 下都被记录（作用域在本方法返回前释放）。
        //   成本：每次 Run 一个小对象 + 5 个 Phase 计时器（相对一次完整排程可忽略）；
        //     另有一条**与作用域无关**的进程级可检索快照（`SolverDiagnostics.LastRunBudgetSnapshot`）。
        if (SolverDiagnostics.HasScope)
        {
            return await SolveCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }

        using var autoScope = SolverDiagnostics.BeginScope();
        return await SolveCoreAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 五阶段排程主体（V1_4 NEW-06 起由 <see cref="SolveAsync"/> 包裹一层诊断作用域后调用）。
    /// </summary>
    private async Task<DomainSolveResult> SolveCoreAsync(
        DomainSolveRequest request,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // ── Phase 边界计时（0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§九 / §十四 第三优先级）──
        //   V1_4 NEW-06 起：`SolveAsync` 入口已保证作用域存在（自动或调用方显式）⇒ `StartPhase()` 恒非 null；
        //   直调本方法（仅测试/内部）时仍按旧口径「无作用域 ⇒ 零开销、零行为影响」。
        //   压测工装在调用 `SolveAsync` 前开 scope 即可拿到 Phase1Ms..Phase5Ms（不触碰 Core 契约 DTO）。

        // ═══════════════════════════════════════════════
        // Phase 1: 硬约束构建
        // ═══════════════════════════════════════════════
        var phase1 = new PhaseOneConstraintBuilder();
        var p1 = SolverDiagnostics.StartPhase();
        ConstraintContext constraints;
        try
        {
            constraints = phase1.BuildConstraints(request);
        }
        catch (SolverInputContractException ex)
        {
            // P0-02（0号位 2026-10-09《APS_V1_2_20261009.md》§三）：输入无法满足冻结契约的唯一标识要求
            //   ⇒ **受控 Fail Closed**（不向外抛未处理异常、**不静默抹除锚点**）。
            SolverDiagnostics.EndPhase(p1, 1);
            return await Task.FromResult(new DomainSolveResult
            {
                Success = false,
                ErrorMessage = "输入契约不满足（Fail Closed）：" + ex.Message,
                Summary = new SolveSummary
                {
                    ElapsedMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
                }
            });
        }
        SolverDiagnostics.EndPhase(p1, 1);

        // ═══════════════════════════════════════════════
        // Phase 2: 初始有限产能排程
        // ═══════════════════════════════════════════════
        var phase2 = new PhaseTwoInitialScheduler();
        var p2 = SolverDiagnostics.StartPhase();
        InitialScheduleResult scheduleResult;
        try
        {
            scheduleResult = phase2.Schedule(request, constraints);
        }
        catch (SolverInputContractException ex)
        {
            // P0-03（0号位 2026-10-09《APS_V1_2_20261009.md》§三）：Phase2 发现**锁定数量闭合不自洽**
            //   （锁定覆盖量超出需求总量）时同样走**受控 Fail Closed**，不带着负的剩余数量继续排程。
            SolverDiagnostics.EndPhase(p2, 2);
            return await Task.FromResult(new DomainSolveResult
            {
                Success = false,
                ErrorMessage = "输入契约不满足（Fail Closed）：" + ex.Message,
                Summary = new SolveSummary
                {
                    ElapsedMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
                }
            });
        }
        SolverDiagnostics.EndPhase(p2, 2);

        // ═══════════════════════════════════════════════
        // Phase 3: 可行性与延期诊断
        // ═══════════════════════════════════════════════
        var phase3 = new PhaseThreeDiagnostics();
        var p3 = SolverDiagnostics.StartPhase();
        var diagnostics = phase3.Diagnose(request, scheduleResult, constraints);
        SolverDiagnostics.EndPhase(p3, 3);

        // ═══════════════════════════════════════════════
        // Phase 4: 有界局部修复
        // ═══════════════════════════════════════════════
        var phase4 = new PhaseFourLocalRepair();
        var p4 = SolverDiagnostics.StartPhase();
        var repairResult = phase4.Repair(request, scheduleResult, diagnostics, constraints);
        SolverDiagnostics.EndPhase(p4, 4);

        // ═══════════════════════════════════════════════
        // Phase 5: 压缩空隙与最终评价
        // ═══════════════════════════════════════════════
        var phase5 = new PhaseFiveCompression();
        var p5 = SolverDiagnostics.StartPhase();
        var finalResult = phase5.Compress(request, scheduleResult, repairResult, diagnostics, constraints);
        SolverDiagnostics.EndPhase(p5, 5);

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
            // B.2：透传 Phase5 导出的求解过程追溯 —— 此前重组装漏拷，会静默丢弃 2号位 r13494 落地的 SolveTraceNotes 载体
            SolveTraceNotes = finalResult.SolveTraceNotes,
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
}