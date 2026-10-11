using System.Collections.Generic;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// **V1_3 F-04 判别性单测**（0号位 2026-10-10《APS_V1_3_20261010.md》§3，REWORK/BLOCKED）。
///
/// 复审判词（§7「1号位必须整改」，逐字）：
///   「F-04 记录 Solver 有效技术预算的 **Run 快照 / 追踪证据**；检验**输入变化、默认和业务旧列变化
///     互不串联**。」
///
/// 判据落法：
///   · **Run 快照 / 追踪证据** ⇒ 本 Run **实际生效**的 `SolverBatchBudget`（版本 / 取源 / 两个生效值）
///     登记进 <see cref="SolverDiagnostics"/>（1号位 自有、非契约的 Run 证据通道）；
///   · **互不串联** 三条**独立**断言：
///       ① 默认（`request == null`）⇒ `VersionedSafeDefault`，取源 `…:default`；
///       ② 输入变化（`Split.MaxOptimizationSplitCount` 改）⇒ 快照**随之变**（单源可追溯）；
///       ③ 业务**旧列**变化（`BatchPolicyRuleSnapshot.MaxOptimizationSplitCount` / `MaxBatchCandidates`
///          改 99/99）⇒ 快照**逐字不变**（B-007：不得从业务旧列读技术预算）。
///
/// 全部纯内存，不触库（用户红线：Integration 直连生产库，只跑 Unit）。
/// </summary>
public class SolverBatchBudgetSnapshotTests
{
    private const string Version = PhaseTwoInitialScheduler.SolverBatchBudget.BudgetVersion;

    private static DomainSolveRequest RequestWithSplit(int maxOptimizationSplitCount)
        => new()
        {
            StrategySnapshot = new SolverStrategySnapshot
            {
                SolverStrategy = new SolverStrategyBlock
                {
                    Split = new SplitParams { MaxOptimizationSplitCount = maxOptimizationSplitCount }
                }
            }
        };

    /// <summary>
    /// ① **默认**：`request == null` ⇒ `VersionedSafeDefault`（3 / 8，取源 `…:default`）。
    ///   并核对**常量与实例一致**（版本号是唯一权威，防两处漂移）。
    /// </summary>
    [Fact]
    public void F04_无请求_取版本化安全默认()
    {
        var budget = PhaseTwoInitialScheduler.ResolveSolverBatchBudget(null);

        Assert.Equal(Version, PhaseTwoInitialScheduler.SolverBatchBudget.BudgetVersion);
        Assert.Equal("SolverBatchBudget/v1", Version);
        Assert.Equal(PhaseTwoInitialScheduler.SolverBatchBudget.DefaultMaxOptimizationSplitCount, budget.MaxOptimizationSplitCount);
        Assert.Equal(PhaseTwoInitialScheduler.SolverBatchBudget.DefaultMaxBatchCandidates, budget.MaxBatchCandidates);
        Assert.Equal(3, budget.MaxOptimizationSplitCount);
        Assert.Equal(8, budget.MaxBatchCandidates);
        Assert.Equal($"{Version}:default", budget.Source);
    }

    /// <summary>
    /// ② **输入变化 ⇒ 快照随之变**：`Split.MaxOptimizationSplitCount = 7` ⇒ 7 / 8，取源 `…:SplitParams`。
    ///   负值 ⇒ 回落默认值 **但仍标 `…:SplitParams`**（取源诚实：确实来自该载体，只是值非法被钳）。
    /// </summary>
    [Fact]
    public void F04_有请求_取自SplitParams_负值回落默认但取源仍标SplitParams()
    {
        var budget = PhaseTwoInitialScheduler.ResolveSolverBatchBudget(RequestWithSplit(7));

        Assert.Equal(7, budget.MaxOptimizationSplitCount);
        Assert.Equal(8, budget.MaxBatchCandidates);       // 无正式载体 ⇒ 恒默认（已登记为待 2/3号位 契约项）
        Assert.Equal($"{Version}:SplitParams", budget.Source);

        var negative = PhaseTwoInitialScheduler.ResolveSolverBatchBudget(RequestWithSplit(-5));
        Assert.Equal(3, negative.MaxOptimizationSplitCount);
        Assert.Equal($"{Version}:SplitParams", negative.Source);
    }

