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
/// 执行批（Execution Batch）形成、批键域与 **Batch Policy 键控消费**的反证单测
/// —— 0号位 2026-10-07《未命名的Markdown文件 (7).md》P0-01 / P0-02 落码验收。
///
/// 0号位 (7).md §十三 指定链：
///   `C Free Slice → 按 Material + ProductionDepartment 命中 Batch Policy → 合法批方案(Min/Max/AllowSplit)
///    → 1..N Execution Batch → 每批独立 Direction+Routing+Resource+Calendar+Setup → 每批一条完整 Path
///    → 同 Batch 全部 Operation 共 BatchDraftKey → 不同 Batch 必不同 Key`
///
/// 0号位 (7).md §十三 指定的反证单测（本文件逐条落地，**不静默省略、不降级**）：
///   · ① 同一 Domain 两个 Material 不同 Policy 不串策略 ⇒ <see cref="策略解析_同Domain两物料不串策略"/>
///   · ② 同一 Material 不同 ProductionDepartment 不同 Policy 不串策略 ⇒ <see cref="策略解析_同物料不同部门不串策略"/>
///   · ③ `Qty=10 / Min=6 / Max=6` 必须判无合法拆分（不得产 5+5）⇒ <see cref="Qty10_Min6_Max6_无合法切分_判冲突"/>
///   · ④ `AllowSplit=false &amp;&amp; Qty&gt;Max` 必须冲突（不得自行强拆）⇒ <see cref="不允许拆批且超硬最大_判冲突不擅自强拆"/>
///   · ⑤ 一个 C Demand 形成 2 个 Execution Batch、两批 Key 不同 ⇒ <see cref="批键域_同Route同Path拆两批_键仍不同"/>
///   · ⑥ 两批**可以选不同 Route/Path**（逐批择优）⇒ <see cref="逐批选路_两批可选不同RoutePath"/>
///   · ⑦ Phase4 局部修复以**执行批**为单位（各批带本批键 + 本批数量）⇒ <see cref="Phase4逐批修复_各批带本批键与本批数量"/>
///   · ⑧ Phase5 两批 TaskDependency 不交叉、份额闭合 ⇒ <see cref="Phase5逐批血缘_两批不交叉且份额闭合"/>
///   · ⑨ 同码跨 Stage 时软偏好只作用于真实承接工序 ⇒ <see cref="软偏好身份_同码跨Stage_只作用于真实承接工序"/>
///
/// P1-01（§八 有界优化候选）：候选枚举有界且消费 Preferred/上限 ⇒ <see cref="优化候选_有界且消费Preferred与上限"/>；
///   基线装不下时取更大合法批数直接排下 ⇒ <see cref="优化候选_基线装不下时取更大合法批数"/>。
///
/// 全部纯内存，不触库（用户红线：Integration 直连生产库，只跑 Unit）。
/// </summary>
public class ExecutionBatchDraftTests
{
    private static readonly DateTime PlanningStart = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningEnd = new DateTime(2026, 10, 31, 0, 0, 0);

    private const int MaterialId = 1;
    private const int DeptId = 100;
    private const string Route = "RTA";
    private const int Path = 1;

    private readonly FiniteCapacitySolver _solver = new();

    // ═══════════════════════════════════════════════════════════════════
    //  夹具
    // ═══════════════════════════════════════════════════════════════════

    private static LogicalProductionDemand Demand(
        string key = "D1",
        decimal netQty = 10m,
        decimal? procQty = null,
        string? routeCode = null,
        int? pathId = null,
        bool isContinuation = false,
        bool noSplitMerge = false,
        int materialId = MaterialId,
        string startStageCode = "STAGE1")
        => new()
        {
            LogicalDemandKey = key,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = 1,
            DemandKey = key,
            MaterialId = materialId,
            FactoryId = 1,
            StartStageCode = startStageCode,
            NetOutputQty = netQty,
            PlannedProcessQty = procQty ?? netQty,
            RequiredAvailableTime = PlanningStart.AddDays(20),
            DemandSequence = 1,
            RouteCode = routeCode,
            PathId = pathId,
            IsContinuation = isContinuation,
            NoSplitMerge = noSplitMerge
        };

    /// <summary>⑧块 Batch Policy 夹具（键 = Material + ProductionDepartment）。</summary>
    private static BatchPolicyRuleSnapshot Policy(
        int materialId = MaterialId,
        int? deptId = DeptId,
        decimal? min = null,
        decimal? max = null,
        decimal? preferred = null,
        bool allowSplit = true,
        bool allowMerge = false,
        int? maxOptSplit = null,
        int? maxCandidates = null)
        => new()
        {
            MaterialId = materialId,
            ProductionDepartmentId = deptId,
            MinExecutionBatchQty = min,
            MaxExecutionBatchQty = max,
            PreferredBatchQty = preferred,
            AllowSplit = allowSplit,
            AllowMerge = allowMerge,
            MaxOptimizationSplitCount = maxOptSplit,
            MaxBatchCandidates = maxCandidates
        };

