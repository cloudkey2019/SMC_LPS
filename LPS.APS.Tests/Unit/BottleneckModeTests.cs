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
/// P1-02（BottleneckMode 四模式）回归测试（纯内存，直接调 FiniteCapacitySolver）。
/// 对齐 3号位 20260916 回执语义：
///   Auto         → 利用率超 BottleneckUtilizationThreshold(0.85) 自动识别；
///   ForceAnchor  → 锚点资源无条件入瓶颈集（即使利用率低）；
///   PreferAnchor → 锚点资源有负荷（利用率>0）时优先入，否则回退 Auto；
///   NotAnchor    → 锚点资源即使超阈值也从瓶颈集排除。
/// 场景：R1 利用率 0.6（低）、R2 利用率 0.9（高），锚点用资源编码 R1/R2 指定。
/// </summary>
public class BottleneckModeTests
{
    private static readonly DateTime PlanningStart = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningEnd = new DateTime(2026, 9, 30, 0, 0, 0);

    private readonly FiniteCapacitySolver _solver = new();

    [Fact]
    public async Task Auto_高利用率判瓶颈_低利用率不判()
    {
        var result = await _solver.SolveAsync(Build(DynamicBottleneckMode.Auto, null));

        var ids = BottleneckIds(result);
        Assert.Contains(2, ids);          // R2 利用率 0.9 > 0.85 → 判瓶颈
        Assert.DoesNotContain(1, ids);    // R1 利用率 0.6 → 不判
    }

    [Fact]
    public async Task ForceAnchor_低利用率锚点强制入瓶颈()
    {
        var result = await _solver.SolveAsync(Build(DynamicBottleneckMode.ForceAnchor, "R1"));

        var ids = BottleneckIds(result);
        Assert.Contains(1, ids);          // R1 强制锚点（利用率 0.6 也入）
        Assert.Contains(2, ids);          // R2 仍超阈值
    }

    [Fact]
    public async Task PreferAnchor_有负荷锚点优先进瓶颈()
    {
        var result = await _solver.SolveAsync(Build(DynamicBottleneckMode.PreferAnchor, "R1"));

        var ids = BottleneckIds(result);
        Assert.Contains(1, ids);          // R1 有负荷（0.6>0）→ 优先锚点
        Assert.Contains(2, ids);          // R2 超阈值
    }

    [Fact]
    public async Task NotAnchor_高利用率锚点被排除()
    {
        var result = await _solver.SolveAsync(Build(DynamicBottleneckMode.NotAnchor, "R2"));

        var ids = BottleneckIds(result);
        Assert.DoesNotContain(2, ids);    // R2 被排除（即使 0.9 超阈值）
        Assert.DoesNotContain(1, ids);    // R1 低利用率本就不判
    }

    // ⚠ 2026-10-07：断言值随 PhaseThreeDiagnostics 的 ReasonCode 对齐最新冻结文档（15 码权威枚举，
    //   《APS数据库字段说明文档 v5.1.9》§八.1 :4833）同步 —— 瓶颈事实的 ReasonCode 由
    //   RESOURCE_CAPACITY_SHORTAGE（非 15 码，已退役）改为 RESOURCE_CAPACITY_WAIT。
    //   本 helper 断言的是 1号位 自己的产出（FiniteCapacitySolver.ExplanationFacts），故随之更新。
    private static List<int> BottleneckIds(DomainSolveResult result)
        => result.ExplanationFacts
            .Where(f => f.ObjectType == "RESOURCE" && f.ReasonCode == "RESOURCE_CAPACITY_WAIT")
            .Select(f => f.ResourceId!.Value)
            .ToList();

    private static DomainSolveRequest Build(DynamicBottleneckMode mode, string? anchorResourceCode)
    {
        const int deptId = 100;
        const string stage = "STAGE1";
        const string opCode = "OP10";

        var logicalDemands = new List<LogicalProductionDemand>
        {
            new()
            {
                LogicalDemandKey = "M1", PlanVersionId = 1L, DomainKey = "DOMAIN",
                AllocationSequence = 1, DemandKey = "M1", MaterialId = 1, FactoryId = 1,
                StartStageCode = stage,
                NetOutputQty = 1m, PlannedProcessQty = 1m,
                RequiredAvailableTime = PlanningStart.AddDays(20), DemandSequence = 1
            },
            new()
            {
                LogicalDemandKey = "M2", PlanVersionId = 1L, DomainKey = "DOMAIN",
                AllocationSequence = 2, DemandKey = "M2", MaterialId = 2, FactoryId = 1,
                StartStageCode = stage,
                NetOutputQty = 1m, PlannedProcessQty = 1m,
                RequiredAvailableTime = PlanningStart.AddDays(20), DemandSequence = 2
            }
        };

        // M1 工时 60、M2 工时 90；日历窗口各 100 分钟 → R1 利用率 0.6、R2 利用率 0.9。
        var routingOps = new List<RoutingOperation>
        {
            new() { MaterialId = 1, ProductionDepartmentId = deptId, RouteCode = "DEFAULT", OperationCode = opCode, StageCode = stage, StandardDuration = 60m, SetupTime = 0m },
            new() { MaterialId = 2, ProductionDepartmentId = deptId, RouteCode = "DEFAULT", OperationCode = opCode, StageCode = stage, StandardDuration = 90m, SetupTime = 0m }
        };

        var eligibilities = new List<OperationResourceEligibility>
        {
            new() { MaterialId = 1, ProductionDepartmentId = deptId, RouteCode = "DEFAULT", OperationCode = opCode, ResourceId = 1, Priority = 1, CapacityFactor = 1m },
            new() { MaterialId = 2, ProductionDepartmentId = deptId, RouteCode = "DEFAULT", OperationCode = opCode, ResourceId = 2, Priority = 1, CapacityFactor = 1m }
        };

        var deptContexts = new List<MaterialStageDepartmentContextDto>
        {
            new() { MaterialId = 1, StageCode = stage, ProductionDepartmentId = deptId },
            new() { MaterialId = 2, StageCode = stage, ProductionDepartmentId = deptId }
        };

        var resources = new List<ResourceDefinition>
        {
            new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m },
            new() { ResourceId = 2, ResourceCode = "R2", FactoryCode = "F1", Capacity = 1m }
        };

        var calendarEnd = PlanningStart.AddMinutes(100);
        var calendarSlots = new List<ResourceCalendarSlot>
        {
            new() { ResourceId = 1, Start = PlanningStart, End = calendarEnd, IsAvailable = true },
            new() { ResourceId = 2, Start = PlanningStart, End = calendarEnd, IsAvailable = true }
        };

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = logicalDemands,
            RoutingOperations = routingOps,
            OperationResourceEligibility = eligibilities,
            MaterialStageDepartmentContexts = deptContexts,
            Resources = resources,
            CalendarSlots = calendarSlots,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = false,
                    AllowSplit = false
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是瓶颈识别，非批决策 ⇒ Material 级宽松策略（恒 1 批）。
                BatchPolicies = TestBatchPolicy.Permissive(logicalDemands.Select(d => d.MaterialId), deptId),
                SolverStrategy = new SolverStrategyBlock
                {
                    BottleneckMode = mode,
                    AnchorResourceCode = anchorResourceCode
                }
            }
        };
    }
}
