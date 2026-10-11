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
/// P1-02 item1（夜间 FULL）：单 Resource × 生产日窗口 × 固定锚点间可移动段的有界序列优化回归
/// （v1.2 §14-§16，纯内存，直接调 FiniteCapacitySolver）。
/// 覆盖：①有益重排（ΣSetup 严格下降才接受）②履约保护（新增延期即拒绝，§16 第六步）
/// ③白天 Candidate 不做全天重排（§17）④可重放（seeded RNG，同 Run 同结果）
/// ⑤固定锚点切割 + 锚点 Setup 随段末产品重算（§15/§11.1，位置不动）。
/// </summary>
public class SetupSequenceOptimizationTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1);

    private readonly FiniteCapacitySolver _solver = new();

    // ════════════════════════════════════════════════════════════
    // ① 经典有益重排：A(m1) B(m2) C(m1)，m1↔m2=60、同产品=0。
    // Phase2 基线 A,B,C：Σ=0+60+60=120；任何把 m1 对相邻的排列 Σ=60（四个等价最优，
    // rng 取先到者）→ 断言与具体顺序无关的不变量：Σ=60、恰一个 60、占用连续贴窗。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 有益重排_同产品相邻_SigmaSetup严格下降()
    {
        var result = await SolveClassic();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(3, result.FinalTasks.Count);

        // ΣSetup 从 120 降到 60（m1 对相邻，只留一个换型点）
        Assert.Equal(60m, result.FinalTasks.Sum(t => t.SetupTime));
        Assert.Equal(1, result.FinalTasks.Count(t => t.SetupTime == 60m));
        Assert.Equal(2, result.FinalTasks.Count(t => t.SetupTime == 0m));

        // 占用连续紧致：8:00 起、12:00 止（3×60 加工 + 60 换型），全部落在生产日窗口 [8-18] 内
        var occStarts = result.FinalTasks
            .Select(t => t.PlannedStartTime - TimeSpan.FromMinutes((double)t.SetupTime)).ToList();
        var occEnds = result.FinalTasks.Select(t => t.PlannedEndTime).ToList();
        Assert.Equal(Day.AddHours(8), occStarts.Min());
        Assert.Equal(Day.AddHours(12), occEnds.Max());
        Assert.All(occStarts, s => Assert.True(s >= Day.AddHours(8)));
        Assert.All(occEnds, e => Assert.True(e <= Day.AddHours(18)));
        // 无重叠：占用窗总长 = 4 小时 = 窗口跨度
        Assert.Equal(240, occEnds.Max().Subtract(occStarts.Min()).TotalMinutes);
    }

    // ════════════════════════════════════════════════════════════
    // ② 履约保护（§16 第六步）：所有 ΣSetup 更优的排列都会让某个基线 on-time 的 Task 新增延期
    // → 全部拒绝，基线序列原样保持。
    // 场景：A(m1,due 10:00) B(m2,物料 9:30 才可用,due 11:30) C(m1,due 宽松)。
    // 基线：A[8-9]0、B 占用[9:30-11:30]60、C 占用[11:30-13:30]60，Σ=120、零延期。
    // 任何有益重排（m1 对相邻）都把 A 或 B 推过各自交期 → 拒绝。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 履约保护_重排导致新增延期即拒绝()
    {
        var result = await SolveClassic(
            aDue: Day.AddHours(10),      // 基线 end 9:00，on-time
            d2Due: Day.AddHours(11.5),   // 基线 end 11:30，恰好 on-time
            bMaterialAvailableAt: Day.AddHours(9.5));

        Assert.True(result.Success, result.ErrorMessage);
        var a = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        var b = result.FinalTasks.Single(t => t.SourceDraftId == "D2");
        var c = result.FinalTasks.Single(t => t.SourceDraftId == "D3");

        // 基线序列 A,B,C 原样保持（所有有益重排都新增延期 → 拒绝）
        Assert.Equal(Day.AddHours(8), a.PlannedStartTime);
        Assert.Equal(0m, a.SetupTime);
        Assert.Equal(60m, b.SetupTime);
        Assert.Equal(Day.AddHours(10.5), b.PlannedStartTime);
        Assert.Equal(Day.AddHours(11.5), b.PlannedEndTime);      // = due，不晚于
        Assert.Equal(60m, c.SetupTime);
        Assert.Equal(Day.AddHours(12.5), c.PlannedStartTime);
        Assert.Equal(Day.AddHours(13.5), c.PlannedEndTime);
        Assert.Equal(120m, a.SetupTime + b.SetupTime + c.SetupTime);
    }

    // ════════════════════════════════════════════════════════════
    // ③ §17：白天 Candidate 局部优先——即使带 ChangeSeedKeys（未命中任何任务），
    // 也不得做全天序列重排（Phase2 基线保持）。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task Candidate白天_不做全天序列重排()
    {
        var result = await SolveClassic(candidate: new CandidateContext
        {
            BasePlanVersionId = 1,
            ChangeSeedKeys = new[] { "NO-SUCH-SEED" }
        });

        Assert.True(result.Success, result.ErrorMessage);
        var c = result.FinalTasks.Single(t => t.SourceDraftId == "D3");

        // Phase2 基线：C 加工 [12:00-13:00]、Setup 60（未被重排到 9:00）
        Assert.Equal(Day.AddHours(12), c.PlannedStartTime);
        Assert.Equal(60m, c.SetupTime);
        Assert.Equal(120m, result.FinalTasks.Sum(t => t.SetupTime));
    }

    // ════════════════════════════════════════════════════════════
    // ④ 可重放：同一 Run（同 PlanVersionId/ScheduleRunId 种子）两次求解结果逐字段一致
    // （seeded RNG → 同搜索轨迹；Run 重放/审计依赖）。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 可重放_同Run两次求解结果一致()
    {
        var r1 = await SolveClassic();
        var r2 = await SolveClassic();

        Assert.True(r1.Success && r2.Success);
        var snap1 = r1.FinalTasks.OrderBy(t => t.SourceDraftId)
            .Select(t => (t.SourceDraftId, t.PlannedStartTime, t.PlannedEndTime, t.SetupTime, t.ResourceId)).ToList();
        var snap2 = r2.FinalTasks.OrderBy(t => t.SourceDraftId)
            .Select(t => (t.SourceDraftId, t.PlannedStartTime, t.PlannedEndTime, t.SetupTime, t.ResourceId)).ToList();

        Assert.Equal(snap1, snap2);
    }

    // ════════════════════════════════════════════════════════════
    // ⑤ §15：固定锚点（FIRM 锁定 Task）切割可移动段——锚点位置绝不动，
    // 但其 Setup 随段末产品重算回写（§11.1）；段内仍做有益重排。
    // 场景：D1(m1)/D2(m2) 可移动 + L(m9) FIRM 锁定 [14:00-15:00]；
    // 规则 1↔2=60、1→9=10、2→9=25。基线 A,B,L：Σ=0+60+25=85；
    // 换序 B,A,L：Σ=0+60+10=70 → 接受；L 位置不变、SetupTime 25→10。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 固定锚点_切割段且Setup随段末产品重算()
    {
        var rules = new List<SetupTransitionRuleSnapshot>
        {
            Rule(1, 2, 60m), Rule(2, 1, 60m), Rule(1, 9, 10m), Rule(2, 9, 25m)
        };
        var demands = new List<LogicalProductionDemand>
        {
            Demand("D1", 1, 1, 1), Demand("D2", 2, 2, 2), Demand("DL", 9, 9, 3)
        };
        var materials = new[] { 1, 2, 9 };

        var request = Build(demands, materials, rules,
            constraints: new List<ExecutionConstraint>
            {
                new ExecutionConstraint
                {
                    DraftId = "DL", ResourceId = 1,
                    LockedStart = Day.AddHours(14), LockedEnd = Day.AddHours(15),
                    ConstraintType = "FIRM", StageCode = "STAGE1", OperationCode = "OP10",
                    LockedQuantity = 1m, LockedNetOutputQty = 1m, LockedPlannedProcessQty = 1m
                }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");
        var anchor = result.FinalTasks.Single(t => t.SourceDraftId == "DL");

        // 锚点绝不动（FIRM 锁定位置原样）
        Assert.Equal(Day.AddHours(14), anchor.PlannedStartTime);
        Assert.Equal(Day.AddHours(15), anchor.PlannedEndTime);
        // 段内有益重排：D2 先（初始 0）、D1 后（2→1=60）
        Assert.Equal(0m, d2.SetupTime);
        Assert.Equal(Day.AddHours(8), d2.PlannedStartTime);
        Assert.Equal(60m, d1.SetupTime);
        Assert.Equal(Day.AddHours(10), d1.PlannedStartTime);
        // 锚点 Setup 随段末产品（m1）重算：2→9=25 变 1→9=10，位置不变
        Assert.Equal(10m, anchor.SetupTime);
        // Σ = 0 + 60 + 10 = 70（基线 85 → 严格下降）
        Assert.Equal(70m, d1.SetupTime + d2.SetupTime + anchor.SetupTime);
    }

    // ════════════════════════════════════════════════════════════
    // ⑥ P1-02 补充验收（0号位 2026-10-09《APS_V1_3_20261009.md》§三 P1-02）：
    //    「同资源不同窗口、**前后都有固定锚点**、同需求多批混合方向下，Optimization 改变顺序后
    //      始终保持硬约束与 JIT 语义」—— 本用例覆盖前两项几何（第三项见
    //      `Phase5BatchDirectionTests.同需求两批方向分歧_BACKWARD批不提前_FORWARD批仍可优化`）。
    //
    // 场景：同一资源两段**互不相连**的日历窗（中间 12:00–15:00 停机）
    //   W1 = [08:00, 13:00]、W2 = [15:00, 20:00]；
    //   前后各有**固定锚点**（FIRM 锁定、位置绝不动）：A1(m9) [08:00,09:00]、A2(m8) [15:00,16:00]。
    //   中间可移动段 = D2(m2, seq1) / D1(m1, seq2) 各 1 件 60min；Phase2 基线按需求序 ⇒ D2 先、Σ=120。
    //   换型规则：m9→m1=5、m9→m2=60、m1↔m2=60（对称）、→m8 无规则（=0）。
    //     · 基线 D2,D1：0(A1) + 60 + 60 + 0(A2) = 120；
    //     · 换序 D1,D2：0 + 5 + 60 + 0 = 65 ⇒ **严格下降** ⇒ 优化体必须接受换序。
    // 断言 = 「换序确实发生 + 两条硬约束（锚点原位、不跨停机段）+ 交期语义」：
    //   · D1 换到段首（Setup=5、09:05 起）—— 基线形态下会落在 11:00 起（Setup=60）⇒ 本断言**红**；
    //   · A1 / A2 **分毫不动**（前后锚点均是段边界，绝不跨锚点重排）；
    //   · 全部加工窗落在各自可用窗内（中间停机段 [13:00,15:00] 无任何占用）；
    //   · 无新增延期（各需求 end ≤ 交期）。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 同资源两窗口_前后固定锚点_重排后硬约束与锚点不动()
    {
        var rules = new List<SetupTransitionRuleSnapshot>
        {
            Rule(9, 1, 5m), Rule(9, 2, 60m), Rule(1, 2, 60m), Rule(2, 1, 60m)
        };
        var demands = new List<LogicalProductionDemand>
        {
            Demand("D2", 1, 2, 1),      // 需求序在 D1 之前 ⇒ Phase2 基线 D2 先（换型 60）
            Demand("D1", 2, 1, 2),      // 换到段首后换型仅 5
            Demand("A1", 3, 9, 3),      // 段前固定锚点（锁在 W1 头部）
            Demand("A2", 4, 8, 4)       // 段后固定锚点（锁在 W2 头部）
        };

        var request = Build(demands, new[] { 1, 2, 9, 8 }, rules,
            constraints: new List<ExecutionConstraint>
            {
                new ExecutionConstraint
                {
                    DraftId = "A1", ResourceId = 1,
                    LockedStart = Day.AddHours(8), LockedEnd = Day.AddHours(9),
                    ConstraintType = "FIRM", StageCode = "STAGE1", OperationCode = "OP10",
                    LockedQuantity = 1m, LockedNetOutputQty = 1m, LockedPlannedProcessQty = 1m
                },
                new ExecutionConstraint
                {
                    DraftId = "A2", ResourceId = 1,
                    LockedStart = Day.AddHours(15), LockedEnd = Day.AddHours(16),
                    ConstraintType = "FIRM", StageCode = "STAGE1", OperationCode = "OP10",
                    LockedQuantity = 1m, LockedNetOutputQty = 1m, LockedPlannedProcessQty = 1m
                }
            },
            calendarWindows: new[]
            {
                (Day.AddHours(8), Day.AddHours(13)),    // W1
                (Day.AddHours(15), Day.AddHours(20))    // W2（与 W1 之间有 12:00–15:00 停机）
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(4, result.FinalTasks.Count);

        var a1 = result.FinalTasks.Single(t => t.SourceDraftId == "A1");
        var a2 = result.FinalTasks.Single(t => t.SourceDraftId == "A2");
        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        // ① 换序**确实发生**：D1 换到段首（紧随 A1）、换型仅 5 ⇒ 09:05 起。
        //   基线形态（D2 先）下 D1 会在 11:00 才起、换型 60 ⇒ 本断言**红**。
        Assert.Equal(5m, d1.SetupTime);
        Assert.Equal(Day.AddHours(9).AddMinutes(5), d1.PlannedStartTime);
        Assert.Equal(Day.AddHours(10).AddMinutes(5), d1.PlannedEndTime);
        Assert.Equal(60m, d2.SetupTime);
        Assert.Equal(Day.AddHours(11).AddMinutes(5), d2.PlannedStartTime);
        Assert.Equal(Day.AddHours(12).AddMinutes(5), d2.PlannedEndTime);
        Assert.Equal(65m, result.FinalTasks.Sum(t => t.SetupTime));

        // ② 前后固定锚点**分毫不动**（段边界锚点绝不跨段重排）。
        Assert.Equal(Day.AddHours(8), a1.PlannedStartTime);
        Assert.Equal(Day.AddHours(9), a1.PlannedEndTime);
        Assert.Equal(Day.AddHours(15), a2.PlannedStartTime);
        Assert.Equal(Day.AddHours(16), a2.PlannedEndTime);

        // ③ 硬约束：全部加工窗落在**各自可用窗**内，中间停机段无任何占用。
        var gapStart = Day.AddHours(13);
        var gapEnd = Day.AddHours(15);
        Assert.All(result.FinalTasks, t =>
        {
            Assert.False(t.PlannedStartTime < gapEnd && t.PlannedEndTime > gapStart,
                $"{t.SourceDraftId} 占用跨越停机段 [{gapStart:HH:mm}-{gapEnd:HH:mm}]");
        });
        Assert.All(result.FinalTasks.Where(t => t.SourceDraftId is "D1" or "D2"),
            t => Assert.True(t.PlannedEndTime <= Day.AddHours(13), $"{t.SourceDraftId} 越出 W1"));

        // ④ 交期语义：换序不得引入新增延期。
        Assert.All(result.FinalTasks, t => Assert.True(t.PlannedEndTime <= Day.AddDays(20)));
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

    private static LogicalProductionDemand Demand(string key, long alloc, int materialId, int seq, DateTime? due = null)
        => new LogicalProductionDemand
        {
            LogicalDemandKey = key, PlanVersionId = 1L, DomainKey = "DOMAIN",
            AllocationSequence = alloc, DemandKey = key, MaterialId = materialId, FactoryId = 1,
            StartStageCode = "STAGE1",
            NetOutputQty = 1m, PlannedProcessQty = 1m,
            RequiredAvailableTime = due ?? Day.AddDays(20), DemandSequence = seq
        };

    /// <summary>经典场景：R1 [8:00-18:00]，A(m1)/B(m2)/C(m1) 各 60min，m1↔m2=60。</summary>
    private Task<DomainSolveResult> SolveClassic(
        DateTime? aDue = null, DateTime? d2Due = null, DateTime? bMaterialAvailableAt = null,
        CandidateContext? candidate = null)
    {
        var demands = new List<LogicalProductionDemand>
        {
            Demand("D1", 1, 1, 1, aDue),
            Demand("D2", 2, 2, 2, d2Due),
            Demand("D3", 3, 1, 3)
        };
        var rules = new List<SetupTransitionRuleSnapshot> { Rule(1, 2, 60m), Rule(2, 1, 60m) };
        List<MaterialAvailabilitySlice>? slices = null;
        if (bMaterialAvailableAt.HasValue)
        {
            slices = new List<MaterialAvailabilitySlice>
            {
                new MaterialAvailabilitySlice
                {
                    AllocationSequence = 2, MaterialId = 2, FactoryId = 1,
                    Quantity = 1m, AvailableTime = bMaterialAvailableAt.Value
                }
            };
        }
        return BuildAndSolve(demands, new[] { 1, 2 }, rules, candidate, slices);
    }

    private async Task<DomainSolveResult> BuildAndSolve(
        List<LogicalProductionDemand> demands, int[] materials,
        List<SetupTransitionRuleSnapshot> rules, CandidateContext? candidate,
        List<MaterialAvailabilitySlice>? slices = null)
        => await _solver.SolveAsync(Build(demands, materials, rules, candidate: candidate, materialSlices: slices));

    private static DomainSolveRequest Build(
        List<LogicalProductionDemand> demands,
        int[] materials,
        List<SetupTransitionRuleSnapshot> rules,
        CandidateContext? candidate = null,
        List<ExecutionConstraint>? constraints = null,
        List<MaterialAvailabilitySlice>? materialSlices = null,
        IReadOnlyList<(DateTime Start, DateTime End)>? calendarWindows = null)
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
            CalendarSlots = (calendarWindows ?? new[] { (Day.AddHours(8), Day.AddHours(18)) })
                .Select(w => new ResourceCalendarSlot
                {
                    ResourceId = 1, Start = w.Start, End = w.End, IsAvailable = true
                })
                .ToList(),
            ExecutionConstraints = constraints ?? new List<ExecutionConstraint>(),
            MaterialConstraints = materialSlices ?? new List<MaterialAvailabilitySlice>(),
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
                //   本夹具验证的是 Setup 序列优化，非批决策 ⇒ Material 级宽松策略（恒 1 批）。
                BatchPolicies = TestBatchPolicy.Permissive(materials, 100),
                SetupTransitionRules = rules
            }
        };
    }
}
