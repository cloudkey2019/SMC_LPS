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
/// P1-09：第15轮审核 P0-01~P0-06 闭环专项回归（纯内存，直接调 FiniteCapacitySolver）。
/// 对齐《APS_V1_1号位代码第15轮审核报告》P1-09 点名的六个未覆盖场景：
///   P0-01 Phase4 修复不得绕过 StartOperation 裁剪（不得把已完成前序工序重排回来）；
///   P0-02 Candidate 直接移动必须服从 Resource Calendar / 完整 Setup 占用；
///   P0-03 Candidate Fallback 不得让同一批 Task 重复进入最终集合；
///   P0-04 Merge M:N 的 AllocationTaskShare 血缘不得在 Phase2 内部丢失；
///   P0-05 同一 Allocation 多执行起点切片，Phase5 数量闭合 = Σ全部切片 NetOutputQty；
///   P0-06 Split 后 TaskDependency 不得全连接 + 每条全量数量（数量不得放大）。
/// 触发机制说明：Phase2 从不 Split；「完整时长装不进任何日历槽 → Phase2 保持未排 →
/// Phase4 RepairUnscheduledDemands → TryResourceSwitch（裁剪 Routing + 有限 Split）」是
/// P0-01/P0-06 的确定性触发点；Candidate 传播/Fallback 由 ChangeSeedKeys + 30% 警戒线触发。
/// </summary>
public class P0RegressionTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1);
    private static readonly DateTime PlanningStart = Day;
    private static readonly DateTime PlanningEnd = Day.AddDays(30);

    private readonly FiniteCapacitySolver _solver = new();

    // ════════════════════════════════════════════════════════════
    // P0-01：Phase4 修复不得绕过 StartOperationCode 裁剪
    // 场景：路由 OP10→OP20，Demand StartOperationCode=OP20（前序已完成）。
    // OP20 完整时长(90min)装不进任何单槽(60min) → Phase2 未排 →
    // Phase4 TryResourceSwitch 修复（AllowSplit 拆 2×45min）。
    // 修复前 Bug：Phase4 按完整 Routing 重建 → 把已完成的 OP10 又排回来。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task P0_01_Phase4修复_不得重排StartOperation之前的工序()
    {
        var request = Build(
            demands: new[] { D("D1", 1, 1, 2m, 1, startOp: "OP20") },
            ops: new[] { Op(1, "OP10", 45m), Op(1, "OP20", 45m) },
            deps: new[] { Dep(1, "OP10", "OP20") },
            eligibilities: new[] { El(1, "OP10", 1), El(1, "OP20", 1) },
            resources: new[] { Res(1, "R1", (Day.AddHours(8), Day.AddHours(9)), (Day.AddHours(10), Day.AddHours(11))) },
            allowSplit: true);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var d1Tasks = result.FinalTasks.Where(t => t.SourceDraftId == "D1").ToList();

        // Phase4 修复成功：OP20 被拆成 2 个 qty=1 的 Task（各 45min 落入两个 60min 槽）
        Assert.Equal(2, d1Tasks.Count);
        // P0-01 核心断言：全部是 OP20，绝不能出现 OP10（StartOperation 之前的工序）
        Assert.All(d1Tasks, t => Assert.Equal("OP20", t.OperationCode));
        Assert.DoesNotContain(d1Tasks, t => t.OperationCode == "OP10");
        Assert.Equal(2m, d1Tasks.Sum(t => t.Quantity));
    }

    // ════════════════════════════════════════════════════════════
    // P0-02：Candidate 直接移动必须服从 Resource Calendar / 完整 Setup 占用
    // 场景：D1 双工序同资源（Setup=30/加工=60），日历含午休断档 [12:00-13:00]；
    // 5 个填充 Demand 使总 Task=7（seed 影响 2 个 ≤ 30% 警戒线，不触发 Fallback，走直接移动路径）。
    // 断言：移动后每个 Task 的占用窗 [Start-Setup, End] 完整落在单个可用槽内（不跨午休、不含 Setup 越界），
    // 且占用互不重叠、工序先后不破。修复前 Bug：朴素 gap 扫描可把 Task 塞进非工作时段/Setup 越界/占用重叠。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task P0_02_Candidate移动_服从Calendar与Setup占用()
    {
        var demands = new List<DemandSpec> { D("D1", 1, 1, 1m, 1) };
        var ops = new List<RoutingOperation> { Op(1, "OP10", 60m, setup: 30m), Op(1, "OP20", 60m, setup: 30m) };
        var deps = new List<RoutingDependency> { Dep(1, "OP10", "OP20") };
        var els = new List<OperationResourceEligibility> { El(1, "OP10", 1), El(1, "OP20", 1) };
        var resources = new List<ResourceDefinition>
        {
            Res(1, "R1", (Day.AddHours(8), Day.AddHours(12)), (Day.AddHours(13), Day.AddHours(17)))
        };

        // 填充：5 个独立物料/资源/单工序 Demand → 总 Task 数 = 2 + 5 = 7
        for (int i = 2; i <= 6; i++)
        {
            demands.Add(D($"D{i}", i, i, 1m, i));
            ops.Add(Op(i, "FOP", 60m));
            els.Add(El(i, "FOP", i));
            resources.Add(Res(i, $"R{i}", (Day.AddHours(8), Day.AddHours(17))));
        }

        var request = Build(demands, ops, deps, els, resources, allowSplit: false,
            candidate: new CandidateContext { BasePlanVersionId = 1, ChangeSeedKeys = new[] { "D1" } },
            // item1 接线后 Setup 走规则：RoutingOperation.SetupTime=30 故意保留——若代码仍读旧字段，
            // OP10（初始设备状态应为 0）会带 30 分钟 Setup 越出日历起点 8:00，下方不变量断言即抓到。
            // OP20 前产品=物料1（同产品）→ EXACT A→A 显式规则 30 分钟（v1.2 §六 允许覆盖默认 0）。
            setupRules: new[]
            {
                new SetupTransitionRuleSnapshot { ProductionDepartmentId = 100, StageCode = "STAGE1",
                    OperationCode = "OP20", ResourceId = 1, FromMaterialId = 1, ToMaterialId = 1,
                    RuleType = "EXACT", SetupMinutes = 30m }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var d1Tasks = result.FinalTasks.Where(t => t.SourceDraftId == "D1").ToList();
        Assert.Equal(2, d1Tasks.Count);

        var morning = (Start: Day.AddHours(8), End: Day.AddHours(12));
        var afternoon = (Start: Day.AddHours(13), End: Day.AddHours(17));

        foreach (var t in d1Tasks)
        {
            // 占用窗含 Setup（P0-02：占用从 Start-Setup 开始），必须完整落在单个可用槽内
            var occStart = t.PlannedStartTime.AddMinutes(-(double)t.SetupTime);
            var occEnd = t.PlannedEndTime;
            bool inMorning = occStart >= morning.Start && occEnd <= morning.End;
            bool inAfternoon = occStart >= afternoon.Start && occEnd <= afternoon.End;
            Assert.True(inMorning || inAfternoon,
                $"Task {t.OperationCode} 占用 [{occStart:HH:mm}-{occEnd:HH:mm}] 跨越日历断档或越界");
        }

        var op10 = d1Tasks.Single(t => t.OperationCode == "OP10");
        var op20 = d1Tasks.Single(t => t.OperationCode == "OP20");

        // 占用互不重叠 + 工序先后不破
        var occ10Start = op10.PlannedStartTime.AddMinutes(-(double)op10.SetupTime);
        var occ20Start = op20.PlannedStartTime.AddMinutes(-(double)op20.SetupTime);
        Assert.True(op10.PlannedEndTime <= occ20Start || op20.PlannedEndTime <= occ10Start,
            "两个 Task 的含 Setup 占用窗重叠");
        Assert.True(op20.PlannedStartTime >= op10.PlannedEndTime, "OP20 应不早于 OP10 完成");
    }

    // ════════════════════════════════════════════════════════════
    // P0-03：Fallback 不得重复进入最终集合
    // 场景：总 Task=2，seed 命中全部 2 个 → 2 > max(1, 2×30%=0)=1 → 立即触发 Fallback（替换语义）。
    // 修复前 Bug：fallback 结果既替换 scheduleResult 又 AddRange 进 RepairedTasks，Phase5 合并后同批 Task 出现两次。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task P0_03_Fallback重排_Task不得重复进入最终集合()
    {
        var request = Build(
            demands: new[] { D("D1", 1, 1, 1m, 1) },
            ops: new[] { Op(1, "OP10", 60m), Op(1, "OP20", 60m) },
            deps: new[] { Dep(1, "OP10", "OP20") },
            eligibilities: new[] { El(1, "OP10", 1), El(1, "OP20", 1) },
            resources: new[] { Res(1, "R1", (Day, Day.AddDays(29))) },
            allowSplit: false,
            candidate: new CandidateContext { BasePlanVersionId = 1, ChangeSeedKeys = new[] { "D1" } });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        // 2 个工序各 1 个 Task，不许多（修复前会出现 4 个 / 重复 FinalDraftId）
        Assert.Equal(2, result.FinalTasks.Count);
        Assert.Equal(2, result.FinalTasks.Select(t => t.FinalDraftId).Distinct().Count());
        Assert.Equal(2, result.FinalTasks.Select(t => (t.SourceDraftId, t.OperationCode)).Distinct().Count());
    }

    // ════════════════════════════════════════════════════════════
    // P0-04：Merge M:N 份额血缘不得丢失
    // 场景：两个自由 Demand（alloc 1 qty20 / alloc 2 qty30）同物料同单工序，AllowMerge=true → 合并为 1 个 Task qty50。
    // 断言：AllocationShares 覆盖两个 Allocation（20+30）。修复前 Bug：Phase5 只按 SourceDraftId 锚点重建，
    // 被合入 Demand 的 alloc2 份额丢失 → Σ闭合校验失败。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task P0_04_MergeMN_Allocation份额血缘闭合()
    {
        var request = Build(
            demands: new[] { D("D1", 1, 1, 20m, 1), D("D2", 2, 1, 30m, 2) },
            ops: new[] { Op(1, "OP10", 1m) },   // 1 min/件 → 20/30 分钟
            deps: Array.Empty<RoutingDependency>(),
            eligibilities: new[] { El(1, "OP10", 1) },
            resources: new[] { Res(1, "R1", (Day, Day.AddDays(29))) },
            allowSplit: false, allowMerge: true);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        // 合并为单 Task，数量 50
        var merged = Assert.Single(result.FinalTasks);
        Assert.Equal(50m, merged.Quantity);
        // M:N 血缘：两个 Allocation 的份额都在，且各归各的数量
        var alloc1 = result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty);
        var alloc2 = result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty);
        Assert.Equal(20m, alloc1);
        Assert.Equal(30m, alloc2);
        Assert.Equal(50m, result.AllocationShares.Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════
    // P0-05：同一 Allocation 多执行起点切片的数量闭合
    // 场景：alloc 7 总量 1000 = 切片A(200, 全路由 OP10→OP20) + 切片B(800, StartOperationCode=OP20)。
    // 断言：闭合目标 = Σ全部切片 NetOutputQty = 1000（修复前 Bug：expectedQty 只取 First().NetOutputQty
    // → 闭合校验必失败）；切片B 不产生 OP10 Task。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task P0_05_同Allocation多执行起点切片_数量闭合按总量()
    {
        var request = Build(
            demands: new[]
            {
                D("DFULL", 7, 1, 200m, 1),
                D("DSLICE", 7, 1, 800m, 2, startOp: "OP20")
            },
            ops: new[] { Op(1, "OP10", 1m), Op(1, "OP20", 1m) },
            deps: new[] { Dep(1, "OP10", "OP20") },
            eligibilities: new[] { El(1, "OP10", 1), El(1, "OP20", 2) },
            resources: new[]
            {
                Res(1, "R1", (Day, Day.AddDays(29))),
                Res(2, "R2", (Day, Day.AddDays(29)))
            },
            allowSplit: false);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // alloc 7 闭合 = 200 + 800 = 1000
        var alloc7Total = result.AllocationShares.Where(s => s.AllocationSequence == 7).Sum(s => s.ComponentQty);
        Assert.Equal(1000m, alloc7Total);

        // 切片血缘：DFULL 有 OP10+OP20；DSLICE 只有 OP20（StartOperation 裁剪在 Phase2 生效）
        var fullTasks = result.FinalTasks.Where(t => t.SourceDraftId == "DFULL").ToList();
        var sliceTasks = result.FinalTasks.Where(t => t.SourceDraftId == "DSLICE").ToList();
        Assert.Equal(2, fullTasks.Count);
        Assert.Contains(fullTasks, t => t.OperationCode == "OP10");
        Assert.Contains(fullTasks, t => t.OperationCode == "OP20");
        var sliceOp20 = Assert.Single(sliceTasks);
        Assert.Equal("OP20", sliceOp20.OperationCode);
        Assert.Equal(800m, sliceOp20.Quantity);
    }

    // ════════════════════════════════════════════════════════════
    // P0-06：Split 后 TaskDependency 真实数量流
    // 场景：qty2 全路由；OP10 完整时长(90min)装不进单槽(60min) → Phase2 未排 →
    // Phase4 Split OP10 为 2×(qty1, 45min)，OP20 单 Task(qty2, 60min) 落第三槽。
    // 断言：依赖边 = 2（上游2×下游1，非全交叉的重复），每条边 Quantity = 下游真实数量/上游数 = 1，
    // Σ边数量 = 2 = NetOutputQty（修复前 Bug：每条边 Quantity=NetOutputQty=2 → Σ=4 数量放大）。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task P0_06_Split后Dependency_数量不放大不全交叉()
    {
        var request = Build(
            demands: new[] { D("D1", 1, 1, 2m, 1) },
            ops: new[] { Op(1, "OP10", 45m), Op(1, "OP20", 30m) },   // qty2 → OP10=90min(拆), OP20=60min(整)
            deps: new[] { Dep(1, "OP10", "OP20") },
            eligibilities: new[] { El(1, "OP10", 1), El(1, "OP20", 1) },
            resources: new[]
            {
                Res(1, "R1",
                    (Day.AddHours(8), Day.AddHours(9)),      // 60min：容 OP10 拆分件1(45)
                    (Day.AddHours(10), Day.AddHours(11)),    // 60min：容 OP10 拆分件2(45)
                    (Day.AddHours(11.5), Day.AddHours(12.5))) // 60min：容 OP20 整件(60)
            },
            allowSplit: true);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var op10Tasks = result.FinalTasks.Where(t => t.OperationCode == "OP10").ToList();
        var op20Tasks = result.FinalTasks.Where(t => t.OperationCode == "OP20").ToList();
        Assert.Equal(2, op10Tasks.Count);                     // OP10 被拆成 2 件
        Assert.All(op10Tasks, t => Assert.Equal(1m, t.Quantity));
        var op20 = Assert.Single(op20Tasks);
        Assert.Equal(2m, op20.Quantity);

        // 依赖边：2 条（每个上游 Split → 唯一下游），每条数量 = 2/2 = 1，总量守恒 = NetOutputQty
        var edges = result.PhysicalPeggingDrafts
            .Where(p => op10Tasks.Any(s => s.FinalDraftId == p.UpstreamFinalDraftId) &&
                        p.DownstreamFinalDraftId == op20.FinalDraftId)
            .ToList();
        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal(1m, e.Quantity)); // 修复前每条 = NetOutputQty = 2（放大）
        Assert.Equal(2m, edges.Sum(e => e.Quantity));         // Σ = 需求总量，不重复累计
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private sealed class DemandSpec
    {
        public string Key = string.Empty;
        public long AllocationSequence;
        public int MaterialId;
        public decimal Qty;
        public int DemandSequence;
        public string? StartOperationCode;
    }

    private static DemandSpec D(string key, long allocSeq, int materialId, decimal qty, int seq, string? startOp = null)
        => new DemandSpec
        {
            Key = key,
            AllocationSequence = allocSeq,
            MaterialId = materialId,
            Qty = qty,
            DemandSequence = seq,
            StartOperationCode = startOp
        };

    private static RoutingOperation Op(int materialId, string opCode, decimal standardDuration, decimal setup = 0m)
        => new RoutingOperation
        {
            MaterialId = materialId,
            ProductionDepartmentId = 100,
            RouteCode = "DEFAULT",
            OperationCode = opCode,
            StageCode = "STAGE1",
            StandardDuration = standardDuration,
            SetupTime = setup
        };

    private static RoutingDependency Dep(int materialId, string from, string to)
        => new RoutingDependency
        {
            MaterialId = materialId,
            ProductionDepartmentId = 100,
            RouteCode = "DEFAULT",
            FromOperationCode = from,
            ToOperationCode = to,
            DependencyType = "ES",
            LagTime = 0m
        };

    private static OperationResourceEligibility El(int materialId, string opCode, int resourceId)
        => new OperationResourceEligibility
        {
            MaterialId = materialId,
            ProductionDepartmentId = 100,
            RouteCode = "DEFAULT",
            OperationCode = opCode,
            ResourceId = resourceId,
            Priority = 1,
            CapacityFactor = 1m
        };

    private static ResourceDefinition Res(int id, string code, params (DateTime Start, DateTime End)[] slots)
    {
        // 登记该资源的日历可用槽（Build 时统一取出装入 CalendarSlots）
        foreach (var s in slots)
        {
            CalendarRegistry.Add(new ResourceCalendarSlot
            {
                ResourceId = id,
                Start = s.Start,
                End = s.End,
                IsAvailable = true
            });
        }
        return new ResourceDefinition { ResourceId = id, ResourceCode = code, FactoryCode = "F1", Capacity = 1m };
    }

    private static DomainSolveRequest Build(
        IReadOnlyList<DemandSpec> demands,
        IReadOnlyList<RoutingOperation> ops,
        IReadOnlyList<RoutingDependency> deps,
        IReadOnlyList<OperationResourceEligibility> eligibilities,
        IReadOnlyList<ResourceDefinition> resources,
        bool allowSplit,
        bool allowMerge = false,
        CandidateContext? candidate = null,
        IReadOnlyList<SetupTransitionRuleSnapshot>? setupRules = null)
    {
        // 资源日历槽由测试方法内联在 Res(...) 定义 → 这里统一收集
        var calendarSlots = CalendarRegistry.ToList();
        CalendarRegistry.Clear();

        var materialIds = demands.Select(d => d.MaterialId).Distinct().ToList();

        var logicalDemands = demands.Select(d => new LogicalProductionDemand
        {
            LogicalDemandKey = d.Key,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = d.AllocationSequence,
            DemandKey = d.Key,
            MaterialId = d.MaterialId,
            FactoryId = 1,
            StartOperationCode = d.StartOperationCode,
            NetOutputQty = d.Qty,
            PlannedProcessQty = d.Qty,
            RequiredAvailableTime = PlanningStart.AddDays(20),
            DemandSequence = d.DemandSequence
        }).ToList();

        var deptContexts = materialIds
            .Select(mid => new MaterialStageDepartmentContextDto
            {
                MaterialId = mid,
                StageCode = "STAGE1",
                ProductionDepartmentId = 100
            }).ToList();

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = logicalDemands,
            RoutingOperations = ops.ToList(),
            RoutingDependencies = deps.ToList(),
            OperationResourceEligibility = eligibilities.ToList(),
            MaterialStageDepartmentContexts = deptContexts,
            Resources = resources.ToList(),
            CalendarSlots = calendarSlots,
            CandidateContext = candidate,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = allowMerge,
                    AllowSplit = allowSplit
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是 P0-01~P0-06 修复链，非批决策 ⇒ Material 级宽松策略（恒 1 批）。
                //   `AllowMerge` 镜像夹具参数：策略是 Merge 的正式控制源（§八 P1-01），不能覆盖掉 Merge 类断言。
                BatchPolicies = TestBatchPolicy.Permissive(materialIds, allowMerge),
                SetupTransitionRules = setupRules ?? Array.Empty<SetupTransitionRuleSnapshot>()
            }
        };
    }

    // 日历槽临时登记（Res(...) 构造时写入，Build 时取出）
    [ThreadStatic] private static List<ResourceCalendarSlot>? _calendarRegistry;
    private static List<ResourceCalendarSlot> CalendarRegistry => _calendarRegistry ??= new List<ResourceCalendarSlot>();
}
