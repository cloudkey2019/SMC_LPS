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
/// AUD-1-R03（0号位 2026-10-10《APS_V1_2_20261010.md》§4，RISK_UNVERIFIED）回归测试：
///   `PhaseThreeDiagnostics.CalculateResourceUtilization` 的**统计区间**不得被无限放大。
///
/// 背景（本号位核对：复审疑点**成立**）：
///   OWN-P0-01 之后利用率分母从 `[planningStart, planningEnd]` 改为「全部未来日历窗」（上界 = ∞），
///   以修正「任务合法落到 90 天之后 ⇒ 分母偏小 ⇒ 利用率被高估（可 > 1）」。
///   但若维护日历延伸到数年之后，分母会被**无限放大** ⇒ 利用率被**稀释**到近 0
///   ⇒ Auto 瓶颈识别（`> BottleneckUtilizationThreshold = 0.85`）失效、产能短缺根因失真。
///
/// 整改口径（见 `PhaseThreeDiagnostics` 内 AUD-1-R03 注释）：
///   统计窗**下界不变**（`planningStart`）、**上界 = `max(planningEnd, 本轮全部任务的最晚完成)`**。
///   ⇒ 既**不高估**（任务越 90 天时窗口随真实负荷延长）、也**不稀释**（不被 10 年维护日历拉平）；
///     且**不**把 90 天重新当作**排程硬截止**（此处仅诊断统计窗，不参与任何可行性/搜索判定）。
/// </summary>
public class UtilizationWindowTests
{
    private static readonly DateTime PlanningStart = new DateTime(2026, 9, 1, 0, 0, 0);

    /// <summary>90 天规划窗（P0-01 后**不是**资源时间终点，仅作统计窗默认上界）。</summary>
    private static readonly DateTime PlanningEnd = PlanningStart.AddDays(90);

    private const int DeptId = 100;
    private const string Stage = "STAGE1";
    private const string OpCode = "OP10";

    private readonly FiniteCapacitySolver _solver = new();

    /// <summary>
    /// ① **不被 10 年维护日历稀释**：
    ///   日历 = `[S, S+10y)`；单个任务占用 `0.9 × 90 天 = 116,640 分钟`（在 90 天窗内）。
    ///   · 整改前（分母 = 全部未来窗 ≈ 10 年）⇒ 利用率 ≈ 0.0022 ⇒ **不判瓶颈**（本条红）；
    ///   · 整改后（分母 = `[S, max(planningEnd, 任务最晚完成=116,640min<90d)] = 90 天`）⇒ 0.9 > 0.85 ⇒ **判瓶颈**。
    /// </summary>
    [Fact]
    public async Task R03_十年维护日历不得稀释利用率_仍按统计窗上限判瓶颈()
    {
        // 0.9 × 90 天 = 116,640 分钟
        var request = BuildCalendarSpan(
            calendarSpan: TimeSpan.FromDays(3650),
            durationMinutes: 116_640m);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);
        Assert.Contains(1, BottleneckIds(result));
    }

    /// <summary>
    /// ② **不重新施加 90 天排程硬截止**，且分母随真实负荷延长：
    ///   日历 = `[S, S+10y)`；单个任务占用 `120 天 = 172,800 分钟`（**合法越过 90 天规划窗**）。
    ///   · 任务必须**被排下**（落在第 120 天，证明 90 天未被当硬截止）；
    ///   · 分母 = `max(planningEnd=90d, 任务最晚完成=120d) = 120 天` ⇒ 利用率 = 1.0 > 0.85 ⇒ **判瓶颈**；
    ///     整改前（分母 = 10 年）⇒ ≈ 0.0055 ⇒ 不判瓶颈（本条红）。
    /// </summary>
    [Fact]
    public async Task R03_任务越过90天_不得当硬截止_分母随真实负荷延长()
    {
        var request = BuildCalendarSpan(
            calendarSpan: TimeSpan.FromDays(3650),
            durationMinutes: 172_800m);   // 120 天

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);
        Assert.Single(result.FinalTasks);

        // 合法越过 90 天规划窗（未被 PlanningEnd 否决）
        var task = result.FinalTasks[0];
        Assert.True(task.PlannedEndTime > PlanningEnd,
            $"任务应合法落在 90 天规划窗之后；实际完成 {task.PlannedEndTime:o}");

        // 分母延长到真实负荷末端 ⇒ 利用率 ≈ 1.0 ⇒ 仍判瓶颈（未被剩余 9.7 年稀释）
        Assert.Contains(1, BottleneckIds(result));
    }

    /// <summary>
    /// ③ **零回归**：日历恰好 = 90 天窗（既有常见几何）⇒ 新旧分母逐字相同。
    ///   占用 116,640 分钟 ⇒ 利用率 0.9 > 0.85 ⇒ 判瓶颈（与整改前一致）。
    /// </summary>
    [Fact]
    public async Task R03_日历恰为90天窗_与整改前逐字一致()
    {
        var request = BuildCalendarSpan(
            calendarSpan: PlanningEnd - PlanningStart,
            durationMinutes: 116_640m);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);
        Assert.Contains(1, BottleneckIds(result));
    }

    /// 瓶颈资源 = 产出 `RESOURCE_CAPACITY_WAIT` 事实的资源（与 `BottleneckModeTests.BottleneckIds` 同口径）。
    private static List<int> BottleneckIds(DomainSolveResult result)
        => result.ExplanationFacts
            .Where(f => f.ObjectType == "RESOURCE" && f.ReasonCode == "RESOURCE_CAPACITY_WAIT")
            .Select(f => f.ResourceId!.Value)
            .ToList();

    // ─────────────────────────── 构造 ───────────────────────────

    /// <summary>单一资源 R1、单一需求 M1；R1 日历窗 = `[PlanningStart, PlanningStart + calendarSpan)`。</summary>
    private static DomainSolveRequest BuildCalendarSpan(TimeSpan calendarSpan, decimal durationMinutes)
        => new()
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new()
                {
                    LogicalDemandKey = "M1", PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = "M1", MaterialId = 1, FactoryId = 1,
                    StartStageCode = Stage,
                    NetOutputQty = 1m, PlannedProcessQty = 1m,
                    RequiredAvailableTime = PlanningStart.AddDays(20), DemandSequence = 1
                }
            },
            RoutingOperations = new List<RoutingOperation>
            {
                new()
                {
                    MaterialId = 1, ProductionDepartmentId = DeptId, RouteCode = "DEFAULT",
                    OperationCode = OpCode, StageCode = Stage, StandardDuration = durationMinutes, SetupTime = 0m
                }
            },
            OperationResourceEligibility = new List<OperationResourceEligibility>
            {
                new()
                {
                    MaterialId = 1, ProductionDepartmentId = DeptId, RouteCode = "DEFAULT",
                    OperationCode = OpCode, ResourceId = 1, Priority = 1, CapacityFactor = 1m
                }
            },
            MaterialStageDepartmentContexts = new List<MaterialStageDepartmentContextDto>
            {
                new() { MaterialId = 1, StageCode = Stage, ProductionDepartmentId = DeptId }
            },
            Resources = new List<ResourceDefinition>
            {
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = PlanningStart, End = PlanningStart.Add(calendarSpan), IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = false,
                    AllowSplit = false
                },
                BatchPolicies = TestBatchPolicy.Permissive(new[] { 1 }, DeptId),
                SolverStrategy = new SolverStrategyBlock
                {
                    BottleneckMode = DynamicBottleneckMode.Auto,
                    AnchorResourceCode = null
                }
            }
        };
}
