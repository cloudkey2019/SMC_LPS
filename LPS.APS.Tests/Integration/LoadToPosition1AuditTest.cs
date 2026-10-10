using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Application.Services;
using LPS.APS.Application.Services.Fixtures;
using LPS.APS.Application.Extensions;
using LPS.APS.BusinessRules.Extensions;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Extensions;
using LPS.APS.Scheduling.Extensions;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Models.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// **装载给 1号位 的整块载荷验收探针**（2026-10-09，用户口径：
/// 「就是整个装载给 1号位的，包括分桶的计算这些，我们需要保证不出问题」）。
///
/// 与 <see cref="StartOperationCodeProbeTest"/> 的区别：那个只跑 BFS 回路（到 voucher 为止，
/// **既不做分桶、也不装 Routing/部门/Stage 链**），所以它证明不了「交给 1号位 的东西是对的」。
/// 本探针走 <see cref="PeggingOrchestrator.RunPeggingOnlyAsync"/> —— 与生产
/// <c>ExecutePeggingWorkflowAsync</c> **共用同一段 <c>BuildSolveRequestAsync</c>**，
/// 差异只有一条：组出 <see cref="DomainSolveRequest"/> 就返回，**不调 Solver、不落库**。
///
/// 输出：`AUDIT ` 前缀的核对行（每条一个检查项 + 计数 + 违规样本）。
/// 判读原则：**计数行是事实，结论要人看**——探针不自动断言「通过」，因为多数项是
/// 「覆盖率/命中率」，阈值属业务口径（PM 裁决）而非代码常量。
/// </summary>
public class LoadToPosition1AuditTest
{
    private const int MaxSample = 5;

    private static int PlanVersionId =>
        int.TryParse(Environment.GetEnvironmentVariable("APS_PROBE_PLAN_VERSION"), out var v) ? v : 2;

    private static long StrategyProfileVersionId =>
        long.TryParse(Environment.GetEnvironmentVariable("APS_PROBE_STRATEGY_VERSION"), out var v) ? v : 811L;

    private static int TimeoutMinutes =>
        int.TryParse(Environment.GetEnvironmentVariable("APS_AUDIT_TIMEOUT_MIN"), out var v) ? v : 90;

    [Fact(DisplayName = "装载验收：交给 1号位 的 DomainSolveRequest 逐项核对（含连续性分桶）")]
    public async Task LoadToPosition1_PayloadAudit()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDatabaseServices(configuration);
        services.AddSchedulingServices();
        services.AddBusinessRuleServices();
        services.AddApplicationServices();
        // 生产 FrozenStrategySnapshotProvider 需要 3号位 治理仓储（StrategyProfileVersionRepository）才能装配冻结快照块
        services.AddGovernanceRepositories();
        // ⚠ **刻意不挂 Fixture**：`FrozenStrategySnapshotFixtureProvider` / `DemandPriorityFixtureProvider` 会让
        //   ⑦换型规则、⑧批量策略、⑤⑥护栏、策略参数全部走**写死的样例值**，
        //   于是「BatchPolicies=0 / SetupTransitionRules=0」这种结论到底是**真缺口**还是**Fixture 没给**就分不清。
        //   本探针要回答"交给 1号位 的东西对不对"，必须拿**生产 Provider**（读冻结快照）跑。
        // 默认不挂日志 Provider：探针自己出结论，不需要被日志 I/O 拖累（LoadSupplyPoolAsync 逐 PI 刷 Warning 是已知 I/O 杀手）。
        // `APS_PROBE_LOG=1` 时挂一个**只转发 Warning 及以上 + 我方埋点前缀**的极简 Console Sink（见下），
        // 用于拿各装载阶段的**分段耗时**——黑盒 160s 里哪一段占大头，光看总量看不出来。
        services.AddLogging(b =>
        {
            if (Environment.GetEnvironmentVariable("APS_PROBE_LOG") == "1")
                b.AddProvider(new ProbeConsoleLoggerProvider());
        });
        services.AddScoped<DatabaseConnectionManager>();

        var sp = services.BuildServiceProvider();
        var orchestrator = (PeggingOrchestrator)sp.GetRequiredService<IPeggingOrchestrator>();
        var conn = sp.GetRequiredService<DatabaseConnectionManager>();

        var domainKey = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT DomainKey FROM PlanVersion WHERE Id=@p", new { p = PlanVersionId }, db: DatabaseId.APS);
        var orderIds = (await conn.QueryAsync<long>(
            "SELECT Id FROM [Order] WHERE PlanVersionId=@p ORDER BY Id",
            new { p = PlanVersionId }, db: DatabaseId.APS)).ToList();

        Say($"Domain={domainKey} PlanVersionId={PlanVersionId} Orders={orderIds.Count}");

        var allOrderCount = orderIds.Count;   // 收窄前 = 本 PV 全量订单数（用于判定是否走了小样本）

