using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using RoutingOperation = LPS.APS.Core.Entities.APS.RoutingOperation;
using OperationResourceEligibility = LPS.APS.Core.Entities.APS.OperationResourceEligibility;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// M5 第一批：RunScope 消费链回归（纯内存，直接调 FiniteCapacitySolver）。
/// 覆盖：①DueDateOverrides → effectiveDue 在延期诊断口径生效（正式交期按期、覆盖交期更紧 → 判延期）；
/// ②DueDateOverrides → 倒排 JIT 锚换用覆盖交期；③null RunScope = FULL 语义零改变；
/// ④TaskTargetOverrides → 传播种子注入（无 ChangeSeedKeys 也进传播、求解不崩零破坏）。
/// ⑤ChangedResourceIds 资源种子（A 项 §69）→ 无 ChangeSeedKeys 也进传播、求解零破坏；⑥与需求种子等效对照。
/// 覆盖二批（InScope/Anchor 补集/拒收细分）待 M5 三方详设后接。
/// </summary>
public class RunScopeConsumptionTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1);

    private readonly FiniteCapacitySolver _solver = new();

    // ── ① effectiveDue 在延期诊断口径生效：正式交期(20天)按期，覆盖交期(5h)晚于物料可用(10h) → 判延期 ──
    [Fact]
    public async Task 交期覆盖_诊断口径按覆盖交期判延期()
    {
        var request = Build(
            materialAvailableAt: Day.AddHours(10),
            formalDue: Day.AddDays(20),
            runScope: new RunScope
            {
                Trigger = BusinessTriggerType.ExistingOrderAdvance,
                DueDateOverrides = new List<DueDateOverride>
                {
                    new DueDateOverride { LogicalDemandKey = "D1", ManualTargetDueDate = Day.AddHours(5) }
                }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        // 正式交期(20天)下 end(Day+10h+1h)≪交期不延期；覆盖交期(5h)下 end > due → 必须判定延期
        var delayedFacts = result.ExplanationFacts
            .Where(f => f.ObjectType == "ORDER" && f.FinalDraftId == task.FinalDraftId).ToList();
        Assert.Single(delayedFacts);
        Assert.Equal((double)(task.PlannedEndTime - Day.AddHours(5)).TotalHours, (double)(delayedFacts[0].ImpactHours ?? 0), 2);
    }

    // ── ② 倒排 JIT 锚换用覆盖交期：BACKWARD 锚 = 覆盖交期 ──
    [Fact]
    public async Task 交期覆盖_倒排锚用覆盖交期()
    {
        var request = Build(
            materialAvailableAt: null,
            formalDue: Day.AddDays(20),
            direction: "BACKWARD",
            runScope: new RunScope
            {
                Trigger = BusinessTriggerType.GanttAdjustment,
                DueDateOverrides = new List<DueDateOverride>
                {
                    new DueDateOverride { LogicalDemandKey = "D1", ManualTargetDueDate = Day.AddDays(5) }
                }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        // 倒排锚 = 覆盖交期(5天)（而非正式 20 天）→ 结束贴覆盖交期
        Assert.Equal(Day.AddDays(5), task.PlannedEndTime);
    }

    // ── ③ null RunScope = FULL 语义零改变：无覆盖不判延期 ──
    [Fact]
    public async Task nullRunScope_零改变不判延期()
    {
        var request = Build(
            materialAvailableAt: Day.AddHours(10),
            formalDue: Day.AddDays(20),
            runScope: null);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        // 正式交期(20天)下按期 → 无 ORDER 级延期事实
        Assert.DoesNotContain(result.ExplanationFacts, f => f.ObjectType == "ORDER");
    }

    // ── ④ TaskTargetOverrides 软目标：无 ChangeSeedKeys 也进传播，求解零破坏 ──
    [Fact]
    public async Task 任务软目标_无种子也进传播零破坏()
    {
        var request = Build(
            materialAvailableAt: null,
            formalDue: Day.AddDays(20),
            runScope: new RunScope
            {
                Trigger = BusinessTriggerType.DomainManualReschedule,
                TaskTargetOverrides = new List<TaskTargetOverride>
                {
                    new TaskTargetOverride { DraftId = "D1", OperationCode = "OP10", TargetTime = Day.AddHours(10) }
                }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        Assert.Equal("OP10", task.OperationCode);
        // 软目标不强制：无硬约束时仍落最早可行位（Day 起点），不因 TargetTime 推迟
        Assert.Equal(Day, task.PlannedStartTime);
    }

    // ── ⑤ ChangedResourceIds 资源种子（A 项，§69 三个 Seed 之一）：无 ChangeSeedKeys 也进传播，求解零破坏 ──
    [Fact]
    public async Task 资源变化_无种子也进传播零破坏()
    {
        var request = Build(
            materialAvailableAt: null,
            formalDue: Day.AddDays(20),
            runScope: new RunScope
            {
                Trigger = BusinessTriggerType.DomainManualReschedule,
                ChangedResourceIds = new List<int> { 1 }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        Assert.Equal("OP10", task.OperationCode);
        Assert.Equal(1, task.ResourceId);
        // 资源种子只作传播起点（§13.4：不得当事实源）—— 不改资源可用性判定，
        // 无硬约束时仍落最早可行位（Day 起点），不因「资源被标记变化」而推迟
        Assert.Equal(Day, task.PlannedStartTime);
    }

    // ── ⑥ 对照：资源种子 与 需求种子 应等效（两者都把同一 Task 送进影响集）──
    // 若 A 项匹配键写错（如误按 ResourceCode 而非 ResourceId 匹配），资源种子侧影响集为空，
    // 本对照即失败 —— 这是 A 项落码的行为级回归保护。
    [Fact]
    public async Task 资源种子_与需求种子等效()
    {
        var byDemandKey = await _solver.SolveAsync(Build(
            materialAvailableAt: null,
            formalDue: Day.AddDays(20),
            candidateContext: new CandidateContext
            {
                ChangeSeedKeys = new List<string> { "D1" }
            }));

        var byResourceId = await _solver.SolveAsync(Build(
            materialAvailableAt: null,
            formalDue: Day.AddDays(20),
            runScope: new RunScope
            {
                Trigger = BusinessTriggerType.DomainManualReschedule,
                ChangedResourceIds = new List<int> { 1 }
            }));

        Assert.True(byDemandKey.Success, byDemandKey.ErrorMessage);
        Assert.True(byResourceId.Success, byResourceId.ErrorMessage);

        var a = Assert.Single(byDemandKey.FinalTasks);
        var b = Assert.Single(byResourceId.FinalTasks);

        Assert.Equal(a.OperationCode, b.OperationCode);
        Assert.Equal(a.ResourceId, b.ResourceId);
        Assert.Equal(a.PlannedStartTime, b.PlannedStartTime);
        Assert.Equal(a.PlannedEndTime, b.PlannedEndTime);
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private DomainSolveRequest Build(
        DateTime? materialAvailableAt,
        DateTime formalDue,
        string direction = "FORWARD",
        RunScope? runScope = null,
        CandidateContext? candidateContext = null)
    {
        var demand = new LogicalProductionDemand
        {
            LogicalDemandKey = "D1",
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = 1,
            DemandKey = "D1",
            MaterialId = 1,
            FactoryId = 1,
            NetOutputQty = 1m,
            PlannedProcessQty = 1m,
            RequiredAvailableTime = formalDue,
            DemandSequence = 1
        };

        var ops = new List<RoutingOperation>
        {
            new() { MaterialId = 1, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                OperationCode = "OP10", StageCode = "STAGE1", StandardDuration = 60m, SetupTime = 0m }
        };

        var els = new List<OperationResourceEligibility>
        {
            new() { MaterialId = 1, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                OperationCode = "OP10", ResourceId = 1, Priority = 1, CapacityFactor = 1m }
        };

        List<MaterialAvailabilitySlice>? slices = materialAvailableAt.HasValue
            ? new List<MaterialAvailabilitySlice>
            {
                new MaterialAvailabilitySlice
                {
                    AllocationSequence = 1, MaterialId = 1, FactoryId = 1,
                    Quantity = 1m, AvailableTime = materialAvailableAt.Value
                }
            }
            : null;

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = Day,
            PlanningEnd = Day.AddDays(30),
            LogicalProductionDemands = new List<LogicalProductionDemand> { demand },
            RoutingOperations = ops,
            OperationResourceEligibility = els,
            MaterialStageDepartmentContexts = new List<MaterialStageDepartmentContextDto>
            {
                new() { MaterialId = 1, StageCode = "STAGE1", ProductionDepartmentId = 100 }
            },
            Resources = new List<ResourceDefinition>
            {
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = Day, End = Day.AddDays(29), IsAvailable = true }
            },
            MaterialConstraints = slices ?? new List<MaterialAvailabilitySlice>(),
            RunScope = runScope,
            CandidateContext = candidateContext,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = direction,
                    AllowMerge = false,
                    AllowSplit = false
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是 RunScope 消费，非批决策 ⇒ Material 级宽松策略（恒 1 批）。
                BatchPolicies = TestBatchPolicy.Permissive(1)
            }
        };
    }
}