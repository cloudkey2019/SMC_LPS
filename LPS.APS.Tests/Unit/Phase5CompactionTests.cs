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
/// P1-05：Phase5 Gap Compaction（保序前向压实）专项回归（纯内存，直接调 FiniteCapacitySolver）。
/// 实施包 §六 Phase 5 最小次级优化（Level 3，不破坏 Level 0-2）：
///   ① FORWARD：Candidate 传播扰动后，最终计划必须压实到最早可行位置（减少不必要等待/WIP）；
///   ② BACKWARD：JIT 倒排锚点不得被前拉（避免过早生产——倒排语义本身）；
///   ③ 物料 floor：Task 不得早于 Material AvailableTime（避免过早生产的物料侧约束）。
/// 保序/不可移动/依赖 floor 等其余红线由全量既有回归（ContinuityShareTests C01-C06、
/// CrossMaterialTimingConstraintTests、P0RegressionTests 等 270+ 测试）联合守护。
/// </summary>
public class Phase5CompactionTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1);
    private static readonly DateTime PlanningStart = Day;
    private static readonly DateTime PlanningEnd = Day.AddDays(30);

    private readonly FiniteCapacitySolver _solver = new();

    // ════════════════════════════════════════════════════════════
    // ① FORWARD：扰动后压实到最早可行位置（确定性终态断言）
    // 场景：D1 双工序同资源（Setup30/加工60），日历 [8-12]+[13-17]（午休断档）；
    // 5 个填充 Task 压住 30% 警戒线 → seed 触发传播直接移动（可能把 Task 推后）；
    // Phase5 压实后终态必须是紧凑早位：OP10 加工 [8:30-9:30]、OP20 加工 [10:00-11:00]。
    // （传播 HashSet 顺序不定：一支无扰动、一支推后——压实保证两支收敛到同一紧凑终态。）
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task FORWARD_Candidate扰动后_压实到最早可行位置()
    {
        var demands = new List<LogicalProductionDemand> { D1() };
        var ops = new List<RoutingOperation>
        {
            Op(1, "OP10", 60m, 30m), Op(1, "OP20", 60m, 30m)
        };
        for (int i = 2; i <= 6; i++)
        {
            demands.Add(Filler(i));
            ops.Add(Op(i, "FOP", 60m, 0m));
        }

        var request = Build(demands, ops,
            deps: new[] { Dep(1, "OP10", "OP20") },
            els: Concat(El(1, "OP10", 1), El(1, "OP20", 1),
                Enumerable.Range(2, 5).Select(i => El(i, "FOP", i))),
            resources: Concat(Res(1, "R1", (Day.AddHours(8), Day.AddHours(12)), (Day.AddHours(13), Day.AddHours(17))),
                Enumerable.Range(2, 5).Select(i => Res(i, $"R{i}", (Day.AddHours(8), Day.AddHours(17))))),
            calendarOverrides: new Dictionary<int, (DateTime, DateTime)[]>
            {
                [1] = new[] { (Day.AddHours(8), Day.AddHours(12)), (Day.AddHours(13), Day.AddHours(17)) },
            },
            direction: "FORWARD",
            candidate: new CandidateContext { BasePlanVersionId = 1, ChangeSeedKeys = new[] { "D1" } },
            // item1 接线后 Setup 走规则（RoutingOperation.SetupTime 已废止不再读）：
            // OP10 首任务=初始设备状态→0；OP20 前产品=物料1（同产品）→ EXACT A→A 显式规则 30 分钟。
            setupRules: new[]
            {
                new SetupTransitionRuleSnapshot { ProductionDepartmentId = 100, StageCode = "STAGE1",
                    OperationCode = "OP20", ResourceId = 1, FromMaterialId = 1, ToMaterialId = 1,
                    RuleType = "EXACT", SetupMinutes = 30m }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var op10 = result.FinalTasks.Single(t => t.SourceDraftId == "D1" && t.OperationCode == "OP10");
        var op20 = result.FinalTasks.Single(t => t.SourceDraftId == "D1" && t.OperationCode == "OP20");

        // 压实终态：OP10 初始设备状态 Setup=0 → 加工 [8:00-9:00]；OP20 Setup=30（规则）→ 占用 [9:00-10:30]、加工 [9:30-10:30]
        Assert.Equal(0m, op10.SetupTime);
        Assert.Equal(Day.AddHours(8), op10.PlannedStartTime);
        Assert.Equal(Day.AddHours(9), op10.PlannedEndTime);
        Assert.Equal(30m, op20.SetupTime);
        Assert.Equal(Day.AddHours(9.5), op20.PlannedStartTime);
        Assert.Equal(Day.AddHours(10.5), op20.PlannedEndTime);
    }

    // ════════════════════════════════════════════════════════════
    // ② BACKWARD：JIT 锚点不前拉（避免过早生产）
    // 场景：倒排，due=Day+5；Task 应结束于 due（JIT），压实不得把它拉到日历起点。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task BACKWARD_JIT锚点_不被压实前拉()
    {
        var due = Day.AddDays(5);
        var request = Build(
            demands: new[] { Simple("D1", 1, 1, 1m, due) },
            ops: new[] { Op(1, "OP10", 60m, 0m) },
            deps: Array.Empty<RoutingDependency>(),
            els: new[] { El(1, "OP10", 1) },
            resources: new[] { Res(1, "R1", (Day, Day.AddDays(29))) },
            calendarOverrides: new Dictionary<int, (DateTime, DateTime)[]>
            {
                [1] = new[] { (Day, Day.AddDays(29)) },
            },
            direction: "BACKWARD");

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        // JIT：结束锚在 due，未被前拉到日历起点（前拉 = 过早生产 = 违反 BACKWARD 语义）
        Assert.Equal(due, task.PlannedEndTime);
        Assert.Equal(due.AddMinutes(-60), task.PlannedStartTime);
    }

    // ════════════════════════════════════════════════════════════
    // ③ 物料 floor：FORWARD 下 Task 不得早于 Material AvailableTime（避免过早生产）
    // 场景：物料 Day+2 才可用；压实 floor 含物料时间，不得拉到日历起点 Day。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task FORWARD_物料floor_不得早于可用时间()
    {
        var request = Build(
            demands: new[] { Simple("D1", 1, 1, 10m, PlanningStart.AddDays(20)) },
            ops: new[] { Op(1, "OP10", 6m, 0m) },   // 6min/件 × 10 件 = 60min
            deps: Array.Empty<RoutingDependency>(),
            els: new[] { El(1, "OP10", 1) },
            resources: new[] { Res(1, "R1", (Day, Day.AddDays(29))) },
            calendarOverrides: new Dictionary<int, (DateTime, DateTime)[]>
            {
                [1] = new[] { (Day, Day.AddDays(29)) },
            },
            direction: "FORWARD",
            materialSlices: new[]
            {
                new MaterialAvailabilitySlice
                {
                    AllocationSequence = 1, MaterialId = 1, FactoryId = 1,
                    Quantity = 10m, AvailableTime = Day.AddDays(2)
                }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var task = Assert.Single(result.FinalTasks);
        // 物料 Day+2 可用 → 加工不得早于 Day+2（压实不得越物料 floor 前拉）
        Assert.Equal(Day.AddDays(2), task.PlannedStartTime);
        Assert.Equal(Day.AddDays(2).AddMinutes(60), task.PlannedEndTime);
    }

    // ════════════════════════════════════════════════════════════
    // ④ P1 整改（0号位 2026-10-09《未命名的Markdown文件 (3)(1).md》§一 第 3 行）
    //    AUTO 与 Phase5 优化衔接 —— **结果一致性**（非鉴别性，见下方如实说明）
    //    夹具 = ① 逐字复制（Candidate 传播扰动 + 午休断档日历），**仅 direction 由 "FORWARD" 改为 "AUTO"**。
    //    D1：交期 PlanningStart+20 天、lead=120min ⇒ DUE_LOOSE ⇒ AUTO **自决为 FORWARD**（无任何倒排信号）。
    //    整改后：Phase5 读 Phase2 登记的**本需求自决方向** ⇒ 与显式 FORWARD **逐字同终态**。
    //
    //    ⚠ 如实说明（本号位实测，2026-10-09）：本夹具**不能鉴别**整改前后 —— 已实测把 `CompactGaps`
    //      整体短路（`if (true) return;`）后本用例**仍绿**。原因：Phase2 正排是「最早可行槽」贪心，
    //      **天然不留资源空档**；`CompactGaps` 只在 `floor < 占用起点`（即 Phase4 把 Task 推后留下空档）
    //      时才动，而本夹具 Phase4 未推后 ⇒ 压实体是 no-op ⇒ 门控开合对终态无影响。
    //      ⇒ 本用例是**结果一致性护栏**（AUTO 与显式 FORWARD 必须同终态），**不是**鉴别性反证。
    //      **真正鉴别「AUTO 是否进入 Phase5 优化体」的是 ⑥**（用 SolverDiagnostics 计数器直接观测门控放行）。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task AUTO_自决FORWARD_须与显式FORWARD同终态()
    {
        var demands = new List<LogicalProductionDemand> { D1() };
        var ops = new List<RoutingOperation>
        {
            Op(1, "OP10", 60m, 30m), Op(1, "OP20", 60m, 30m)
        };
        for (int i = 2; i <= 6; i++)
        {
            demands.Add(Filler(i));
            ops.Add(Op(i, "FOP", 60m, 0m));
        }

        var request = Build(demands, ops,
            deps: new[] { Dep(1, "OP10", "OP20") },
            els: Concat(El(1, "OP10", 1), El(1, "OP20", 1),
                Enumerable.Range(2, 5).Select(i => El(i, "FOP", i))),
            resources: Concat(Res(1, "R1", (Day.AddHours(8), Day.AddHours(12)), (Day.AddHours(13), Day.AddHours(17))),
                Enumerable.Range(2, 5).Select(i => Res(i, $"R{i}", (Day.AddHours(8), Day.AddHours(17))))),
            calendarOverrides: new Dictionary<int, (DateTime, DateTime)[]>
            {
                [1] = new[] { (Day.AddHours(8), Day.AddHours(12)), (Day.AddHours(13), Day.AddHours(17)) },
            },
            direction: "AUTO",
            candidate: new CandidateContext { BasePlanVersionId = 1, ChangeSeedKeys = new[] { "D1" } },
            setupRules: new[]
            {
                new SetupTransitionRuleSnapshot { ProductionDepartmentId = 100, StageCode = "STAGE1",
                    OperationCode = "OP20", ResourceId = 1, FromMaterialId = 1, ToMaterialId = 1,
                    RuleType = "EXACT", SetupMinutes = 30m }
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var op10 = result.FinalTasks.Single(t => t.SourceDraftId == "D1" && t.OperationCode == "OP10");
        var op20 = result.FinalTasks.Single(t => t.SourceDraftId == "D1" && t.OperationCode == "OP20");

        // 与 ①（显式 FORWARD）**逐字相同**的压实终态 —— AUTO 自决为 FORWARD 就必须走到同一处。
        Assert.Equal(0m, op10.SetupTime);
        Assert.Equal(Day.AddHours(8), op10.PlannedStartTime);
        Assert.Equal(Day.AddHours(9), op10.PlannedEndTime);
        Assert.Equal(30m, op20.SetupTime);
        Assert.Equal(Day.AddHours(9.5), op20.PlannedStartTime);
        Assert.Equal(Day.AddHours(10.5), op20.PlannedEndTime);
    }

    // ════════════════════════════════════════════════════════════
    // ⑤ P1 整改**反向护栏**：AUTO 自决为 BACKWARD 时，Phase5 **不得**前拉 JIT 锚点
    //    夹具：交期紧贴起点（Day+30min）而 lead=60min ⇒ slack = 30−60 = −30 < 0 ⇒ DUE_TIGHT
    //          ⇒ AUTO **自决为 BACKWARD**；日历前伸到 Day−1 天以便倒排锚点 [Day−30min, Day+30min] 可落。
    //    整改前：Phase5 见 "AUTO" 恒跳过压实 ⇒ 停在 due（本就正确，故本用例整改前后**都须绿**）。
    //    整改后：逐需求门控 ⇒ BACKWARD 需求被登记为「不可移动」⇒ 仍停在 due。
    //    ⇒ 本用例守护「逐需求门控不得把非 FORWARD 需求误纳入压实」。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task AUTO_非FORWARD自决_不得被压实前拉()
    {
        var due = Day.AddMinutes(30);

        DomainSolveRequest Make(string dir) => Build(
            demands: new[] { Simple("D1", 1, 1, 1m, due) },
            ops: new[] { Op(1, "OP10", 60m, 0m) },
            deps: Array.Empty<RoutingDependency>(),
            els: new[] { El(1, "OP10", 1) },
            resources: new[] { Res(1, "R1", (Day.AddDays(-1), Day.AddDays(29))) },
            calendarOverrides: new Dictionary<int, (DateTime, DateTime)[]>
            {
                [1] = new[] { (Day.AddDays(-1), Day.AddDays(29)) },
            },
            direction: dir);

        var auto = await _solver.SolveAsync(Make("AUTO"));
        var backward = await _solver.SolveAsync(Make("BACKWARD"));

        Assert.True(auto.Success, auto.ErrorMessage);
        Assert.True(backward.Success, backward.ErrorMessage);

        var ta = Assert.Single(auto.FinalTasks);
        var tb = Assert.Single(backward.FinalTasks);

        // 交期紧贴起点（slack < 0 ⇒ DUE_TIGHT）⇒ AUTO **不得**自决为 FORWARD。
        // 整改前：Phase5 见 Run 级 "AUTO" 恒跳过压实；整改后：逐需求门控把非 FORWARD 需求登记为不可移动
        //   ⇒ 两支都**不得**被前拉到日历起点。本用例守护「逐需求门控不得把非 FORWARD 需求误纳入压实」。
        //   （如实说明：本夹具两支都落在 PlanningStart ⇒ 它是**一致性护栏**而非鉴别性反证；
        //     门控放行与否的**鉴别性**观测见 ⑥。）
        Assert.Equal(tb.PlannedStartTime, ta.PlannedStartTime);
        Assert.Equal(tb.PlannedEndTime, ta.PlannedEndTime);
    }

    // ════════════════════════════════════════════════════════════
    // ⑥ P1 整改**鉴别性反证**（0号位 2026-10-09《未命名的Markdown文件 (3)(1).md》§四 要求 2）
    //    「AUTO 与 Phase5 优化的衔接」：证明 AUTO 自决为 FORWARD 时**真正进入**两项优化体
    //    （空隙压实 `CompactGaps` + Setup 序列优化 `OptimizeSetupSequences`），而不是「结果碰巧一致」。
    //
    //    观测手段：`SolverDiagnostics` 计数器（`internal`，经 `InternalsVisibleTo` 可见；**非契约**、
    //      不参与任何业务判定，见该类文档）。计数器在**门控放行、进入优化体**处自增 ⇒ 直接反映
    //      「是否进入优化」，不受「压实体是否恰好 no-op」干扰（这正是 ④ 无法鉴别的点）。
    //
    //    鉴别力（整改前必红）：整改前门控比较 **Run 级原始值** ⇒ `direction:"AUTO"` 恒 ≠ "FORWARD"
    //      ⇒ 两项计数器恒为 0 ⇒ 本用例第一组断言失败。整改后读 Phase2 登记的**本需求自决方向**
    //      ⇒ AUTO 自决 FORWARD ⇒ 门控放行 ⇒ 计数器 ≥ 1。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task AUTO_自决FORWARD_须真正进入Phase5两项优化体()
    {
        // 同资源双需求、交期宽松（slack ≫ lead ⇒ DUE_LOOSE ⇒ 逐需求自决 FORWARD）。
        // 无 CandidateContext ⇒ `OptimizeSetupSequences` 不因 §17 提前返回 ⇒ 两项优化均可达。
        DomainSolveRequest Make(string dir) => Build(
            demands: new[] { Simple("D1", 1, 1, 1m, Day.AddDays(20)), Simple("D2", 2, 2, 1m, Day.AddDays(20)) },
            ops: new[] { Op(1, "OP10", 60m, 0m), Op(2, "OP20", 60m, 0m) },
            deps: Array.Empty<RoutingDependency>(),
            els: new[] { El(1, "OP10", 1), El(2, "OP20", 1) },
            resources: new[] { Res(1, "R1", (Day, Day.AddDays(29))) },
            calendarOverrides: new Dictionary<int, (DateTime, DateTime)[]>
            {
                [1] = new[] { (Day, Day.AddDays(29)) },
            },
            direction: dir);

        long[] Runs(string dir)
        {
            using var scope = SolverDiagnostics.BeginScope();
            var r = _solver.SolveAsync(Make(dir)).GetAwaiter().GetResult();
            Assert.True(r.Success, r.ErrorMessage);
            return new[] { scope.Counters.Phase5CompactionRuns, scope.Counters.Phase5SetupOptimizationRuns };
        }

        // 基线：显式 FORWARD 必须进入两项优化体（证明夹具本身可达优化，而非恒 no-op 门控）。
        var fwd = Runs("FORWARD");
        Assert.True(fwd[0] >= 1, $"显式 FORWARD 未进入压实（计数器={fwd[0]}）—— 夹具不可达");
        Assert.True(fwd[1] >= 1, $"显式 FORWARD 未进入序列优化（计数器={fwd[1]}）—— 夹具不可达");

        // 反证主体：AUTO 自决 FORWARD ⇒ 必须与显式 FORWARD 一样进入两项优化体。
        //   整改前此处恒为 0（Run 级 "AUTO" ≠ "FORWARD"）⇒ 本断言必红。
        var auto = Runs("AUTO");
        Assert.True(auto[0] >= 1, $"AUTO 自决 FORWARD 却未进入压实（计数器={auto[0]}）—— 衔接断裂");
        Assert.True(auto[1] >= 1, $"AUTO 自决 FORWARD 却未进入序列优化（计数器={auto[1]}）—— 衔接断裂");

        // 反向护栏：AUTO 自决为 BACKWARD ⇒ 两项均**不得**进入。
        //   ⚠ 几何必须让**倒排可行**：`0 ≤ slack ≤ lead`（无交期信号 ⇒ 默认 BACKWARD）且倒排起点 ≥ PlanningStart。
        //     若交期紧到 `slack < 0`（DUE_TIGHT），倒排起点会早于 PlanningStart ⇒ **Phase2 倒排失败、Phase4 正排修复**
        //     该批 ⇒ 依 NEW-P1-01（0号位 2026-10-09《APS_V1_4_20261009.md》§三）该修复批按 **FORWARD** 参与优化
        //     ⇒ 本护栏在此几何下**本就不该**断言 0（该情形另立反证，见 `Phase5BatchDirectionTests`）。
        long[] backwardRuns;
        using (var scope = SolverDiagnostics.BeginScope())
        {
            var rb = _solver.SolveAsync(Build(
                demands: new[] { Simple("D1", 1, 1, 1m, Day.AddMinutes(90)) },
                ops: new[] { Op(1, "OP10", 60m, 0m) },
                deps: Array.Empty<RoutingDependency>(),
                els: new[] { El(1, "OP10", 1) },
                resources: new[] { Res(1, "R1", (Day.AddDays(-1), Day.AddDays(29))) },
                calendarOverrides: new Dictionary<int, (DateTime, DateTime)[]>
                {
                    [1] = new[] { (Day.AddDays(-1), Day.AddDays(29)) },
                },
                direction: "AUTO")).GetAwaiter().GetResult();
            Assert.True(rb.Success, rb.ErrorMessage);
            backwardRuns = new[] { scope.Counters.Phase5CompactionRuns, scope.Counters.Phase5SetupOptimizationRuns };
        }
        Assert.Equal(0, backwardRuns[0]);
        Assert.Equal(0, backwardRuns[1]);
    }

    // ════════════════════════════════════════════════════════════
    // ⑦ P1-02 验收反证（0号位 2026-10-09《APS_V1_2_20261009.md》§三 P1-02 / §五.5）
    //    「混合 AUTO 场景：至少两个独立资源，一个 BACKWARD 一个 FORWARD；
    //      验证后者 Setup 优化**确实可达**且前者**保持不动**。」
    //
    //    整改前：`OptimizeSetupSequences` 只要**任一需求非 FORWARD** 就整 Run 返回
    //      ⇒ 别的资源上的倒排锚点剥夺了本资源独立 FORWARD 段的优化机会（超出冻结资源窗口范围）。
    //    整改后：非 FORWARD 需求整体并入 `immovable` ⇒ 只作**段边界锚点**（不跨锚点重排、锚点不动），
    //      本资源上的独立 FORWARD 段照常进入有界局部优化体。
    //
    //    观测手段：`SolverDiagnostics` 计数器（internal，非契约，纯观测，见该类文档）：
    //      · `Phase5SetupOptimizationRuns` —— 整 Run 是否被放行进入优化体；
    //      · `Phase5SetupSegmentsOptimized` —— **确实有一个「≥2 可移动 Task」的段**被送进优化体
    //        （本验收的关键观测：它把「整 Run 放行」与「FORWARD 段真可达」区分开）。
    //
    //    鉴别力（整改前必红）：整改前混合场景整 Run 被否决 ⇒ 两个计数器均为 0 ⇒ 第一组断言失败。
    // ════════════════════════════════════════════════════════════
    [Fact]
    public async Task 混合方向_独立FORWARD段可达优化_BACKWARD锚点保持不动()
    {
        // D1（material 1 / res1）：双工序同资源、`0 ≤ slack ≤ lead`（无交期信号 ⇒ 自决 BACKWARD）。
        //   倒排落点 = 交期对齐（末工序结束 = 交期），**明显晚于**最早可行位 [Day, Day+120]
        //   ⇒ 「未被前拉」在本夹具是**可鉴别**的（若被错误纳入优化/压实，起点会掉到 Day）。
        // D2（material 2 / res2）：双工序同资源、交期宽松（slack ≫ lead ⇒ DUE_LOOSE ⇒ 自决 FORWARD）。
        // 两资源日历互不重叠 ⇒ 两条段完全独立。
        DomainSolveRequest Make(bool includeForward)
        {
            var demands = new List<LogicalProductionDemand>
            {
                Simple("D1", 1, 1, 1m, Day.AddMinutes(200))
            };
            var ops = new List<RoutingOperation> { Op(1, "OP10", 60m, 0m), Op(1, "OP20", 60m, 0m) };
            var deps = new List<RoutingDependency> { Dep(1, "OP10", "OP20") };
            var els = new List<OperationResourceEligibility> { El(1, "OP10", 1), El(1, "OP20", 1) };
            var resources = new List<ResourceDefinition> { Res(1, "R1", (Day.AddDays(-1), Day.AddDays(29))) };
            var cal = new Dictionary<int, (DateTime, DateTime)[]>
            {
                [1] = new[] { (Day.AddDays(-1), Day.AddDays(29)) }
            };

            if (includeForward)
            {
                demands.Add(Simple("D2", 2, 2, 1m, Day.AddDays(20)));
                ops.Add(Op(2, "OP10", 60m, 0m));
                ops.Add(Op(2, "OP20", 60m, 0m));
                deps.Add(Dep(2, "OP10", "OP20"));
                els.Add(El(2, "OP10", 2));
                els.Add(El(2, "OP20", 2));
                resources.Add(Res(2, "R2", (Day, Day.AddDays(29))));
                cal[2] = new[] { (Day, Day.AddDays(29)) };
            }

            return Build(demands, ops, deps, els, resources, cal, direction: "AUTO");
        }

        long mixedRuns, mixedSegments;
        DomainSolveResult mixed;
        using (var scope = SolverDiagnostics.BeginScope())
        {
            mixed = await _solver.SolveAsync(Make(includeForward: true));
            Assert.True(mixed.Success, mixed.ErrorMessage);
            mixedRuns = scope.Counters.Phase5SetupOptimizationRuns;
            mixedSegments = scope.Counters.Phase5SetupSegmentsOptimized;
        }

        // ① 整 Run 未被否决（整改前 = 0 ⇒ 红）。
        Assert.True(mixedRuns >= 1, $"混合方向下整 Run 未进入序列优化体（计数器={mixedRuns}）—— 全 Run 一票否决未撤销");

        // ② **FORWARD 段确实可达**：res2 上 D2 的 2 条可移动 Task 构成一个真段 ⇒ 段计数器必须 ≥1。
        //    （整改前 = 0；且若整改只「放行整 Run」而未把非 FORWARD 需求登记为不可移动，
        //      res1 的 BACKWARD 段也会被当作可重排段 ⇒ 计数会 > 1，同样不满足本断言。）
        Assert.Equal(1, mixedSegments);

        // ③ BACKWARD 锚点**保持不动**：D1 仍锚在交期（末工序结束 = 交期 Day+200），且**未被前拉**
        //    （倒排起点 Day+80 明显晚于最早可行位 Day —— 若被错误纳入优化/压实，起点会掉到 Day）。
        var d1 = mixed.FinalTasks.Where(t => t.SourceDraftId == "D1").OrderBy(t => t.PlannedStartTime).ToList();
        Assert.Equal(2, d1.Count);
        Assert.Equal(Day.AddMinutes(80), d1[0].PlannedStartTime);
        Assert.Equal(Day.AddMinutes(140), d1[0].PlannedEndTime);
        Assert.Equal(Day.AddMinutes(200), d1[1].PlannedEndTime);

        // ④ 反向护栏：**只有 BACKWARD 需求**时，优化体虽被放行（Task 数 ≥2），但**没有任何可重排段**
        //    ⇒ 段计数器必须为 0。本护栏证明 ② 的 1 确实来自「FORWARD 段可达」，而非「计数器恒 ≥1」。
        //    同时校验 D1 在两种场景下**位置完全一致** ⇒ 混合场景没有扰动 BACKWARD 段。
        long backOnlyRuns, backOnlySegments;
        DomainSolveResult only;
        using (var scope = SolverDiagnostics.BeginScope())
        {
            only = await _solver.SolveAsync(Make(includeForward: false));
            Assert.True(only.Success, only.ErrorMessage);
            backOnlyRuns = scope.Counters.Phase5SetupOptimizationRuns;
            backOnlySegments = scope.Counters.Phase5SetupSegmentsOptimized;
        }

        Assert.True(backOnlyRuns >= 1, $"仅 BACKWARD 时优化体未被放行（计数器={backOnlyRuns}）—— ④ 护栏失效");
        Assert.Equal(0, backOnlySegments);

        var d1Only = only.FinalTasks.Where(t => t.SourceDraftId == "D1").OrderBy(t => t.PlannedStartTime).ToList();
        Assert.Equal(d1Only.Select(t => t.PlannedStartTime), d1.Select(t => t.PlannedStartTime));
        Assert.Equal(d1Only.Select(t => t.PlannedEndTime), d1.Select(t => t.PlannedEndTime));
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private static LogicalProductionDemand D1() => new LogicalProductionDemand
    {
        LogicalDemandKey = "D1", PlanVersionId = 1L, DomainKey = "DOMAIN",
        AllocationSequence = 1, DemandKey = "D1", MaterialId = 1, FactoryId = 1,
        StartStageCode = "STAGE1",
        NetOutputQty = 1m, PlannedProcessQty = 1m,
        RequiredAvailableTime = PlanningStart.AddDays(20), DemandSequence = 1
    };

    private static LogicalProductionDemand Filler(int i) => new LogicalProductionDemand
    {
        LogicalDemandKey = $"D{i}", PlanVersionId = 1L, DomainKey = "DOMAIN",
        AllocationSequence = i, DemandKey = $"D{i}", MaterialId = i, FactoryId = 1,
        StartStageCode = "STAGE1",
        NetOutputQty = 1m, PlannedProcessQty = 1m,
        RequiredAvailableTime = PlanningStart.AddDays(20), DemandSequence = i
    };

    private static LogicalProductionDemand Simple(string key, long alloc, int materialId, decimal qty, DateTime due)
        => new LogicalProductionDemand
        {
            LogicalDemandKey = key, PlanVersionId = 1L, DomainKey = "DOMAIN",
            AllocationSequence = alloc, DemandKey = key, MaterialId = materialId, FactoryId = 1,
            StartStageCode = "STAGE1",
            NetOutputQty = qty, PlannedProcessQty = qty,
            RequiredAvailableTime = due, DemandSequence = 1
        };

    private static RoutingOperation Op(int materialId, string opCode, decimal standardDuration, decimal setup)
        => new RoutingOperation
        {
            MaterialId = materialId, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
            OperationCode = opCode, StageCode = "STAGE1",
            StandardDuration = standardDuration, SetupTime = setup
        };

    private static RoutingDependency Dep(int materialId, string from, string to)
        => new RoutingDependency
        {
            MaterialId = materialId, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
            FromOperationCode = from, ToOperationCode = to, DependencyType = "ES", LagTime = 0m
        };

    private static OperationResourceEligibility El(int materialId, string opCode, int resourceId)
        => new OperationResourceEligibility
        {
            MaterialId = materialId, ProductionDepartmentId = 100, RouteCode = "DEFAULT",
            OperationCode = opCode, ResourceId = resourceId, Priority = 1, CapacityFactor = 1m
        };

    private static ResourceDefinition Res(int id, string code, params (DateTime Start, DateTime End)[] slots)
        => new ResourceDefinition { ResourceId = id, ResourceCode = code, FactoryCode = "F1", Capacity = 1m };

    private static List<T> Concat<T>(T first, IEnumerable<T> rest)
        => new List<T>(new[] { first }.Concat(rest));

    private static List<T> Concat<T>(T a, T b, IEnumerable<T> rest)
        => new List<T>(new[] { a, b }.Concat(rest));

    private static DomainSolveRequest Build(
        IReadOnlyList<LogicalProductionDemand> demands,
        IReadOnlyList<RoutingOperation> ops,
        IReadOnlyList<RoutingDependency> deps,
        IReadOnlyList<OperationResourceEligibility> els,
        IReadOnlyList<ResourceDefinition> resources,
        Dictionary<int, (DateTime Start, DateTime End)[]> calendarOverrides,
        string direction,
        CandidateContext? candidate = null,
        IReadOnlyList<MaterialAvailabilitySlice>? materialSlices = null,
        IReadOnlyList<SetupTransitionRuleSnapshot>? setupRules = null)
    {
        var calendarSlots = new List<ResourceCalendarSlot>();
        foreach (var kvp in calendarOverrides)
        {
            foreach (var (start, end) in kvp.Value)
            {
                calendarSlots.Add(new ResourceCalendarSlot
                {
                    ResourceId = kvp.Key, Start = start, End = end, IsAvailable = true
                });
            }
        }

        var deptContexts = demands.Select(d => d.MaterialId).Distinct()
            .Select(mid => new MaterialStageDepartmentContextDto
            {
                MaterialId = mid, StageCode = "STAGE1", ProductionDepartmentId = 100
            }).ToList();

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = demands,
            RoutingOperations = ops,
            RoutingDependencies = deps,
            OperationResourceEligibility = els,
            MaterialStageDepartmentContexts = deptContexts,
            Resources = resources,
            CalendarSlots = calendarSlots,
            MaterialConstraints = materialSlices ?? Array.Empty<MaterialAvailabilitySlice>(),
            CandidateContext = candidate,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = direction,
                    AllowMerge = false,
                    AllowSplit = false
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是 Phase5 压实，非批决策 ⇒ Material 级宽松策略（恒 1 批）。
                BatchPolicies = TestBatchPolicy.Permissive(demands.Select(d => d.MaterialId), 100),
                SetupTransitionRules = setupRules ?? Array.Empty<SetupTransitionRuleSnapshot>()
            }
        };
    }
}