    /// <summary>一条两工序路径（OP10@STAGE1 → OP20@STAGE2，各 <paramref name="opDuration"/> 分钟/件），资源 1 + 全窗日历。</summary>
    private static DomainSolveRequest BuildRequest(
        string logicalDemandKey = "D1",
        decimal qty = 1m,
        string? demandRouteCode = null,
        int? demandPathId = null,
        IReadOnlyList<MaterialStageDepartmentContextDto>? stageDepts = null,
        IReadOnlyList<BatchPolicyRuleSnapshot>? batchPolicies = null,
        decimal opDuration = 60m,
        IReadOnlyList<ResourceCalendarSlot>? calendarSlots = null,
        bool allowSplit = false,
        CandidateContext? candidate = null,
        decimal? opDurationStage2 = null)
    {
        var ops = new List<RoutingOperation>();
        var deps = new List<RoutingDependency>();
        var elig = new List<OperationResourceEligibility>();

        foreach (var (code, stage, duration) in new[]
                 {
                     ("OP10", "STAGE1", opDuration),
                     ("OP20", "STAGE2", opDurationStage2 ?? opDuration)
                 })
        {
            ops.Add(new RoutingOperation
            {
                MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                RouteCode = Route, PathId = Path,
                OperationCode = code, StageCode = stage,
                StandardDuration = duration, OperationPlanningMode = "FINITE_RESOURCE"
            });
            elig.Add(new OperationResourceEligibility
            {
                MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                RouteCode = Route, PathId = Path,
                OperationCode = code, ResourceId = 1, Priority = 1, CapacityFactor = 1m
            });
        }

        deps.Add(new RoutingDependency
        {
            MaterialId = MaterialId, ProductionDepartmentId = DeptId,
            RouteCode = Route, PathId = Path,
            FromOperationCode = "OP10", ToOperationCode = "OP20"
        });

        var deptContexts = stageDepts ?? new List<MaterialStageDepartmentContextDto>
        {
            new() { MaterialId = MaterialId, StageCode = "STAGE1", ProductionDepartmentId = DeptId },
            new() { MaterialId = MaterialId, StageCode = "STAGE2", ProductionDepartmentId = DeptId }
        };

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new()
                {
                    LogicalDemandKey = logicalDemandKey, PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = logicalDemandKey,
                    MaterialId = MaterialId, FactoryId = 1,
                    StartStageCode = "STAGE1",
                    NetOutputQty = qty, PlannedProcessQty = qty,
                    RequiredAvailableTime = PlanningStart.AddDays(20), DemandSequence = 1,
                    RouteCode = demandRouteCode, PathId = demandPathId
                }
            },
            RoutingOperations = ops,
            RoutingDependencies = deps,
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = new List<MaterialStageDepartmentContextDto>(deptContexts),
            ExecutionConstraints = Array.Empty<ExecutionConstraint>(),
            Resources = new List<ResourceDefinition>
            {
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = calendarSlots ?? new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = PlanningStart, End = PlanningEnd, IsAvailable = true }
            },
            CandidateContext = candidate,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowSplit = allowSplit
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶缺有效 Batch Policy ⇒ Fail Closed。
                //   默认给一条 Material 级宽松策略（恒 1 批，与 P0-01 之前行为逐字一致），
                //   使**非批决策**类用例不受影响；验证 Fail Closed 本身的用例显式传 `Array.Empty<>()`。
                BatchPolicies = batchPolicies ?? TestBatchPolicy.Permissive(MaterialId)
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  A. 批键域（P0-02）—— 机制层，0号位 (7).md 反证 ⑤
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **0号位 (7).md 反证 ⑤**：同一 Demand 拆 2 批，**即使都走同一 Route / 同一 Path**，批键仍必须不同。
    ///
    /// 旧键 `EB|{demand}|{route}|{path}` 由 Route/Path 派生 ⇒ 同 Route/Path 的两批会**撞成同一个键**
    /// ⇒「同 Batch 共键」成立但「**不同 Batch 必不同键**」**结构性失效**。
    ///
    /// 反证构造：需求**显式带** RouteCode="RTA" / PathId=1，且批数为 2 ⇒
    ///   新实现：`EB|D1|001` ≠ `EB|D1|002`（绿）；
    ///   旧实现：两批都是 `EB|D1|RTA|1` ⇒ <c>Assert.Equal(2, keys.Distinct().Count())</c> **红**。
    /// </summary>
    [Fact]
    public void 批键域_同Route同Path拆两批_键仍不同()
    {
        var demand = Demand(netQty: 10m, routeCode: Route, pathId: Path);
        var policy = Policy(max: 6m);

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(demand, policy);
        Assert.True(formation.IsLegal, formation.ConflictReason);
        var batches = formation.Batches;

        Assert.Equal(2, batches.Count);
        Assert.Equal(2, batches.Select(b => b.BatchDraftKey).Distinct().Count());
        Assert.Equal(new[] { "EB|D1|001", "EB|D1|002" }, batches.Select(b => b.BatchDraftKey));
        Assert.Equal(new[] { 1, 2 }, batches.Select(b => b.Ordinal));

        // 批键**不得**包含 RouteCode（否则又是「由路由反推批身份」）
        // （批键形如 EB|D1|001 ⇒ 既无 RouteCode，也无 PathId 段）
        Assert.All(batches, b => Assert.DoesNotContain(Route, b.BatchDraftKey, StringComparison.Ordinal));
        Assert.All(batches, b =>
        {
            var parts = b.BatchDraftKey.Split('|');
            Assert.Equal(3, parts.Length);
            Assert.Equal("EB", parts[0]);
            Assert.Equal("D1", parts[1]);
        });
    }

    /// <summary>
    /// 批键由 (逻辑需求键, 批序号) **唯一决定**：Route/Path 只是批的**属性**，
    /// 改 Route/Path 不得改变批键序列（否则批身份仍被路由反向决定）。
    /// </summary>
    [Fact]
    public void 批键域_由需求键与批序号唯一决定_与RoutePath无关()
    {
        var policy = Policy(max: 6m);

        var keysWithRoute = PhaseTwoInitialScheduler
            .FormExecutionBatches(Demand(netQty: 10m, routeCode: Route, pathId: Path), policy)
            .Batches.Select(b => b.BatchDraftKey).ToArray();

        var keysWithoutRoute = PhaseTwoInitialScheduler
            .FormExecutionBatches(Demand(netQty: 10m), policy)
            .Batches.Select(b => b.BatchDraftKey).ToArray();

        Assert.Equal(keysWithoutRoute, keysWithRoute);

        // 纯函数口径锁
        Assert.Equal("EB|D1|001", PhaseTwoInitialScheduler.ExecutionBatchKey("D1"));
        Assert.Equal("EB|D1|002", PhaseTwoInitialScheduler.ExecutionBatchKey("D1", 2));
        Assert.Equal("EB|D1|007", PhaseTwoInitialScheduler.ExecutionBatchKey("D1", 7));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  B. 合法批域（P0-02）—— Min / Max / AllowSplit
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A/B（连续份额）**硬约束**：`IsContinuation` / `NoSplitMerge` ⇒ 恒 1 批，
    /// **即使硬最大批量小到「理论上该拆很多批」也不拆**（v1.6 NoSplitMerge；与 Phase4 P0_07 同向）。
    /// </summary>
    [Fact]
    public void AB连续份额_恒不拆批_硬最大也不拆()
    {
        var tinyMax = Policy(max: 1m);

        var byContinuation = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 100m, isContinuation: true, noSplitMerge: true), tinyMax);
        Assert.True(byContinuation.IsLegal, byContinuation.ConflictReason);
        Assert.Single(byContinuation.Batches);
        Assert.Equal("EB|D1|001", byContinuation.Batches[0].BatchDraftKey);

        var byNoSplitMerge = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 100m, noSplitMerge: true), tinyMax);
        Assert.True(byNoSplitMerge.IsLegal, byNoSplitMerge.ConflictReason);
        Assert.Single(byNoSplitMerge.Batches);
    }

    /// <summary>
    /// **P0-01（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§四）**：
    ///   C 桶**缺有效 Batch Policy**（`policy == null`）⇒ **`BATCH_POLICY_MISSING` / Fail Closed**，
    ///   **不得**再被静默降级成「不拆、恒 1 批」。
    ///
    /// 正式兜底链（0号位 2026-09-28 §十五）：① Material + ProductionDepartment 精确 →
    ///   ② Material 级默认 → ③ 正式发布的 Global Batch Default → ④ `BATCH_POLICY_MISSING`。
    ///   1号位 **不越权设计** ①②③ 的展开方式（归 2/3号位），但**收到最终输入后仍找不到有效 Policy 时必须 Fail Closed**。
    ///
    /// 反证性：旧实现 `policy == null → ExecutionBatchPlan.Legal(1)`（= 隐藏业务默认「默认策略 = 不拆批」）⇒ 本用例 **红**。
    /// </summary>
    [Fact]
    public void 缺批策略_FailClosed_不产批()
    {
        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(Demand(netQty: 100m), null);

        Assert.False(formation.IsLegal);
        Assert.True(formation.IsMissingPolicy);
        Assert.Empty(formation.Batches);
        Assert.Contains("BATCH_POLICY_MISSING", formation.ConflictReason);
    }

    /// <summary>
    /// **0号位 (7).md 反证 ④**：`AllowSplit=false &amp;&amp; Qty &gt; Max` ⇒ **必须冲突**，**不得自行强拆**。
    ///
    /// 依据：v5.1.9 §7.3 `TaskSplitRuleConfig.AllowSplit` 逐字「0且Qty&gt;Max时返回 `BATCH_POLICY_CONFLICT`」。
    ///
    /// 反证构造：`AllowSplit=false`、硬 Max=4、Qty=10 ⇒
    ///   旧实现：`ceil(10/4)=3` 批（把硬 Max 当「强制拆分」开关，**无视 AllowSplit**）⇒ 本用例 **红**；
    ///   新实现：`IsLegal=false`（冲突）⇒ 绿。
    /// </summary>
    [Fact]
    public void 不允许拆批且超硬最大_判冲突不擅自强拆()
    {
        var policy = Policy(max: 4m, allowSplit: false);

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(Demand(netQty: 10m), policy);

        Assert.False(formation.IsLegal);
        Assert.Empty(formation.Batches);
        Assert.Contains("BATCH_POLICY_CONFLICT", formation.ConflictReason);
    }

    /// <summary>
    /// **0号位 (7).md 反证 ③**：`Qty=10 / Min=6 / Max=6` ⇒ **无合法切分** ⇒ 冲突，**不得产 5+5**。
    ///
    /// 推导：n=1 ⇒ 6 ≤ 10 ≤ 6 假（10 &gt; 6）；n=2 ⇒ 12 ≤ 10 假 ⇒ 无 n。
    ///   ⇒ 每个批必须 ∈ [6,6]，而任何合法划分之和都不等于 10。
    ///
    /// 反证构造：旧实现（只看 `ceil(Qty/Max)`、Min 无作用面）⇒ 产 `5+5`（**两批都 &lt; Min**，非法）⇒ 本用例 **红**；
    ///   新实现：`IsLegal=false` ⇒ 绿。
    /// </summary>
    [Fact]
    public void Qty10_Min6_Max6_无合法切分_判冲突()
    {
        var policy = Policy(min: 6m, max: 6m);

        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(Demand(netQty: 10m), policy);

        Assert.False(formation.IsLegal);
        Assert.Empty(formation.Batches);
        Assert.Contains("BATCH_POLICY_CONFLICT", formation.ConflictReason);
    }

    /// <summary>
    /// **硬 Min 有作用面**（推翻旧「Min 在 V1 无作用面」结论，0号位 (7).md §五 明令禁止该结论）。
    ///
    /// 正向：`Qty=12 / Min=6 / Max=6` ⇒ n=2（12≤12≤12）⇒ 2 批，每批 6 ⇒ 合法。
    /// 负向：`Qty=10 / Min=4 / Max=2` ⇒ nMin=ceil(10/2)=5、nMax=floor(10/4)=2 ⇒ 5&gt;2 ⇒ 冲突。
    /// </summary>
    [Fact]
    public void 硬最小批量_既约束批数也决定是否有解()
    {
        var legal = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 12m), Policy(min: 6m, max: 6m));
        Assert.True(legal.IsLegal, legal.ConflictReason);
        Assert.Equal(2, legal.Batches.Count);
        Assert.All(legal.Batches, b => Assert.Equal(6m, b.NetOutputQty));

        var conflict = PhaseTwoInitialScheduler.FormExecutionBatches(
            Demand(netQty: 10m), Policy(min: 4m, max: 2m));
        Assert.False(conflict.IsLegal);
        Assert.Empty(conflict.Batches);
    }

    /// <summary>
    /// 数量**逐分不丢**：`Σ NetOutputQty = 需求 NetOutputQty`、`Σ PlannedProcessQty = 需求 PlannedProcessQty`。
    /// 构造刻意用除不尽的 10 / 3（前两批向下取整 3.3333，末批取余 3.3334）。
    /// </summary>
    [Fact]
    public void 数量守恒_逐分不丢_Σ等于需求数量()
    {
        var demand = Demand(netQty: 10m, procQty: 7m);
        var formation = PhaseTwoInitialScheduler.FormExecutionBatches(demand, Policy(max: 4m));

        Assert.True(formation.IsLegal, formation.ConflictReason);
        var batches = formation.Batches;

        Assert.Equal(3, batches.Count);
        Assert.Equal(10m, batches.Sum(b => b.NetOutputQty));
        Assert.Equal(7m, batches.Sum(b => b.PlannedProcessQty));

        // 末批取余数 ⇒ 各批之和逐分等于需求（decimal 精确，无浮点误差）
        Assert.Equal(3.3333m, batches[0].NetOutputQty);
        Assert.Equal(3.3333m, batches[1].NetOutputQty);
        Assert.Equal(3.3334m, batches[2].NetOutputQty);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  C. Batch Policy 键控解析（P0-01）—— 0号位 (7).md 反证 ①②
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **0号位 (7).md 反证 ①**：同一 Domain 内**两个 Material** 用不同 Batch Policy，**不得串策略**。
    ///
    /// 反证构造：策略集同时含 `Material 1 → Max=6` 与 `Material 2 → Max=100`。
    ///   旧实现（Domain 单值）无法区分 ⇒ 两物料共用同一策略 ⇒ 本用例 **红**；
    ///   新实现：按 `(MaterialId, Dept)` 键控解析 ⇒ 各取各的 ⇒ 绿。
    /// </summary>
    [Fact]
    public void 策略解析_同Domain两物料不串策略()
    {
        var request = BuildRequest();
        var constraints = new PhaseOneConstraintBuilder().BuildConstraints(request);
        constraints.ExecutionBatchPolicies.Add(Policy(materialId: 1, max: 6m));
        constraints.ExecutionBatchPolicies.Add(Policy(materialId: 2, deptId: null, max: 100m));

        var p1 = PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy(
            Demand(key: "D1", materialId: 1), request, constraints);
        var p2 = PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy(
            Demand(key: "D2", materialId: 2), request, constraints);

        Assert.NotNull(p1);
        Assert.NotNull(p2);
        Assert.Equal(6m, p1!.MaxExecutionBatchQty);
        Assert.Equal(100m, p2!.MaxExecutionBatchQty);
    }

    /// <summary>
    /// **0号位 (7).md 反证 ②**：同一 Material 的**不同 ProductionDepartment** 用不同 Policy，**不得串策略**。
    ///
    /// 反证构造：`Material 1 + Dept 100 → Max=6`、`Material 1 + Dept 200 → Max=100`；
    ///   需求按 `StartStageCode` 经 `MaterialStageDepartmentContexts` 反查部门 ⇒ 各取各的。
    ///   旧实现（Domain 单值）⇒ 两部门共用同一策略 ⇒ 本用例 **红**。
    /// </summary>
    [Fact]
    public void 策略解析_同物料不同部门不串策略()
    {
        var request = BuildRequest(stageDepts: new List<MaterialStageDepartmentContextDto>
        {
            new() { MaterialId = 1, StageCode = "STAGE1", ProductionDepartmentId = 100 },
            new() { MaterialId = 1, StageCode = "STAGE2", ProductionDepartmentId = 200 }
        });

        var constraints = new PhaseOneConstraintBuilder().BuildConstraints(request);
        constraints.ExecutionBatchPolicies.Add(Policy(materialId: 1, deptId: 100, max: 6m));
        constraints.ExecutionBatchPolicies.Add(Policy(materialId: 1, deptId: 200, max: 100m));

        var byDept100 = PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy(
            Demand(startStageCode: "STAGE1"), request, constraints);
        var byDept200 = PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy(
            Demand(startStageCode: "STAGE2"), request, constraints);

        Assert.Equal(6m, byDept100!.MaxExecutionBatchQty);
        Assert.Equal(100m, byDept200!.MaxExecutionBatchQty);
    }

    /// <summary>
    /// 解析回落顺序：**精确 (Material, Dept) 未命中 ⇒ 回落 (Material, null) Material 级默认**；
    /// **都无 ⇒ null** ⇒ 调用方 **`BATCH_POLICY_MISSING` Fail Closed**
    ///   （P0-01，0号位 2026-10-08 §四：**不得**降级成「不拆、恒 1 批」）。
    /// **禁止**回落到「全局默认策略」（0号位 (7).md §十一 第 3 条）。
    ///
    /// 本用例自行往 `constraints.ExecutionBatchPolicies` 里加策略 ⇒ 请求侧须传**空**策略集，
    ///   否则夹具默认的 Material 级宽松策略会先被命中，掩盖本用例要验证的回落顺序。
    /// </summary>
    [Fact]
    public void 策略解析_精确未命中回落物料级默认_再无则缺策略()
    {
        var request = BuildRequest(batchPolicies: Array.Empty<BatchPolicyRuleSnapshot>());
        var constraints = new PhaseOneConstraintBuilder().BuildConstraints(request);

        // 只有 Material 级默认（Dept=null）
        constraints.ExecutionBatchPolicies.Add(Policy(materialId: 1, deptId: null, max: 42m));

        // Dept 100 精确未命中 ⇒ 回落 Material 级默认
        Assert.Equal(42m, PhaseTwoInitialScheduler
            .ResolveExecutionBatchPolicy(Demand(startStageCode: "STAGE1"), request, constraints)!
            .MaxExecutionBatchQty);

        // 别的物料无任何策略 ⇒ null（缺策略，**不**回落全局默认）
        Assert.Null(PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy(
            Demand(key: "D9", materialId: 9), request, constraints));

        // 空策略集 ⇒ null
        constraints.ExecutionBatchPolicies.Clear();
        Assert.Null(PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy(
            Demand(), request, constraints));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  D. Phase4 逐批修复（P0-03）—— 0号位 (7).md 反证 ⑦
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **0号位 (7).md 反证 ⑦**：Phase4 局部修复的基本单位是**执行批**，不是需求。
    ///
    /// 构造：`Qty=10 / Max=6` ⇒ 2 批（`EB|D1|001` / `EB|D1|002`，各 5 件）；单工序 50 分钟
    ///   （10 分钟/件 × 5 件）装不进任何 30 分钟单槽 ⇒ **Phase2 不 Split，两批均落不下**（需求整体未排）
    ///   ⇒ 走 Phase4 逐批修复（每批按**本批数量 5 件**重建 + 有限 Split 成 2×25 分钟）。
    ///
    /// 断言（反证点）：
    ///   · 最终 Task 按 `ExecutionBatchDraftKey` 分成 **2 组**，键恰为 `EB|D1|001` / `EB|D1|002`；
    ///   · 每批**每道工序**的数量和 = 本批数量 5（不是整份需求数量 10）—— 旧实现用整份需求数量重建 +
    ///     `keys[0]` 猜批身份 ⇒ 只剩 1 组、每工序数量和 10 ⇒ 本条断言**红**；
    ///   · 每批各自走完 STAGE1 / STAGE2 两道工序（批内不得被劈成残链）。
    ///
    /// ⚠ 本用例**不能**构造「Phase2 中 Batch-001 成功、只有 Batch-002 失败」的字面场景：Phase2 的批循环在
    ///   第一批不可行时即 `batchFailed = true; break`（`PhaseTwoInitialScheduler.cs:519-524`），后续批不再试排。
    ///   故 ⑦ 的可验证实质 = **修复以批为单位、各批带本批键与本批数量** —— 这正是旧实现按 `LogicalDemandKey`
    ///   猜批身份（`ResolveExecutionBatchKeyForRebuild` 取 `keys[0]`）所结构性破坏的性质。
    /// </summary>
    [Fact]
    public async Task Phase4逐批修复_各批带本批键与本批数量()
    {
        // 10 个 30 分钟可用槽、两两相隔 30 分钟不可用 ⇒ 无任何连续 50 分钟可用窗（Phase2 装不下），
        // 但 25 分钟的拆分件（5 件拆 2 份）装得下 ⇒ Phase4 有限 Split 可修复。
        var slots = new List<ResourceCalendarSlot>();
        for (int i = 0; i < 10; i++)
        {
            var start = PlanningStart.AddHours(8 + i);
            slots.Add(new ResourceCalendarSlot
            {
                ResourceId = 1, Start = start, End = start.AddMinutes(30), IsAvailable = true
            });
        }

        var request = BuildRequest(
            qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new List<BatchPolicyRuleSnapshot> { Policy(max: 6m) },
            opDuration: 10m,        // 5 件/批 ⇒ 50 分钟 > 单槽 30 分钟
            calendarSlots: slots,
            allowSplit: true);      // Phase4 有限 Split 才可修复

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var d1Tasks = result.FinalTasks.Where(t => t.SourceDraftId == "D1").ToList();
        Assert.NotEmpty(d1Tasks);
        Assert.All(d1Tasks, t => Assert.False(string.IsNullOrEmpty(t.ExecutionBatchDraftKey),
            "修复后的 Task 必须带执行批键（不得为空）"));

        var byBatch = d1Tasks
            .GroupBy(t => t.ExecutionBatchDraftKey!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 反证：旧实现（按需求整链重建 + `keys[0]` 猜批身份）⇒ 只剩 1 组 `EB|D1|001`、数量 10。
        Assert.Equal(2, byBatch.Count);
        Assert.Contains("EB|D1|001", byBatch.Keys);
        Assert.Contains("EB|D1|002", byBatch.Keys);

        // 每批**按本批数量**修复（5），不是整份需求数量（10）。
        // ⚠ 口径：`Quantity` 是**逐工序**数量（本需求 2 道工序 ⇒ 每批 2 道工序的 Task 各自载本批数量）
        //   ⇒ 按 (批, 工序) 分组求和 = 5；**不可**把整批全部 Task 直接相加（会得 10 = 2 道工序 × 5）。
        foreach (var (_, tasks) in byBatch)
        {
            foreach (var stageGroup in tasks.GroupBy(t => t.StageCode))
            {
                Assert.Equal(5m, stageGroup.Sum(t => t.Quantity));
            }
        }

        // 每批各自走完完整两工序（批内不得被劈成残链）。
        Assert.All(byBatch.Values, tasks =>
        {
            Assert.Contains(tasks, t => t.StageCode == "STAGE1");
            Assert.Contains(tasks, t => t.StageCode == "STAGE2");
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  E. Phase5 逐批血缘（P0-04）—— 0号位 (7).md 反证 ⑧
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **0号位 (7).md 反证 ⑧**：同一需求拆出的两批，Phase5 的 `TaskDependency` **不得跨批**，
    ///   且 `AllocationTaskShare` 闭合。
    ///
    /// 构造（唯一能触发旧实现「跨批交叉积」的形状）：`Qty=10 / Max=6` ⇒ 2 批（各 5 件）；
    ///   **两道工序时长不同** ⇒ 同一批内**上游/下游 Task 数不等**：
    ///     · OP10 = 5 分钟/件 ⇒ 本批 25 分钟 ≤ 单槽 30 分钟 ⇒ **1 个 Task**；
    ///     · OP20 = 10 分钟/件 ⇒ 本批 50 分钟 &gt; 单槽 ⇒ Phase4 有限 Split 成 **2 个 Task**（各 25 分钟）。
    ///   此时旧实现（按需求整体建边）看到 upstream=2 / downstream=4 ⇒ 走「均摊」分支 ⇒
    ///   **2×4=8 条边**，其中 **4 条跨批**（Batch-001 的 OP10 连到 Batch-002 的 OP20）。
    ///
    /// 断言：
    ///   · 每条边的上游/下游 Task 必须**同批键**（反证点：旧实现此处红）；
    ///   · 边数 = 每批 1 上游 × 2 下游 = **2 条/批，共 4 条**（旧实现 8 条 ⇒ 也红）；
    ///   · `AllocationTaskShare` 按 Allocation 闭合 = 需求 NetOutputQty = 10。
    /// </summary>
    [Fact]
    public async Task Phase5逐批血缘_两批不交叉且份额闭合()
    {
        // 10 个 30 分钟可用槽、两两相隔 30 分钟不可用（与反证 ⑦ 同形）。
        var slots = new List<ResourceCalendarSlot>();
        for (int i = 0; i < 10; i++)
        {
            var start = PlanningStart.AddHours(8 + i);
            slots.Add(new ResourceCalendarSlot
            {
                ResourceId = 1, Start = start, End = start.AddMinutes(30), IsAvailable = true
            });
        }

        var request = BuildRequest(
            qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new List<BatchPolicyRuleSnapshot> { Policy(max: 6m) },
            opDuration: 5m,           // OP10：5 分钟/件 ⇒ 本批 25 分钟 ⇒ 1 个 Task（不拆）
            opDurationStage2: 10m,    // OP20：10 分钟/件 ⇒ 本批 50 分钟 ⇒ 拆 2 个 Task
            calendarSlots: slots,
            allowSplit: true);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var batchKeyByTask = result.FinalTasks
            .ToDictionary(t => t.FinalDraftId, t => t.ExecutionBatchDraftKey);

        // 每批：1 个 STAGE1 + 2 个 STAGE2。
        var byBatch = result.FinalTasks
            .Where(t => t.SourceDraftId == "D1")
            .GroupBy(t => t.ExecutionBatchDraftKey!)
            .ToDictionary(g => g.Key, g => g.ToList());
        Assert.Equal(2, byBatch.Count);
        Assert.All(byBatch.Values, tasks =>
        {
            Assert.Equal(1, tasks.Count(t => t.StageCode == "STAGE1"));
            Assert.Equal(2, tasks.Count(t => t.StageCode == "STAGE2"));
        });

        // 反证点：**不得跨批**（旧实现 4 条跨批边在此红）。
        var intraMaterialEdges = result.PhysicalPeggingDrafts
            .Where(d => batchKeyByTask.ContainsKey(d.UpstreamFinalDraftId)
                        && batchKeyByTask.ContainsKey(d.DownstreamFinalDraftId))
            .ToList();

        Assert.All(intraMaterialEdges, d => Assert.Equal(
            batchKeyByTask[d.UpstreamFinalDraftId],
            batchKeyByTask[d.DownstreamFinalDraftId]));

        // 边数：每批 1 上游 × 2 下游 = 2 条 ⇒ 共 4 条（旧实现按需求整体算 ⇒ 8 条）。
        Assert.Equal(4, intraMaterialEdges.Count);

        // AllocationTaskShare 闭合：本 Allocation 下全部需求 NetOutputQty 之和 = 10。
        var closedQty = result.AllocationShares
            .Where(s => s.AllocationSequence == 1)
            .Sum(s => s.ComponentQty);
        Assert.Equal(10m, closedQty);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  G. 软偏好身份（P1-02）—— 0号位 (7).md 反证 ⑨
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **0号位 (7).md 反证 ⑨**：同一 Route 内**同 OperationCode 跨两个 Stage** 时，
    ///   `PreferredResourceCode` / `PreferredResourceId` 的软偏好**只作用于真实承接工序**，
    ///   不得因 OperationCode 相同而扩散到另一 Stage 的同名工序。
    ///
    /// 依据 0号位 2026-09-29 裁决 §5.3（节点身份 = `(StageCode, OperationCode)`；2号位 实测 117 物料同码跨 Stage）。
    /// `OrderResourcesByPreference` 是 Phase2（两处）与 Phase4（一处）**共用的生产路径纯函数**，
    /// 故本用例直接锁其身份判据，不依赖特定路由夹具的可构造性。
    ///
    /// 反证构造：承接工序身份 = `(STAGE1, OP10)`；合法资源 = `[R1, R2]`（R1 在前），软偏好 = `R2`。
    ///   · `(STAGE1, OP10)`（真实承接工序）⇒ 偏好生效 ⇒ `[R2, R1]`；
    ///   · `(STAGE2, OP10)`（同码、另一 Stage）⇒ **不得施加偏好** ⇒ `[R1, R2]`。
    ///   单键实现（只看 OperationCode）在两处都返回 `[R2, R1]` ⇒ 第二条断言**红**。
    /// </summary>
    [Fact]
    public void 软偏好身份_同码跨Stage_只作用于真实承接工序()
    {
        var constraints = new ConstraintContext();
        constraints.ResourceIdsByCode["R2"] = 2;

        var demand = new LogicalProductionDemand
        {
            LogicalDemandKey = "D1", PlanVersionId = 1L, DomainKey = "DOMAIN",
            AllocationSequence = 1, DemandKey = "D1",
            MaterialId = MaterialId, FactoryId = 1,
            NetOutputQty = 1m, PlannedProcessQty = 1m, DemandSequence = 1,
            StartStageCode = "STAGE1",
            StartOperationCode = "OP10",
            IsContinuation = true,
            PreferredResourceCode = "R2"
        };

        var eligible = new List<int> { 1, 2 };

        // 真实承接工序 (STAGE1, OP10)：软偏好生效（R2 提到最前）
        Assert.Equal(new[] { 2, 1 },
            PhaseTwoInitialScheduler.OrderResourcesByPreference(demand, eligible, constraints, "OP10", "STAGE1"));

        // 同码跨 Stage 的非承接工序 (STAGE2, OP10)：不得施加偏好（单键实现此处返回 [2,1] ⇒ 红）
        Assert.Equal(new[] { 1, 2 },
            PhaseTwoInitialScheduler.OrderResourcesByPreference(demand, eligible, constraints, "OP10", "STAGE2"));

        // 另一工序码同样不施加偏好
        Assert.Equal(new[] { 1, 2 },
            PhaseTwoInitialScheduler.OrderResourcesByPreference(demand, eligible, constraints, "OP20", "STAGE2"));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F. 求解层 —— 批键落到 FinalTask
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 求解层批键域锁：单批时**同一批的全部工序 Task 共用一个** `ExecutionBatchDraftKey`，
    /// 且键值 = `EB|{需求键}|001`（**不含 RouteCode / PathId**）。
    ///
    /// 反证性：旧键 `EB|D1|RTA|1` ⇒ 本用例 <c>Assert.Equal("EB|D1|001", ...)</c> **红**。
    /// </summary>
    [Fact]
    public async Task 求解层_批键域锁_全体工序共键且不含RoutePath()
    {
        var result = await _solver.SolveAsync(BuildRequest(qty: 1m, demandRouteCode: Route, demandPathId: Path));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        var tasks = result.FinalTasks.OrderBy(t => t.StageCode, StringComparer.Ordinal).ToList();
        Assert.Equal(2, tasks.Count);   // 完整多工序链

        Assert.All(tasks, t => Assert.Equal("EB|D1|001", t.ExecutionBatchDraftKey));
        Assert.All(tasks, t => Assert.DoesNotContain(Route, t.ExecutionBatchDraftKey!, StringComparison.Ordinal));

        // 同批 ⇒ 同 Route/Path（每批一条完整 Path），且 Route/Path 仍如实落到 Task
        Assert.All(tasks, t => Assert.Equal(Route, t.RouteCode));
        Assert.All(tasks, t => Assert.Equal(Path, t.PathId));
    }

    /// <summary>
    /// **载体贯通锁（P0-01 端到端入口）**：⑧块策略经
    /// `SolverStrategySnapshot.BatchPolicies` → `PhaseOneConstraintBuilder.BuildExecutionBatchPolicies`
    /// → `ConstraintContext.ExecutionBatchPolicies` **逐字到达**求解层（不在装载层裁剪/合并）。
    ///
    /// 反证性：若装载层塌成单值或按 Domain 去重 ⇒ 本用例 <c>Assert.Equal(2, ...)</c> **红**。
    /// </summary>
    [Fact]
    public void 载体贯通_策略经SolverStrategySnapshot逐字到达约束上下文()
    {
        var request = BuildRequest(batchPolicies: new List<BatchPolicyRuleSnapshot>
        {
            Policy(materialId: 1, deptId: 100, max: 6m),
            Policy(materialId: 1, deptId: 200, max: 100m)
        });

        var constraints = new PhaseOneConstraintBuilder().BuildConstraints(request);

        Assert.Equal(2, constraints.ExecutionBatchPolicies.Count);
        Assert.Contains(constraints.ExecutionBatchPolicies, p => p.ProductionDepartmentId == 100 && p.MaxExecutionBatchQty == 6m);
        Assert.Contains(constraints.ExecutionBatchPolicies, p => p.ProductionDepartmentId == 200 && p.MaxExecutionBatchQty == 100m);

        // 缺 ⑧块（显式传空）⇒ 策略集为空 ⇒ **`BATCH_POLICY_MISSING` Fail Closed**
        //   （P0-01 后**不再**是「空集合 ⇒ 恒 1 批」；恒 1 批只由**有效策略** `AllowSplit=false` 表达）
        Assert.Empty(new PhaseOneConstraintBuilder()
            .BuildConstraints(BuildRequest(batchPolicies: Array.Empty<BatchPolicyRuleSnapshot>()))
            .ExecutionBatchPolicies);
    }

    /// <summary>
    /// **0号位 (7).md 反证 ⑤（端到端）**：一个 C Demand 经**真 Solver**形成 2 个 Execution Batch，
    /// 两批 `ExecutionBatchDraftKey` 不同，且**每批各自有一条完整多工序链**（OP10@STAGE1 → OP20@STAGE2）。
    ///
    /// 走的是**生产装配路径**（`SolverStrategySnapshot.BatchPolicies` 载体），非夹具注入。
    /// </summary>
    [Fact]
    public async Task 端到端_一个CDemand拆两批_两批各自完整链路()
    {
        var request = BuildRequest(
            qty: 10m,
            demandRouteCode: Route,
            demandPathId: Path,
            batchPolicies: new List<BatchPolicyRuleSnapshot>
            {
                Policy(materialId: 1, deptId: DeptId, max: 6m)
            });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        var byBatch = result.FinalTasks
            .Where(t => !string.IsNullOrEmpty(t.ExecutionBatchDraftKey))
            .GroupBy(t => t.ExecutionBatchDraftKey!)
            .ToDictionary(g => g.Key, g => g.ToList());

        Assert.Equal(2, byBatch.Count);
        Assert.Contains("EB|D1|001", byBatch.Keys);
        Assert.Contains("EB|D1|002", byBatch.Keys);

        // 每批各自一条完整多工序链，且批内 Route/Path 一致
        foreach (var (_, tasks) in byBatch)
        {
            Assert.Equal(2, tasks.Count);
            Assert.Contains(tasks, t => t.StageCode == "STAGE1");
            Assert.Contains(tasks, t => t.StageCode == "STAGE2");
            Assert.All(tasks, t => Assert.Equal(Route, t.RouteCode));
            Assert.All(tasks, t => Assert.Equal(Path, t.PathId));
        }
    }

    /// <summary>
    /// **P0-01 端到端（0号位 2026-10-08 §十二 第 1 行）**：C 桶**无任何有效 Batch Policy**
    ///   ⇒ `BATCH_POLICY_MISSING`，**0 FinalTask**，需求进 `UnscheduledTasks` 且 `Reason` 逐字为该硬失败码。
    ///
    /// 必须调**正式** `FiniteCapacitySolver.SolveAsync()`（§十二：「不能只测 Helper」）。
    ///   旧实现（`BuildRequest` 默认空策略 ⇒ `policy == null` ⇒ `Legal(1)`）在本用例下会**产出 FinalTask** ⇒ **红**。
    ///
    /// **不是**「多批端到端不可达」的论证 —— 多批端到端见 <see cref="端到端_一个CDemand拆两批_两批各自完整链路"/>。
    /// </summary>
    [Fact]
    public async Task 缺策略_端到端FailClosed_零FinalTask且Reason为BATCH_POLICY_MISSING()
    {
        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: Array.Empty<BatchPolicyRuleSnapshot>());

        var constraints = new PhaseOneConstraintBuilder().BuildConstraints(request);
        Assert.Empty(constraints.ExecutionBatchPolicies);

        var result = await _solver.SolveAsync(request);

        // §四：不生成 FinalTask、不进入 Phase4 普通修复
        Assert.Empty(result.FinalTasks);

        // 出口必须明确报未排，且 Reason = 硬失败码（不得降格成「Phase 2 初始排程失败…」泛化原因）
        var unscheduled = Assert.Single(result.UnscheduledTasks);
        Assert.Equal("D1", unscheduled.DraftId);
        Assert.Equal("BATCH_POLICY_MISSING", unscheduled.Reason);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  H. 逐批选路（RT-003 / RT-004）—— 0号位 (7).md 反证 ⑥
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **双路径夹具（反证 ⑥）**：同一物料两条**互斥备选路径**，需求**不声明固定路径**
    ///   ⇒ `TryGetRoutingGraphs` 返回两条候选，由**每批各自**择优（RT-003「每批一条完整 Path」/ RT-004）。
    ///   · 路径 A = `("RTA", 1)`：OP10@STAGE1 / OP20@STAGE2 **均落资源 1**；
    ///   · 路径 B = `("RTB", 1)`：OP10@STAGE1 / OP20@STAGE2 **均落资源 2**。
    ///
    /// 日历：两资源各一段 `[+8h, +19h]`（11 小时）——恰好容纳**一批**（两工序 × 5 小时 = 10 小时），
    ///   剩余 1 小时不足以容纳第二批 ⇒ 首批占满资源 1 后，第二批在**路径 A 上不可行**（试排产 0 Task）。
    ///
    /// 这是「逐批选路」的**唯一**可构造形状：需求级选路（旧实现）只选一次、两批同路径；
    ///   逐批选路下第二批面对首批已占资源，才会落到另一条路径。
    /// </summary>
    private static DomainSolveRequest BuildTwoPathRequest(
        decimal qty = 10m,
        IReadOnlyList<BatchPolicyRuleSnapshot>? batchPolicies = null,
        decimal opDuration = 60m)
    {
        const string routeA = "RTA";
        const string routeB = "RTB";
        const int path = 1;

        var ops = new List<RoutingOperation>();
        var deps = new List<RoutingDependency>();
        var elig = new List<OperationResourceEligibility>();

        foreach (var (route, resourceId) in new[] { (routeA, 1), (routeB, 2) })
        {
            foreach (var (code, stage) in new[] { ("OP10", "STAGE1"), ("OP20", "STAGE2") })
            {
                ops.Add(new RoutingOperation
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = route, PathId = path,
                    OperationCode = code, StageCode = stage,
                    StandardDuration = opDuration, OperationPlanningMode = "FINITE_RESOURCE"
                });
                elig.Add(new OperationResourceEligibility
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = route, PathId = path,
                    OperationCode = code, ResourceId = resourceId, Priority = 1, CapacityFactor = 1m
                });
            }

            deps.Add(new RoutingDependency
            {
                MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                RouteCode = route, PathId = path,
                FromOperationCode = "OP10", ToOperationCode = "OP20"
            });
        }

        var windowStart = PlanningStart.AddHours(8);
        var windowEnd = windowStart.AddHours(11);

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new()
                {
                    LogicalDemandKey = "D1", PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = "D1",
                    MaterialId = MaterialId, FactoryId = 1,
                    StartStageCode = "STAGE1",
                    NetOutputQty = qty, PlannedProcessQty = qty,
                    RequiredAvailableTime = PlanningStart.AddDays(20), DemandSequence = 1
                    // RouteCode / PathId 故意留空 ⇒ 走 C 桶候选内择优（两条备选路径），而非固定单路径。
                }
            },
            RoutingOperations = ops,
            RoutingDependencies = deps,
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = new List<MaterialStageDepartmentContextDto>
            {
                new() { MaterialId = MaterialId, StageCode = "STAGE1", ProductionDepartmentId = DeptId },
                new() { MaterialId = MaterialId, StageCode = "STAGE2", ProductionDepartmentId = DeptId }
            },
            ExecutionConstraints = Array.Empty<ExecutionConstraint>(),
            Resources = new List<ResourceDefinition>
            {
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m },
                new() { ResourceId = 2, ResourceCode = "R2", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = windowStart, End = windowEnd, IsAvailable = true },
                new() { ResourceId = 2, Start = windowStart, End = windowEnd, IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters { SchedulingDirection = "FORWARD" },
                // P0-01（0号位 2026-10-08 §四）：缺有效 Batch Policy ⇒ Fail Closed。
                BatchPolicies = batchPolicies ?? TestBatchPolicy.Permissive(MaterialId)
            }
        };
    }

    /// <summary>
    /// **0号位 (7).md 反证 ⑥**：同一需求拆出的两批**可以选不同的 Route/Path**（RT-003 / RT-004）。
    ///
    /// 构造见 <see cref="BuildTwoPathRequest"/>：`Qty=10 / Max=6` ⇒ 2 批（各 5 件，各需 10 小时）；
    ///   两条候选路径分别独占资源 1 / 资源 2，两资源各只有 11 小时窗。
    ///   · 批 001：两候选均可行、完成时间相同（同为 `+18h`）⇒ 走确定性 tiebreak `(RouteCode, PathId) 序`
    ///     ⇒ 选 **A**（`RTA` < `RTB`），占满资源 1；
    ///   · 批 002：路径 A 只剩 1 小时（&lt; 10 小时）⇒ 试排**不可行**；路径 B 资源空闲 ⇒ 选 **B**。
    ///
    /// 断言（反证点）：
    ///   · 两批的 `(RouteCode, PathId)` **不同**（旧实现「需求级选路一次」⇒ 两批同路径 ⇒ 本条**红**）；
    ///   · 两批合起来恰是 `{("RTA",1), ("RTB",1)}`；
    ///   · **每批内部**全部工序同一 `(RouteCode, PathId)`，且两道工序齐全（每批一条**完整** Path，不得半条）。
    /// </summary>
    [Fact]
    public async Task 逐批选路_两批可选不同RoutePath()
    {
        var request = BuildTwoPathRequest(
            qty: 10m,
            batchPolicies: new List<BatchPolicyRuleSnapshot> { Policy(max: 6m) });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        var byBatch = result.FinalTasks
            .Where(t => t.SourceDraftId == "D1")
            .GroupBy(t => t.ExecutionBatchDraftKey!)
            .ToDictionary(g => g.Key, g => g.ToList());

        Assert.Equal(2, byBatch.Count);
        Assert.Contains("EB|D1|001", byBatch.Keys);
        Assert.Contains("EB|D1|002", byBatch.Keys);

        // 每批 = 一条**完整** Path：全部工序同一 (RouteCode, PathId)，且 STAGE1/STAGE2 齐全。
        foreach (var (_, tasks) in byBatch)
        {
            var pairs = tasks.Select(t => (t.RouteCode, t.PathId)).Distinct().ToList();
            Assert.Single(pairs);
            Assert.Contains(tasks, t => t.StageCode == "STAGE1");
            Assert.Contains(tasks, t => t.StageCode == "STAGE2");
        }

        var pair001 = (byBatch["EB|D1|001"][0].RouteCode, byBatch["EB|D1|001"][0].PathId);
        var pair002 = (byBatch["EB|D1|002"][0].RouteCode, byBatch["EB|D1|002"][0].PathId);

        // 反证点：两批**必须**走不同路径（需求级选路 ⇒ 两批同路径 ⇒ 本条红）。
        Assert.NotEqual(pair001, pair002);

        // 两批合起来恰是两条候选路径，且按确定性 tiebreak：001 → RTA，002 → RTB。
        Assert.Equal(
            new (string?, long?)[] { ("RTA", 1L), ("RTB", 1L) },
            new (string?, long?)[] { pair001, pair002 });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  I. 有界优化候选（P1-01）—— 0号位 (7).md §八
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **P1-01 机制层**：候选枚举**有界**，且 `PreferredBatchQty` / `MaxOptimizationSplitCount` /
    ///   `MaxBatchCandidates` **真正被消费**（0号位 (7).md §八：三个字段「已经属于当前正式 Batch Policy」）。
    ///
    /// 构造：`Qty=12 / Max=5 / AllowSplit` ⇒ `nMin = ceil(12/5) = 3`；`Min` 缺省 ⇒ `nMax = ∞`。
    ///   · `PreferredBatchQty=2` ⇒ `nPref = round(12/2) = 6`（合法 ⇒ 进候选）；
    ///   · 合法不拆（1）/ 2 批均 &lt; `nMin=3` ⇒ **越界被丢**（不得因优化候选越界就把需求判冲突）。
    ///   期望候选批数序列 = `[3, 6]`（首元素 = 基线）。
    ///
    /// 反证点：三个上限字段若未被消费（旧实现「未真正参与候选搜索 / 未消费」），
    ///   `[3,6]` 与两条 `Single` 断言**全部红**。
    /// </summary>
    [Fact]
    public void 优化候选_有界且消费Preferred与上限()
    {
        var demand = Demand(netQty: 12m, routeCode: Route, pathId: Path);
        var policy = Policy(max: 5m, preferred: 2m);

        var baseline = PhaseTwoInitialScheduler.FormExecutionBatches(demand, policy);
        Assert.True(baseline.IsLegal, baseline.ConflictReason);
        Assert.Equal(3, baseline.Batches.Count);

        // 候选 = 基线(3) + Preferred 附近(6)；1 / 2 批越界被丢
        var list = PhaseTwoInitialScheduler.EnumerateLegalBatchPlanCandidates(demand, policy, baseline);
        Assert.Equal(new[] { 3, 6 }, list.Select(f => f.Batches.Count));

        // 上限①：MaxOptimizationSplitCount=4 ⇒ 优化候选 6 > 4 被裁掉 ⇒ 只剩基线
        var cappedBySplit = PhaseTwoInitialScheduler.EnumerateLegalBatchPlanCandidates(
            demand, Policy(max: 5m, preferred: 2m, maxOptSplit: 4), baseline);
        Assert.Single(cappedBySplit);
        Assert.Equal(3, cappedBySplit[0].Batches.Count);

        // 上限②：MaxBatchCandidates=1 ⇒ 只评估基线
        var cappedByCandidates = PhaseTwoInitialScheduler.EnumerateLegalBatchPlanCandidates(
            demand, Policy(max: 5m, preferred: 2m, maxCandidates: 1), baseline);
        Assert.Single(cappedByCandidates);
        Assert.Equal(3, cappedByCandidates[0].Batches.Count);

        // 不展开的条件：无策略 / A/B
        Assert.Single(PhaseTwoInitialScheduler.EnumerateLegalBatchPlanCandidates(demand, null, baseline));

        var abDemand = Demand(netQty: 12m, routeCode: Route, pathId: Path,
            isContinuation: true, noSplitMerge: true);
        var abBaseline = PhaseTwoInitialScheduler.FormExecutionBatches(abDemand, policy);
        Assert.Single(abBaseline.Batches);
        Assert.Single(PhaseTwoInitialScheduler.EnumerateLegalBatchPlanCandidates(abDemand, policy, abBaseline));
    }

    /// <summary>
    /// **P1-01 求解层反证**：当**基线（最小合法批数）装不下**、而**更大批数装得下**时，
    ///   Phase2 必须取更大的合法批数**直接排下**，而不是把需求整份丢给 Phase4。
    ///
    /// 构造：`Qty=10 / Max=6 / AllowSplit` ⇒ 合法批数 `{2, 3, 4, …}`，基线 = **2 批（各 5 件）**；
    ///   单件 10 分钟 ⇒ 基线每工序 50 分钟，而日历**只有 40 分钟槽**（12 个，两两相隔 20 分钟）
    ///   ⇒ 基线**装不下**（`batchFailed`）；**3 批**（各 ≈3.33 件 ⇒ 33.3 分钟）**装得下**。
    ///
    /// 反证点：候选 == 1（旧实现：只取 `nMin`、无优化候选）⇒ 需求整体 Unscheduled、无 Task
    ///   ⇒ `Assert.Empty(result.UnscheduledTasks)` 与 `Assert.Equal(3, byBatch.Count)` **红**。
    ///   （`FiniteCapacityParameters.AllowSplit` 保持 false ⇒ Phase4 无 Split 手段，无法替代 Phase2 排下。）
    /// </summary>
    [Fact]
    public async Task 优化候选_基线装不下时取更大合法批数()
    {
        // 12 个 40 分钟可用槽、两两相隔 20 分钟：50 分钟工序装不下，33.3 分钟装得下。
        var slots = new List<ResourceCalendarSlot>();
        for (int i = 0; i < 12; i++)
        {
            var start = PlanningStart.AddHours(8 + i);
            slots.Add(new ResourceCalendarSlot
            {
                ResourceId = 1, Start = start, End = start.AddMinutes(40), IsAvailable = true
            });
        }

        var request = BuildRequest(
            qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new List<BatchPolicyRuleSnapshot> { Policy(max: 6m) },
            opDuration: 10m,     // 基线 2 批 × 5 件 = 50 分钟 > 单槽 40 分钟
            calendarSlots: slots);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        var byBatch = result.FinalTasks
            .Where(t => t.SourceDraftId == "D1")
            .GroupBy(t => t.ExecutionBatchDraftKey!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 反证点：旧实现（只取 nMin=2）⇒ 0 批、需求 Unscheduled。
        Assert.Equal(3, byBatch.Count);
        Assert.Equal(new[] { "EB|D1|001", "EB|D1|002", "EB|D1|003" },
            byBatch.Keys.OrderBy(k => k, StringComparer.Ordinal));

        // 每批各走完整两工序；每批数量均在 [Min, Max] 内。
        Assert.All(byBatch.Values, tasks =>
        {
            Assert.Contains(tasks, t => t.StageCode == "STAGE1");
            Assert.Contains(tasks, t => t.StageCode == "STAGE2");
            Assert.All(tasks, t => Assert.InRange(t.Quantity, 0m, 6m));
        });

        // 数量守恒（逐分不丢）：各批 STAGE1 数量之和 = 需求数量 10。
        Assert.Equal(10m,
            byBatch.Values.SelectMany(t => t).Where(t => t.StageCode == "STAGE1").Sum(t => t.Quantity));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  K. §十二 端到端反证（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》）
    //
    //  0号位 明文：「以下测试必须调用正式 `FiniteCapacitySolver.SolveAsync()`，不能只测 Helper」
    //  ⇒ 除第 8 行（守卫本身需要**手工构造重复输入**才可能被触发，见该用例说明）外，
    //     全部经**正式求解入口**驱动，不得以「内部 Helper 断言」替代。
    //
    //  第 1 行（C 桶无有效 Policy ⇒ `BATCH_POLICY_MISSING`，0 FinalTask）已落地于 §G：
    //    <see cref="缺策略_端到端FailClosed_零FinalTask且Reason为BATCH_POLICY_MISSING"/>。
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// **§十二 第 2 行**：`Qty=10 / Min=6 / Max=6` ⇒ `BATCH_POLICY_CONFLICT`，**Phase4 不得排下**。
    ///
    /// 反证性：旧实现 Phase2 判冲突后只把原因写进 `BatchPolicyConflicts`（字符串表），Phase4 不读该表
    ///   ⇒ 把需求当「资源/日历没排下」走**普通 Local Repair** 重排 ⇒ 产出**绕过 Min/Max 硬约束**的
    ///   FinalTask（10 件整批或 5+5 都非法）⇒ `Assert.Empty(result.FinalTasks)` **红**。
    /// </summary>
    [Fact]
    public async Task 反证2_Qty10_Min6_Max6_端到端Conflict且Phase4不得排下()
    {
        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new[] { Policy(min: 6m, max: 6m) });

        var result = await _solver.SolveAsync(request);

        // §五：硬失败 ⇒ 不产 FinalTask，且**不得**被 Phase4 普通修复绕过
        Assert.Empty(result.FinalTasks);

        var unscheduled = Assert.Single(result.UnscheduledTasks);
        Assert.Equal("D1", unscheduled.DraftId);
        Assert.Equal("BATCH_POLICY_CONFLICT", unscheduled.Reason);
    }

    /// <summary>
    /// **§十二 第 3 行**：`AllowSplit=false` 且 `Qty &gt; Max` ⇒ Conflict，**Phase4 不得强拆或整批排下**。
    ///
    /// 两侧同时关闭才构成完整反证：策略侧 `AllowSplit=false`（批决策侧，v5.1.9 §7.3 逐字「0 且 Qty&gt;Max
    ///   时返回 `BATCH_POLICY_CONFLICT`」）+ `FiniteCapacityParameters.AllowSplit=false`（Phase4 有限 Split 侧）。
    ///
    /// 反证性：旧实现 Phase4 不读冲突表 ⇒ 用有限 Split 把 10 件**强拆**排下，或按整批排下
    ///   ⇒ `Assert.Empty(result.FinalTasks)` **红**。
    /// </summary>
    [Fact]
    public async Task 反证3_不允许拆批且Qty超Max_端到端Conflict且不得强拆或整批排下()
    {
        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new[] { Policy(max: 4m, allowSplit: false) },
            allowSplit: false);

        var result = await _solver.SolveAsync(request);

        Assert.Empty(result.FinalTasks);

        var unscheduled = Assert.Single(result.UnscheduledTasks);
        Assert.Equal("D1", unscheduled.DraftId);
        Assert.Equal("BATCH_POLICY_CONFLICT", unscheduled.Reason);
    }

    /// <summary>
    /// **2026-10-08 复审 NEW-P0-01（0号位《未命名的Markdown文件 (2)(1).md》§二 / §十二）+ §十五 第 1、2 项**：
    ///   **Batch-001 成功、Batch-002 失败 ⇒ 部分落定 ⇒ 必须判 Domain 失败**。
    ///
    /// ⚠ **本用例此前断言 `Success == true`，那是我方按「代码实现困难」改出来的错口径，已撤销。**
    ///   冻结基线：`AllocationQty = Σ AllocationTaskShare中的预计合格产出份额`（v1.7:1052）；
    ///   且「TaskShare数量不闭合」属**有限产能硬错误 ⇒ 必须使 Domain 失败**（v1.7:1026-1034 / v1.6:790）。
    ///   0号位 §十二 明示：「无法按客户DueDate完成不是Solver失败」与「Allocation只排出一部分数量
    ///   且 TaskShare 不闭合也可以正式成功」**不是一个概念**。
    ///
    /// 构造（`Min=4 / Max=6` ⇒ 合法批数**唯一** n=2，无候选择优歧义；`Qty=10` ⇒ 两批各 5 件）：
    ///   日历**只有一段 10 小时**可用窗，而单批两工序链（5 件 × 60 分钟 × 2 工序）**恰好占满 10 小时**
    ///   ⇒ Phase2：Batch-001 **落定**、Batch-002 **落不下**（`failedBatchIndex = 1`）；
    ///   `Parameters.AllowSplit=false` ⇒ Phase4 无 Split 手段，Batch-002 **确定修不成**。
    ///
    /// 断言（§十五 第 2 项的机器证据）：
    ///   · **`Success == false`** —— 部分落定**不得**作为成功正式结果输出；
    ///   · 失败报文**精确落在数量闭合**上，且**同时**出现「声明量 10」与「落定量 5」
    ///     ⇒ 直接证明闭合目标**没有被降成 5**（降了就变成 `ΣShare=5 ≤ Quantity=5` ⇒ 静默通过 ⇒ 此处红）；
    ///   · 失败路径是「全有全无」（Phase5 既有设计）⇒ 不输出半截正式结果。
    /// </summary>
    [Fact]
    public async Task 反证4_5_6_首批成功次批失败_部分落定必须判Domain失败()
    {
        // 唯一可用窗 = 恰好容下**一批**两工序链：5 件 × 60 分钟 × 2 工序 = 10 小时（留 30 分钟边界余量，
        //   但远不足以再容下第二批的 10 小时 ⇒ 第二批必然落不下）。
        var window = new List<ResourceCalendarSlot>
        {
            new()
            {
                ResourceId = 1, Start = PlanningStart,
                End = PlanningStart.AddHours(10).AddMinutes(30), IsAvailable = true
            }
        };

        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new[] { Policy(min: 4m, max: 6m) },
            calendarSlots: window,
            allowSplit: false);   // 关掉 Phase4 有限 Split ⇒ 失败批确定修不成

        var result = await _solver.SolveAsync(request);

        // §十五 第 2 项：部分 Execution Batch **不得**经由「降低闭合目标」变成 Success
        Assert.False(result.Success, "部分落定（声明 10 / 落定 5）必须判 Domain 失败，不得作为成功结果输出");

        // 失败报文必须定位到数量闭合，且「声明量 10」与「落定量 5」同时在
        //   —— 闭合目标仍为**声明数量** 10 的机器证据（本用例只有 1 个参与构成的 Task ⇒ 份额恒为 10）
        Assert.Contains("ΣShare=10", result.ErrorMessage);
        Assert.Contains("超过 Quantity=5", result.ErrorMessage);

        // 全有全无：失败即不输出任何正式结果（避免下游拿到「半截成功」）
        Assert.Empty(result.FinalTasks);
        Assert.Empty(result.AllocationShares);
        Assert.Empty(result.UnscheduledTasks);
    }

    /// <summary>
    /// **2026-10-08 复审 NEW-P0-01 + §十五 第 2 项（第二种部分落定形态）**：
    ///   **两个执行批在 Phase2 都失败、Phase4 只修好其中一个 ⇒ 仍是部分落定 ⇒ 必须判 Domain 失败**。
    ///
    /// ⚠ 本用例此前断言 `Success == true` + `UnscheduledTasks` 单条，同属**已撤销的错口径**。
    ///
    /// 构造：`Qty=10 / Min=4 / Max=6` ⇒ 2 批（各 5 件）；`opDuration=10m` ⇒ 每批每工序 50 分钟，
    ///   而日历只有 **4 个 30 分钟槽**（两两相隔 30 分钟不可用）⇒ 无任何连续 50 分钟窗
    ///   ⇒ Phase2 **两批都落不下**（`failedBatchIndex = 0` ⇒ `FailedExecutionBatches = [001, 002]`）；
    ///   `AllowSplit=true` ⇒ Phase4 有限 Split 把每批每工序拆成 2×25 分钟 ⇒ **一批占 4 个槽**
    ///   ⇒ 只有 **1 个批**能被修好，另一个因槽位耗尽仍落不下。
    ///
    /// 断言：`Success == false`，且失败报文落在「ΣShare 超过落定 Task 的 Quantity」上
    ///   —— 声明量 10 被分摊到仅存的落定 Task（每件 Quantity=2.5）⇒ 第 3 项硬校验拒绝。
    ///   **不**断言 ΣShare 的具体数字：参与构成的 Task 按 `FinalDraftId`（随机 Guid）排序，
    ///   份额在 {3.333, 3.333, 3.334} 之间随机落位 ⇒ 只有「超过」这一性质是稳定的。
    /// </summary>
    [Fact]
    public async Task 反证6_两批都失败只修好一批_部分落定仍必须判Domain失败()
    {
        // 4 个 30 分钟可用槽、两两相隔 30 分钟不可用 ⇒ 总可用 2 小时：
        //   恰好够「1 批 × 2 工序 × 拆 2 份 × 25 分钟 = 4 槽」，不够第 2 批。
        var slots = new List<ResourceCalendarSlot>();
        for (int i = 0; i < 4; i++)
        {
            var start = PlanningStart.AddHours(8 + i);
            slots.Add(new ResourceCalendarSlot
            {
                ResourceId = 1, Start = start, End = start.AddMinutes(30), IsAvailable = true
            });
        }

        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new[] { Policy(min: 4m, max: 6m) },
            opDuration: 10m,        // 每批每工序 50 分钟 > 单槽 30 分钟 ⇒ Phase2 两批均落不下
            calendarSlots: slots,
            allowSplit: true);      // Phase4 有限 Split 才能修好其中一批

        var result = await _solver.SolveAsync(request);

        Assert.False(result.Success, "只修好一个批（部分落定）必须判 Domain 失败");

        // 声明量 10 分摊到落定批的拆分件（Quantity=2.5）⇒ ΣShare > Quantity ⇒ 第 3 项硬校验拒绝。
        //   （若闭合目标被降成落定量 5，份额为 5/3=1.667 ≤ 2.5 ⇒ 不会失败 ⇒ 此处红。）
        Assert.Contains("ΣShare=", result.ErrorMessage);
        Assert.Contains("超过 Quantity=2.5", result.ErrorMessage);

        Assert.Empty(result.FinalTasks);
        Assert.Empty(result.AllocationShares);
        Assert.Empty(result.UnscheduledTasks);
    }

    /// <summary>
    /// **§十二 第 7 行**：`Qty=10 / Min=4 / Max=6` ⇒ 2 批**全部排下** ⇒
    ///   **每个工序上 Σ各 Execution Batch Quantity 严格等于需求对应数量 10**，
    ///   且**不存在**重复批身份下的第二套完整链。
    ///
    /// 与 <see cref="端到端_一个CDemand拆两批_两批各自完整链路"/> 的区别：本用例断言的是 **P0-04
    ///   最终物理数量闭合**（§七），即「逐工序 Σ = 需求数量」这一条此前**无任何守卫**的性质。
    /// </summary>
    [Fact]
    public async Task 反证7_两批全排下_逐工序数量严格闭合且无重复批链()
    {
        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new[] { Policy(min: 4m, max: 6m) });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 每个执行批恰好一条完整链（2 工序）—— 不存在同批键下的第二套链
        foreach (var batchGroup in result.FinalTasks.GroupBy(t => t.ExecutionBatchDraftKey!))
        {
            Assert.Equal(2, batchGroup.Count());
        }

        // §七 逐工序数量严格闭合：Σ各 Execution Batch Quantity = 需求 NetOutputQty = 10
        foreach (var stageGroup in result.FinalTasks.GroupBy(t => t.StageCode))
        {
            Assert.Equal(10m, stageGroup.Sum(t => t.Quantity));
        }
    }

    /// <summary>
    /// **2026-10-08 复审 NEW-P0-01 + §十五 第 1 项（正向半）**：
    ///   **全部落定**时，冻结等式 `AllocationQty = Σ AllocationTaskShare中的预计合格产出份额`
    ///   （v1.7:1052）必须在**份额层**严格成立，且每个 Task 的 ΣShare 不超过自身 Quantity。
    ///
    /// 与 <see cref="反证7_两批全排下_逐工序数量严格闭合且无重复批链"/> 的分工：反证7 断言的是
    ///   **Task 物理数量**闭合（逐工序 Σ=10）；本用例断言的是 **Allocation→份额**闭合（ΣShare=10），
    ///   两者是冻结文档里**两条不同的等式**。反向半（不等 ⇒ 必须判失败）由
    ///   <see cref="反证4_5_6_首批成功次批失败_部分落定必须判Domain失败"/> 提供。
    /// </summary>
    [Fact]
    public async Task NEW_P0_01_全部落定时_份额严格闭合到声明AllocationQty()
    {
        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new[] { Policy(min: 4m, max: 6m) });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // 本夹具只有 D1（AllocationSequence=1、NetOutputQty=10）⇒ ΣShare 必须严格等于 10
        Assert.All(result.AllocationShares, s => Assert.Equal(1L, s.AllocationSequence));
        Assert.Equal(10m, result.AllocationShares.Sum(s => s.ComponentQty));

        // 份额不得把某个 Task 压超自身数量（硬校验第 3 项在**成功路径**上同样成立）
        var taskQty = result.FinalTasks.ToDictionary(t => t.FinalDraftId, t => t.Quantity);
        foreach (var shareGroup in result.AllocationShares.GroupBy(s => s.FinalDraftId))
        {
            Assert.True(taskQty.ContainsKey(shareGroup.Key), $"份额指向不存在的 Task: {shareGroup.Key}");
            Assert.True(shareGroup.Sum(s => s.ComponentQty) <= taskQty[shareGroup.Key] + 0.001m);
        }
    }

    /// <summary>
    /// **§十二 第 8 行**：**重复 Batch ⇒ 必须被最终硬校验拒绝**。
    ///
    /// 分两段（缺一不可）：
    ///   **(a) 端到端不可达**：`Qty=10 / Min=4 / Max=6` ⇒ 2 批全排下；同一 (批键, 工序) 下**只有一个 Task**
    ///       —— P0-03 修好后（Phase4 只修失败批）「重复执行批链」在正式路径上**已不可达**；
    ///   **(b) 守卫本身必须有效**：用手工构造的**重复批链**（`EB|D1|001` 两套 + `EB|D1|002` 一套 ⇒ 逐工序
    ///       Σ=15 &gt; 需求 10）直接驱动 Phase5 最终硬校验 ⇒ 必须 `IsValid=false` 并报数量不闭合。
    ///
    /// 为何 (b) 不能走 `SolveAsync()`：守卫一旦「不可达」就永不被触发、也就永不被验证 ⇒ 只能给守卫本身
    ///   **造一次真实输入**。这不是绕过正式路径（另 8 行全部走 `SolveAsync()`），而是对守卫的直接证明。
    ///   任务时间窗刻意**互不重叠**，以免先撞上「资源互斥」检查而掩盖数量闭合检查。
    /// </summary>
    [Fact]
    public async Task 反证8_重复批链_被最终硬校验拒绝()
    {
        var request = BuildRequest(qty: 10m, demandRouteCode: Route, demandPathId: Path,
            batchPolicies: new[] { Policy(min: 4m, max: 6m) });

        // ── (a) 正式路径：重复批链不可达 ──
        var result = await _solver.SolveAsync(request);
        Assert.True(result.Success, result.ErrorMessage);

        foreach (var group in result.FinalTasks.GroupBy(t => (t.ExecutionBatchDraftKey, t.StageCode)))
        {
            Assert.Single(group);
        }

        // ── (b) 守卫直接证明：手工构造「同批键两套完整链」 ──
        var constraints = new PhaseOneConstraintBuilder().BuildConstraints(request);

        FinalTaskDraft MakeTask(string stage, string op, string batchKey, int slot)
            => new()
            {
                SourceDraftId = "D1",
                MaterialId = MaterialId,
                FactoryId = 1,
                StageCode = stage,
                OperationCode = op,
                ResourceId = 1,
                RouteCode = Route,
                PathId = Path,
                Quantity = 5m,
                PlannedProcessQty = 5m,
                ExecutionBatchDraftKey = batchKey,
                PlannedStartTime = PlanningStart.AddHours(5 * slot),
                PlannedEndTime = PlanningStart.AddHours(5 * (slot + 1))
            };

        var duplicated = new List<FinalTaskDraft>
        {
            MakeTask("STAGE1", "OP10", "EB|D1|001", 0),
            MakeTask("STAGE2", "OP20", "EB|D1|001", 1),
            MakeTask("STAGE1", "OP10", "EB|D1|002", 2),
            MakeTask("STAGE2", "OP20", "EB|D1|002", 3),
            // 重复的第二套 EB|D1|001 完整链 —— 正是 P0-03「10 件产出 15 件」的症状
            MakeTask("STAGE1", "OP10", "EB|D1|001", 4),
            MakeTask("STAGE2", "OP20", "EB|D1|001", 5)
        };

        var (isValidScheduled, errorScheduled) = PhaseFiveCompression.ValidateHardResultForTest(
            duplicated,
            new List<AllocationTaskShare>(),
            new List<FinalTaskPeggingDraft>(),
            request, constraints,
            new HashSet<string>(StringComparer.Ordinal),   // D1 视为「已排定」⇒ 必须**严格**闭合
            new Dictionary<string, List<(string DemandKey, decimal ShareQty)>>());

        Assert.False(isValidScheduled);
        Assert.Contains("数量未闭合", errorScheduled, StringComparison.Ordinal);

        // 同一构造在「需求未排定」口径下：允许小于、**绝不允许放大** ⇒ 同样必须拒绝
        var (isValidUnscheduled, errorUnscheduled) = PhaseFiveCompression.ValidateHardResultForTest(
            duplicated,
            new List<AllocationTaskShare>(),
            new List<FinalTaskPeggingDraft>(),
            request, constraints,
            new HashSet<string>(new[] { "D1" }, StringComparer.Ordinal),
            new Dictionary<string, List<(string DemandKey, decimal ShareQty)>>());

        Assert.False(isValidUnscheduled);
        Assert.Contains("被放大", errorUnscheduled, StringComparison.Ordinal);
    }

    /// <summary>
    /// **§十二 第 9 行**：`AllowMerge=true` + multiBatch ⇒ **不得在入口处无条件禁用**。
    ///
    /// 0号位 §八 同时明令「**不得简单删 `!multiBatch`**」（Execution Batch 已成正式身份）⇒ 本用例锁死
    ///   整改后的**身份保持**语义，并给出「简单删除」这条错解的反证点：
    ///   构造 D2（单批，`Qty=5`）先排、D1（多批，`Qty=10 / Min=4 / Max=6` ⇒ 2 批）后到，
    ///   **同物料、同工序、同 Route/Path** ⇒ D1 的批**有已归批的目标可合并**。
    ///   · 简单删除 `!multiBatch` ⇒ D1 的 `EB|D1|001` 被并进 D2 的 Task（沿用它 `EB|D2|001`）⇒
    ///     D1 只剩 1 个批键 ⇒ **断言 ② 红**；
    ///   · 整改后（入口解禁 + 目标侧身份保持）⇒ 目标已归批、拒绝合并 ⇒ D1 两批身份完整 ⇒ 绿。
    ///
    /// ⚠ **如实登记的缺口**（不降目标、不假装达标）：本用例**不能**证明「multiBatch 合并真的发生」——
    ///   现有 `FinalTaskDraft.ExecutionBatchDraftKey` 为**单值**、`AllocationTaskShare` 与 Phase5
    ///   `mergeLineage` **均无批键** ⇒「一个 Task 承载两个 Execution Batch 身份」在当前载体上无法表达，
    ///   而正常装配路径下**所有生产 Task 均带批键** ⇒ 多批需求**没有**合法的未归批合并目标。
    ///   第二半（让 multiBatch 合并真正可用）需**载体升级** ⇒ 载体归 2号位、语义归 0号位，已出件提请。
    /// </summary>
    [Fact]
    public async Task 反证9_AllowMerge开且多批_入口不得整类禁用且批身份不得被吞()
    {
        var request = BuildSingleOpMergeRequest(d1Qty: 10m, d2Qty: 5m);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // ① 每个 Task 的批键必须**属于其自身需求**（不得出现「Task 装 D1 的数量、却带 D2 的批键」）
        Assert.All(result.FinalTasks, t => Assert.StartsWith(
            $"EB|{t.SourceDraftId}|", t.ExecutionBatchDraftKey ?? string.Empty, StringComparison.Ordinal));

        // ② D1 的两个批身份必须**都还在**（简单删除 `!multiBatch` ⇒ `EB|D1|001` 被 D2 的 Task 吞掉 ⇒ 红）
        var d1Keys = result.FinalTasks
            .Where(t => t.SourceDraftId == "D1")
            .Select(t => t.ExecutionBatchDraftKey!)
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "EB|D1|001", "EB|D1|002" }, d1Keys);

        // ③ 数量闭合（单工序 ⇒ 逐需求 Σ = 需求数量）
        Assert.Equal(10m, result.FinalTasks.Where(t => t.SourceDraftId == "D1").Sum(t => t.Quantity));
        Assert.Equal(5m, result.FinalTasks.Where(t => t.SourceDraftId == "D2").Sum(t => t.Quantity));
    }

    /// <summary>
    /// §十二 第 9 行夹具：**单工序**（`OP10@STAGE1`，资源 1）+ 两个**同物料**需求
    ///   （Merge 要求 `operations.Count == 1` 且同 `MaterialId`）。
    ///   顺序：`D2`（`AllocationSequence=1`）先排、`D1`（`=2`）后到 ⇒ D1 的批有「**已归批**的目标」可合并。
    ///   策略 `Min=4 / Max=6` 对两需求同时成立：`D2 Qty=5` ⇒ 1 批；`D1 Qty=10` ⇒ 2 批。
    /// </summary>
    private static DomainSolveRequest BuildSingleOpMergeRequest(decimal d1Qty, decimal d2Qty)
    {
        LogicalProductionDemand MergeDemand(string key, decimal qty, long allocSeq)
            => new()
            {
                LogicalDemandKey = key, PlanVersionId = 1L, DomainKey = "DOMAIN",
                AllocationSequence = allocSeq, DemandKey = key,
                MaterialId = MaterialId, FactoryId = 1,
                StartStageCode = "STAGE1",
                NetOutputQty = qty, PlannedProcessQty = qty,
                RequiredAvailableTime = PlanningStart.AddDays(20),
                DemandSequence = (int)allocSeq,
                RouteCode = Route, PathId = Path
            };

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                MergeDemand("D2", d2Qty, 1),
                MergeDemand("D1", d1Qty, 2)
            },
            RoutingOperations = new List<RoutingOperation>
            {
                new()
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = Route, PathId = Path,
                    OperationCode = "OP10", StageCode = "STAGE1",
                    StandardDuration = 60m, OperationPlanningMode = "FINITE_RESOURCE"
                }
            },
            RoutingDependencies = new List<RoutingDependency>(),
            OperationResourceEligibility = new List<OperationResourceEligibility>
            {
                new()
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = Route, PathId = Path,
                    OperationCode = "OP10", ResourceId = 1, Priority = 1, CapacityFactor = 1m
                }
            },
            MaterialStageDepartmentContexts = new List<MaterialStageDepartmentContextDto>
            {
                new() { MaterialId = MaterialId, StageCode = "STAGE1", ProductionDepartmentId = DeptId }
            },
            ExecutionConstraints = Array.Empty<ExecutionConstraint>(),
            Resources = new List<ResourceDefinition>
            {
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = PlanningStart, End = PlanningEnd, IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = true,    // §八：Merge 的正式控制源是 Batch Policy，此处同步打开旧全局参数
                    AllowSplit = false
                },
                BatchPolicies = new List<BatchPolicyRuleSnapshot>
                {
                    Policy(min: 4m, max: 6m, allowMerge: true)
                }
            }
        };
    }
}
