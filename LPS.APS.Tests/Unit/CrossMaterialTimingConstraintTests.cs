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
/// 跨物料时序硬约束（任务喂任务，0号位方案A）回归测试。
/// 纯内存，直接构造 DomainSolveRequest 调 FiniteCapacitySolver.SolveAsync，不碰数据库。
///
/// 覆盖场景（块5）：
/// 1. FORWARD：父件开工 >= 子件完成（约束把父件推后）
/// 2. BACKWARD：父件交期太早 → 倒排失败 → Phase4 修复仍不早于子件完成
/// 3. 子件缺料（无路由）→ 父件缺料 Unscheduled
/// 4. BOM 依赖环 → 技术失败
/// 5. 多层 BOM：祖父 >= 父 >= 子
/// 6. 共料/多父：一个子件喂两个父件
/// 7. 子件全库存（无 link）→ 平铺排程，全部排上
/// 8. 软边界：正排排到 PlanningEnd 之后（0号位裁决：无末期限制）
/// </summary>
public class CrossMaterialTimingConstraintTests
{
    private static readonly DateTime PlanningStart = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningEnd = new DateTime(2026, 9, 30, 0, 0, 0);

    private readonly FiniteCapacitySolver _solver = new();

    // ─────────────────────────── 测试 ───────────────────────────

    [Fact]
    public async Task Forward_ParentStartsAfterChildCompletes()
    {
        var request = Build(
            new[]
            {
                D("child", 1, 1, new DateTime(2026, 9, 5), 1),
                D("parent", 2, 2, new DateTime(2026, 9, 10), 2),
            },
            new[] { L("parent", "child") },
            "FORWARD", PlanningStart, PlanningEnd, TimeSpan.FromDays(30));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var childTask = result.FinalTasks.Single(t => t.SourceDraftId == "child");
        var parentTask = result.FinalTasks.Single(t => t.SourceDraftId == "parent");

        Assert.True(parentTask.PlannedStartTime >= childTask.PlannedEndTime,
            $"父件开工 {parentTask.PlannedStartTime:O} 应 >= 子件完成 {childTask.PlannedEndTime:O}");
        // 约束真正生效：父件恰好被子件完成时间卡住，而非从 planningStart 直接排
        Assert.Equal(childTask.PlannedEndTime, parentTask.PlannedStartTime);
    }

    [Fact]
    public async Task Backward_TightParentDueDate_RepairedToAfterChild()
    {
        // 父件交期只比子件完成晚 30 分钟，倒排会撞到子件完成时间 → Phase2 失败
        // Phase4 正排修复后，仍不得早于子件完成
        var request = Build(
            new[]
            {
                D("child", 1, 1, new DateTime(2026, 9, 5), 1),
                D("parent", 2, 2, new DateTime(2026, 9, 5, 0, 30, 0), 2),
            },
            new[] { L("parent", "child") },
            "BACKWARD", PlanningStart, PlanningEnd, TimeSpan.FromDays(30));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var childTask = result.FinalTasks.Single(t => t.SourceDraftId == "child");
        var parentTask = result.FinalTasks.Single(t => t.SourceDraftId == "parent");

        Assert.True(parentTask.PlannedStartTime >= childTask.PlannedEndTime,
            $"修复后父件 {parentTask.PlannedStartTime:O} 仍应 >= 子件完成 {childTask.PlannedEndTime:O}");
    }

    [Fact]
    public async Task ParentUnscheduled_WhenChildCannotSchedule()
    {
        // 子件无路由（materialId=1 不生成 Routing）→ 子件未排成 → 父件缺料 Unscheduled
        var request = Build(
            new[]
            {
                D("child", 1, 1, new DateTime(2026, 9, 5), 1),
                D("parent", 2, 2, new DateTime(2026, 9, 10), 2),
            },
            new[] { L("parent", "child") },
            "FORWARD", PlanningStart, PlanningEnd, TimeSpan.FromDays(30),
            materialsWithoutRouting: new HashSet<int> { 1 });

        var result = await _solver.SolveAsync(request);

        // 业务 Unscheduled（缺料）不是技术失败，Success 仍 true
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains(result.UnscheduledTasks, u => u.DraftId == "child");
        Assert.Contains(result.UnscheduledTasks, u => u.DraftId == "parent");
    }

    [Fact]
    public async Task Cycle_Detected_ReturnsTechnicalFailure()
    {
        // A 依赖 B，B 依赖 A → 环
        var request = Build(
            new[]
            {
                D("A", 1, 1, new DateTime(2026, 9, 5), 1),
                D("B", 2, 2, new DateTime(2026, 9, 5), 2),
            },
            new[]
            {
                L("A", "B"),
                L("B", "A"),
            },
            "FORWARD", PlanningStart, PlanningEnd, TimeSpan.FromDays(30));

        var result = await _solver.SolveAsync(request);

        Assert.False(result.Success);
        Assert.Contains("环", result.ErrorMessage);
    }

