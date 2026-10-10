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
/// 跨物料 Task-to-Task 血缘（<c>PhysicalPeggingDrafts</c> 的跨物料边）回归测试。
///
/// 依据：0号位《多层BOM任务喂任务血缘闭环正式裁决 v1.0 20260910》
///   - §十五 跨物料依赖的 Task 端点（子件供给 Task → 父件消费 Task）
///   - §十六 数量只记「由生产 Task 实际提供的新增生产份额」，不得写理论总需求
///   - §十七 TaskDependency 必须由 1号位 生成
///   - §二十 Case A（三层 BOM + 库存 + 新增生产）/ Case B（全库存：不生成生产 Task 血缘）
///
/// 纯内存，直接构造 <see cref="DomainSolveRequest"/> 调 <c>FiniteCapacitySolver.SolveAsync</c>，不碰数据库。
/// 时序侧（父件开工不得早于子件完成）由 <c>CrossMaterialTimingConstraintTests</c> 覆盖，本文件只验血缘边产出。
/// </summary>
public class CrossMaterialPeggingDraftTests
{
    private static readonly DateTime PlanningStart = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningEnd = new DateTime(2026, 9, 30, 0, 0, 0);

    private readonly FiniteCapacitySolver _solver = new();

    // ─────────────────────────── 测试 ───────────────────────────

    [Fact]
    public async Task 三层BOM_产出跨物料血缘_上游为子件下游为父件()
    {
        // §二十 Case A：A100 ← B(需求200 / 生产60) ← D(需求180 / 生产50)
        var request = Build(
            new[]
            {
                D("A", 1, 1, 100m, 1),
                D("B", 2, 2, 60m, 2),
                D("D", 3, 3, 50m, 3)
            },
            new[] { L("A", "B", 200m), L("B", "D", 180m) },
            "FORWARD");

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var crossMaterial = result.PhysicalPeggingDrafts
            .Where(d => d.UpstreamMaterialId != d.DownstreamMaterialId)
            .ToList();

        // 两条跨物料边：D→B、B→A（同物料工序边不在此列）
        Assert.Equal(2, crossMaterial.Count);

        // §十五：Upstream = 子件（生产方）、Downstream = 父件（消费方）
        var dToB = Assert.Single(crossMaterial.Where(d => d.UpstreamMaterialId == 3));
        Assert.Equal(2, dToB.DownstreamMaterialId);   // D 生产 → B 消费

        var bToA = Assert.Single(crossMaterial.Where(d => d.UpstreamMaterialId == 2));
        Assert.Equal(1, bToA.DownstreamMaterialId);   // B 生产 → A 消费

        // ES：上游 Task 结束 <= 下游 Task 开始（V1 只用 ES）
        var tasksById = result.FinalTasks.ToDictionary(t => t.FinalDraftId);

        Assert.Equal("ES", bToA.DependencyType);
        Assert.Equal("ES", dToB.DependencyType);

        Assert.True(
            tasksById[bToA.DownstreamFinalDraftId].PlannedStartTime
                >= tasksById[bToA.UpstreamFinalDraftId].PlannedEndTime,
            "B→A 边：父件开工应不早于子件完成");

        Assert.True(
            tasksById[dToB.DownstreamFinalDraftId].PlannedStartTime
                >= tasksById[dToB.UpstreamFinalDraftId].PlannedEndTime,
            "D→B 边：父件开工应不早于子件完成");
    }

