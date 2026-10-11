using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;
using RoutingOperation = LPS.APS.Core.Entities.APS.RoutingOperation;
using RoutingDependency = LPS.APS.Core.Entities.APS.RoutingDependency;
using OperationResourceEligibility = LPS.APS.Core.Entities.APS.OperationResourceEligibility;

namespace LPS.APS.Tests.Benchmarks;

/// <summary>
/// 性能基准专用 `[Fact]`：**默认 Skip**（不进 CI），设环境变量 `LPS_BENCH_RUN=1` 即启用。
/// 这样工装**无需改代码**即可运行（旧版要求「注释掉 Skip」，改完容易忘记还原）。
/// </summary>
public sealed class BenchmarkFactAttribute : FactAttribute
{
    public BenchmarkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LPS_BENCH_RUN") != "1")
        {
            Skip = "性能基准：设 LPS_BENCH_RUN=1 启用（见类注释）。默认不进 CI。";
        }
    }
}

/// <summary>
/// 性能标定 / profiling 工装。
///
/// 【本轮为什么重写（0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§八 / §十一 / §十四 第三优先级）】
///   0号位 判：现有 100k Benchmark ① **处于 Skip**、② 场景是**旧模型**（单 Operation / 单 Path /
///   `RouteCode="DEFAULT"` / 整 90 天单日历窗）、③ 更严重 —— **没有提供正式 `BatchPolicies`**，
///   而当前 Solver 已明确「C 桶无有效 Batch Policy ⇒ `BATCH_POLICY_MISSING` ⇒ Fail Closed」
///   ⇒ 旧工装**已不能代表当前正式 C 桶输入**，其结论（含 2026-09-20 的 173s/220s）**不得**作为
///   当前 Commit 的达标证据。
///   ⇒ 本轮工装分两类，**用途严格分开**：
///     · <see cref="BuildScenario"/>（**简化趋势基准**）：单 Operation / 单 Path / 大窗口。
///       **只**用于纯算法趋势对照；**禁止**用于 15 分钟验收（§十一 明文）。
///     · <see cref="BuildRealisticScenario"/>（**正式基准**）：含 §十一 点名的全部要素 ——
///       `BatchPolicies` / 真实 RouteCode+PathId / 多 Routing 候选 / 1..N Execution Batch / 多 Operation /
///       真实班次 Calendar / Setup 规则 / PreferredResource / **A/B Continuation + C Free Slice 混合**。
///
/// 【profiling 输出（§九 / §十五 4-7）】
///   每 Phase 耗时 + 关键循环计数（BatchPlanCandidate 试跑 / Routing 试跑 / CloneOccupancy /
///   CloneTaskList / CloneShares / SlotSearch / Phase4 传播 Task 数）+ **业务结果签名**。
///   计数与计时来自 `LPS.APS.Scheduling.Solvers.SolverDiagnostics`（`internal`，经 `InternalsVisibleTo` 可见）
///   —— **不触碰任何 Core 契约 DTO**（Phase 级字段入 `DomainSolveResult` 属 Core 变更，归 2号位）。
///   业务结果签名（<see cref="BusinessSignature"/>）用于 §十五 第 7 项「优化前后同输入同输出一致性对照」。
///
/// 【默认 Skip】大型矩阵不进 CI。手动运行：
///   dotnet test LPS.APS.Tests/LPS.APS.Tests.csproj --filter "FullyQualifiedName~SolverBenchmark" --nologo
///   规模经环境变量覆盖：LPS_BENCH_RESOURCES / LPS_BENCH_FINAL_TASKS / LPS_BENCH_MAX_BATCH_QTY /
///   LPS_BENCH_CONTINUATION_RATIO / LPS_BENCH_MATERIAL_POOL
/// </summary>
public class SolverBenchmark
{
    private readonly ITestOutputHelper _output;