    [Fact]
    public async Task MultiLevelBom_GrandparentAfterParentAfterChild()
    {
        // grandparent 依赖 parent，parent 依赖 child
        var request = Build(
            new[]
            {
                D("child", 1, 1, new DateTime(2026, 9, 3), 1),
                D("parent", 2, 2, new DateTime(2026, 9, 6), 2),
                D("grandparent", 3, 3, new DateTime(2026, 9, 9), 3),
            },
            new[]
            {
                L("parent", "child"),
                L("grandparent", "parent"),
            },
            "FORWARD", PlanningStart, PlanningEnd, TimeSpan.FromDays(30));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var childTask = result.FinalTasks.Single(t => t.SourceDraftId == "child");
        var parentTask = result.FinalTasks.Single(t => t.SourceDraftId == "parent");
        var grandTask = result.FinalTasks.Single(t => t.SourceDraftId == "grandparent");

        Assert.True(parentTask.PlannedStartTime >= childTask.PlannedEndTime);
        Assert.True(grandTask.PlannedStartTime >= parentTask.PlannedEndTime);
    }

    [Fact]
    public async Task SharedChild_FeedsMultipleParents()
    {
        // 一个子件同时喂两个父件
        var request = Build(
            new[]
            {
                D("child", 1, 1, new DateTime(2026, 9, 3), 1),
                D("parent1", 2, 2, new DateTime(2026, 9, 6), 2),
                D("parent2", 3, 3, new DateTime(2026, 9, 6), 3),
            },
            new[]
            {
                L("parent1", "child"),
                L("parent2", "child"),
            },
            "FORWARD", PlanningStart, PlanningEnd, TimeSpan.FromDays(30));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var childTask = result.FinalTasks.Single(t => t.SourceDraftId == "child");
        var p1 = result.FinalTasks.Single(t => t.SourceDraftId == "parent1");
        var p2 = result.FinalTasks.Single(t => t.SourceDraftId == "parent2");

        Assert.True(p1.PlannedStartTime >= childTask.PlannedEndTime);
        Assert.True(p2.PlannedStartTime >= childTask.PlannedEndTime);
    }