        // ── 小样本定向跑（2026-10-09 用户口径：先从真实数据里挑「符合某条计算逻辑」的数据，
        //    跑起来定位问题，捋顺了再统一全量跑一次 —— 别每轮都全量）──
        //
        // 为什么必须做：全量 PV2 一轮 = `Orders=22,144` / 需求 6.4 万 / PI Position 11.8 万，
        //   装载耗时 **~200 秒**（见 §0 耗时行）。改一行代码跑一轮 200s 的迭代节奏没用。
        //
        // ⚠ 2026-10-09 实测更正：**订单收窄本身不会让装载变快**。初版以为「装载全按本批订单收窄」是想当然 ——
        //   实测小样本仍是 160s，因为两个最大头根本不看订单集：`LoadBomSnapshotAsync` 只收 `(planVersionId, ct)`，
        //   按整 PV 的 `ResolvedBOMNO` 装 ⇒ `MaterialUniverse` 是全 PV 的 ⇒ 5 个订单照样装出 118,151 条 PI 事实、
        //   5号位 PI Position 算掉 **105s**（占总 160s 的 65%）。分段耗时埋点见 `[Pegging][供给池阶段耗时]`。
        //   修法：`RunPeggingOnlyAsync(..., scopeBomToOrders: true)` ⇒ 见下方调用点的 `scopeBomToOrders`；
        //   BOM 宇宙随订单收窄后 160s → **46s**（PI 事实 118,151 → 25,748）。
        //   一句话：**只缩数据量、不改代码路径**这条性质，是修了 BOM 收窄之后才成立的，不是天生就有。
        // 用法：
        //   `APS_PROBE_ORDER_IDS=123,456,789`            —— 显式指定订单（最快、最可控，推荐）
        //   `APS_PROBE_SAMPLE=20`                        —— 自动挑 20 个「BOM 命中关注现象」的订单：
        //       子件在主档缺行 / 子件 SupplyMode=PURCHASE 且无 RoutingOperation
        //       ⇒ 恰好覆盖本轮修的两条路径。实测 6 秒内出样本。
        //   不设任何一个 ⇒ 全量（定稿验收用）。
        orderIds = await ScopeOrderIdsAsync(conn, orderIds);

        var now = DateTime.Now;
        var request = new PeggingExecutionRequest
        {
            PlanVersionId      = PlanVersionId,
            DomainKey          = domainKey ?? string.Empty,
            OrderIds           = orderIds,
            SnapshotAt         = now,
            FrozenWindowStart  = now,
            FrozenWindowEnd    = now.AddHours(2),
            AllowCrossFactory  = false,
            DefaultStrategy    = PeggingStrategyType.FIFO,
            MaxBomDepth        = 10,
            TimeoutSeconds     = 900,
            ExecutionMode      = "FULL_RUN",
            IsCandidate        = false,
            SchedulingContext  = new SchedulingContext
            {
                StrategyProfileVersionId = StrategyProfileVersionId,
                DataCutoffTime           = now,
                PlanHorizonStart         = now,
                PlanHorizonEnd           = now.AddDays(90)
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(TimeoutMinutes));
        // 小样本模式下把 BOM 宇宙一起收窄（否则 5 个订单仍要装整 PV 的 BOM ⇒ 118,151 条 PI 事实 ⇒ 光 5号位
        //   PI Position 计算就 105s，白等）。全量模式（未收窄订单）保持 scopeBomToOrders=false，与生产逐字一致。
        var payload = await orchestrator.RunPeggingOnlyAsync(
            request, cts.Token, scopeBomToOrders: orderIds.Count < allOrderCount);

        AuditPayload(payload);
        AuditGraph(payload.SolveRequest);
        AuditDepartmentsAndParams(payload.SolveRequest);

        // 需求物料的 PI 归属交叉核（DB 侧权威：PI 号 → 物料）
        var piMaterial = await LoadPiToMaterialMapAsync(conn);
        AuditPiOwnership(payload.SolveRequest.LogicalProductionDemands, piMaterial);

        // 分桶切不出的**逐条**证据（2026-10-09）：需求侧 PI 已能全中 PI Position，但 E 恒为 0 ⇒ 把命中的
        //   上下文原样 dump（E / Slices 明细 / Issue），别再从代码推。
        AuditContinuityContexts(payload);
    }