    public SolverBenchmark(ITestOutputHelper output)
    {
        _output = output;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 工装自检（常开，微型规模 < 2s）：场景构造器 + 求解链路可跑通
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>简化趋势基准的自检。</summary>
    [Fact]
    public async Task 工装自检_简化场景可跑通()
    {
        var request = BuildScenario(resources: 2, tasksPerResource: 5, ruleDensity: 0.1, "FORWARD", seed: 7);
        var result = await new FiniteCapacitySolver().SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(10, result.FinalTasks.Count);
        Assert.Empty(result.UnscheduledTasks);
    }

    /// <summary>
    /// **正式基准场景自检**（常开）：证明新工装①**不再 Fail Closed**（BatchPolicies 齐备）、
    /// ② 真实产生多 Routing / 多 Operation / 多 Execution Batch / A/B+C 混合。
    /// </summary>
    [Fact]
    public async Task 工装自检_正式场景可跑通()
    {
        var request = BuildRealisticScenario(finalTaskTarget: 60, resources: 4, seed: 20261008);
        var result = await new FiniteCapacitySolver().SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotEmpty(result.FinalTasks);

        // ① 多 Operation：每个需求 3 道工序
        Assert.True(result.FinalTasks.Select(t => t.OperationCode).Distinct().Count() >= 3,
            "正式场景必须覆盖多 Operation");
        // ② 真实 RouteCode / PathId（不得是 DEFAULT / 1 的退化形态）
        Assert.DoesNotContain(result.FinalTasks, t => t.RouteCode == "DEFAULT");
        Assert.True(result.FinalTasks.Select(t => (t.RouteCode, t.PathId)).Distinct().Count() >= 2,
            "正式场景必须覆盖多 Routing 候选");
        // ③ 多 Execution Batch（BatchPolicy.AllowSplit ⇒ 1..N 批）
        Assert.True(result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct().Count() > 1,
            "正式场景必须覆盖 1..N Execution Batch");
        // ④ A/B Continuation + C Free Slice 混合
        Assert.Contains(request.LogicalProductionDemands, d => d.IsContinuation);
        Assert.Contains(request.LogicalProductionDemands, d => !d.IsContinuation);

        // ⑤ **同参重放**（§十五 第 7 项的地基）：同输入再跑一次，业务签名必须一致。
        //    ⚠ 这条同时是 `BusinessSignature` 的自检 —— 首版签名用 `FinalDraftId`（随机 Guid）排序，
        //      跨进程/跨次不可比，该断言本会失败。
        var again = await new FiniteCapacitySolver().SolveAsync(
            BuildRealisticScenario(finalTaskTarget: 60, resources: 4, seed: 20261008));
        Assert.Equal(BusinessSignature(result), BusinessSignature(again));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 正式 profiling（默认 Skip；手动启用）
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// **正式 100k FinalTask / 90 天 FULL profiling**（§九 / §十五 4-6）。
    /// 输出每 Phase 耗时 + 关键循环计数 + 业务结果签名 —— 先定位 4 小时耗在哪，再改（§十四 第三优先级）。
    /// </summary>
    [BenchmarkFact]
    public async Task 正式基准_100kTask_90天FULL_profiling()
    {
        int resources = EnvInt("LPS_BENCH_RESOURCES", 200);
        int finalTasks = EnvInt("LPS_BENCH_FINAL_TASKS", 100_000);

        var request = BuildRealisticScenario(
            finalTaskTarget: finalTasks,
            resources: resources,
            seed: 20261008,
            maxBatchQty: EnvInt("LPS_BENCH_MAX_BATCH_QTY", 2),
            continuationRatio: EnvDouble("LPS_BENCH_CONTINUATION_RATIO", 0.30),
            preferredRatio: EnvDouble("LPS_BENCH_PREFERRED_RATIO", 0.20),
            materialPool: EnvInt("LPS_BENCH_MATERIAL_POOL", 500));

        await RunProfile("正式基准 100k/90天/FULL", request);
    }

    /// <summary>
    /// **规模扫描**（热点定位）：按 FinalTask 目标递增跑同一形态，观察单条成本是否随 N 上升
    /// （§十四 第三优先级「先找到 4 小时真正耗在哪里」）。
    /// </summary>
    [BenchmarkFact]
    public async Task 正式基准_规模扫描_热点定位()
    {
        // 档位可用 LPS_BENCH_SCAN_TARGETS 覆盖（逗号分隔），便于小步试跑。
        var targets = (Environment.GetEnvironmentVariable("LPS_BENCH_SCAN_TARGETS") ?? "2000,5000,10000,20000,40000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.Parse(t))
            .ToArray();

        // ⚠ **资源数必须钉住**才能读「单条成本 vs N」的斜率 —— 否则 resources 随 target 变（旧式 `target/500`）
        //   会让每资源负荷同步变化，曲线被两个变量混淆。用 LPS_BENCH_SCAN_RESOURCES 钉住。
        int fixedResources = EnvInt("LPS_BENCH_SCAN_RESOURCES", 0);

        foreach (int target in targets)
        {
            var request = BuildRealisticScenario(
                finalTaskTarget: target,
                resources: fixedResources > 0 ? fixedResources : Math.Max(8, target / 500),
                seed: 20261008,
                maxBatchQty: EnvInt("LPS_BENCH_MAX_BATCH_QTY", 2));

            await RunProfile($"规模扫描 FinalTask≈{target:N0} (resources={request.Resources.Count})", request);
        }
    }

    /// <summary>
    /// **简化趋势基准**（保留作纯算法趋势对照；**禁止**用于 15 分钟验收，§十一 明文）。
    /// </summary>
    [BenchmarkFact]
    public async Task 简化基准_趋势对照_FORWARD()
        => await RunProfile("简化趋势基准 FORWARD", BuildScenario(
            EnvInt("LPS_BENCH_RESOURCES", 50), EnvInt("LPS_BENCH_TASKS_PER_RESOURCE", 200),
            EnvDouble("LPS_BENCH_RULE_DENSITY", 0.05), "FORWARD", seed: 20260920));

    // ═══════════════════════════════════════════════════════════════════════
    // profiling 执行 + 报表
    // ═══════════════════════════════════════════════════════════════════════

    private async Task RunProfile(string label, DomainSolveRequest request)
    {
        var buildSw = Stopwatch.StartNew();
        int demandCount = request.LogicalProductionDemands.Count;
        buildSw.Stop();

        var totalSw = Stopwatch.StartNew();
        long allocBefore = GC.GetTotalAllocatedBytes(precise: false);
        int gc0Before = GC.CollectionCount(0), gc1Before = GC.CollectionCount(1), gc2Before = GC.CollectionCount(2);
        DomainSolveResult result;
        SolverDiagnostics.Counters counters;
        // 计数作用域必须在 SolveAsync **之前**建立（ExecutionContext 在 async 入口捕获引用）。
        using (var scope = SolverDiagnostics.BeginScope())
        {
            result = await new FiniteCapacitySolver().SolveAsync(request);
            counters = scope.Counters;
        }
        totalSw.Stop();
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - allocBefore;
        int gc0 = GC.CollectionCount(0) - gc0Before, gc1 = GC.CollectionCount(1) - gc1Before, gc2 = GC.CollectionCount(2) - gc2Before;

        long phaseSum = counters.Phase1Ms + counters.Phase2Ms + counters.Phase3Ms
                      + counters.Phase4Ms + counters.Phase5Ms;

        _output.WriteLine($"── [{label}] ──");
        _output.WriteLine($"  输入: DemandCount={demandCount:N0}  构造={buildSw.ElapsedMilliseconds:N0}ms");
        _output.WriteLine($"  输出: Success={result.Success}  FinalTaskCount={result.FinalTasks.Count:N0}"
                        + $"  Unscheduled={result.UnscheduledTasks.Count:N0}"
                        + $"  ExplanationFacts={result.ExplanationFacts.Count:N0}");
        _output.WriteLine($"  ExecutionBatchCount={ExecutionBatchCount(result):N0}"
                        + $"  RoutingUsedCount={RoutingUsedCount(result):N0}"
                        + $"  AllocationShares={result.AllocationShares.Count:N0}");
        _output.WriteLine($"  Phase1Ms={counters.Phase1Ms,10:N0}  Phase2Ms={counters.Phase2Ms,10:N0}"
                        + $"  Phase3Ms={counters.Phase3Ms,10:N0}");
        _output.WriteLine($"  Phase4Ms={counters.Phase4Ms,10:N0}  Phase5Ms={counters.Phase5Ms,10:N0}"
                        + $"  ΣPhase={phaseSum,10:N0}");
        _output.WriteLine($"  SolverMs(Summary)={result.Summary.ElapsedMs,10:N0}  TotalMs(含scope)={totalSw.ElapsedMilliseconds,10:N0}");
        _output.WriteLine($"  计数: BatchPlanCandidate试跑={counters.BatchPlanCandidateTrials:N0}"
                        + $"  Routing试跑={counters.RoutingTrials:N0}");
        _output.WriteLine($"        CloneOccupancy={counters.CloneOccupancy:N0}"
                        + $"  CloneTaskList={counters.CloneTaskList:N0}"
                        + $"  CloneShares={counters.CloneShares:N0}");
        _output.WriteLine($"        SlotSearch={counters.SlotSearches:N0}"
                        + $"  Phase4传播Task={counters.Phase4PropagatedTasks:N0}");
        // V1_3 F-04：本 Run **实际生效**的技术预算快照（版本 / 取源 / 生效值）—— 逐 Run 可复现的证据。
        _output.WriteLine($"  技术预算快照: version={counters.SolverBatchBudgetVersion ?? "(未登记)"}"
                        + $"  source={counters.SolverBatchBudgetSource ?? "(未登记)"}"
                        + $"  MaxOptimizationSplitCount={counters.SolverBatchBudgetMaxOptimizationSplitCount:N0}"
                        + $"  MaxBatchCandidates={counters.SolverBatchBudgetMaxBatchCandidates:N0}");
        _output.WriteLine($"  F-03门禁: PiLegalQty不可评估={counters.PiLegalQuantityGateUnevaluated:N0}"
                        + $"  判越限FailClosed={counters.PiLegalQuantityGateBlocked:N0}"
                        + $"  F-01回滚未命中={counters.OccupancyRollbackUnmatched:N0}");

        // ── **热点耗时排行**（谁最慢；含嵌套，故各项之和 > ΣPhase）──
        var hotspots = new (string Name, double Ms)[]
        {
            ("TimelineClone", counters.HotspotMs(SolverDiagnostics.Hotspot.TimelineClone)),
            ("CloneOccupancy", counters.HotspotMs(SolverDiagnostics.Hotspot.CloneOccupancy)),
            ("CloneTaskList", counters.HotspotMs(SolverDiagnostics.Hotspot.CloneTaskList)),
            ("CloneShares", counters.HotspotMs(SolverDiagnostics.Hotspot.CloneShares)),
            ("SlotSearch", counters.HotspotMs(SolverDiagnostics.Hotspot.SlotSearch)),
            ("BatchPlanTrial(含嵌套)", counters.HotspotMs(SolverDiagnostics.Hotspot.BatchPlanTrial)),
            ("DemandSchedule(含嵌套)", counters.HotspotMs(SolverDiagnostics.Hotspot.DemandSchedule)),
            ("Phase4Propagation", counters.HotspotMs(SolverDiagnostics.Hotspot.Phase4Propagation)),
        };
        foreach (var (name, ms) in hotspots.OrderByDescending(h => h.Ms))
        {
            double pct = phaseSum > 0 ? ms / phaseSum * 100.0 : 0;
            _output.WriteLine($"        [{name,-24}] {ms,12:N0} ms  ({pct,5:F1}% of ΣPhase)");
        }
        _output.WriteLine($"  业务签名: {BusinessSignature(result)}");
        _output.WriteLine($"  内存/GC: 分配={allocatedBytes / 1024.0 / 1024.0:N1} MB"
                        + $"  Gen0={gc0:N0}  Gen1={gc1:N0}  Gen2={gc2:N0}"
                        + $"  分配/单条={(result.FinalTasks.Count == 0 ? 0 : (double)allocatedBytes / result.FinalTasks.Count / 1024.0):N1} KB");
        _output.WriteLine($"  15分钟红线(900000ms): {(totalSw.ElapsedMilliseconds <= 900_000 ? "✅ 达标" : "❌ 超时")}"
                        + $"   单条成本={(result.FinalTasks.Count == 0 ? 0 : (double)totalSw.ElapsedMilliseconds / result.FinalTasks.Count):F2} ms/Task");
        _output.WriteLine("");

        Assert.True(result.Success, result.ErrorMessage);
    }

    /// <summary>FinalTask 上出现的**不同执行批键**数量（0 批键计 1 类）。</summary>
    private static int ExecutionBatchCount(DomainSolveResult r)
        => r.FinalTasks.Select(t => t.ExecutionBatchDraftKey ?? "<null>").Distinct(StringComparer.Ordinal).Count();

    /// <summary>FinalTask 上实际落定的**不同 (RouteCode, PathId)** 数量。</summary>
    private static int RoutingUsedCount(DomainSolveResult r)
        => r.FinalTasks.Select(t => (t.RouteCode, t.PathId)).Distinct().Count();

    /// <summary>
    /// **业务结果签名**（§十五 第 7 项：优化前后必须一致）。
    /// 覆盖 0号位 点名的「FinalTask数量、Route/Path、Resource、Start/End、TaskShare、Unscheduled」；
    /// 任一因「提速」发生未授权变化 ⇒ 签名不同 ⇒ 可被机器检出。
    ///
    /// ⚠ **不得使用 `FinalDraftId`**：它是 `Guid.NewGuid().ToString()`（`PhaseTwoInitialScheduler.cs:138`
    ///   等 5 处），**每次运行都不同** ⇒ 直接用它既让排序随机、又让签名跨进程不可比
    ///   （首版签名因此失效：同参两次跑计数/Σ 全同而 hash 不同）。
    ///   故任务身份改用**稳定业务复合键**（来源需求 + 工序 + 路径 + 批键 + 资源）。
    /// </summary>
    internal static string BusinessSignature(DomainSolveResult r)
    {
        string TaskKey(FinalTaskDraft t)
            => $"{t.SourceDraftId}|{t.OperationCode}|{t.StageCode}|{t.RouteCode}|{t.PathId}"
             + $"|{t.ExecutionBatchDraftKey}|{t.ResourceId}|{t.Quantity}";

        var tasks = string.Join(";", r.FinalTasks
            .Select(t => $"{TaskKey(t)}|{t.PlannedStartTime:O}|{t.PlannedEndTime:O}")
            .OrderBy(s => s, StringComparer.Ordinal));

        // 份额：把指向随机 Guid 的 FinalDraftId 翻译成稳定任务键，再排序。
        var keyById = r.FinalTasks.ToDictionary(t => t.FinalDraftId, TaskKey, StringComparer.Ordinal);
        var shares = string.Join(";", r.AllocationShares
            .Select(s => $"{s.AllocationSequence}|{(keyById.TryGetValue(s.FinalDraftId, out var k) ? k : "?")}|{s.ComponentQty}")
            .OrderBy(s => s, StringComparer.Ordinal));

        var unscheduled = string.Join(";", r.UnscheduledTasks
            .Select(u => $"{u.DraftId}|{u.Reason}")
            .OrderBy(s => s, StringComparer.Ordinal));

        return $"T={r.FinalTasks.Count};S={r.AllocationShares.Count};U={r.UnscheduledTasks.Count}"
             + $";ΣShare={r.AllocationShares.Sum(s => s.ComponentQty):F3}"
             + $";ΣQty={r.FinalTasks.Sum(t => t.Quantity):F3}"
             + $";hash={StableHash(tasks + "#" + shares + "#" + unscheduled)}";
    }

    /// <summary>FNV-1a 64 位（确定性、跨进程稳定；`string.GetHashCode` 有随机化，不可用于签名）。</summary>
    private static string StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h.ToString("X16");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 场景构造 A：**简化趋势基准**（单 Operation / 单 Path / 大窗口）
    //   只用于纯算法趋势对照；**不得**用于 15 分钟验收（§十一）。
    //   ⚠ 本轮**必须**补 `BatchPolicies`：否则 C 桶 `BATCH_POLICY_MISSING` Fail Closed ⇒ 工装不可用（§八）。
    // ═══════════════════════════════════════════════════════════════════════

    public static DomainSolveRequest BuildScenario(
        int resources, int tasksPerResource, double ruleDensity, string direction, long seed,
        int searchBudget = 500, int maxStaleTries = 50, int materialPool = 500)
    {
        var rng = new DeterministicRandom(seed);
        var planningStart = new DateTime(2026, 10, 1);
        var planningEnd = planningStart.AddDays(90);

        int demandCount = resources * tasksPerResource;
        int poolSize = Math.Min(materialPool, demandCount);

        var demands = new List<LogicalProductionDemand>(demandCount);
        var ops = new List<RoutingOperation>(poolSize);
        var els = new List<OperationResourceEligibility>(demandCount * 2);
        var deptContexts = new List<MaterialStageDepartmentContextDto>(poolSize);

        var durationByMaterial = new Dictionary<int, int>();
        for (int m = 1; m <= poolSize; m++)
        {
            int duration = 30 + rng.Next(31);   // 30~60 min
            durationByMaterial[m] = duration;
            ops.Add(new RoutingOperation
            {
                MaterialId = m,
                ProductionDepartmentId = 100,
                RouteCode = "DEFAULT",
                OperationCode = "OP10",
                StageCode = "STAGE1",
                StandardDuration = duration,
                SetupTime = 0m   // 已废止字段，置 0（规则查找是唯一 Setup 来源）
            });
            int primaryRes = (m % resources) + 1;
            int backupRes = ((m + 1) % resources) + 1;
            els.Add(MakeEligibility(m, primaryRes, 1));
            if (backupRes != primaryRes)
                els.Add(MakeEligibility(m, backupRes, 2));
            deptContexts.Add(new MaterialStageDepartmentContextDto
            {
                MaterialId = m,
                StageCode = "STAGE1",
                ProductionDepartmentId = 100
            });
        }

        for (int i = 0; i < demandCount; i++)
        {
            int materialId = (i % poolSize) + 1;          // 物料池循环 → 同产品重复生产
            int dueDay = 5 + rng.Next(81);                // 交期 5~85 天（部分紧张）

            demands.Add(new LogicalProductionDemand
            {
                LogicalDemandKey = $"D{i + 1}",
                PlanVersionId = 1L,
                DomainKey = "BENCH",
                AllocationSequence = i + 1,
                DemandKey = $"D{i + 1}",
                MaterialId = materialId,
                FactoryId = 1,
                NetOutputQty = 1m,
                PlannedProcessQty = 1m,
                RequiredAvailableTime = planningStart.AddDays(dueDay),
                DemandSequence = i + 1
            });
        }

        var resourceDefs = new List<ResourceDefinition>(resources);
        var calendarSlots = new List<ResourceCalendarSlot>(resources);
        for (int r = 1; r <= resources; r++)
        {
            resourceDefs.Add(new ResourceDefinition
            {
                ResourceId = r,
                ResourceCode = $"R{r}",
                FactoryCode = "F1",
                Capacity = 1m
            });
            // 简化基准口径：整计划期单窗口 7×24（**正式基准**用班次日历，见 BuildRealisticScenario）
            calendarSlots.Add(new ResourceCalendarSlot
            {
                ResourceId = r,
                Start = planningStart,
                End = planningEnd,
                IsAvailable = true
            });
        }

        var rules = BuildSetupRules(rng, resources, poolSize, ruleDensity, stages: 1);

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            ScheduleRunId = seed,
            DomainKey = "BENCH",
            PlanningStart = planningStart,
            PlanningEnd = planningEnd,
            LogicalProductionDemands = demands,
            RoutingOperations = ops,
            OperationResourceEligibility = els,
            MaterialStageDepartmentContexts = deptContexts,
            Resources = resourceDefs,
            CalendarSlots = calendarSlots,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = direction,
                    AllowMerge = false,
                    AllowSplit = false
                },
                SolverStrategy = new SolverStrategyBlock
                {
                    Setup = new SetupParams
                    {
                        SetupSearchBudget = searchBudget,
                        SetupMaxNeighborhoodTries = maxStaleTries
                    }
                },
                SetupTransitionRules = rules,
                // §八 P0-01：C 桶必须显式给出有效 Batch Policy，否则 `BATCH_POLICY_MISSING` Fail Closed。
                BatchPolicies = Enumerable.Range(1, poolSize)
                    .Select(m => new BatchPolicyRuleSnapshot
                    {
                        MaterialId = m,
                        ProductionDepartmentId = null,
                        MinExecutionBatchQty = null,
                        MaxExecutionBatchQty = null,
                        AllowSplit = false,
                        AllowMerge = false
                    })
                    .ToArray()
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 场景构造 B：**正式基准**（§十一 全要素）
    //   BatchPolicies / 真实 RouteCode+PathId / 多 Routing / 1..N Execution Batch /
    //   多 Operation / 真实班次 Calendar / Setup 规则 / PreferredResource / A/B+C 混合
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>正式场景形态常量：每个物料 3 道工序（3 个 Stage）。</summary>
    private static readonly (string Op, string Stage)[] Ops =
    {
        ("OP10", "STAGE1"), ("OP20", "STAGE2"), ("OP30", "STAGE3")
    };

    private const int Dept = 100;

    /// <summary>
    /// **正式 100k / 90 天 FULL 场景**（§十一）。
    /// </summary>
    /// <param name="finalTaskTarget">目标 FinalTask 数（需求数 = target / 3，每需求 3 工序）。</param>
    /// <param name="resources">资源（机台）数。</param>
    /// <param name="seed">确定性种子（同参同景可重放）。</param>
    /// <param name="maxBatchQty">`BatchPolicy.MaxExecutionBatchQty` ⇒ 需求批数 = `ceil(Qty / maxBatchQty)`（1..N）。</param>
    /// <param name="continuationRatio">A/B 桶（`IsContinuation`，固定 Route/Path）占比。</param>
    /// <param name="preferredRatio">带 `PreferredResourceCode` 软偏好的需求占比（仅 C 桶）。</param>
    /// <param name="materialPool">物料池大小（同产品重复度 = Setup 序列优化增益来源）。</param>
    /// <param name="ruleDensity">Setup EXACT 规则密度。</param>
    public static DomainSolveRequest BuildRealisticScenario(
        int finalTaskTarget,
        int resources,
        long seed,
        int maxBatchQty = 2,
        double continuationRatio = 0.30,
        double preferredRatio = 0.20,
        int materialPool = 500,
        double ruleDensity = 0.01)
    {
        var rng = new DeterministicRandom(seed);
        var planningStart = new DateTime(2026, 10, 1);
        var planningEnd = planningStart.AddDays(90);

        int demandCount = Math.Max(1, finalTaskTarget / Ops.Length);
        int poolSize = Math.Max(1, Math.Min(materialPool, demandCount));

        var ops = new List<RoutingOperation>(poolSize * Ops.Length * 2);
        var deps = new List<RoutingDependency>(poolSize * Ops.Length);
        var els = new List<OperationResourceEligibility>(poolSize * Ops.Length * 2);
        var deptContexts = new List<MaterialStageDepartmentContextDto>(poolSize * Ops.Length);
        var policies = new List<BatchPolicyRuleSnapshot>(poolSize);

        // 物料级主数据：每个物料 2 条 Path（PathId=1 主 / PathId=2 备），RouteCode 真实（非 DEFAULT）
        for (int m = 1; m <= poolSize; m++)
        {
            string routeCode = $"RT{m}";
            for (int s = 0; s < Ops.Length; s++)
            {
                var (opCode, stageCode) = Ops[s];
                deptContexts.Add(new MaterialStageDepartmentContextDto
                {
                    MaterialId = m, StageCode = stageCode, ProductionDepartmentId = Dept
                });

                for (int pathId = 1; pathId <= 2; pathId++)
                {
                    ops.Add(new RoutingOperation
                    {
                        MaterialId = m,
                        ProductionDepartmentId = Dept,
                        RouteCode = routeCode,
                        PathId = pathId,
                        OperationCode = opCode,
                        StageCode = stageCode,
                        StandardDuration = 20 + rng.Next(41),   // 20~60 min
                        OperationPlanningMode = "FINITE_RESOURCE",
                        SetupTime = 0m
                    });

                    els.Add(new OperationResourceEligibility
                    {
                        MaterialId = m,
                        ProductionDepartmentId = Dept,
                        RouteCode = routeCode,
                        PathId = pathId,
                        OperationCode = opCode,
                        ResourceId = ResourceFor(m, s, pathId, resources),
                        Priority = pathId == 1 ? 1 : 2,
                        CapacityFactor = 1m,
                        IsPrimary = pathId == 1
                    });
                }

                // 同 Path 内工序链（PathId 1 与 2 各一条）
                for (int pathId = 1; pathId <= 2; pathId++)
                {
                    if (s == 0) continue;
                    deps.Add(new RoutingDependency
                    {
                        MaterialId = m,
                        ProductionDepartmentId = Dept,
                        RouteCode = routeCode,
                        PathId = pathId,
                        FromOperationCode = Ops[s - 1].Op,
                        ToOperationCode = opCode
                    });
                }
            }

            // 1..N Execution Batch：AllowSplit + MaxExecutionBatchQty ⇒ 批数 = ceil(Qty / Max)
            policies.Add(new BatchPolicyRuleSnapshot
            {
                MaterialId = m,
                ProductionDepartmentId = null,
                MinExecutionBatchQty = null,
                MaxExecutionBatchQty = maxBatchQty,
                AllowSplit = true,
                AllowMerge = true
            });
        }

        var demands = new List<LogicalProductionDemand>(demandCount);
        for (int i = 0; i < demandCount; i++)
        {
            int materialId = (i % poolSize) + 1;
            int dueDay = 5 + rng.Next(81);
            // 数量 1..4 ⇒ 配合 MaxExecutionBatchQty 产生 1..N 执行批
            decimal qty = 1m + rng.Next(4);

            bool isContinuation = Ratio(rng) < continuationRatio;
            string routeCode = $"RT{materialId}";
            int pathId = 1 + rng.Next(2);

            demands.Add(new LogicalProductionDemand
            {
                LogicalDemandKey = $"D{i + 1}",
                PlanVersionId = 1L,
                DomainKey = "BENCH",
                AllocationSequence = i + 1,
                DemandKey = $"D{i + 1}",
                MaterialId = materialId,
                FactoryId = 1,
                NetOutputQty = qty,
                PlannedProcessQty = qty,
                RequiredAvailableTime = planningStart.AddDays(dueDay),
                DemandSequence = i + 1,
                // A/B 桶：连续份额，必须带**固定** Route/Path（否则 Fail Closed，见 P0-05）
                IsContinuation = isContinuation,
                NoSplitMerge = isContinuation,
                ContinuationKey = isContinuation ? $"CK{i + 1}" : null,
                RouteCode = isContinuation ? routeCode : null,
                PathId = isContinuation ? pathId : null,
                // C 桶软偏好（仅非连续需求；资源连续性软偏好，只进次级不 Hard Lock）
                PreferredResourceCode = !isContinuation && Ratio(rng) < preferredRatio
                    ? $"R{ResourceFor(materialId, 0, 1, resources)}"
                    : null
            });
        }

        var resourceDefs = new List<ResourceDefinition>(resources);
        var calendarSlots = new List<ResourceCalendarSlot>(resources * 180);
        for (int r = 1; r <= resources; r++)
        {
            resourceDefs.Add(new ResourceDefinition
            {
                ResourceId = r, ResourceCode = $"R{r}", FactoryCode = "F1", Capacity = 1m
            });

            // **真实班次 Calendar**：每天两班 08:00-12:00 / 13:00-17:00，共 90 天 × 2 = 180 窗
            for (int d = 0; d < 90; d++)
            {
                var day = planningStart.AddDays(d);
                calendarSlots.Add(new ResourceCalendarSlot
                {
                    ResourceId = r, Start = day.AddHours(8), End = day.AddHours(12), IsAvailable = true
                });
                calendarSlots.Add(new ResourceCalendarSlot
                {
                    ResourceId = r, Start = day.AddHours(13), End = day.AddHours(17), IsAvailable = true
                });
            }
        }

        var rules = BuildSetupRules(rng, resources, poolSize, ruleDensity, stages: Ops.Length);

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            ScheduleRunId = seed,
            DomainKey = "BENCH",
            PlanningStart = planningStart,
            PlanningEnd = planningEnd,
            LogicalProductionDemands = demands,
            RoutingOperations = ops,
            RoutingDependencies = deps,
            OperationResourceEligibility = els,
            MaterialStageDepartmentContexts = deptContexts,
            ExecutionConstraints = Array.Empty<ExecutionConstraint>(),
            Resources = resourceDefs,
            CalendarSlots = calendarSlots,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "AUTO",   // 正式默认：AUTO 由上下文自决（B-005）
                    AllowMerge = true,
                    AllowSplit = true
                },
                SolverStrategy = new SolverStrategyBlock
                {
                    Setup = new SetupParams
                    {
                        SetupSearchBudget = 500,
                        SetupMaxNeighborhoodTries = 50
                    }
                },
                SetupTransitionRules = rules,
                BatchPolicies = policies.ToArray()
            }
        };
    }