    [Fact]
    public async Task NoLinks_AllDemandsScheduled_FlatOrder()
    {
        // 子件全库存（无 link）→ 按 DemandSequence 平铺，全部排上，零影响
        var request = Build(
            new[]
            {
                D("d1", 1, 1, new DateTime(2026, 9, 5), 1),
                D("d2", 2, 2, new DateTime(2026, 9, 5), 2),
            },
            Array.Empty<LinkSpec>(),
            "FORWARD", PlanningStart, PlanningEnd, TimeSpan.FromDays(30));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.FinalTasks.Count);
        Assert.Empty(result.UnscheduledTasks);
    }

    [Fact]
    public async Task Forward_SoftBoundary_SchedulesPastPlanningEnd()
    {
        // 静态到货晚于 PlanningEnd，日历已延长；旧代码会在 PlanningEnd 截断导致 Unscheduled，
        // 软边界（0号位 2026-09-12 裁决：无末期限制）后正排应排到 PlanningEnd 之后。
        //
        // ⚠ 生产生效前提（2026-09-24 复核，据 2号位 回执 §六）：
        //   本用例靠 calendarExtension 让日历覆盖到 PlanningEnd 之后。生产侧该覆盖**已不再**由
        //   「合成 7×24 日历」提供 —— 0号位 2026-09-23 S4 裁决「无日历 = 不可用」后，2号位 合成日历
        //   路径已下线，改为真实 ResourceCalendarSlot 装载，覆盖能力转 **5号位 真实日历铺天数**
        //   （需 ≥ PlanHorizonEnd + 180 天）。
        //   铺天数不足时：本用例语义在生产不可达，正常表现是该需求落 Unscheduled ——
        //   该「无日历 → 不可用、不产 Task」语义已由
        //   PhaseTwoOperationPlanningModeTests.人工槽无日历_不可用不产Task 锁定。
        var planningEnd = new DateTime(2026, 9, 10);
        var materialReady = new DateTime(2026, 9, 15); // > planningEnd
        var request = Build(
            new[]
            {
                D("d1", 1, 1, new DateTime(2026, 9, 20), 1, materialReady),
            },
            Array.Empty<LinkSpec>(),
            "FORWARD", PlanningStart, planningEnd, TimeSpan.FromDays(30));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = result.FinalTasks.Single(t => t.SourceDraftId == "d1");

        Assert.True(task.PlannedStartTime >= materialReady,
            $"任务应在物料到货 {materialReady:O} 之后，实际 {task.PlannedStartTime:O}");
        Assert.True(task.PlannedStartTime > planningEnd,
            $"软边界应允许排到 PlanningEnd {planningEnd:O} 之后，实际 {task.PlannedStartTime:O}");
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private sealed class DemandSpec
    {
        public string Key = string.Empty;
        public int MaterialId;
        public long AllocationSequence;
        public DateTime RequiredAvailableTime;
        public int DemandSequence;
        public DateTime? MaterialAvailableTime; // 静态到货（null = 无静态约束）
    }

    private sealed class LinkSpec
    {
        public string ParentKey = string.Empty;
        public string ChildKey = string.Empty;
    }

    private static DemandSpec D(
        string key, int materialId, long allocSeq, DateTime requiredTime, int demandSeq,
        DateTime? materialAvailableTime = null)
        => new DemandSpec
        {
            Key = key,
            MaterialId = materialId,
            AllocationSequence = allocSeq,
            RequiredAvailableTime = requiredTime,
            DemandSequence = demandSeq,
            MaterialAvailableTime = materialAvailableTime
        };

    private static LinkSpec L(string parentKey, string childKey)
        => new LinkSpec { ParentKey = parentKey, ChildKey = childKey };

    /// <summary>
    /// 构造最小可运行的 DomainSolveRequest：
    /// 每个物料一个单工序 OP10（60 分钟，无 Setup），独立资源（ResourceId = MaterialId），
    /// 部门锁定 (MaterialId, STAGE1) → deptId 100，日历窗口 = [planningStart, planningEnd + extension]。
    /// </summary>
    private static DomainSolveRequest Build(
        IReadOnlyList<DemandSpec> demands,
        IReadOnlyList<LinkSpec> links,
        string direction,
        DateTime planningStart,
        DateTime planningEnd,
        TimeSpan calendarExtension,
        ISet<int>? materialsWithoutRouting = null)
    {
        const int deptId = 100;
        const string stage = "STAGE1";
        const string opCode = "OP10";

        var withoutRouting = materialsWithoutRouting ?? new HashSet<int>();
        var routedDemands = demands.Where(d => !withoutRouting.Contains(d.MaterialId)).ToList();

        var logicalDemands = demands.Select(d => new LogicalProductionDemand
        {
            LogicalDemandKey = d.Key,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = d.AllocationSequence,
            DemandKey = d.Key,
            MaterialId = d.MaterialId,
            FactoryId = 1,
            StartStageCode = stage,
            NetOutputQty = 1m,
            PlannedProcessQty = 1m,
            RequiredAvailableTime = d.RequiredAvailableTime,
            DemandSequence = d.DemandSequence
        }).ToList();

        var routingOps = routedDemands.Select(d => new RoutingOperation
        {
            MaterialId = d.MaterialId,
            ProductionDepartmentId = deptId,
            RouteCode = "DEFAULT",
            OperationCode = opCode,
            StageCode = stage,
            StandardDuration = 60m,
            SetupTime = 0m
        }).ToList();

        var eligibilities = routedDemands.Select(d => new OperationResourceEligibility
        {
            MaterialId = d.MaterialId,
            ProductionDepartmentId = deptId,
            RouteCode = "DEFAULT",
            OperationCode = opCode,
            ResourceId = d.MaterialId,
            Priority = 1,
            CapacityFactor = 1m
        }).ToList();

        var deptContexts = routedDemands.Select(d => new MaterialStageDepartmentContextDto
        {
            MaterialId = d.MaterialId,
            StageCode = stage,
            ProductionDepartmentId = deptId
        }).ToList();

        var resources = demands.Select(d => new ResourceDefinition
        {
            ResourceId = d.MaterialId,
            ResourceCode = $"R{d.MaterialId}",
            FactoryCode = "F1",
            Capacity = 1m
        }).ToList();

        var calendarEnd = planningEnd + calendarExtension;
        var calendarSlots = demands.Select(d => new ResourceCalendarSlot
        {
            ResourceId = d.MaterialId,
            Start = planningStart,
            End = calendarEnd,
            IsAvailable = true
        }).ToList();

        var materialConstraints = demands
            .Where(d => d.MaterialAvailableTime.HasValue)
            .Select(d => new MaterialAvailabilitySlice
            {
                AllocationSequence = d.AllocationSequence,
                MaterialId = d.MaterialId,
                FactoryId = 1,
                Quantity = 1m,
                AvailableTime = d.MaterialAvailableTime!.Value
            }).ToList();

        var linksDto = links.Select(l =>
        {
            var parent = demands.First(d => d.Key == l.ParentKey);
            var child = demands.First(d => d.Key == l.ChildKey);
            return new MaterialRequirementLink
            {
                ConsumerLogicalDemandKey = l.ParentKey,
                ProducerLogicalDemandKey = l.ChildKey,
                ConsumerMaterialId = parent.MaterialId,
                ProducerMaterialId = child.MaterialId,
                RequiredQty = 1m,
                ProducerAllocationSequence = child.AllocationSequence
            };
        }).ToList();

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = planningStart,
            PlanningEnd = planningEnd,
            LogicalProductionDemands = logicalDemands,
            MaterialRequirementLinks = linksDto,
            RoutingOperations = routingOps,
            OperationResourceEligibility = eligibilities,
            MaterialStageDepartmentContexts = deptContexts,
            Resources = resources,
            CalendarSlots = calendarSlots,
            MaterialConstraints = materialConstraints,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters { SchedulingDirection = direction },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                BatchPolicies = TestBatchPolicy.Permissive(logicalDemands.Select(d => d.MaterialId), deptId)
            }
        };
    }
}