    /// <summary>
    /// 把订单集收窄成「小样本」（见调用点注释）。不设环境变量时原样返回全量。
    /// </summary>
    private static async Task<List<long>> ScopeOrderIdsAsync(
        DatabaseConnectionManager conn, List<long> allOrderIds)
    {
        var explicitIds = Environment.GetEnvironmentVariable("APS_PROBE_ORDER_IDS");
        if (!string.IsNullOrWhiteSpace(explicitIds))
        {
            var wanted = explicitIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var x) ? x : -1L)
                .Where(x => x > 0)
                .ToHashSet();
            var hit = allOrderIds.Where(wanted.Contains).ToList();
            Say($"⚠ 小样本定向跑：APS_PROBE_ORDER_IDS 指定 {wanted.Count} 个订单，" +
                $"本 PV 命中 {hit.Count} 个（未命中的 {wanted.Count - hit.Count} 个不在 PlanVersionId={PlanVersionId} 内）");
            return hit.Count > 0 ? hit : allOrderIds;
        }

        // ── 定向挑「在制」订单（2026-10-09）──
        // 背景：分桶=0 的合法输入只有「PI 在 MES 处于 IN_PROGRESS」的订单 —— PV2 实测 **284/22,144（1.3%）**。
        // 按「BOM 主档缺行」挑样本命中率 15.9%，n=5 期望命中 0.8 个 ⇒ **根本打不到分桶那条逻辑**（我踩过）。
        // 用法：`APS_PROBE_PICK=INPROGRESS`（可配 `APS_PROBE_SAMPLE=N` 限条数，默认 20）。
        if (string.Equals(Environment.GetEnvironmentVariable("APS_PROBE_PICK"), "INPROGRESS",
                          StringComparison.OrdinalIgnoreCase))
        {
            var pickN = int.TryParse(Environment.GetEnvironmentVariable("APS_PROBE_SAMPLE"), out var pn) && pn > 0
                ? pn : 20;
            var inProgress = (await conn.QueryAsync<long>(
                @"SELECT DISTINCT TOP (@n) o.Id
                  FROM [Order] o
                  INNER JOIN MESWorkOrderSnapshot m
                          ON m.ProductionInstructionNo = o.MTS_InstructionNo
                         AND m.WorkOrderStatus = 'IN_PROGRESS'
                  WHERE o.PlanVersionId = @pv
                  ORDER BY o.Id",
                new { n = pickN, pv = PlanVersionId },
                db: DatabaseId.APS, commandTimeout: 120)).ToList();
            Say($"⚠ 小样本定向跑：APS_PROBE_PICK=INPROGRESS 挑中 {inProgress.Count} 个「PI 在制」订单" +
                $"（口径：该订单 MTS_InstructionNo 在 MESWorkOrderSnapshot 有 IN_PROGRESS 工单；全 PV 仅 284 个）");
            return inProgress.Count > 0 ? inProgress : allOrderIds;
        }

        var sampleRaw = Environment.GetEnvironmentVariable("APS_PROBE_SAMPLE");
        if (!int.TryParse(sampleRaw, out var n) || n <= 0)
            return allOrderIds;

        // 关注现象（本轮修的两条路径）：
        //   ① BOM 子件在 `Material` 主档缺行（→ 旧实现在装载层被 ISNULL(...,0) 静默改成 MaterialId=0）；**稀有**
        //   ② BOM 子件 `SupplyMode='PURCHASE'` 且**无** `RoutingOperation`（→ 旧实现当自制件 ⇒ 凭空产生产需求）；
        //      「且无 Routing」是这一条的要害，不能省：采购件本就该没有工艺，有工艺的反而不是本路径的样本。
        //
        // ⚠ 2026-10-09 踩坑留痕：第一版把两条并成一个查询、`SupplyMode` 用**相关子查询 `EXISTS`**
        //   （1.8M 行 MSC × 84 万 BOM 行），30s 默认命令超时直接炸（`SqlException 执行超时已过期`）。
        //   改法：MSC 采购码集**先物化成临时表**（`SELECT DISTINCT … INTO #pc`），BOM 侧只做**等值 JOIN**；
        //   并且两阶段**分开跑、按需**（阶段②只在阶段①凑不满 N 时才跑）——阶段① 1.5s / 阶段② 6s。
        //   另：显式给 `commandTimeout: 120`，别再让默认 30s 把「查询重」伪装成「偶发失败」。
        const string sqlBatchNo =
            @"SELECT TOP 1 r.BatchNo
              FROM OrderBomRequestLink r
              INNER JOIN [Order] o ON o.Id = r.OrderId
              WHERE o.PlanVersionId = @pv
              ORDER BY r.SyncedAt DESC";

        var batchNo = await conn.QueryFirstOrDefaultAsync<string>(
            sqlBatchNo, new { pv = PlanVersionId }, db: DatabaseId.APS, commandTimeout: 120);

        if (string.IsNullOrWhiteSpace(batchNo))
        {
            Say($"⚠ 小样本定向跑：APS_PROBE_SAMPLE={n} 失效 —— 本 PV 无 OrderBomRequestLink.BatchNo，退回全量");
            return allOrderIds;
        }

        // 阶段①：主档缺行（稀有，优先取）。
        var picked = (await conn.QueryAsync<long>(
            @"SELECT DISTINCT TOP (@n) o.Id
              FROM [Order] o
              INNER JOIN OrderBomRequestLink r ON r.OrderId = o.Id
              INNER JOIN APS_BOM_RAW b ON b.BOMNO = r.ResolvedBOMNO AND b.BatchNo = @bn
              LEFT JOIN Material m ON m.MaterialCode = b.ChildMaterialCode
              WHERE o.PlanVersionId = @pv AND m.Id IS NULL
              ORDER BY o.Id",
            new { n, pv = PlanVersionId, bn = batchNo },
            db: DatabaseId.APS, commandTimeout: 120)).ToList();
        var fromStage1 = picked.Count;

        // 阶段②：采购件无工艺（补齐到 N）。
        if (picked.Count < n)
        {
            var topUp = (await conn.QueryAsync<long>(
                @"SELECT DISTINCT TOP (@n) o.Id
                  FROM [Order] o
                  INNER JOIN OrderBomRequestLink r ON r.OrderId = o.Id
                  INNER JOIN APS_BOM_RAW b ON b.BOMNO = r.ResolvedBOMNO AND b.BatchNo = @bn
                  INNER JOIN Material m ON m.MaterialCode = b.ChildMaterialCode
                  INNER JOIN (SELECT DISTINCT MaterialCode FROM MaterialSupplyContext
                              WHERE IsCurrent = 1 AND SupplyMode = 'PURCHASE') p
                          ON p.MaterialCode = b.ChildMaterialCode
                  LEFT JOIN RoutingOperation ro ON ro.MaterialId = m.Id
                  WHERE o.PlanVersionId = @pv AND ro.Id IS NULL
                  ORDER BY o.Id",
                new { n, pv = PlanVersionId, bn = batchNo },
                db: DatabaseId.APS, commandTimeout: 120)).ToList();

            var already = picked.ToHashSet();
            picked.AddRange(topUp.Where(id => !already.Contains(id)));
        }

        var sample = picked.Take(n).ToList();

        Say($"⚠ 小样本定向跑：APS_PROBE_SAMPLE={n} 自动挑中 {sample.Count} 个订单" +
            $"（阶段① 子件主档缺行={fromStage1} 个；阶段② 采购件子件无 Routing 补齐；BatchNo={batchNo}）");
        return sample.Count > 0 ? sample : allOrderIds;
    }

    // ─────────────────────────────────────────────────────────────────────
    // §1 载荷规模 + 分桶守恒 + E 重复计
    // ─────────────────────────────────────────────────────────────────────
    private static void AuditPayload(PeggingOrchestrator.PeggingPayload payload)
    {
        var req = payload.SolveRequest;
        var pre = payload.PreBucketingDemands;
        var post = req.LogicalProductionDemands;

        Say("──────── §0 规模 ────────");
        Say($"红线: 错误={payload.RedLineErrors.Count} {Join(payload.RedLineErrors)}");
        Say($"耗时: Pegging={payload.PeggingMs}ms 总(到载荷为止)={payload.TotalMs}ms");
        Say($"需求: 分桶前={pre.Count} 分桶后={post.Count} 差={post.Count - pre.Count}");
        Say($"需求量: ΣQ前={pre.Sum(d => d.NetOutputQty)} ΣQ后={post.Sum(d => d.NetOutputQty)} " +
            $"Σ工艺投入前={pre.Sum(d => d.PlannedProcessQty)} Σ后={post.Sum(d => d.PlannedProcessQty)}");
        Say($"供给分配: Allocation={req.AllocationLineage.Count} 物料需求链接={req.MaterialRequirementLinks.Count}");

        // ── 分桶守恒：源 key → 片 ──
        // 源 key 反解：Continuation 片 = key + "/WO:xxx"；Free 片 = key + "/FREE"；未切分 = 原 key。
        string SourceKeyOf(string sliceKey)
        {
            var i = sliceKey.IndexOf("/WO:", StringComparison.Ordinal);
            if (i >= 0) return sliceKey.Substring(0, i);
            if (sliceKey.EndsWith("/FREE", StringComparison.Ordinal))
                return sliceKey.Substring(0, sliceKey.Length - "/FREE".Length);
            return sliceKey;
        }

        var preByKey = pre.GroupBy(d => d.LogicalDemandKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.NetOutputQty), StringComparer.Ordinal);
        var postByKey = post.GroupBy(d => SourceKeyOf(d.LogicalDemandKey), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.NetOutputQty), StringComparer.Ordinal);

        var lost = preByKey.Keys.Where(k => !postByKey.ContainsKey(k)).ToList();
        var added = postByKey.Keys.Where(k => !preByKey.ContainsKey(k)).ToList();
        var inflated = new List<string>();
        var shrunk = new List<string>();
        foreach (var kv in preByKey)
        {
            if (!postByKey.TryGetValue(kv.Key, out var after)) continue;
            if (after - kv.Value > 0.001m) inflated.Add($"{kv.Key}(源{kv.Value}→片{after})");
            else if (kv.Value - after > 0.001m) shrunk.Add($"{kv.Key}(源{kv.Value}→片{after})");
        }

        Say("──────── §1 分桶守恒（一进多出：Σ片Q 应 == 源Q，除 E>Q 超量与 Q=0 缺口）────────");
        Say($"源需求键={preByKey.Count} 片侧源键={postByKey.Count} 守恒={preByKey.Count - inflated.Count - shrunk.Count} " +
            $"放大={inflated.Count} 缩水={shrunk.Count} 丢失={lost.Count} 凭空多出={added.Count}");
        if (inflated.Count > 0) Say($"  ⚠放大样本: {Join(inflated.Take(MaxSample))}");
        if (shrunk.Count > 0) Say($"  ⚠缩水样本: {Join(shrunk.Take(MaxSample))}");
        if (lost.Count > 0) Say($"  ⚠丢失样本: {Join(lost.Take(MaxSample))}");
        if (added.Count > 0) Say($"  ⚠凭空多出样本: {Join(added.Take(MaxSample))}");

        var cont = post.Where(d => d.IsContinuation).ToList();
        var free = post.Where(d => !d.IsContinuation && d.LogicalDemandKey.EndsWith("/FREE", StringComparison.Ordinal)).ToList();
        Say($"切片: Continuation={cont.Count} 片 ΣE={cont.Sum(d => d.NetOutputQty)}；" +
            $"Free={free.Count} 片 ΣQ={free.Sum(d => d.NetOutputQty)}；" +
            $"未切分={post.Count - cont.Count - free.Count}");
        Say($"超量事件: {payload.OverCommits.Count} 条 " +
            $"(EXECUTION_OVER_COMMIT={payload.OverCommits.Count(e => e.Kind == "EXECUTION_OVER_COMMIT")}, " +
            $"DEMAND_MISMATCH={payload.OverCommits.Count(e => e.Kind == "DEMAND_MISMATCH")})");
        foreach (var g in payload.OverCommits.GroupBy(e => e.Kind)
                     .SelectMany(g => g.Take(MaxSample).Select(e => $"[{e.Kind}] {e.Message}")))
            Say($"  ⚠{g}");

        // ── E 重复计：同一 PI 被多个源需求携带时，各需求独立切「该 PI 的全部上下文」 ⇒ E 会被重复放大 ──
        var activeEByPi = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var ctxCountByPi = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (pi, pos) in payload.PiPositions)
        {
            var act = (pos.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>())
                .Where(c => c.DerivedRemainingQty > 0m).ToList();
            if (act.Count == 0) continue;
            activeEByPi[pi] = act.Sum(c => c.DerivedRemainingQty);
            ctxCountByPi[pi] = act.Count;
        }
        var eOfPiFromSlices = cont.Where(d => !string.IsNullOrEmpty(d.ProductionInstructionNo))
            .GroupBy(d => d.ProductionInstructionNo!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.NetOutputQty), StringComparer.Ordinal);
        var dupPi = eOfPiFromSlices
            .Where(kv => activeEByPi.TryGetValue(kv.Key, out var real) && kv.Value - real > 0.001m)
            .Select(kv => $"PI={kv.Key} 片ΣE={kv.Value} 真实ΣE={activeEByPi[kv.Key]} 上下文数={ctxCountByPi[kv.Key]}")
            .ToList();
        var piWithMultiDemand = pre.Where(d => !string.IsNullOrEmpty(d.ProductionInstructionNo))
            .GroupBy(d => d.ProductionInstructionNo!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).ToList();

        Say("──────── §1b E 重复计（同 PI 被多需求携带 ⇒ 逐需求各切一遍全部上下文）────────");
        Say($"切出 Continuation 的 PI={eOfPiFromSlices.Count} 其中 片ΣE>真实ΣE 的 PI={dupPi.Count}；" +
            $"带 PI 的源需求按 PI 分组后 >1 条的 PI={piWithMultiDemand.Count}");
        foreach (var s in dupPi.Take(MaxSample)) Say($"  ⚠{s}");

        // ── 分桶为什么没切（Continuation=0 时必查）：需求侧的 PI 是否真的落在「有既存执行上下文」的 PI 上 ──
        var posWithCtx = payload.PiPositions
            .Where(kv => (kv.Value.ExistingExecutionContexts?.Count ?? 0) > 0)
            .Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        var posWithActiveCtx = payload.PiPositions
            .Where(kv => (kv.Value.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>())
                .Any(c => c.DerivedRemainingQty > 0m))
            .Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        var demandPiSet = pre.Where(d => !string.IsNullOrEmpty(d.ProductionInstructionNo))
            .Select(d => d.ProductionInstructionNo!).ToHashSet(StringComparer.Ordinal);
        Say($"分桶前置: PI Position={payload.PiPositions.Count} 其中带执行上下文={posWithCtx.Count} 带**活跃**(E>0)上下文={posWithActiveCtx.Count}");
        Say($"分桶前置: 源需求侧 PI={demandPiSet.Count}；与「带上下文 PI」交集={demandPiSet.Count(p => posWithCtx.Contains(p))} " +
            $"与「活跃上下文 PI」交集={demandPiSet.Count(p => posWithActiveCtx.Contains(p))}" +
            (cont.Count == 0 ? " ⚠（Continuation 为 0 的根因看这几个交集：分母非 0 而交集为 0 ⇒ 需求 PI 与 MES 工单 PI 不同源）" : ""));

        // ── Continuation 片 1号位 硬 Fail Closed 字段 ──
        var missCk = cont.Count(d => string.IsNullOrEmpty(d.ContinuationKey));
        var missRoute = cont.Count(d => string.IsNullOrEmpty(d.RouteCode));
        var missPath = cont.Count(d => d.PathId is null);
        var missStartOp = cont.Count(d => string.IsNullOrEmpty(d.StartOperationCode));
        var missNoSplit = cont.Count(d => !d.NoSplitMerge);
        Say("──────── §1c Continuation 片身份字段（缺任一 ⇒ 1号位 记 Unscheduled 且不产 Task）────────");
        Say($"片数={cont.Count} 缺ContinuationKey={missCk} 缺RouteCode={missRoute} 缺PathId={missPath} " +
            $"缺StartOperationCode={missStartOp} NoSplitMerge 未置={missNoSplit}");

        // ── 身份/数量自洽 ──
        say_identity();
        void say_identity()
        {
            var dupKey = post.GroupBy(d => d.LogicalDemandKey, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
            Say("──────── §2 需求身份自洽 ────────");
            Say($"LogicalDemandKey 重复={dupKey.Count} {Join(dupKey.Take(MaxSample).Select(g => $"×{g.Count()}"))}");
            Say($"MaterialId<=0={post.Count(d => d.MaterialId <= 0)} FactoryId<=0={post.Count(d => d.FactoryId <= 0)} " +
                $"样本: {Join(post.Where(d => d.MaterialId <= 0).Take(MaxSample).Select(d => $"mat={d.MaterialId} key={d.LogicalDemandKey} Q={d.NetOutputQty}"))} | " +
                $"NetOutputQty<=0={post.Count(d => d.NetOutputQty <= 0m)} PlannedProcessQty<=0={post.Count(d => d.PlannedProcessQty <= 0m)}");
            Say($"OrderId 空={post.Count(d => d.OrderId is null)} DomainKey 空={post.Count(d => string.IsNullOrEmpty(d.DomainKey))} " +
                $"StartStageCode 空={post.Count(d => string.IsNullOrEmpty(d.StartStageCode))} RequiredStageCode 空={post.Count(d => string.IsNullOrEmpty(d.RequiredStageCode))} " +
                $"StartOperationCode 空={post.Count(d => string.IsNullOrEmpty(d.StartOperationCode))} UOM 空={post.Count(d => string.IsNullOrEmpty(d.UOM))}");
            Say($"AllocationSequence<=0={post.Count(d => d.AllocationSequence <= 0)} DemandKey 空={post.Count(d => string.IsNullOrEmpty(d.DemandKey))}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // §4 工艺图（RoutingOperations / RoutingDependencies / Eligibility）
    // ─────────────────────────────────────────────────────────────────────
    private static void AuditGraph(DomainSolveRequest req)
    {
        Say("──────── §4 工艺图（交给 1号位 的节点/边/资源能力）────────");
        var ops = req.RoutingOperations;
        var deps = req.RoutingDependencies;
        var elig = req.OperationResourceEligibility;
        Say($"节点={ops.Count} 覆盖物料={ops.Select(o => o.MaterialId).Distinct().Count()} " +
            $"边={deps.Count} 覆盖物料={deps.Select(d => d.MaterialId).Distinct().Count()} 资源能力={elig.Count}");

        var dupNode = ops.GroupBy(o => (o.MaterialId, o.RouteCode, o.PathId, o.OperationCode))
            .Count(g => g.Count() > 1);
        Say($"节点键(Material,Route,Path,OpCode) 重复组={dupNode}" + (dupNode > 0 ? " ⚠" : ""));
        Say($"RouteCode 分布: {Join(ops.GroupBy(o => o.RouteCode).OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key}={g.Count()}"))}");

        // 逐 (Material,Route,Path) 建图：悬空边 / 无源结点 / 成环
        var nodeSet = ops.GroupBy(o => (o.MaterialId, o.RouteCode, o.PathId))
            .ToDictionary(g => g.Key, g => g.Select(o => o.OperationCode)
                .ToHashSet(StringComparer.Ordinal));
        var depByGraph = deps.GroupBy(d => (d.MaterialId, d.RouteCode, d.PathId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var dangling = 0;
        var danglingSample = new List<string>();
        foreach (var kv in depByGraph)
        {
            nodeSet.TryGetValue(kv.Key, out var nodes);
            foreach (var d in kv.Value)
            {
                var ok = nodes != null && nodes.Contains(d.FromOperationCode) && nodes.Contains(d.ToOperationCode);
                if (!ok)
                {
                    dangling++;
                    if (danglingSample.Count < MaxSample)
                        danglingSample.Add($"{kv.Key.MaterialId}/{kv.Key.RouteCode}/{kv.Key.PathId} {d.FromOperationCode}->{d.ToOperationCode}");
                }
            }
        }
        var noSource = 0;
        var cyclic = 0;
        var cyclicSample = new List<string>();
        var noSourceSample = new List<string>();
        foreach (var kv in nodeSet)
        {
            depByGraph.TryGetValue(kv.Key, out var edges);
            edges ??= new List<Core.Entities.APS.RoutingDependency>();
            var indeg = kv.Value.ToDictionary(x => x, _ => 0, StringComparer.Ordinal);
            var adj = kv.Value.ToDictionary(x => x, _ => new List<string>(), StringComparer.Ordinal);
            foreach (var e in edges)
            {
                if (!indeg.ContainsKey(e.ToOperationCode) || !adj.ContainsKey(e.FromOperationCode)) continue;
                adj[e.FromOperationCode].Add(e.ToOperationCode);
                indeg[e.ToOperationCode]++;
            }
            if (!indeg.Values.Any(v => v == 0)) { noSource++; if (noSourceSample.Count < MaxSample) noSourceSample.Add($"{kv.Key.MaterialId}/{kv.Key.RouteCode}/{kv.Key.PathId}"); }

            var queue = new Queue<string>(indeg.Where(x => x.Value == 0).Select(x => x.Key));
            var seen = 0;
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue(); seen++;
                foreach (var nx in adj[cur]) if (--indeg[nx] == 0) queue.Enqueue(nx);
            }
            if (seen != kv.Value.Count)
            {
                cyclic++;
                if (cyclicSample.Count < MaxSample) cyclicSample.Add($"{kv.Key.MaterialId}/{kv.Key.RouteCode}/{kv.Key.PathId} 图节点={kv.Value.Count} 可达={seen}");
            }
        }
        Say($"悬空边(端点不在同图节点集内)={dangling}" + (dangling > 0 ? " ⚠" : "") + $" {Join(danglingSample)}");
        Say($"无入边源结点的图={noSource}" + (noSource > 0 ? " ⚠（该图无起点，1号位 建不出可达前沿）" : "") + $" {Join(noSourceSample)}");
        Say($"成环图={cyclic}" + (cyclic > 0 ? " ⚠（Phase5 硬校验会整域全丢）" : "") + $" {Join(cyclicSample)}");

        // 工序无任何可用资源 ⇒ 有限产能下排不下
        var eligKeys = elig.Select(e => (e.MaterialId, e.RouteCode, e.PathId, e.OperationCode)).ToHashSet();
        var noRes = ops.Where(o => !eligKeys.Contains((o.MaterialId, o.RouteCode, o.PathId, o.OperationCode))).ToList();
        Say($"无资源能力记录的工序={noRes.Count}/{ops.Count}" +
            $" {Join(noRes.Take(MaxSample).Select(o => $"{o.MaterialId}/{o.OperationCode}"))}");

        // ── 这些"坏图"是否真会砸到需求上（成环 = Phase5 硬校验整域全丢）──
        var demandMaterials = req.LogicalProductionDemands.Select(d => d.MaterialId).ToHashSet();
        var cyclicMaterials = cyclicSample
            .Select(s => s.Split(' ').Skip(1).FirstOrDefault() ?? string.Empty)
            .Where(s => s.Contains('/'))
            .Select(s => int.TryParse(s.Split('/')[0], out var m) ? m : 0)
            .Where(m => m > 0).ToHashSet();
        var opsMaterials = ops.Select(o => o.MaterialId).ToHashSet();
        var noRoutingMaterials = demandMaterials.Where(m => !opsMaterials.Contains(m)).ToList();
        Say($"需求物料={demandMaterials.Count}；**其中无任何 Routing 节点={noRoutingMaterials.Count}**" +
            $" {Join(noRoutingMaterials.Take(MaxSample).Select(m => m.ToString()))}" +
            (noRoutingMaterials.Count > 0 ? " ⚠（该物料的新增需求 1号位 建不出工序 ⇒ Unscheduled）" : ""));
        Say($"成环图涉及物料={Join(cyclicMaterials.Select(m => m.ToString()))}；" +
            $"是否落在需求物料集内={Join(cyclicMaterials.Select(m => $"{m}:{(demandMaterials.Contains(m) ? "是⚠" : "否")}"))}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §5 部门上下文 / Stage 链 / 提前期 + §6 求解参数
    // ─────────────────────────────────────────────────────────────────────
    private static void AuditDepartmentsAndParams(DomainSolveRequest req)
    {
        var demands = req.LogicalProductionDemands;
        var msc = req.MaterialStageDepartmentContexts;
        var chains = req.StageSequenceChains;

        Say("──────── §5 部门上下文 / Stage 链 / 提前期 ────────");
        Say($"MaterialStageDeptContext 行={msc.Count} 覆盖(物料,Stage)={msc.Select(c => (c.MaterialId, c.StageCode)).Distinct().Count()} " +
            $"覆盖物料={msc.Select(c => c.MaterialId).Distinct().Count()}");
        Say($"Stage 链={chains.Count} 覆盖物料={chains.Select(c => c.MaterialId).Distinct().Count()} " +
            $"每物料多条的物料数={chains.GroupBy(c => c.MaterialId).Count(g => g.Count() > 1)}");
        Say($"StageLeadTime 行={req.StageLeadTimes.Count} 覆盖(物料,Stage)={req.StageLeadTimes.Select(l => (l.MaterialId, l.StageCode)).Distinct().Count()}");

        var chainByMat = chains.GroupBy(c => c.MaterialId).ToDictionary(g => g.Key, g => g.First().Stages);
        var dupStageInChain = 0;
        var unsortedChain = 0;
        foreach (var kv in chainByMat)
        {
            if (kv.Value.Select(s => s.StageCode).Distinct().Count() != kv.Value.Count) dupStageInChain++;
            for (var i = 1; i < kv.Value.Count; i++)
                if (kv.Value[i].StageSeq < kv.Value[i - 1].StageSeq) { unsortedChain++; break; }
        }
        Say($"链内 Stage 重复的物料={dupStageInChain} StageSeq 非升序的物料={unsortedChain}");

        var mscKeys = msc.Select(c => (c.MaterialId, c.StageCode)).ToHashSet();
        var startMiss = demands.Count(d => !string.IsNullOrEmpty(d.StartStageCode) && !mscKeys.Contains((d.MaterialId, d.StartStageCode)));
        var reqMiss = demands.Count(d => !string.IsNullOrEmpty(d.RequiredStageCode) && !mscKeys.Contains((d.MaterialId, d.RequiredStageCode!)));
        Say($"StartStageCode 无部门映射的需求={startMiss}/{demands.Count}" +
            $" RequiredStageCode 无部门映射的需求={reqMiss}" + (startMiss > 0 || reqMiss > 0 ? " ⚠（1号位 记 MISSING_PRODUCTION_DEPARTMENT_CONTEXT 并置 Unscheduled）" : ""));

        var noChain = demands.Count(d => !chainByMat.ContainsKey(d.MaterialId));
        Say($"无 Stage 链的需求物料={noChain}；需求物料数={demands.Select(d => d.MaterialId).Distinct().Count()}" +
            (noChain > 0 ? " ⚠" : ""));

        Say("──────── §6 求解参数 / 冻结块投影 ────────");
        var p = req.StrategySnapshot.Parameters;
        Say($"Parameters: AllowSplit={p.AllowSplit} AllowMerge={p.AllowMerge} Direction={p.SchedulingDirection} " +
            $"ImpactedTaskWarningPercent={p.ImpactedTaskWarningPercent} MaxPropagationRounds={p.MaxPropagationRounds} " +
            $"SplitAlternatives={p.SplitAlternatives} MinBatchQty={p.MinBatchQty}");
        Say($"Snapshot: StrategyProfileVersionId={req.StrategySnapshot.StrategyProfileVersionId} " +
            $"ParameterSetVersionId={req.StrategySnapshot.ParameterSetVersionId} " +
            $"SolverStrategy.Mode={req.StrategySnapshot.SolverStrategy?.Mode.ToString() ?? "(null)"} " +
            $"BatchPolicies={req.StrategySnapshot.BatchPolicies?.Count ?? 0} " +
            $"SetupTransitionRules={req.StrategySnapshot.SetupTransitionRules?.Count ?? 0}");
        Say($"Resources={req.Resources.Count} CalendarSlots={req.CalendarSlots.Count} ResourceEligibility={req.ResourceEligibility.Count}");
        Say($"ExecutionConstraints={req.ExecutionConstraints.Count}" +
            (req.ExecutionConstraints.Count == 0 ? " ⚠（PM §6.1 指出的未闭合项：本域上一版 ACTIVE 旧块未转锚点）" : ""));
        Say($"CandidateContext={(req.CandidateContext is null ? "null(FULL)" : "非空")} RunScope={(req.RunScope is null ? "null(FULL)" : "非空")} " +
            $"UpstreamDomainResourceBlocks={req.UpstreamDomainResourceBlocks.Count}");
        Say($"窗口: PlanningStart={req.PlanningStart:yyyy-MM-dd HH:mm} PlanningEnd={req.PlanningEnd:yyyy-MM-dd HH:mm} DataCutoffTime={req.DataCutoffTime:yyyy-MM-dd HH:mm}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // §3 PI 归属（串味核验）：载荷里带 PI 的需求，其 PI 是否真属于该物料
    // ─────────────────────────────────────────────────────────────────────
    private static void AuditPiOwnership(
        IReadOnlyList<LogicalProductionDemand> demands,
        IReadOnlyDictionary<string, int> piMaterial)
    {
        Say("──────── §3 PI 归属（跨物料串味核验）────────");
        var withPi = demands.Where(d => !string.IsNullOrEmpty(d.ProductionInstructionNo)).ToList();
        var multiMatInPayload = withPi.GroupBy(d => d.ProductionInstructionNo!, StringComparer.Ordinal)
            .Select(g => new { Pi = g.Key, Mats = g.Select(d => d.MaterialId).Distinct().Count() })
            .Where(x => x.Mats > 1).ToList();
        Say($"带 PI 的片={withPi.Count} 覆盖 PI={withPi.Select(d => d.ProductionInstructionNo).Distinct().Count()}；" +
            $"载荷内 同一 PI 挂多个 MaterialId 的 PI={multiMatInPayload.Count} " +
            $"{Join(multiMatInPayload.Take(MaxSample).Select(x => $"{x.Pi}×{x.Mats}"))}");

        var checkedCnt = 0;
        var mismatch = new List<string>();
        foreach (var d in withPi)
        {
            if (!piMaterial.TryGetValue(d.ProductionInstructionNo!, out var owner)) continue;
            checkedCnt++;
            if (owner != d.MaterialId)
                mismatch.Add($"PI={d.ProductionInstructionNo} 权威物料={owner} 载荷物料={d.MaterialId} key={d.LogicalDemandKey}");
        }
        Say($"可与 DB 权威(PI→物料)对上的片={checkedCnt}/{withPi.Count}；" +
            $"**物料不符={mismatch.Count}**" + (mismatch.Count > 0 ? " ⚠" : ""));
        foreach (var s in mismatch.Take(MaxSample)) Say($"  ⚠{s}");
    }

    /// <summary>DB 侧 PI → MaterialId 权威映射（与 LoadPiRemainingFactsAsync 同源同闸门口径）。</summary>
    private static async Task<IReadOnlyDictionary<string, int>> LoadPiToMaterialMapAsync(DatabaseConnectionManager conn)
    {
        var rows = await conn.QueryAsync<PiMatRow>(
            @"SELECT MTS_InstructionNo AS PI, MaterialId FROM [Order]
              WHERE PlanVersionId=@p AND MTS_InstructionNo IS NOT NULL AND MTS_InstructionNo <> ''
              GROUP BY MTS_InstructionNo, MaterialId",
            new { p = PlanVersionId }, db: DatabaseId.APS);

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows)
            if (!map.ContainsKey(r.PI)) map[r.PI] = r.MaterialId;   // 一号多物料时取首个，并由 §3 的「一PI多物料」计数暴露
        return map;
    }

    private sealed class PiMatRow
    {
        public string PI { get; set; } = string.Empty;
        public int MaterialId { get; set; }
    }

    private static void Say(string line) => Console.WriteLine("AUDIT " + line);

    /// <summary>
    /// §1d 分桶切不出的逐条证据：把「需求侧 PI 命中的 PI Position 上下文」原样 dump。
    /// 用途：需求侧 PI 已能全中 PI Position（交集=命中数），但 `DerivedRemainingQty` 恒 0 ⇒ 到底
    /// 是 5号位 算出来就是 0、还是 Slices 空、还是我读错字段，**看值不看代码**。
    /// </summary>
    private static void AuditContinuityContexts(PeggingOrchestrator.PeggingPayload payload)
    {
        var demandPis = payload.SolveRequest.LogicalProductionDemands
            .Select(d => d.ProductionInstructionNo)
            .Where(pi => !string.IsNullOrWhiteSpace(pi))
            .Select(pi => pi!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var matched = demandPis.Where(pi => payload.PiPositions.ContainsKey(pi)).ToList();

        Say("──────── §1d 分桶为何切不出：需求侧 PI 命中的上下文逐条 ────────");
        Say($"需求侧 PI={demandPis.Count} 命中 PI Position={matched.Count}");

        foreach (var pi in matched.Take(MaxSample))
        {
            var pos = payload.PiPositions[pi];
            var ctxs = pos.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>();
            Say($"PI={pi} 上下文数={ctxs.Count}");
            foreach (var c in ctxs.Take(3))
            {
                var slices = c.Slices ?? Array.Empty<ExistingExecutionSliceDto>();
                Say($"   工单={c.MESWorkOrderNo} 状态={c.WorkOrderStatus} E={c.DerivedRemainingQty} " +
                    $"Slices={slices.Count} ΣSliceQty={slices.Sum(s => s.SliceQty)} " +
                    $"起点Stage={c.StartStageCode ?? "<null>"} 起点工序={c.StartOperationCode ?? "<null>"} " +
                    $"Issue={c.IssueDescription ?? "-"}");
                foreach (var s in slices.Take(3))
                    Say($"      └Slice Stage={s.StartStageCode} 工序={s.StartOperationCode ?? "<null>"} " +
                        $"量={s.SliceQty} Issue={s.IssueCode ?? "-"}");
            }
        }

        var allCtx = matched
            .SelectMany(pi => payload.PiPositions[pi].ExistingExecutionContexts
                              ?? Array.Empty<ExistingExecutionContextDto>())
            .ToList();
        Say($"命中上下文合计={allCtx.Count}；其中 E>0={allCtx.Count(c => c.DerivedRemainingQty > 0)}；" +
            $"Slices 非空={allCtx.Count(c => (c.Slices?.Count ?? 0) > 0)}；ΣE={allCtx.Sum(c => c.DerivedRemainingQty)}");
        Say("命中上下文 工单状态分布: " + string.Join(" | ",
            allCtx.GroupBy(c => c.WorkOrderStatus ?? "<null>")
                  .OrderByDescending(g => g.Count())
                  .Select(g => $"{g.Key}={g.Count()}")));
        Say("命中上下文 起点Stage 分布: " + string.Join(" | ",
            allCtx.GroupBy(c => c.StartStageCode ?? "<null>")
                  .OrderByDescending(g => g.Count())
                  .Take(8)
                  .Select(g => $"{g.Key}={g.Count()}")));
    }

    /// <summary>
    /// 极简 Console 日志 Sink（`APS_PROBE_LOG=1` 时挂）——只为取**分段耗时**。
    /// 只放行 Warning+ 与含 <c>[Pegging]</c> 的埋点，其余（尤其逐 PI 的 Debug/Info）扔掉，
    /// 否则 I/O 反而污染计时。不引 `Microsoft.Extensions.Logging.Console` 包，手写最省事。
    /// </summary>
    private sealed class ProbeConsoleLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ProbeConsoleLogger(categoryName);
        public void Dispose() { }

        private sealed class ProbeConsoleLogger : ILogger
        {
            private readonly string _category;
            public ProbeConsoleLogger(string category) => _category = category;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel level) => true;

            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? ex,
                                    Func<TState, Exception?, string> formatter)
            {
                var msg = formatter(state, ex);
                // `APS_PROBE_LOG=1`（默认）：只留 `[Pegging][` 埋点行（阶段/供给池分段耗时、埋点计数）。
                //   上一版连 Warning 一起放行 ⇒ 逐 PI 的 Warning 刷了 **36,767 行**，日志 I/O 自己成了瓶颈。
                // `APS_PROBE_LOG=2`：再加上一切 Warning 及以上（查数据问题时才用）。
                var wantWarnings = Environment.GetEnvironmentVariable("APS_PROBE_LOG") == "2";
                var keep = msg.Contains("[Pegging][", StringComparison.Ordinal)
                           || (wantWarnings && level >= LogLevel.Warning);
                if (!keep) return;
                Console.WriteLine($"LOGV {DateTime.Now:HH:mm:ss.fff} [{level}] {_category.Split('.').Last()}: {msg}");
            }
        }
    }

    private static string Join(IEnumerable<string> xs) => string.Join(" | ", xs);
}