    /// <summary>确定性资源分配：同物料同工序的 Path1/Path2 落在**不同**资源上。</summary>
    private static int ResourceFor(int materialId, int stageIndex, int pathId, int resources)
        => ((materialId * 7 + stageIndex * 13 + pathId * 101) % resources) + 1;

    /// <summary>`DeterministicRandom` → [0,1) 比例（LCG 只有整数接口，这里做确定性折算）。</summary>
    private static double Ratio(DeterministicRandom rng) => rng.Next(10_000) / 10_000.0;

    /// <summary>Setup 规则：EXACT 物料对按密度 + 每（资源 × Stage × OP）DEFAULT=30。</summary>
    private static List<SetupTransitionRuleSnapshot> BuildSetupRules(
        DeterministicRandom rng, int resources, int poolSize, double ruleDensity, int stages)
    {
        var rules = new List<SetupTransitionRuleSnapshot>();

        // EXACT 只在 STAGE1/OP10 上铺（规则面 = 物料池；同产品重复生产形态下全部有效）
        for (int a = 1; a <= poolSize; a++)
        {
            for (int b = 1; b <= poolSize; b++)
            {
                if (a == b) continue;
                if (rng.Next(10000) >= ruleDensity * 10000) continue;
                rules.Add(new SetupTransitionRuleSnapshot
                {
                    ProductionDepartmentId = Dept,
                    StageCode = "STAGE1",
                    OperationCode = "OP10",
                    ResourceId = (a % resources) + 1,
                    FromMaterialId = a,
                    ToMaterialId = b,
                    RuleType = "EXACT",
                    SetupMinutes = 10 + rng.Next(51)
                });
            }
        }

        // 每（资源 × Stage × OP）一条 DEFAULT，保证**任意**资源上的换型都有兜底规则（不静默 0）
        for (int r = 1; r <= resources; r++)
        {
            for (int s = 0; s < stages; s++)
            {
                var (opCode, stageCode) = Ops[s];
                rules.Add(new SetupTransitionRuleSnapshot
                {
                    ProductionDepartmentId = Dept,
                    StageCode = stageCode,
                    OperationCode = opCode,
                    ResourceId = r,
                    RuleType = "DEFAULT",
                    SetupMinutes = 30m
                });
            }
        }

        return rules;
    }

    private static OperationResourceEligibility MakeEligibility(int materialId, int resourceId, int priority)
        => new OperationResourceEligibility
        {
            MaterialId = materialId,
            ProductionDepartmentId = 100,
            RouteCode = "DEFAULT",
            OperationCode = "OP10",
            ResourceId = resourceId,
            Priority = priority,
            CapacityFactor = 1m
        };

    private static int EnvInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

    private static double EnvDouble(string name, double fallback)
        => double.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
}
