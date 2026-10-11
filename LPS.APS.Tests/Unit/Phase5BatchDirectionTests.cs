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
/// **NEW-P1-01**：同一 `LogicalDemand` 拆多执行批、各批**各自落定不同 Direction** 时，
///   Phase5 必须**按 Task 所归执行批**（`ExecutionBatchDraftKey`）消费方向，
///   分别**保护 BACKWARD 批**、**优化 FORWARD 批** —— 不得退化为「同一需求一个方向」。
///
/// 复审判词（0号位 2026-10-09《APS_V1_3_20261009.md》§三 NEW-P1-01）：
///   ① `RunBatchPlan` 循环结束仅返回**一个** `BatchPlanRunOutcome.Direction`（= **最后一个落定批**的方向）；
///   ② 调用方仅保存 `ResolvedDirections[demand.LogicalDemandKey] = outcome.Direction`；
///   ③ Phase5 `IsForwardDemand()` **仅按需求 Key** 读取方向 ⇒ Gap Compaction 与 Setup 用它决定
///      **所有** Task 的可移动性。
///   风险几何（复审原文）：Batch-001 选长 Lead Path 而 AUTO 解析 `BACKWARD`，Batch-002 选短 Lead Path
///      解析 `FORWARD`；若第二批最后落定，**全需求**被登记为 `FORWARD` ⇒ Phase5 可能把第一批
///      应保护的 JIT 片段当作前推/Setup 重排候选（反之则错误限制优化）。
///
/// 整改：Phase2 逐批留痕（`ConstraintContext.ResolvedBatchDirections`），Phase5 新增
///   `IsForwardTask`（批键 → 需求级 → Run 级三级回落）并在**门控 + 逐 Task 不可移动集**两处改为批级判定。
///
/// 本用例即复审 §三「反证」要求的完整 `SolveAsync` 端到端构造（纯内存夹具，不触库）。
/// </summary>
public class Phase5BatchDirectionTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1);
    private static readonly DateTime PlanningStart = Day;
    private static readonly DateTime PlanningEnd = Day.AddDays(30);

    private const int MaterialId = 1;
    private const int DeptId = 100;

    // ── 几何常量（推演见 BuildDivergentRequest 注释）──
    private static readonly DateTime WindowStart = Day.AddHours(8);    // Day+480min
    private static readonly DateTime WindowEnd = Day.AddHours(20);     // Day+1200min
    private static readonly DateTime Due = Day.AddMinutes(1140);       // Day+19:00

    private readonly FiniteCapacitySolver _solver = new();

    /// <summary>
    /// 造「同需求两批 · 两批方向分歧」几何。**推演**（全部为代码上可验的确定性结论）：
    ///
    /// · 路由：`RTA`（两道工序各 `StandardDuration=300` ⇒ lead 600）+ 独占资源 1；
    ///   `RTB`（各 `StandardDuration=30` ⇒ lead 60）+ 独占资源 2。两资源日历同窗 `[Day+8h, Day+20h]`。
    /// · 交期 `Due = Day+1140min`；`direction = AUTO` ⇒ 逐候选按**自身工序**自决：
    ///     – `RTA`：`slack = 1140 − 600 = 540 ∈ [0, 600]` ⇒ 无交期信号 ⇒ `NO_CONTEXT_SIGNAL` ⇒ **BACKWARD**；
    ///     – `RTB`：`slack = 1140 − 60 = 1080 > 60` ⇒ `DUE_LOOSE` ⇒ **FORWARD**。
    /// · 批域 `Min = Max = 1`、`AllowSplit = true`、需求 2 件 ⇒ **唯一合法**批数 = 2（各 1 件）⇒ 无批方案择优扰动。
    /// · 批序：
    ///     – Batch-001：两候选均可行且延期都为 0、**方向不同** ⇒ 第 ③ 层判平 ⇒ 确定性 `(RouteCode, PathId)`
    ///       序裁决 ⇒ 胜出 **RTA**（= BACKWARD 批），占用资源 1 的 `[540, 1140]`；
    ///     – Batch-002：资源 1 只剩 `[480,540]`（60min &lt; 600min）⇒ RTA 试排不可行 ⇒ 胜出 **RTB**
    ///       （= FORWARD 批）⇒ 资源 2 的 `[480, 510] + [510, 540]`。
    /// · 于是 `ResolvedDirections["D1"]` = **最后落定批**方向 = `FORWARD`，而 Batch-001 实为 `BACKWARD`
    ///   ⇒ 整改前 Phase5 会把 Batch-001 的 JIT 锚点当作可前推对象。
    /// </summary>
    private static DomainSolveRequest BuildDivergentRequest()
    {
        const string routeA = "RTA";
        const string routeB = "RTB";
        const int path = 1;

        var ops = new List<RoutingOperation>();
        var deps = new List<RoutingDependency>();
        var elig = new List<OperationResourceEligibility>();

        foreach (var (route, resourceId, duration) in new[] { (routeA, 1, 300m), (routeB, 2, 30m) })
        {
            foreach (var (code, stage) in new[] { ("OP10", "STAGE1"), ("OP20", "STAGE2") })
            {
                ops.Add(new RoutingOperation
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = route, PathId = path,
                    OperationCode = code, StageCode = stage,
                    StandardDuration = duration, OperationPlanningMode = "FINITE_RESOURCE"
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
                FromOperationCode = "OP10", ToOperationCode = "OP20",
                DependencyType = "ES", LagTime = 0m, IsActive = true
            });
        }

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
                    NetOutputQty = 2m, PlannedProcessQty = 2m,
                    RequiredAvailableTime = Due, DemandSequence = 1
                    // RouteCode / PathId 留空 ⇒ C 桶候选内择优（两条备选路径）。
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
                new() { ResourceId = 1, Start = WindowStart, End = WindowEnd, IsAvailable = true },
                new() { ResourceId = 2, Start = WindowStart, End = WindowEnd, IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "AUTO",   // ← B-005 逐候选自决
                    AllowMerge = false,
                    AllowSplit = false
                },
                // P0-01：C 桶必须显式给出有效 Batch Policy。
                //   `Min = Max = 1` + `AllowSplit = true` ⇒ 2 件需求**唯一合法**批数 = 2（各 1 件）。
                BatchPolicies = new[]
                {
                    new BatchPolicyRuleSnapshot
                    {
                        MaterialId = MaterialId,
                        // AUD-1-004（0号位 2026-10-10 §3）：必须**明确部门** ——
                        //   NULL 部门历史行在 Solver 里**不再**是「覆盖任意部门」的 Material 级默认。
                        ProductionDepartmentId = DeptId,
                        MinExecutionBatchQty = 1m,
                        MaxExecutionBatchQty = 1m,
                        AllowSplit = true,
                        AllowMerge = false
                    }
                }
            }
        };
    }

    /// <summary>
    /// **反证主体**：同一需求两批分别落定 `BACKWARD` / `FORWARD` ⇒ Phase5 必须
    ///   **保护 BACKWARD 批的 JIT 锚点（不提前）**、**FORWARD 批仍在可优化集合内**。
    ///
    /// 鉴别力：
    ///   · 位置：整改前 `ResolvedDirections["D1"] = FORWARD`（最后落定批）⇒ 压实把 BACKWARD 批的
    ///     `RTA/OP10` 从交期锚定的 `Day+9h` 前拉到窗口起点 `Day+8h` ⇒ 起点断言**红**；
    ///   · 段计数：整改前两条资源段都被当作可重排段 ⇒ `Phase5SetupSegmentsOptimized == 2`；
    ///     整改后 BACKWARD 批并入不可移动集 ⇒ 只剩 FORWARD 批一段 ⇒ `== 1`。
    /// </summary>
    [Fact]
    public async Task 同需求两批方向分歧_BACKWARD批不提前_FORWARD批仍可优化()
    {
        long compactionRuns, setupRuns, segments;
        DomainSolveResult result;
        using (var scope = SolverDiagnostics.BeginScope())
        {
            result = await _solver.SolveAsync(BuildDivergentRequest());
            compactionRuns = scope.Counters.Phase5CompactionRuns;
            setupRuns = scope.Counters.Phase5SetupOptimizationRuns;
            segments = scope.Counters.Phase5SetupSegmentsOptimized;
        }

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // ── ① Phase2：一需求两批、**批键互异**，且两批各占一条 Route（逐批独立选路）──
        var d1 = result.FinalTasks.Where(t => t.SourceDraftId == "D1").ToList();
        Assert.Equal(4, d1.Count);                                        // 2 批 × 2 工序

        var batchKeys = d1.Select(t => t.ExecutionBatchDraftKey).Distinct().ToList();
        Assert.Equal(2, batchKeys.Count);
        Assert.All(batchKeys, k => Assert.False(string.IsNullOrEmpty(k)));

        var byBatchKey = batchKeys.ToDictionary(
            k => k!,
            k => d1.Where(t => t.ExecutionBatchDraftKey == k).OrderBy(t => t.PlannedStartTime).ToList());

        var backwardBatch = byBatchKey.Values.Single(b => b.All(t => t.RouteCode == "RTA"));
        var forwardBatch = byBatchKey.Values.Single(b => b.All(t => t.RouteCode == "RTB"));

        // 每批内部工序齐全（一条**完整** Path）且同资源。
        Assert.Equal(new[] { "OP10", "OP20" }, backwardBatch.Select(t => t.OperationCode).ToArray());
        Assert.Equal(new[] { "OP10", "OP20" }, forwardBatch.Select(t => t.OperationCode).ToArray());
        Assert.All(backwardBatch, t => Assert.Equal(1, t.ResourceId));
        Assert.All(forwardBatch, t => Assert.Equal(2, t.ResourceId));

        // ── ② BACKWARD 批 = 交期锚定 JIT 片段，且**未被前拉** ──
        //   倒排批（lead 600）结束锚在交期 `Day+1140` ⇒ 起点 `Day+540`。
        //   ⚠ 资源 1 窗口起点为 `Day+480` ⇒ 若被错误当作 FORWARD 压实，起点会掉到 `Day+480`
        //     ⇒ 本断言正是鉴别点（整改前必红）。
        Assert.Equal(Day.AddMinutes(540), backwardBatch[0].PlannedStartTime);
        Assert.Equal(Day.AddMinutes(840), backwardBatch[0].PlannedEndTime);
        Assert.Equal(Day.AddMinutes(840), backwardBatch[1].PlannedStartTime);
        Assert.Equal(Due, backwardBatch[1].PlannedEndTime);

        // ── ③ FORWARD 批 = 最早可行位（窗口起点），即「前推批仍可参与有界优化」的落点 ──
        Assert.Equal(WindowStart, forwardBatch[0].PlannedStartTime);
        Assert.Equal(Day.AddMinutes(540), forwardBatch[1].PlannedEndTime);

        // ── ④ 门控放行 + **只有** FORWARD 批那一段进入优化体 ──
        //   整改前：两条资源段都是「可重排段」（BACKWARD 批被误当 FORWARD）⇒ segments == 2 ⇒ 红。
        Assert.True(compactionRuns >= 1, $"Phase5 压实未放行（计数器={compactionRuns}）—— 夹具不可达");
        Assert.True(setupRuns >= 1, $"Phase5 序列优化未放行（计数器={setupRuns}）—— 夹具不可达");
        Assert.Equal(1, segments);

        // ── ⑤ 数量 / TaskShare 闭合：两批各 1 件 ⇒ 逐工序 Σ = 需求 2 件 ──
        //   净产出按**末端工序**计量（两批各 1 件 ⇒ OP20 Σ = 2）；各批每道工序亦各承载 1 件。
        Assert.Equal(2m, d1.Where(t => t.OperationCode == "OP20").Sum(t => t.Quantity));
        Assert.Equal(2m, d1.Where(t => t.OperationCode == "OP10").Sum(t => t.Quantity));
        Assert.All(d1, t => Assert.Equal(1m, t.Quantity));

        // 份额闭合：D1 的全部 Task 上 Σ 份额 = 需求 2 件（`AllocationTaskShare` 按 `FinalDraftId` 归属，
        //   无 DemandKey 字段 ⇒ 以 D1 的 Task 身份集合筛选，`AllocationSequence = 1` 即本需求）。
        var d1DraftIds = d1.Select(t => t.FinalDraftId).ToHashSet(StringComparer.Ordinal);
        var d1Shares = result.AllocationShares.Where(s => d1DraftIds.Contains(s.FinalDraftId)).ToList();
        Assert.NotEmpty(d1Shares);
        Assert.All(d1Shares, s => Assert.Equal(1L, s.AllocationSequence));
        Assert.Equal(2m, d1Shares.Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // NEW-P1-01（续）· **Phase4 修复批的批级方向身份**（0号位 2026-10-09《APS_V1_4_20261009.md》§三）
    //
    // 复审判词（成立，静态调用链已证）：
    //   「`PhaseTwoInitialScheduler.cs:1249`/`:605-608`：批级方向**仅在 Phase2 拥有胜出候选时**登记」；
    //   「对 `PhaseFourLocalRepair.cs` 的源码检查**没有找到**写入 `ResolvedBatchDirections` 的逻辑」；
    //   「`PhaseFiveCompression.cs:392-400`：未查到批级方向时**直接回落** `ResolvedDirections[LogicalDemandKey]`
    //     乃至 Run 级方向」。
    //   ⇒ 可达风险：某批 Phase2 全候选不成功（无方向留痕）→ Phase4 修复出**带该批键**的 Task →
    //     Phase5 用**别的批 / Run 值**决定它能否前推/换序。
    //
    // 整改（本号位）：`PhaseFourLocalRepair` 两处逐批修复循环在落定后 `TryAdd` 本批**实际**方向
    //   （`SchedulingDirectionResolver.Forward`）。**不变量（代码可证，非猜测）**：Phase4 的**全部**放置路径
    //   都是正排 —— 两处循环均经 `TryResourceSwitch` 以 `earliestStart` 为下界、只调 `FindForwardSlot`
    //   （本文件放置调用 `:319`/`:1075`/`:1539` 全为 `FindForwardSlot`，**无任何倒排/交期锚定路径**）
    //   ⇒ 修复批的实际落定方向**唯一且恒为 FORWARD**。故此处是「登记本批可追溯的实际方向」，
    //   而非「方向不可判时的兜底猜测」（复审给的两条路里，我们走的是前者）。
    //
    // 本用例 = 复审 §三 要求的**完整 `SolveAsync` 反证**几何（纯内存夹具，不触库）：
    //   · 需求 2 件、批域 `Min = Max = 1` + `AllowSplit = true` ⇒ **唯一合法批数 = 2**（各 1 件）；
    //   · Run 级 `BACKWARD`（非 AUTO ⇒ 无候选自决扰动）；单条路由 `RT`（两道工序 OP10→OP20，
    //     各 `StandardDuration = 60` ⇒ lead 120），**两批争抢同一资源 1**；日历 `[Day+6h, Day+20h]`；
    //   · 交期 `Due = Day+9h` ⇒ 倒排锚定：
    //       – Batch-001（第 1 批）：结束锚 `Due`、起点 `Due−120 = Day+7h` ⇒ 占 `[Day+7h, Day+9h]`，
    //         落在日历内 ⇒ **Phase2 成功且 BACKWARD**；
    //       – Batch-002（第 2 批）：倒排需再往前一段 ⇒ 早于日历起点 `Day+6h` ⇒ **Phase2 无胜出候选**
    //         （批失败，登记进 `FailedExecutionBatches`，**无方向留痕**）；
    //       – Phase4 正排修复 Batch-002（`FindForwardSlot`，下界含既有占用）⇒ **落定方向 FORWARD**。
    //   · ⇒ 同一需求两批方向**确实不同**（BACKWARD / FORWARD），且方向分歧**由 Phase4 修复产生**。
    //
    // 鉴别力（整改前必红，**已按红线实测**）：整改前修复批查不到批级方向 ⇒ `IsForwardTask` 回落
    //   需求级 `BACKWARD` ⇒ 两个 Task 双双并入不可移动集 ⇒ Phase5 门控（`:430-431`）关闭
    //   ⇒ `Phase5CompactionRuns == 0`。整改后登记 FORWARD ⇒ 门控放行 ⇒ `>= 1`。
    //   （注意：本夹具资源无空档 ⇒ `CompactGaps` 本体可能 no-op；门控放行**必须**用
    //     `SolverDiagnostics` 计数器观测 —— 见 `Phase5CompactionTests` ④ 的如实说明。）
    // ════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Phase4修复批_须登记本批方向_Phase5门控按批放行且倒排批不被误动()
    {
        long compactionRuns;
        DomainSolveResult result;
        using (var scope = SolverDiagnostics.BeginScope())
        {
            result = await _solver.SolveAsync(BuildPhase4RepairRequest());
            compactionRuns = scope.Counters.Phase5CompactionRuns;
        }

        // ── ① 业务出口：无技术失败、**无 Unscheduled**（Phase4 已修复该批 ⇒ 出口按 `:114` 的判据
        //      不得再把 D1 报成未排程）──
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // ── ② 两批 × 两工序 = 4 Task，**批键互异且非空**（Phase4 修复的 Task 亦保留本批键）──
        var d1 = result.FinalTasks.Where(t => t.SourceDraftId == "D1").ToList();
        Assert.Equal(4, d1.Count);

        var batchKeys = d1.Select(t => t.ExecutionBatchDraftKey).Distinct().ToList();
        Assert.Equal(2, batchKeys.Count);
        Assert.All(batchKeys, k => Assert.False(string.IsNullOrEmpty(k)));

        var byBatchKey = batchKeys.ToDictionary(
            k => k!,
            k => d1.Where(t => t.ExecutionBatchDraftKey == k).OrderBy(t => t.PlannedStartTime).ToList());

        // ── ③ 归批：结束锚在交期的 = Phase2 落定的**倒排批**；另一批 = Phase4 修复批 ──
        var backwardBatch = byBatchKey.Values.Single(b => b.Max(t => t.PlannedEndTime) == Due2);
        var repairedBatch = byBatchKey.Values.Single(b => b.Max(t => t.PlannedEndTime) != Due2);

        Assert.NotEqual(backwardBatch[0].ExecutionBatchDraftKey, repairedBatch[0].ExecutionBatchDraftKey);

        // 两批各自一条**完整**工序链、同资源、各 1 件。
        Assert.Equal(new[] { "OP10", "OP20" }, backwardBatch.Select(t => t.OperationCode).ToArray());
        Assert.Equal(new[] { "OP10", "OP20" }, repairedBatch.Select(t => t.OperationCode).ToArray());
        Assert.All(d1, t => Assert.Equal(1, t.ResourceId));
        Assert.All(d1, t => Assert.Equal(1m, t.Quantity));

        // ── ④ 倒排批（BACKWARD）：`[Due−lead, Due]` 交期锚定，**且未被 Phase5 前拉** ──
        //   （若 Phase5 误把它当 FORWARD 压实，起点会掉向日历起点 `Day+6h` ⇒ 本断言红。）
        Assert.Equal(Due2, backwardBatch[^1].PlannedEndTime);
        Assert.Equal(Due2.AddMinutes(-120), backwardBatch[0].PlannedStartTime);

        // ── ⑤ 修复批（FORWARD）：正排落点在日历窗内、与倒排批在**同一资源上互不重叠** ──
        var detail = string.Join(" | ", byBatchKey.Values.Select(b =>
            $"[{b[0].ExecutionBatchDraftKey}] " + string.Join(", ", b.Select(t =>
                $"{t.OperationCode} {t.PlannedStartTime:MM-dd HH:mm}-{t.PlannedEndTime:HH:mm}"))));

        Assert.True(repairedBatch[0].PlannedStartTime >= Window2Start,
            $"修复批落点早于日历起点。{detail}");
        Assert.True(repairedBatch[^1].PlannedEndTime <= Window2End,
            $"修复批落点晚于日历终点。{detail}");

        // Level 0 硬约束：同一资源（Capacity=1）上四段占用两两不重叠（按起点排序后逐对校验）。
        var ordered = d1.OrderBy(t => t.PlannedStartTime).ThenBy(t => t.PlannedEndTime).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            Assert.True(ordered[i].PlannedStartTime >= ordered[i - 1].PlannedEndTime,
                $"同资源时间重叠（硬约束破坏）：{detail}");
        }

        // ── ⑥ **鉴别性观测（本反证主体）**：修复批的正排方向必须让 Phase5 门控**按批放行** ──
        //   整改前：修复批无批级方向 ⇒ 回落需求级 BACKWARD ⇒ 门控关闭 ⇒ 计数器 == 0（**已实测必红**）。
        Assert.True(compactionRuns >= 1,
            $"Phase5 压实未按批放行（计数器={compactionRuns}）—— Phase4 修复批的方向登记未生效");

        // ── ⑦ 数量 / TaskShare 闭合：两批各 1 件 ⇒ 逐工序 Σ = 需求 2 件 ──
        Assert.Equal(2m, d1.Where(t => t.OperationCode == "OP20").Sum(t => t.Quantity));
        var d1DraftIds = d1.Select(t => t.FinalDraftId).ToHashSet(StringComparer.Ordinal);
        var d1Shares = result.AllocationShares.Where(s => d1DraftIds.Contains(s.FinalDraftId)).ToList();
        Assert.NotEmpty(d1Shares);
        Assert.All(d1Shares, s => Assert.Equal(1L, s.AllocationSequence));
        Assert.Equal(2m, d1Shares.Sum(s => s.ComponentQty));
    }

    // ── 几何常量（推演见上方 ⑥ 段落注释）──
    private static readonly DateTime Due2 = Day.AddHours(9);          // Day+540min
    private static readonly DateTime Window2Start = Day.AddHours(6);  // Day+360min
    private static readonly DateTime Window2End = Day.AddHours(20);   // Day+1200min

    /// <summary>
    /// 造「Phase2 落一批 BACKWARD + Phase4 修复一批 FORWARD」几何。
    /// **推演**（确定性结论）：
    ///   · 单条路由 `RT`：`OP10(STAGE1) → OP20(STAGE2)`（`ES`/lag 0），各 `StandardDuration = 60`
    ///     ⇒ lead = 120；两工序**独占同一资源 1**（`Capacity = 1`）；日历 `[Day+6h, Day+20h]`。
    ///   · `direction = BACKWARD`（**非 AUTO** ⇒ 无候选自决扰动，方向确定可控）；`Due = Day+9h`。
    ///   · 批域 `Min = Max = 1` + `AllowSplit = true`、需求 2 件 ⇒ **唯一合法**批数 = 2（各 1 件）。
    ///   · Batch-001：倒排 ⇒ 结束锚 `Due`、起点 `Due−120 = Day+7h` ⇒ 占 `[Day+7h, Day+9h]`（在窗内）⇒ **成功**。
    ///   · Batch-002：再倒排一段会早于窗起点 `Day+6h` ⇒ 无可行位 ⇒ **Phase2 失败**（无方向留痕）。
    ///   · Phase4 `FindForwardSlot` 正排修复 Batch-002 ⇒ 落定方向 **FORWARD**（不早于既有占用）。
    /// </summary>
    private static DomainSolveRequest BuildPhase4RepairRequest()
    {
        const string route = "RT";
        const int path = 1;

        var ops = new List<RoutingOperation>();
        var deps = new List<RoutingDependency>();
        var elig = new List<OperationResourceEligibility>();

        foreach (var (code, stage) in new[] { ("OP10", "STAGE1"), ("OP20", "STAGE2") })
        {
            ops.Add(new RoutingOperation
            {
                MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                RouteCode = route, PathId = path,
                OperationCode = code, StageCode = stage,
                StandardDuration = 60m, OperationPlanningMode = "FINITE_RESOURCE"
            });
            elig.Add(new OperationResourceEligibility
            {
                MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                RouteCode = route, PathId = path,
                OperationCode = code, ResourceId = 1, Priority = 1, CapacityFactor = 1m
            });
        }

        deps.Add(new RoutingDependency
        {
            MaterialId = MaterialId, ProductionDepartmentId = DeptId,
            RouteCode = route, PathId = path,
            FromOperationCode = "OP10", ToOperationCode = "OP20",
            DependencyType = "ES", LagTime = 0m, IsActive = true
        });

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
                    NetOutputQty = 2m, PlannedProcessQty = 2m,
                    RequiredAvailableTime = Due2, DemandSequence = 1
                    // RouteCode / PathId 留空 ⇒ C 桶候选（本夹具**只有一条**路由 ⇒ 无候选择优扰动）。
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
                new() { ResourceId = 1, ResourceCode = "R1", FactoryCode = "F1", Capacity = 1m }
            },
            CalendarSlots = new List<ResourceCalendarSlot>
            {
                new() { ResourceId = 1, Start = Window2Start, End = Window2End, IsAvailable = true }
            },
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "BACKWARD",   // ← 固定倒排：批 1 必 BACKWARD，不给 AUTO 自决留扰动
                    AllowMerge = false,
                    AllowSplit = false
                },
                // P0-01：C 桶必须显式给出有效 Batch Policy。
                //   `Min = Max = 1` + `AllowSplit = true` ⇒ 2 件需求**唯一合法**批数 = 2（各 1 件）。
                BatchPolicies = new[]
                {
                    new BatchPolicyRuleSnapshot
                    {
                        MaterialId = MaterialId,
                        // AUD-1-004（0号位 2026-10-10 §3）：必须**明确部门** ——
                        //   NULL 部门历史行在 Solver 里**不再**是「覆盖任意部门」的 Material 级默认。
                        ProductionDepartmentId = DeptId,
                        MinExecutionBatchQty = 1m,
                        MaxExecutionBatchQty = 1m,
                        AllowSplit = true,
                        AllowMerge = false
                    }
                }
            }
        };
    }
}