    [Fact]
    public async Task 跨物料边数量取生产份额_非理论总需求()
    {
        // §十六：「Task-to-Task 血缘数量只表达由生产 Task 实际提供的新增生产份额」
        // A→B 完整需求 200（其中 140 来自库存），B 新增生产只有 60 → 边数量必须是 60，不是 200。
        var request = Build(
            new[]
            {
                D("A", 1, 1, 100m, 1),
                D("B", 2, 2, 60m, 2),
                D("D", 3, 3, 50m, 3)
            },
            new[] { L("A", "B", 200m), L("B", "D", 180m) },
            "FORWARD");

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var crossMaterial = result.PhysicalPeggingDrafts
            .Where(d => d.UpstreamMaterialId != d.DownstreamMaterialId)
            .ToList();

        var bToA = Assert.Single(crossMaterial.Where(d => d.UpstreamMaterialId == 2));
        var dToB = Assert.Single(crossMaterial.Where(d => d.UpstreamMaterialId == 3));

        // 数量 = 子件生产需求的 NetOutputQty（生产份额）
        Assert.Equal(60m, bToA.Quantity);
        Assert.Equal(50m, dToB.Quantity);

        // 而 MaterialRequirementLink.RequiredQty 是「完整 Child Demand 数量」（§十九-1.2），两者必须不同
        Assert.Equal(200m, request.MaterialRequirementLinks
            .Single(l => l.ConsumerLogicalDemandKey == "A" && l.ProducerLogicalDemandKey == "B").RequiredQty);
        Assert.Equal(180m, request.MaterialRequirementLinks
            .Single(l => l.ConsumerLogicalDemandKey == "B" && l.ProducerLogicalDemandKey == "D").RequiredQty);

        Assert.NotEqual(200m, bToA.Quantity);
        Assert.NotEqual(180m, dToB.Quantity);
    }

    [Fact]
    public async Task 无血缘边时_不产出任何跨物料TaskDependency()
    {
        // §二十 Case B 的「不生成」半边：子件全库存 → 无子件生产需求 → 无 link → 不得凭空产出跨物料边。
        var request = Build(
            new[]
            {
                D("A", 1, 1, 100m, 1),
                D("B", 2, 2, 60m, 2)
            },
            Array.Empty<LinkSpec>(),
            "FORWARD");

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        Assert.DoesNotContain(
            result.PhysicalPeggingDrafts,
            d => d.UpstreamMaterialId != d.DownstreamMaterialId);
    }