    /// <summary>
    /// ③ **Run 快照 / 追踪证据**：开 scope 后调用 ⇒ `SolverDiagnostics.Counters` 上可读到本 Run
    ///   **实际生效**的版本 / 取源 / 两个生效值（逐 Run 可复现「到底用了什么技术预算」）。
    ///   未开 scope ⇒ 零登记、零开销（生产路径不受影响）。
    /// </summary>
    [Fact]
    public void F04_Run快照登记版本取源与生效值()
    {
        // 未开 scope ⇒ 不登记（且不抛）
        PhaseTwoInitialScheduler.ResolveSolverBatchBudget(RequestWithSplit(5));

        using var scope = SolverDiagnostics.BeginScope();
        PhaseTwoInitialScheduler.ResolveSolverBatchBudget(RequestWithSplit(5));

        Assert.Equal(Version, scope.Counters.SolverBatchBudgetVersion);
        Assert.Equal($"{Version}:SplitParams", scope.Counters.SolverBatchBudgetSource);
        Assert.Equal(5, scope.Counters.SolverBatchBudgetMaxOptimizationSplitCount);
        Assert.Equal(8, scope.Counters.SolverBatchBudgetMaxBatchCandidates);
    }

    /// <summary>
    /// ④ **互不串联（反证核心）**：把**业务旧列** —— `BatchPolicyRuleSnapshot.MaxOptimizationSplitCount` /
    ///   `MaxBatchCandidates` —— 改成 99 / 99，Run 快照**逐字不变**。
    ///   若有人把技术预算改回从业务 policy 读 ⇒ 本用例**红**（快照会变成 99/99）。
    /// </summary>
    [Fact]
    public void F04_业务旧列变化_不影响Run快照()
    {
        // 业务旧列（v5.1.10 恒 null 的两列）——**刻意**设成 99/99 作为诱饵
        var request = new DomainSolveRequest
        {
            StrategySnapshot = new SolverStrategySnapshot
            {
                SolverStrategy = new SolverStrategyBlock
                {
                    Split = new SplitParams { MaxOptimizationSplitCount = 7 }
                },
                BatchPolicies = new List<BatchPolicyRuleSnapshot>
                {
                    new()
                    {
                        MaterialId = 1,
                        ProductionDepartmentId = 100,
                        MaxOptimizationSplitCount = 99,
                        MaxBatchCandidates = 99,
                        AllowSplit = true
                    }
                }
            }
        };

        using var scope = SolverDiagnostics.BeginScope();
        var budget = PhaseTwoInitialScheduler.ResolveSolverBatchBudget(request);

        // 取源与生效值**只**认正式载体 ⇒ 业务旧列的 99/99 不参与
        Assert.Equal(7, budget.MaxOptimizationSplitCount);
        Assert.Equal(8, budget.MaxBatchCandidates);
        Assert.Equal($"{Version}:SplitParams", budget.Source);
        Assert.NotEqual(99, budget.MaxOptimizationSplitCount);
        Assert.NotEqual(99, budget.MaxBatchCandidates);

        Assert.Equal(7, scope.Counters.SolverBatchBudgetMaxOptimizationSplitCount);
        Assert.Equal(8, scope.Counters.SolverBatchBudgetMaxBatchCandidates);
        Assert.Equal($"{Version}:SplitParams", scope.Counters.SolverBatchBudgetSource);
    }

    /// <summary>
    /// ⑤ **互不串联（对照组）**：同一份「业务旧列 99/99」的请求，**只**把正式载体从 7 改成 12
    ///   ⇒ 快照**随之变**为 12/8。
    ///   ⇒ 与 ④ 合读即得判据：**变的是正式载体，不变的才是业务旧列**（两条通道不串联）。
    /// </summary>
    [Fact]
    public void F04_同一业务旧列下_正式载体变化仍生效()
    {
        var policies = new List<BatchPolicyRuleSnapshot>
        {
            new()
            {
                MaterialId = 1, ProductionDepartmentId = 100,
                MaxOptimizationSplitCount = 99, MaxBatchCandidates = 99, AllowSplit = true
            }
        };

        var at7 = PhaseTwoInitialScheduler.ResolveSolverBatchBudget(new DomainSolveRequest
        {
            StrategySnapshot = new SolverStrategySnapshot
            {
                SolverStrategy = new SolverStrategyBlock { Split = new SplitParams { MaxOptimizationSplitCount = 7 } },
                BatchPolicies = policies
            }
        });
        var at12 = PhaseTwoInitialScheduler.ResolveSolverBatchBudget(new DomainSolveRequest
        {
            StrategySnapshot = new SolverStrategySnapshot
            {
                SolverStrategy = new SolverStrategyBlock { Split = new SplitParams { MaxOptimizationSplitCount = 12 } },
                BatchPolicies = policies
            }
        });

        Assert.Equal(7, at7.MaxOptimizationSplitCount);
        Assert.Equal(12, at12.MaxOptimizationSplitCount);
        Assert.NotEqual(at7.MaxOptimizationSplitCount, at12.MaxOptimizationSplitCount);
    }
}
