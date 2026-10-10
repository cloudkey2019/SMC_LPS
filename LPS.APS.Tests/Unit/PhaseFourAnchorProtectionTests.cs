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
/// Phase4 锚点保护 + 占用口径回归（20260923 两处修复）：
///
/// ① 锚点自身保护：`ExecutionConstraints` 标记的 Task（锚点）**不进影响集**——
///    修复前 relocation 主循环只排除「连带牺牲者」，不查 immovableTasks 自身，
///    故「既是锚点、又落入 ChangeSeedKeys」的 Task 存在被重排的**潜在风险**
///    （注意：relocation 默认 `earliestStart = taskOccupancyStart` 保位，故该风险非必然触发，
///     仅在特定约束组合下显现；本测试锁住「锚点即便被误列为变化种子也保位」这一性质，作回归网）。
///
/// ② 占用登记口径：`RepairWithPropagation` / `RepairUnscheduledDemands` 的 repairedTasks
///    占用登记须**从 Setup 起点算起**（与 BuildResourceOccupancy / 移除口径一致）；
///    修复前用 PlannedStartTime（加工起点）会漏登 Setup 段 → 后续 Task 可能被排进本 Task 的 Setup 窗口。
/// </summary>
public class PhaseFourAnchorProtectionTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1);
    private readonly FiniteCapacitySolver _solver = new();

    // ════════════════════════════════════════════════════════════
    // ① 锚点同时被列为变化种子：仍须保持原位（Candidate 传播场景）
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 锚点同时是变化种子_保持原位不被重排()
    {
        var request = Build(
            new List<LogicalProductionDemand>
            {
                Demand("D1", 1, 1, 1),
                Demand("DL", 9, 9, 2)
            },
            new[] { 1, 9 },
            constraints: new List<ExecutionConstraint>
            {
                new ExecutionConstraint
                {
                    DraftId = "DL", ResourceId = 1,
                    LockedStart = Day.AddHours(14), LockedEnd = Day.AddHours(15),
                    ConstraintType = "FIRM", StageCode = "STAGE1", OperationCode = "OP10",
                    LockedQuantity = 1m, LockedNetOutputQty = 1m, LockedPlannedProcessQty = 1m
                }
            },
            // 输入矛盾场景：锚点 DL 同时被列为变化种子
            candidate: new CandidateContext { BasePlanVersionId = 1, ChangeSeedKeys = new[] { "DL" } });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var anchor = result.FinalTasks.Single(t => t.SourceDraftId == "DL");
        // 锚点保持 FIRM 锁定位置（修复前存在被 relocation 搬走的潜在风险）
        Assert.Equal(Day.AddHours(14), anchor.PlannedStartTime);
        Assert.Equal(Day.AddHours(15), anchor.PlannedEndTime);
    }

    // ════════════════════════════════════════════════════════════
    // ② 同资源 Task 的占用窗（含 Setup）两两不重叠
    //    Setup>0 由规则制造换型；占用窗口径 = [PlannedStartTime - SetupTime, PlannedEndTime]
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 占用窗含Setup_同资源两两不重叠()
    {
        var request = Build(
            new List<LogicalProductionDemand>
            {
                Demand("D1", 1, 1, 1),
                Demand("D2", 2, 2, 2),
                Demand("D3", 3, 1, 3)
            },
            new[] { 1, 2, 3 },
            rules: new List<SetupTransitionRuleSnapshot>
            {
                Rule(1, 2, 60m), Rule(2, 1, 60m), Rule(1, 3, 45m), Rule(2, 3, 30m)
            },
            candidate: new CandidateContext { BasePlanVersionId = 1, ChangeSeedKeys = new[] { "D1" } });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // 占用窗含 Setup（P0-02 口径）；按起点排序后逐对校验无重叠
        var windows = result.FinalTasks
            .Where(t => t.ResourceId is not null)
            .Select(t => (
                Start: t.PlannedStartTime - TimeSpan.FromMinutes((double)t.SetupTime),
                t.PlannedEndTime))
            .OrderBy(w => w.Start)
            .ToList();

        Assert.True(windows.Count >= 2, "需至少两个占用窗才有校验意义");
        for (int i = 1; i < windows.Count; i++)
        {
            Assert.True(windows[i].Start >= windows[i - 1].PlannedEndTime,
                $"占用窗重叠：第 {i} 窗起点 {windows[i].Start:HH:mm} 早于前窗终点 {windows[i - 1].PlannedEndTime:HH:mm}");
        }
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private static SetupTransitionRuleSnapshot Rule(int from, int to, decimal minutes)
        => new SetupTransitionRuleSnapshot
        {
            ProductionDepartmentId = 100, StageCode = "STAGE1",
            OperationCode = "OP10", ResourceId = 1,
            FromMaterialId = from, ToMaterialId = to,
            RuleType = "EXACT", SetupMinutes = minutes
        };

    private static LogicalProductionDemand Demand(string key, long alloc, int materialId, int seq)
        => new LogicalProductionDemand
        {
            LogicalDemandKey = key, PlanVersionId = 1L, DomainKey = "DOMAIN",
            AllocationSequence = alloc, DemandKey = key, MaterialId = materialId, FactoryId = 1,
            NetOutputQty = 1m, PlannedProcessQty = 1m,
            RequiredAvailableTime = Day.AddDays(20), DemandSequence = seq
        };

    private static DomainSolveRequest Build(
        List<LogicalProductionDemand> demands,
        int[] materials,
        List<SetupTransitionRuleSnapshot>? rules = null,
        CandidateContext? candidate = null,
        List<ExecutionConstraint>? constraints = null)
    {
        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            ScheduleRunId = 42,
            DomainKey = "DOMAIN",
            PlanningStart = Day,
            PlanningEnd = Day.AddDays(30),
            LogicalProductionDemands = demands,
            RoutingOperations = materials.Select(m => new RoutingOperation
            {
                MaterialId = m, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                OperationCode = "OP10", StageCode = "STAGE1",
                StandardDuration = 60m, SetupTime = 0m
            }).ToList(),
            OperationResourceEligibility = materials.Select(m => new OperationResourceEligibility
            {
                MaterialId = m, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                OperationCode = "OP10", ResourceId = 1, Priority = 1, CapacityFactor = 1m
            }).ToList(),
            MaterialStageDepartmentContexts = materials
                .Select(m => new MaterialStageDepartmentContextDto
                {
                    MaterialId = m, StageCode = "STAGE1", ProductionDepartmentId = 100
                }).ToList(),
            Resources = new List<ResourceDefinition>
            {
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = Day.AddHours(8), End = Day.AddHours(18), IsAvailable = true }
            },
            ExecutionConstraints = constraints ?? new List<ExecutionConstraint>(),
            MaterialConstraints = new List<MaterialAvailabilitySlice>(),
            CandidateContext = candidate,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = false,
                    AllowSplit = false
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是锚点保护，非批决策 ⇒ Material 级宽松策略（恒 1 批）。
                BatchPolicies = TestBatchPolicy.Permissive(demands.Select(d => d.MaterialId)),
                SetupTransitionRules = rules ?? new List<SetupTransitionRuleSnapshot>()
            }
        };
    }
}