    [Fact]
    public async Task CaseB_子件全库存_Producer键为空_不崩溃且不产跨物料边()
    {
        // §二十 Case B：A→B 需求 200，B 全库存承接 → 无 B 生产需求 →
        // link 的 ProducerLogicalDemandKey = null（2号位 20260924 方案 (a) 落码：string? + ChildDemandKey）。
        //
        // 1号位 必须两条都做到：
        //   ① 不崩 —— Dictionary.TryGetValue(null) 抛 ArgumentNullException（.NET 契约；注意与
        //      HashSet<T>.Contains(null) 返回 false 语义不同），故消费端必须先显式判空再查字典；
        //   ② 不产跨物料 TaskDependency —— §二十「不生成 B 生产 Task → A TaskDependency」。
        var request = Build(
            new[] { D("A", 1, 1, 100m, 1) },
            new[] { LCaseB("A", childMaterialId: 2, childDemandKey: "ORDER_1_B_1", requiredQty: 200m) },
            "FORWARD");

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // 只有 A 的 Task（B 无生产需求 → 无 B Task）
        Assert.Single(result.FinalTasks);

        Assert.DoesNotContain(
            result.PhysicalPeggingDrafts,
            d => d.UpstreamMaterialId != d.DownstreamMaterialId);
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private sealed class DemandSpec
    {
        public string Key = string.Empty;
        public int MaterialId;
        public long AllocationSequence;
        public decimal NetOutputQty;
        public int DemandSequence;
    }

    private sealed class LinkSpec
    {
        public string ParentKey = string.Empty;

        /// <summary>Case B（子件全库存/采购占位）时为 null：子件无生产需求，link 的 Producer 键为空。</summary>
        public string? ChildKey;
        public decimal RequiredQty;

        /// <summary>Case B 时子件不在 demands 中，须直接给物料 Id。</summary>
        public int ChildMaterialId;
        public string ChildDemandKey = string.Empty;
    }

    private static DemandSpec D(
        string key, int materialId, long allocSeq, decimal netOutputQty, int demandSeq)
        => new DemandSpec
        {
            Key = key,
            MaterialId = materialId,
            AllocationSequence = allocSeq,
            NetOutputQty = netOutputQty,
            DemandSequence = demandSeq
        };

    private static LinkSpec L(string parentKey, string childKey, decimal requiredQty)
        => new LinkSpec { ParentKey = parentKey, ChildKey = childKey, RequiredQty = requiredQty };

    /// <summary>§二十 Case B：子件全库存 / 采购占位 → 无子件生产需求，ProducerLogicalDemandKey = null。</summary>
    private static LinkSpec LCaseB(
        string parentKey, int childMaterialId, string childDemandKey, decimal requiredQty)
        => new LinkSpec
        {
            ParentKey = parentKey,
            ChildKey = null,
            RequiredQty = requiredQty,
            ChildMaterialId = childMaterialId,
            ChildDemandKey = childDemandKey
        };

    /// <summary>
    /// 构造最小可运行的 DomainSolveRequest：
    /// 每个物料一个单工序 OP10（60 分钟，无 Setup），独立资源（ResourceId = MaterialId），
    /// 部门锁定 (MaterialId, STAGE1) → deptId 100，日历 = [planningStart, planningEnd + 30 天]。
    /// </summary>
    private static DomainSolveRequest Build(
        IReadOnlyList<DemandSpec> demands,
        IReadOnlyList<LinkSpec> links,
        string direction)
    {
        const int deptId = 100;
        const string stage = "STAGE1";
        const string opCode = "OP10";

        var logicalDemands = demands.Select(d => new LogicalProductionDemand
        {
            LogicalDemandKey = d.Key,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = d.AllocationSequence,
            DemandKey = d.Key,
            MaterialId = d.MaterialId,
            FactoryId = 1,
            NetOutputQty = d.NetOutputQty,
            PlannedProcessQty = d.NetOutputQty,
            RequiredAvailableTime = PlanningEnd,
            DemandSequence = d.DemandSequence
        }).ToList();

        var routingOps = demands.Select(d => new RoutingOperation
        {
            MaterialId = d.MaterialId,
            ProductionDepartmentId = deptId,
            RouteCode = "DEFAULT",
            OperationCode = opCode,
            StageCode = stage,
            StandardDuration = 60m,
            SetupTime = 0m
        }).ToList();

        var eligibilities = demands.Select(d => new OperationResourceEligibility
        {
            MaterialId = d.MaterialId,
            ProductionDepartmentId = deptId,
            RouteCode = "DEFAULT",
            OperationCode = opCode,
            ResourceId = d.MaterialId,
            Priority = 1,
            CapacityFactor = 1m
        }).ToList();

        var deptContexts = demands.Select(d => new MaterialStageDepartmentContextDto
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

        var calendarEnd = PlanningEnd.AddDays(30);
        var calendarSlots = demands.Select(d => new ResourceCalendarSlot
        {
            ResourceId = d.MaterialId,
            Start = PlanningStart,
            End = calendarEnd,
            IsAvailable = true
        }).ToList();

        var linksDto = links.Select(l =>
        {
            var parent = demands.First(d => d.Key == l.ParentKey);

            // §二十 Case B：子件全库存 / 采购占位 → 无子件生产需求，ProducerLogicalDemandKey = null，
            // 但仍产 link 以保留「父需求→子需求」真相（2号位 20260924 方案 (a) 落码）。
            if (l.ChildKey is null)
            {
                return new MaterialRequirementLink
                {
                    ConsumerLogicalDemandKey = l.ParentKey,
                    ProducerLogicalDemandKey = null,
                    ConsumerMaterialId = parent.MaterialId,
                    ProducerMaterialId = l.ChildMaterialId,
                    ChildDemandKey = l.ChildDemandKey,
                    RequiredQty = l.RequiredQty,
                    ProducerAllocationSequence = 0L
                };
            }

            var child = demands.First(d => d.Key == l.ChildKey);
            return new MaterialRequirementLink
            {
                ConsumerLogicalDemandKey = l.ParentKey,
                ProducerLogicalDemandKey = l.ChildKey,
                ConsumerMaterialId = parent.MaterialId,
                ProducerMaterialId = child.MaterialId,
                RequiredQty = l.RequiredQty,
                ProducerAllocationSequence = child.AllocationSequence
            };
        }).ToList();

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = logicalDemands,
            MaterialRequirementLinks = linksDto,
            RoutingOperations = routingOps,
            OperationResourceEligibility = eligibilities,
            MaterialStageDepartmentContexts = deptContexts,
            Resources = resources,
            CalendarSlots = calendarSlots,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters { SchedulingDirection = direction },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                BatchPolicies = TestBatchPolicy.Permissive(logicalDemands.Select(d => d.MaterialId))
            }
        };
    }
}