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
/// P1-02 item1 接线（阶段二）端到端回归：Setup 消费链切换验证（纯内存，直接调 FiniteCapacitySolver）。
/// 断言口径：v1.2 §2/§5/§6/§11/§12 + §1.2/§20.3（RoutingOperation.SetupTime 废止）——
///   EXACT 命中（真实占用：加工开始 = 前任务结束 + Setup）/ DEFAULT 回退 / 无规则=0 /
///   方向性（A→B ≠ B→A）/ 同产品=0 / 初始设备状态=0 / 旧字段不再被读取。
/// 场景骨架：同一资源 R1 上两个单工序 Demand 顺序放置，D2 的 Setup 取决于「D1 产品 → D2 产品」规则。
/// </summary>
public class SetupWiringTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1);

    private readonly FiniteCapacitySolver _solver = new();

    [Fact]
    public async Task EXACT命中_Setup真实占用资源()
    {
        // D1=物料1 先排（初始设备状态 Setup=0），D2=物料2：EXACT(OP10,R1,1→2)=45。
        // 对称补 EXACT(2→1)=45：夜间 FULL 序列优化下换序无严格增益（45↔45）→ 顺序稳定，
        // 断言聚焦 Phase2 规则消费本身（否则优化器会利用「2→1 缺规则=0」合法换序）。
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 0m,
            rules: new[] { Exact("OP10", 1, 1, 2, 45m), Exact("OP10", 1, 2, 1, 45m) });

        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        Assert.Equal(0m, d1.SetupTime);                       // 初始设备状态（无前产品）
        Assert.Equal(45m, d2.SetupTime);                       // EXACT 命中
        Assert.Equal(d1.PlannedEndTime.AddMinutes(45), d2.PlannedStartTime);  // Setup 真实占用：加工在换型后
        Assert.Equal(d1.PlannedEndTime, d2.PlannedStartTime.AddMinutes(-45)); // 占用窗紧邻不重叠
    }

    [Fact]
    public async Task DEFAULT回退_无明确产品对()
    {
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 0m,
            rules: new[] { Default("OP10", 1, 20m) });

        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        Assert.Equal(20m, d2.SetupTime);
    }

    [Fact]
    public async Task EXACT优先于DEFAULT()
    {
        // 对称补 EXACT(2→1)=45 稳定 FULL 序列优化下的顺序（换序无严格增益），聚焦断言 EXACT > DEFAULT 命中优先级
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 0m,
            rules: new[] { Exact("OP10", 1, 1, 2, 45m), Exact("OP10", 1, 2, 1, 45m), Default("OP10", 1, 20m) });

        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        Assert.Equal(45m, d2.SetupTime);
    }

    [Fact]
    public async Task 无规则_零分钟排程照常()
    {
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 0m,
            rules: Array.Empty<SetupTransitionRuleSnapshot>());

        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        Assert.Equal(0m, d2.SetupTime);                        // 规则缺失降级 = 0（+WARNING 追踪，见 B2追踪_ 系列）
        Assert.Equal(d1.PlannedEndTime, d2.PlannedStartTime);  // 紧邻无空转
    }

    [Fact]
    public async Task 方向性_AB与BA不同值()
    {
        // EXACT(1→2)=45、EXACT(2→1)=15；D1=物料2、D2=物料1 → 命中 2→1 = 15
        var result = await Solve(
            d1Material: 2, d2Material: 1,
            legacySetupTime: 0m,
            rules: new[] { Exact("OP10", 1, 1, 2, 45m), Exact("OP10", 1, 2, 1, 15m) });

        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        Assert.Equal(15m, d2.SetupTime);   // 方向性：命中的是 2→1，不是 1→2
    }

    [Fact]
    public async Task 同产品连续_默认零()
    {
        // 同物料两 Demand（AllowMerge=false 各自成 Task）：A→A 无显式规则 = 0（v1.2 §六）
        var result = await Solve(
            d1Material: 1, d2Material: 1,
            legacySetupTime: 0m,
            rules: new[] { Default("OP10", 1, 20m) });   // 即使有 DEFAULT，同产品也不走它

        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        Assert.Equal(0m, d2.SetupTime);
    }

    [Fact]
    public async Task 旧字段废止_RoutingOperationSetupTime不再被读取()
    {
        // v1.2 §1.2/§20.3：旧字段填 999 也不得进入占用/SetupTime——无规则即 0
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 999m,
            rules: Array.Empty<SetupTransitionRuleSnapshot>());

        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        var d2 = result.FinalTasks.Single(t => t.SourceDraftId == "D2");

        Assert.Equal(0m, d1.SetupTime);
        Assert.Equal(0m, d2.SetupTime);
        Assert.Equal(60m, (decimal)(d1.PlannedEndTime - d1.PlannedStartTime).TotalMinutes);  // 加工 60，无 999 占用
        Assert.Equal(d1.PlannedEndTime, d2.PlannedStartTime);
    }

    // ── B.2 求解过程追溯（0号位 §5.1/§5.3；载体 = 2号位 r13494 DomainSolveResult.SolveTraceNote）──
    // 覆盖「Phase2 产出 → ConstraintContext.TraceNotes 收集 → Phase5 导出 → FiniteCapacitySolver 透传」全链，
    // 以及「仅需说明的解析结果产 trace」判据与 Level 值域归一（SetupOptimizer 全大写 → 契约首字母大写）。

    [Fact]
    public async Task B2追踪_初始状态与规则缺失各产一条且Level归一()
    {
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 0m,
            rules: Array.Empty<SetupTransitionRuleSnapshot>());

        // D1 无前产品 → InitialState("INFO")；D2 无规则 → RuleMissing("WARNING")。恰好 2 条。
        Assert.Equal(2, result.SolveTraceNotes.Count);

        var init = result.SolveTraceNotes.Single(n => n.Key == "D1");
        Assert.Equal("INITIAL_SETUP_STATE", init.ReasonCode);
        Assert.Equal("Info", init.Level);                       // 归一："INFO" → "Info"
        Assert.False(string.IsNullOrEmpty(init.Message));

        var missing = result.SolveTraceNotes.Single(n => n.Key == "D2");
        Assert.Equal("SETUP_RULE_MISSING_ZERO_FALLBACK", missing.ReasonCode);
        Assert.Equal("Warning", missing.Level);                 // 归一："WARNING" → "Warning"
        Assert.Contains("OP10", missing.Message);
    }

    [Fact]
    public async Task B2追踪_DEFAULT回退产INFO()
    {
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 0m,
            rules: new[] { Default("OP10", 1, 20m) });

        // DEFAULT 回退属「需说明」（非显式规则命中）→ 产 trace；D1 仍为初始状态。
        var fb = result.SolveTraceNotes.Single(n => n.Key == "D2");
        Assert.Equal("DEFAULT_SETUP_FALLBACK", fb.ReasonCode);
        Assert.Equal("Info", fb.Level);
    }

    [Fact]
    public async Task B2追踪_EXACT命中不产trace()
    {
        var result = await Solve(
            d1Material: 1, d2Material: 2,
            legacySetupTime: 0m,
            rules: new[] { Exact("OP10", 1, 1, 2, 45m), Exact("OP10", 1, 2, 1, 45m) });

        // EXACT 正常命中 ExplanationType 为 null → 不产 trace（10 万 Task 级体积约束，实施包 §19）。
        Assert.DoesNotContain(result.SolveTraceNotes, n => n.Key == "D2");
        // D1 初始状态仍产一条 → 证明「D2 不产」是判据过滤的结果，而非整链未工作。
        Assert.Single(result.SolveTraceNotes);
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private static SetupTransitionRuleSnapshot Exact(string op, int resourceId, int from, int to, decimal minutes)
        => new SetupTransitionRuleSnapshot
        {
            ProductionDepartmentId = 100, StageCode = "STAGE1",
            OperationCode = op, ResourceId = resourceId,
            FromMaterialId = from, ToMaterialId = to,
            RuleType = "EXACT", SetupMinutes = minutes
        };

    private static SetupTransitionRuleSnapshot Default(string op, int resourceId, decimal minutes)
        => new SetupTransitionRuleSnapshot
        {
            ProductionDepartmentId = 100, StageCode = "STAGE1",
            OperationCode = op, ResourceId = resourceId,
            RuleType = "DEFAULT", SetupMinutes = minutes
        };

    /// <summary>
    /// 场景：R1 大日历 [Day, Day+29]；物料 d1Material/d2Material 各一道 OP10（60min/件，qty1），
    /// 均资格于 R1；FORWARD；D1(seq1) 先排、D2(seq2) 紧随——D2 的 Setup 由规则决定。
    /// </summary>
    private Task<DomainSolveResult> Solve(int d1Material, int d2Material, decimal legacySetupTime,
        IReadOnlyList<SetupTransitionRuleSnapshot> rules)
    {
        var materials = new[] { d1Material, d2Material }.Distinct().ToList();

        var demands = new List<LogicalProductionDemand>
        {
            Demand("D1", 1, d1Material, 1),
            Demand("D2", 2, d2Material, 2)
        };

        var ops = materials.Select(m => new RoutingOperation
        {
            MaterialId = m, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
            OperationCode = "OP10", StageCode = "STAGE1",
            StandardDuration = 60m, SetupTime = legacySetupTime   // 旧字段：接线后不得被读取
        }).ToList();

        var els = materials.Select(m => new OperationResourceEligibility
        {
            MaterialId = m, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
            OperationCode = "OP10", ResourceId = 1, Priority = 1, CapacityFactor = 1m
        }).ToList();

        var request = new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = Day,
            PlanningEnd = Day.AddDays(30),
            LogicalProductionDemands = demands,
            RoutingOperations = ops,
            OperationResourceEligibility = els,
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
                new() { ResourceId = 1, Start = Day, End = Day.AddDays(29), IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = false,
                    AllowSplit = false
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是 Setup 接线，非批决策 ⇒ 给一条 Material 级宽松策略（恒 1 批，不改变既有行为）。
                BatchPolicies = TestBatchPolicy.Permissive(demands.Select(d => d.MaterialId), 100),
                SetupTransitionRules = rules
            }
        };

        return _solver.SolveAsync(request);
    }

    private static LogicalProductionDemand Demand(string key, long alloc, int materialId, int seq)
        => new LogicalProductionDemand
        {
            LogicalDemandKey = key, PlanVersionId = 1L, DomainKey = "DOMAIN",
            AllocationSequence = alloc, DemandKey = key, MaterialId = materialId, FactoryId = 1,
            StartStageCode = "STAGE1",
            NetOutputQty = 1m, PlannedProcessQty = 1m,
            RequiredAvailableTime = Day.AddDays(20), DemandSequence = seq
        };
}
