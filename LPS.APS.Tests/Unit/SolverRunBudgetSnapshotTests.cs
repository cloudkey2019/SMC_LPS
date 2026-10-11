using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;
using RoutingOperation = LPS.APS.Core.Entities.APS.RoutingOperation;
using OperationResourceEligibility = LPS.APS.Core.Entities.APS.OperationResourceEligibility;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// **V1_4 NEW-06 判别性单测**（0号位 2026-10-10《APS_V1_4_20261010.md》§6，P1/CONFIRMED + EVIDENCE_MISSING）。
///
/// 复审判词（逐字）：
///   「预算诊断不是生产 Run 正式快照。`SolverDiagnostics.Current` 是 `AsyncLocal`；未 `BeginScope()`
///     时无任何记录；正常 `FiniteCapacitySolver.SolveAsync` 只调 `StartPhase/EndPhase`，
///     **不自动建立作用域** ⇒ 不是 `ScheduleRunId`/`StrategyProfileVersionId`/`ParameterSetVersionId`
///     可追溯的正式记录。」
///   §8 第 5 项：「补**没有打开 Benchmark scope 时运行输出仍可追溯**的测试。」
///
/// 本文件把「未开 scope 也可追溯」落成反证：
///   · **不开** `BeginScope()` 直接 `SolveAsync` ⇒ 该 Run 的预算快照**可按 `ScheduleRunId` 精确检索**
///     （`SolverDiagnostics.TryGetRunBudgetSnapshot`），且带 Run 版本源三元组 + 生效预算值
///     （版本 / 取源 / 两个生效数）；进程级单槽 `LastRunBudgetSnapshot` 同样非 null；
///   · 快照值与同一 Run 的 `PhaseTwoInitialScheduler.ResolveSolverBatchBudget` 结果**自洽**；
///   · `SolveAsync` 自动建立的作用域**不泄漏**（调用返回后 `HasScope == false`）；
///   · 调用方**已开** scope 时沿用其作用域（压测工装读 `Counters` 的语义不变）。
///
/// ⚠ 快照是**进程内可检索证据**，不是持久化载体（落盘 `ScheduleRun` 表归 2号位）——
///   本文件**不声称**已达成持久化 Run 快照。
/// </summary>
public class SolverRunBudgetSnapshotTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1, 0, 0, 0);

    private const int MaterialId = 1;
    private const int DeptId = 100;

    private readonly FiniteCapacitySolver _solver = new();

    /// <summary>
    /// NEW-06 **反证**：**不开** `BeginScope()` 跑生产入口 ⇒ 预算快照仍可检索，且带
    ///   `ScheduleRunId` / `StrategyProfileVersionId` / `ParameterSetVersionId` 三元组。
    ///   （整改前：无 scope ⇒ `Current` 为 null ⇒ **无任何记录** ⇒ 本用例红。）
    /// </summary>
    [Fact]
    public async Task NEW06_未开Scope_SolveAsync仍产出带Run版本源的预算快照()
    {
        Assert.False(SolverDiagnostics.HasScope);              // 前置：本用例**没有**开作用域

        // ⚠ 用**本用例专属**的 Run Id（高段位，避开其它用例的 1/2/3/100…）——
        //   按 Run Id 检索**不受**并行用例的并发写入影响（这是本文件可稳定重跑的关键）。
        const long RunId = 9000001L;
        Assert.Null(SolverDiagnostics.TryGetRunBudgetSnapshot(RunId));   // 前置：该 Run 尚无记录

        var request = Build(scheduleRunId: RunId, profileVersionId: 5L, parameterSetVersionId: 9L);
        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var snap = SolverDiagnostics.TryGetRunBudgetSnapshot(RunId);
        Assert.NotNull(snap);

        // Run 版本源三元组（判词逐字要求「可追溯」）
        Assert.Equal(RunId, snap!.ScheduleRunId);
        Assert.Equal(5L, snap.StrategyProfileVersionId);
        Assert.Equal(9L, snap.ParameterSetVersionId);

        // 生效预算：版本 / 取源 / 两个生效数
        Assert.Equal(PhaseTwoInitialScheduler.SolverBatchBudget.BudgetVersion, snap.BudgetVersion);
        Assert.False(string.IsNullOrWhiteSpace(snap.BudgetSource));
        Assert.StartsWith(PhaseTwoInitialScheduler.SolverBatchBudget.BudgetVersion, snap.BudgetSource);
        Assert.True(snap.ResolvedAtUtcTicks > 0);

        // 与同一 Run 的解析结果**自洽**（快照不是另算一份）
        var expected = PhaseTwoInitialScheduler.ResolveSolverBatchBudget(request);
        Assert.Equal(expected.MaxOptimizationSplitCount, snap.MaxOptimizationSplitCount);
        Assert.Equal(expected.MaxBatchCandidates, snap.MaxBatchCandidates);
        Assert.Equal(expected.Source, snap.BudgetSource);

        // 进程级单槽同样被写入（未开 scope 也非 null）；只断言**与并发无关**的部分
        //   （版本号对任何 Run 都相同 ⇒ 其它用例并发写入不会使本断言变红）。
        var slot = SolverDiagnostics.LastRunBudgetSnapshot;
        Assert.NotNull(slot);
        Assert.Equal(PhaseTwoInitialScheduler.SolverBatchBudget.BudgetVersion, slot!.BudgetVersion);
    }

    /// <summary>
    /// NEW-06：`SolveAsync` 自动建立的作用域**不得泄漏** —— 调用返回后 `HasScope` 必须回到 false。
    /// </summary>
    [Fact]
    public async Task NEW06_自动作用域不泄漏_调用后HasScope为false()
    {
        Assert.False(SolverDiagnostics.HasScope);

        var result = await _solver.SolveAsync(Build(1L, 1L, 1L));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(SolverDiagnostics.HasScope);              // 自动作用域已释放（未泄漏到调用方）
    }

    /// <summary>
    /// NEW-06 **零回归守卫**：调用方**已开** scope ⇒ 沿用其作用域（不叠加、不丢弃）
    ///   ⇒ 压测工装读 `scope.Counters` 的语义**逐字不变**。
    /// </summary>
    [Fact]
    public async Task NEW06_调用方已开Scope_沿用其作用域_计数可读()
    {
        using var scope = SolverDiagnostics.BeginScope();

        var result = await _solver.SolveAsync(Build(11L, 12L, 13L));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(PhaseTwoInitialScheduler.SolverBatchBudget.BudgetVersion, scope.Counters.SolverBatchBudgetVersion);
        Assert.True(scope.Counters.SolverBatchBudgetMaxBatchCandidates > 0);
    }

    /// <summary>
    /// NEW-06：**按 Run Id 可精确检索**，后一个 Run **不覆盖**前一个 Run 的记录。
    ///   （整改前：只有「最后写入者胜」的进程级单槽 ⇒ 连续两个 Run 后，第一个 Run 的快照**已丢失**
    ///     ⇒ 本用例红。整改后：`TryGetRunBudgetSnapshot(runId)` 分槽保存 ⇒ 两个 Run 的版本源各自可查。）
    ///   ⚠ 本断言**与并发无关**（各用专属 Run Id），可在并行测试集合下稳定重跑。
    /// </summary>
    [Fact]
    public async Task NEW06_连续两个Run_按RunId各自可检索_互不覆盖()
    {
        const long RunId1 = 9000011L;
        const long RunId2 = 9000012L;

        Assert.True((await _solver.SolveAsync(Build(RunId1, 200L, 300L))).Success);
        Assert.True((await _solver.SolveAsync(Build(RunId2, 400L, 500L))).Success);

        var first = SolverDiagnostics.TryGetRunBudgetSnapshot(RunId1);
        var second = SolverDiagnostics.TryGetRunBudgetSnapshot(RunId2);

        Assert.NotNull(first);            // 第一个 Run 的记录**未被**第二个 Run 覆盖
        Assert.NotNull(second);

        Assert.Equal(RunId1, first!.ScheduleRunId);
        Assert.Equal(200L, first.StrategyProfileVersionId);
        Assert.Equal(300L, first.ParameterSetVersionId);

        Assert.Equal(RunId2, second!.ScheduleRunId);
        Assert.Equal(400L, second.StrategyProfileVersionId);
        Assert.Equal(500L, second.ParameterSetVersionId);
    }

    // ─────────────────────────── 夹具 ───────────────────────────

    /// <summary>单工序最小夹具（资源 1，10min），足以走完五阶段并触发预算解析。</summary>
    private static DomainSolveRequest Build(long scheduleRunId, long profileVersionId, long parameterSetVersionId)
        => new()
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            ScheduleRunId = scheduleRunId,
            PlanningStart = Day,
            PlanningEnd = Day.AddDays(90),
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new()
                {
                    LogicalDemandKey = "A", PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = "A",
                    MaterialId = MaterialId, FactoryId = 1,
                    StartStageCode = "STAGE1",
                    NetOutputQty = 10m, PlannedProcessQty = 10m,
                    RequiredAvailableTime = Day.AddDays(5),
                    DemandSequence = 1,
                    RouteCode = "RT", PathId = 1
                }
            },
            RoutingOperations = new List<RoutingOperation>
            {
                new()
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = "RT", PathId = 1,
                    OperationCode = "OP10", StageCode = "STAGE1",
                    StandardDuration = 1m, OperationPlanningMode = "FINITE_RESOURCE"
                }
            },
            RoutingDependencies = Array.Empty<LPS.APS.Core.Entities.APS.RoutingDependency>(),
            OperationResourceEligibility = new List<OperationResourceEligibility>
            {
                new()
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = "RT", PathId = 1,
                    OperationCode = "OP10", ResourceId = 1, Priority = 1, CapacityFactor = 1m
                }
            },
            MaterialStageDepartmentContexts = new List<MaterialStageDepartmentContextDto>
            {
                new() { MaterialId = MaterialId, StageCode = "STAGE1", ProductionDepartmentId = DeptId }
            },
            ExecutionConstraints = Array.Empty<ExecutionConstraint>(),
            Resources = new List<ResourceDefinition>
            {
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = Day, End = Day.AddDays(30), IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                StrategyProfileVersionId = profileVersionId,
                ParameterSetVersionId = parameterSetVersionId,
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = false,
                    AllowSplit = false
                },
                BatchPolicies = TestBatchPolicy.Permissive(MaterialId, DeptId, allowMerge: false, allowSplit: false),
                SetupTransitionRules = Array.Empty<SetupTransitionRuleSnapshot>()
            }
        };
}
