using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;
using RoutingOperation = LPS.APS.Core.Entities.APS.RoutingOperation;
using RoutingDependency = LPS.APS.Core.Entities.APS.RoutingDependency;
using OperationResourceEligibility = LPS.APS.Core.Entities.APS.OperationResourceEligibility;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// **V1_4 NEW-05 判别性端到端反证**（0号位 2026-10-10《APS_V1_4_20261010.md》§4，P0/CONFIRMED）。
///
/// 复审判词（逐字）：
///   「MIXED「倒排部分成功后失败→正排」无全状态回滚 …
///     `ScheduleBackward` 每成功安排一道即 `AddOccupancyWindow` + `ProductTimeline.Place`；
///     若下游工序已排、上游失败，只 `return new List&lt;FinalTaskDraft&gt;()` ⇒ **无恢复**。
///     ⇒ 增加「**倒排最后一道成功、上一道失败、正排成功，最终占用恰等于 FinalTask**」反证，
///       同时覆盖**未合批普通需求**。」
///   §8 第 4 项：「F-01 的有序回滚原语可复用，但须覆盖**普通非合批**主路径。」
///
/// 本文件用**完整** <see cref="FiniteCapacitySolver.SolveAsync"/>（纯内存、不触库）构造该形态：
///
/// 【夹具几何（为什么必须这么摆）】
///   · 三工序串行链 `OP10(r1,10min) → OP20(r2,10min) → OP30(r3,60min)`，需求 A 的
///     `RequiredAvailableTime`（= 倒排锚）取 `Day+65`；
///   · 倒排（锚 = `Day+65`）：
///       - `OP30` 反推 `candidateStart = Day+65 − 60 = Day+5 ≥ PlanningStart` ⇒ **成功**，占用 r3 `[Day+5, Day+65]`；
///       - `OP20` 反推 `candidateStart = Day+5 − 10 = Day−5 &lt; PlanningStart` ⇒ **失败** ⇒ 倒排整体返回空表；
///     ⇒ 恰好是判词点名的「**最后一道成功、上一道失败**」**部分成功**形态；
///   · 正排（从 `PlanningStart` 起）：`OP10 [Day, Day+10] → OP20 [Day+10, Day+20] → OP30` 最早可落
///     `Day+20`。
///       - **残留未撤销（缺陷）**：r3 上还压着幽灵 `[Day+5, Day+65]` ⇒ `OP30` 被挤到 `Day+65`
///         （占用 60min 的工序占了 120min 的 r3 时间轴 = 占用 ≠ FinalTask）；
///       - **残留已撤销（整改后）**：r3 干净 ⇒ `OP30` 落 `Day+20`（占用恰等于 FinalTask 自身窗口）。
///
///   ⇒ 判别器 = **`OP30.PlannedStartTime`**：`Day+20`（正确）vs `Day+65`（幽灵残留）。
///     二元、不依赖时间精度、不依赖诊断计数（**非契约计数器只作旁证**）。
///
/// ⚠ 本缺陷为**历史遗留**（早于 V1_4 固定 Commit），整改不声称「本 Commit 新引入」。
/// ⚠ 夹具 `AllowMerge=false` / `AllowSplit=false` ⇒ **未合批普通需求主路径**（判词逐字要求）。
/// </summary>
public class MixedBackwardRollbackTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningStart = Day;
    private static readonly DateTime PlanningEnd = Day.AddDays(90);

    private const int MaterialId = 1;
    private const int DeptId = 100;

    private readonly FiniteCapacitySolver _solver = new();

    /// <summary>
    /// NEW-05 **反证**：MIXED + 倒排部分成功后失败 ⇒ 转正排前必须**彻底撤销**倒排残留。
    ///   `OP30` 必须落在其 DAG 最早可落位 `Day+20`（= 占用恰等于 FinalTask），
    ///   而不是被幽灵占用挤到 `Day+65`。
    /// </summary>
    [Fact]
    public async Task NEW05_MIXED_倒排最后一道成功后失败_转正排前须撤销残留_OP30落DAG最早位()
    {
        var result = await _solver.SolveAsync(Build(direction: "MIXED"));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        var op30 = Assert.Single(result.FinalTasks.Where(t => t.OperationCode == "OP30"));
        Assert.Equal(Day.AddMinutes(20), op30.PlannedStartTime);   // 幽灵未撤销 ⇒ Day+65（红）
        Assert.Equal(Day.AddMinutes(80), op30.PlannedEndTime);
    }

    /// <summary>
    /// NEW-05 **对照（零回归守卫）**：同一夹具、显式 `FORWARD` ⇒ **不进入**该路径，
    ///   `OP30` 同样落 `Day+20`。⇒ 证明 `Day+20` 是「无幽灵」的真值，上一条的差异确由幽灵带来。
    /// </summary>
    [Fact]
    public async Task NEW05_对照_显式FORWARD_OP30同样落DAG最早位()
    {
        var result = await _solver.SolveAsync(Build(direction: "FORWARD"));

        Assert.True(result.Success, result.ErrorMessage);
        var op30 = Assert.Single(result.FinalTasks.Where(t => t.OperationCode == "OP30"));
        Assert.Equal(Day.AddMinutes(20), op30.PlannedStartTime);
    }

    /// <summary>
    /// NEW-05 **等效 MIXED 分支**：未知 Direction 走的是**同一段**隔离/回滚路径
    ///   ⇒ 同样必须撤销残留（不得只在显式 MIXED 上修）。
    /// </summary>
    [Fact]
    public async Task NEW05_未知Direction_等效MIXED分支_同样须撤销残留()
    {
        var result = await _solver.SolveAsync(Build(direction: "MIXED_UNKNOWN_MODE"));

        Assert.True(result.Success, result.ErrorMessage);
        var op30 = Assert.Single(result.FinalTasks.Where(t => t.OperationCode == "OP30"));
        Assert.Equal(Day.AddMinutes(20), op30.PlannedStartTime);
    }

    /// <summary>
    /// NEW-05 **旁证（非契约计数器）**：残留确被撤销 ⇒ `MixedBackwardRollbackWindows` ≥ 1
    ///   且 `OccupancyRollbackUnmatched == 0`（按身份回滚**逐窗命中**，无未匹配项）。
    ///   ⚠ 计数器**不是**契约出口，只作旁证；判据仍是上一条的 `OP30` 落位。
    /// </summary>
    [Fact]
    public async Task NEW05_旁证_按身份回滚逐窗命中_无未匹配()
    {
        using var scope = SolverDiagnostics.BeginScope();

        var result = await _solver.SolveAsync(Build(direction: "MIXED"));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(scope.Counters.MixedBackwardRollbackWindows >= 1,
            "倒排部分成功却无任何窗口被撤销 ⇒ 残留未清理");
        Assert.Equal(0, scope.Counters.OccupancyRollbackUnmatched);
    }

    // ─────────────────────────── 夹具 ───────────────────────────

    private static readonly IReadOnlyList<(int ResourceId, DateTime Start, DateTime End)> Calendar =
        new[]
        {
            (1, Day, Day.AddDays(30)),
            (2, Day, Day.AddDays(30)),
            (3, Day, Day.AddDays(30))
        };

    /// <summary>
    /// 三工序串行链（`OP10@STAGE1→r1`、`OP20@STAGE2→r2`、`OP30@STAGE3→r3`），
    ///   需求 A 数量 10 ⇒ 工时 `1min/件 × 10 = 10`、`10`、`6min/件 × 10 = 60`。
    ///   倒排锚 = `RequiredAvailableTime = Day+65`（令 `OP30` 倒排成功、`OP20` 倒排失败）。
    /// </summary>
    private static DomainSolveRequest Build(string direction)
    {
        const string RouteCode = "RT";
        const int PathId = 1;
        const decimal Qty = 10m;

        var ops = new[]
        {
            (Stage: "STAGE1", Op: "OP10", Res: 1, Min: 1m),
            (Stage: "STAGE2", Op: "OP20", Res: 2, Min: 1m),
            (Stage: "STAGE3", Op: "OP30", Res: 3, Min: 6m)
        };

        var routingOps = ops.Select(o => new RoutingOperation
        {
            MaterialId = MaterialId, ProductionDepartmentId = DeptId,
            RouteCode = RouteCode, PathId = PathId,
            OperationCode = o.Op, StageCode = o.Stage,
            StandardDuration = o.Min, OperationPlanningMode = "FINITE_RESOURCE"
        }).ToList();

        var elig = ops.Select(o => new OperationResourceEligibility
        {
            MaterialId = MaterialId, ProductionDepartmentId = DeptId,
            RouteCode = RouteCode, PathId = PathId,
            OperationCode = o.Op, ResourceId = o.Res, Priority = 1, CapacityFactor = 1m
        }).ToList();

        var deps = new[] { ("OP10", "OP20"), ("OP20", "OP30") }.Select(d => new RoutingDependency
        {
            MaterialId = MaterialId, ProductionDepartmentId = DeptId,
            RouteCode = RouteCode, PathId = PathId,
            FromOperationCode = d.Item1, ToOperationCode = d.Item2,
            DependencyType = "ES", LagTime = 0m, IsActive = true
        }).ToList();

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new()
                {
                    LogicalDemandKey = "A", PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = "A",
                    MaterialId = MaterialId, FactoryId = 1,
                    StartStageCode = "STAGE1",
                    NetOutputQty = Qty, PlannedProcessQty = Qty,
                    RequiredAvailableTime = Day.AddMinutes(65),
                    DemandSequence = 1,
                    RouteCode = RouteCode, PathId = PathId
                }
            },
            RoutingOperations = routingOps,
            RoutingDependencies = deps,
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = ops.Select(o => new MaterialStageDepartmentContextDto
            {
                MaterialId = MaterialId, StageCode = o.Stage, ProductionDepartmentId = DeptId
            }).ToList(),
            ExecutionConstraints = Array.Empty<ExecutionConstraint>(),
            Resources = new[] { 1, 2, 3 }.Select(rid => new ResourceDefinition
            {
                ResourceId = rid, ResourceCode = $"R{rid}", FactoryCode = "F1", Capacity = 1m
            }).ToList(),
            CalendarSlots = Calendar.Select(c => new ResourceCalendarSlot
            {
                ResourceId = c.ResourceId, Start = c.Start, End = c.End, IsAvailable = true
            }).ToList(),
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = direction,
                    AllowMerge = false,
                    AllowSplit = false
                },
                // C 桶必须显式给出有效 Batch Policy（否则 Fail Closed）；Min/Max 均 null ⇒ 恒 1 批。
                BatchPolicies = TestBatchPolicy.Permissive(MaterialId, DeptId, allowMerge: false, allowSplit: false),
                SetupTransitionRules = Array.Empty<SetupTransitionRuleSnapshot>()
            }
        };
    }
}
