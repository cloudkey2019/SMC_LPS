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
/// Phase2 OperationPlanningMode 分型测试（0号位 20260922 无设备小工序裁决）：
///   - UNCONSTRAINED / WAIT_ONLY 工序**无资源也产出 Task**（ResourceId=NULL，保留工艺时间走链）；
///   - FINITE_RESOURCE 无合格资源仍 **fail-closed**（禁止自动无限产能，0号位 R7）；
///   - 非资源工序时长 = StandardDuration × PlannedProcessQty（1号位 自定：不除 CapacityFactor）。
/// </summary>
public class PhaseTwoOperationPlanningModeTests
{
    private readonly FiniteCapacitySolver _solver = new();

    private static DomainSolveRequest BuildRequest(string planningMode, bool withEligibility)
    {
        var day = new DateTime(2026, 9, 1);
        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = day,
            PlanningEnd = day.AddDays(30),
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new() { LogicalDemandKey = "D1", PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = "D1", MaterialId = 1, FactoryId = 1,
                    StartStageCode = "STAGE1",
                    NetOutputQty = 1m, PlannedProcessQty = 1m,
                    RequiredAvailableTime = day.AddDays(20), DemandSequence = 1 }
            },
            RoutingOperations = new List<RoutingOperation>
            {
                new() { MaterialId = 1, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                    OperationCode = "OP10", StageCode = "STAGE1", StandardDuration = 30m,
                    OperationPlanningMode = planningMode }
            },
            OperationResourceEligibility = withEligibility
                ? new List<OperationResourceEligibility>
                  {
                      new() { MaterialId = 1, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                          OperationCode = "OP10", ResourceId = 1, Priority = 1, CapacityFactor = 1m }
                  }
                : new List<OperationResourceEligibility>(),
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
                new() { ResourceId = 1, Start = day, End = day.AddDays(29), IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters { SchedulingDirection = "FORWARD" },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                BatchPolicies = TestBatchPolicy.Permissive(1, 100)
            }
        };
    }

    [Fact]
    public async Task UNCONSTRAINED_无资源工序_产出ResourceId为Null的Task()
    {
        var result = await _solver.SolveAsync(BuildRequest("UNCONSTRAINED", withEligibility: false));

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        Assert.Equal("OP10", task.OperationCode);
        Assert.Null(task.ResourceId);   // 非资源工序：不占资源（0号位 20260922）
        // 时长 = StandardDuration(30) × PlannedProcessQty(1) = 30 分钟（不除 CapacityFactor）
        Assert.Equal(30d, (task.PlannedEndTime - task.PlannedStartTime).TotalMinutes, 3);
    }

    [Fact]
    public async Task WAIT_ONLY_无资源工序_产出ResourceId为Null的Task()
    {
        var result = await _solver.SolveAsync(BuildRequest("WAIT_ONLY", withEligibility: false));

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        Assert.Null(task.ResourceId);   // 等待约束：不占资源，仅占工艺时间
    }

    [Fact]
    public async Task FINITE_RESOURCE_无资源工序_fail_closed不产Task()
    {
        var result = await _solver.SolveAsync(BuildRequest("FINITE_RESOURCE", withEligibility: false));

        // FINITE_RESOURCE 无合格资源 → 整单 fail-closed（禁止自动无限产能，0号位 R7）
        Assert.Empty(result.FinalTasks);
    }

    [Fact]
    public async Task FINITE_RESOURCE_有资源_正常产Task且ResourceId非空()
    {
        var result = await _solver.SolveAsync(BuildRequest("FINITE_RESOURCE", withEligibility: true));

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        Assert.Equal(1, task.ResourceId);   // 有限资源工序：绑定资源
    }

    // ── 人工能力槽（0号位 v1.3 / 2号位 方案A：ResourceId = -(ManualSlotId) 负值合成）──

    private static DomainSolveRequest BuildManualSlotRequest()
    {
        var day = new DateTime(2026, 9, 1);
        const int manualSlotResourceId = -101;   // 2号位 方案A：人工槽合成 Id = -(ManualSlotId)
        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = day,
            PlanningEnd = day.AddDays(30),
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new() { LogicalDemandKey = "D1", PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = "D1", MaterialId = 1, FactoryId = 1,
                    StartStageCode = "STAGE1",
                    NetOutputQty = 1m, PlannedProcessQty = 1m,
                    RequiredAvailableTime = day.AddDays(20), DemandSequence = 1 }
            },
            RoutingOperations = new List<RoutingOperation>
            {
                // 人工槽工序仍是 FINITE_RESOURCE（人工槽是「资源」，不改变 PlanningMode）
                new() { MaterialId = 1, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                    OperationCode = "OP10", StageCode = "STAGE1", StandardDuration = 30m,
                    OperationPlanningMode = "FINITE_RESOURCE" }
            },
            OperationResourceEligibility = new List<OperationResourceEligibility>
            {
                // 人工槽解锁该工序：ResourceId = 负值合成 Id
                new() { MaterialId = 1, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
                    OperationCode = "OP10", ResourceId = manualSlotResourceId, Priority = 1, CapacityFactor = 1m }
            },
            MaterialStageDepartmentContexts = new List<MaterialStageDepartmentContextDto>
            {
                new() { MaterialId = 1, StageCode = "STAGE1", ProductionDepartmentId = 100 }
            },
            Resources = new List<ResourceDefinition>
            {
                // 2号位 方案A：Resources 补人工槽行（负 Id）
                new() { ResourceId = manualSlotResourceId, ResourceCode = "MAN:DEPT:精修:01",
                    FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                // 人工槽日历投影（ManualCapacitySlotCalendar → ResourceCalendarSlot）
                new() { ResourceId = manualSlotResourceId, Start = day, End = day.AddDays(29), IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters { SchedulingDirection = "FORWARD" },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                BatchPolicies = TestBatchPolicy.Permissive(1, 100)
            }
        };
    }

    [Fact]
    public async Task 人工槽负Id_被正常消费_产出Task带负ResourceId()
    {
        var result = await _solver.SolveAsync(BuildManualSlotRequest());

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        Assert.Equal("OP10", task.OperationCode);
        Assert.Equal(-101, task.ResourceId);          // 负 Id 原样带回（2号位 落库转 Task.ManualSlotId）
        Assert.Equal("MAN:DEPT:精修:01", task.ResourceCode);   // 负 Id 命中 ResourceCodes（负键安全）
    }

    [Fact]
    public async Task 人工槽无日历_不可用不产Task()
    {
        var r = BuildManualSlotRequest();
        var request = new DomainSolveRequest
        {
            PlanVersionId = r.PlanVersionId, DomainKey = r.DomainKey,
            PlanningStart = r.PlanningStart, PlanningEnd = r.PlanningEnd,
            LogicalProductionDemands = r.LogicalProductionDemands,
            RoutingOperations = r.RoutingOperations,
            OperationResourceEligibility = r.OperationResourceEligibility,
            MaterialStageDepartmentContexts = r.MaterialStageDepartmentContexts,
            Resources = r.Resources,
            CalendarSlots = new List<ResourceCalendarSlot>(),   // 人工槽无日历
            StrategySnapshot = r.StrategySnapshot
        };

        var result = await _solver.SolveAsync(request);

        // 0号位 v1.3 §九：无有效日历 = 不可用（禁止 7×24 自动解释）→ 整单 fail-closed
        Assert.Empty(result.FinalTasks);
    }
}