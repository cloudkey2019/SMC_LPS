using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Xunit;
using Xunit.Abstractions;
using LPS.APS.Application.Extensions;
using LPS.APS.Application.Services;
using LPS.APS.BusinessRules.Extensions;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Services.Sync;
using LPS.APS.Scheduling.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 【2号位 · 真实域全量跑通留痕】—— 唯一一个**故意不清理、故意留真实结果**的集成测试。
///
/// 目的：主链在真实数据上完整跑一遍，把真实 Task / Pegging / PeggingSupplyAllocation /
///       AllocationTaskShare / PI Position 快照落进 APS_Production，供验收 SQL
///       （Database/Scripts/APS/03_SQL脚本/pegging-chain-verify-sql）读取。
///
/// 与 <see cref="RealSchedulingIntegrationTest"/> 的根本差别（**别混用**）：
/// 1. **生产口径 DI**：本类**不**用 <c>DemandPriorityFixtureProvider</c> /
///    <c>FrozenStrategySnapshotFixtureProvider</c> 覆盖 —— 订单优先级与供给排序全部走
///    3号位 冻结快照（<see cref="FrozenStrategySnapshotProvider"/>）+ 2号位 投影层
///    （<see cref="DemandPriorityProjector"/>）。真实跑得通 = 生产跑得通。
/// 2. **真实规模**：FAMILY_X 全量订单（经 sp_SyncOrdersToPartitionTable 从 Order_Canonical 灌），
///    不是合成单条 TEST-SO-001。
/// 3. **不回滚**：结果就是要留下的证据。重跑安全 —— ExecuteDomainAsync 自带结果四表幂等清理，
///    订单装载 SP 是 NOT EXISTS 纯增量。
/// 4. **Run 必须收口**：走 <see cref="SchedulingOrchestrator.RunSchedulingAndFinalizeAsync"/>
///    （带真实 ScheduleRunId，置 COMPLETED/FAILED），**绝不**留 RUNNING ——
///    否则 RunSchedulingAutoAsync 的「RUNNING + Created PV」领取条件会把本 Run 当夜间活领走。
///
/// 运行：dotnet test --filter FullyQualifiedName~RealDomainFullRunTest
///
/// **本类含两个门禁测试（都在 <c>APS_REAL_DOMAIN_RUN=1</c> 后才跑）**：
/// - <see cref="RunRealDomainAsync"/> —— 走 **候选单域 seam** <c>RunSchedulingAndFinalizeAsync</c>（原有）。
/// - <see cref="RunRealSeamAsync"/> —— 走 **夜间发令枪 seam** <c>ExecuteRunAsync</c>（P1-03 抽出，2026-09-29 新增）。
///   补这一条的原因：<c>RunSchedulingAutoAsync</c> **全仓无任何测试调用**，P1-03 把它的方法体搬进
///   <c>ExecuteRunAsync</c> 后，那条路径仍是零覆盖 —— 编译能过 ≠ 跑得起来。
/// 开发者：2号位
/// </summary>
public class RealDomainFullRunTest
{
    // ── 本次跑批的身份（改这里 = 换一次跑批）──────────────────────────────
    /// <summary>真实域（DomainDefinition.ScopeType=FAMILY, ScopeValue=FAMILY_X）。</summary>
    private const string DomainKey = "FAMILY_X";

    /// <summary>生产默认策略包版本 SP-DEMO-V3.0（IsDefault=1）。实际以库为准并断言，不盲信常量。</summary>
    private const long ExpectedStrategyProfileVersionId = 811L;

    /// <summary>本次跑批的 PlanVersion.VersionCode（**精确值，非 LIKE**，按日期一天一发）。</summary>
    private static string VersionCode => $"MANUAL_{DateTime.Today:yyyyMMdd}_{DomainKey}";

    private const string TriggeredBy = "2号位-真实域全量跑通留痕";

    private readonly ITestOutputHelper _output;

    /// <summary>在测试方法体内装配（构造函数不碰 DI，见构造函数注释）。</summary>
    private DatabaseConnectionManager _connectionManager = null!;

    /// <summary>
    /// 构造函数**只接输出**，不做任何 DI/DB 工作 —— 否则构造函数先于 <c>Skip.IfNot</c> 执行，
    /// 闸门关着也会因为构造失败报 FAIL（假红），把「默认跳过」这件事做废。
    /// </summary>
    public RealDomainFullRunTest(ITestOutputHelper output)
    {
        _output = output;
    }

    private static ServiceProvider BuildProductionProvider()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(System.IO.Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDatabaseServices(configuration);
        // 严格对齐生产组合根 LPS.APS.Web/Program.cs 的注册顺序与集合 —— 少一行就不是生产口径了。
        // ⚠️ AddGovernanceRepositories 是**被 Fixture 长期遮住**的一行：历史所有集成测试都用
        //    FrozenStrategySnapshotFixtureProvider 顶掉 IFrozenStrategySnapshotProvider，
        //    于是 IStrategyProfileVersionRepository 从未被真正解析过，缺注册也从未暴露。
        services.AddGovernanceRepositories();
        services.AddSchedulingServices();
        services.AddBusinessRuleServices();
        services.AddApplicationServices();
        // 复用 ContinuityRedLine 的极简 Console Provider（本工程未引 Microsoft.Extensions.Logging.Console）。
        // 真实跑批必须看得到日志 —— 失败时要能从日志定位是装载、Pegging 还是求解。
        services.AddLogging(b => b.AddProvider(new ConsoleOutLoggerProvider()));

        // 具体类不在接口扫描范围，必须显式注册（与 RealSchedulingIntegrationTest 同口径）
        services.AddScoped<SchedulingOrchestrator>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 环境变量闸门：默认**跳过**。
    /// 本测试会往 APS_Production 写真实域的全量结果并跑满主链（分钟级、重资源），
    /// 绝不能因为一次宽泛的 <c>dotnet test</c> 被误触发。要跑必须显式开门：
    /// <code>APS_REAL_DOMAIN_RUN=1 dotnet test --filter FullyQualifiedName~RealDomainFullRunTest</code>
    /// </summary>
    private const string GateEnvVar = "APS_REAL_DOMAIN_RUN";

    [SkippableFact(DisplayName = "2号位: 真实域 FAMILY_X 全量跑通留痕（生产口径，结果不回滚）")]
    public async Task RunRealDomainAsync()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(GateEnvVar) == "1",
            $"真实域全量跑批默认关闭（会写 APS_Production 并跑满主链）。设 {GateEnvVar}=1 显式开启。");

        var ct = CancellationToken.None;

        using var provider = BuildProductionProvider();
        var connectionManager = provider.GetRequiredService<DatabaseConnectionManager>();
        var schedulingOrchestrator = provider.GetRequiredService<SchedulingOrchestrator>();
        var orderLoadingService = provider.GetRequiredService<IOrderLoadingService>();
        _connectionManager = connectionManager;

        // ── 步骤 0：策略包版本 —— 以库为准，不盲信常量 ─────────────────────
        var strategyVersionId = await ResolveDefaultStrategyVersionIdAsync(ct);
        _output.WriteLine($"[0] 生产默认策略包版本 = {strategyVersionId}");

        // ── 步骤 1：Run + PlanVersion 壳（已存在则复用，保证可重跑）────────
        var (runId, planVersionId, reused) = await EnsureRunAndPlanVersionAsync(strategyVersionId, ct);
        _output.WriteLine($"[1] ScheduleRunId={runId}, PlanVersionId={planVersionId} ({(reused ? "复用既有" : "新建")})");

        // ── 步骤 2：灌订单（Order_Canonical → [Order]，纯增量幂等）─────────
        var orderCountBefore = await CountAsync("SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @Id", planVersionId, ct);
        var loaded = await orderLoadingService.LoadOrdersToPartitionTableAsync(planVersionId, ct);
        var orderCountAfter = await CountAsync("SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @Id", planVersionId, ct);
        _output.WriteLine($"[2] 订单装载: 本次新增 {loaded} 条，装载前 {orderCountBefore} → 装载后 {orderCountAfter}");

        Assert.True(orderCountAfter > 0,
            $"PlanVersionId={planVersionId} 的 [Order] 仍为 0 行 —— 订单装载未生效。"
            + " 前置条件：PlanVersion.DomainKey 非空 且 有 IsActive=1 的 DomainDefinition，否则 SP 会 RAISERROR。");

        // ── 步骤 3：跑主链（生产 DI：冻结快照 + 投影层，无 Fixture）────────
        var result = await schedulingOrchestrator.RunSchedulingAndFinalizeAsync(
            planVersionId, runId, strategyVersionId, ct);

        _output.WriteLine($"[3] RunSchedulingAndFinalizeAsync: IsSuccess={result.IsSuccess}, "
            + $"ErrorMessage={result.ErrorMessage ?? "(null)"}");

        // ── 步骤 4：落库证据（无论成败都打印，失败时正是要看的）───────────
        await DumpEvidenceAsync(runId, planVersionId, ct);

        Assert.True(result.IsSuccess,
            $"主链求解未成功：PlanVersionId={planVersionId}, RunId={runId}, Error={result.ErrorMessage}");
    }

    /// <summary>
    /// 【2号位 · 既有 PlanVersion 单域重跑（诊断入口）】对**已存在**的 PlanVersion 走手动入口
    /// <see cref="SchedulingOrchestrator.RunSchedulingAsync(int, long, CancellationToken)"/> 重跑。
    ///
    /// **为什么需要它**：2026-10-08 真实域跑批出现「22,135 条需求进求解器 → 722s → FinalTask=0」，
    ///   而 <see cref="RunRealDomainAsync"/> 走的是 <c>MANUAL_*</c> 新壳 —— 该壳**没有**
    ///   <c>OrderBomRequestLink</c>（OBRL 由 bom-request-push 按 PV 生成，新壳拿不到），
    ///   于是 Pegging 回退「按 MaterialId 全量装载」、BOM 快照为空。
    ///   夜间壳（<c>NIGHTLY_20261008_FAMILY_X</c>）才带 OBRL 与已落库的 <c>APS_BOM_RAW</c>。
    ///   要复现**带 BOM 的真实口径**，必须能点名既有 PV 重跑，而不是每次新建壳。
    ///
    /// 与 <see cref="RunRealDomainAsync"/> 的差别：本入口 <c>scheduleRunId=0</c> ⇒
    ///   <c>LoadSchedulingContextAsync</c> 的 <c>if (scheduleRunId &gt; 0)</c> 守卫使 **MES 进度快照不装载**。
    ///   诊断「求解器为什么丢弃需求」不受影响；但**连续性切片**依赖 MES 进度，
    ///   在本入口下恒为空 —— **不要用本入口验连续性**。
    ///
    /// 开门：<code>APS_REAL_DOMAIN_RUN=1 APS_REAL_DOMAIN_PV_ID=2 dotnet test --filter FullyQualifiedName~RunExistingPlanVersionAsync</code>
    /// </summary>
    [SkippableFact(DisplayName = "2号位: 既有 PlanVersion 单域重跑（诊断入口，生产口径）")]
    public async Task RunExistingPlanVersionAsync()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(GateEnvVar) == "1",
            $"真实域跑批默认关闭（会写 APS_Production 并跑满主链）。设 {GateEnvVar}=1 显式开启。");

        var pvIdRaw = Environment.GetEnvironmentVariable("APS_REAL_DOMAIN_PV_ID");
        Skip.IfNot(int.TryParse(pvIdRaw, out var planVersionId) && planVersionId > 0,
            $"未指定 APS_REAL_DOMAIN_PV_ID（当前值 '{pvIdRaw ?? "(null)"}'）—— 本入口必须显式点名既有 PlanVersionId。");

        var ct = CancellationToken.None;

        using var provider = BuildProductionProvider();
        _connectionManager = provider.GetRequiredService<DatabaseConnectionManager>();
        var schedulingOrchestrator = provider.GetRequiredService<SchedulingOrchestrator>();

        var strategyVersionId = await ResolveDefaultStrategyVersionIdAsync(ct);

        // 反查既有 PV 的归属 Run（只为 DumpEvidence 打印终态，不参与执行）
        var sourceRunId = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            "SELECT SourceScheduleRunId FROM PlanVersion WHERE Id = @Id",
            new { Id = planVersionId }, db: DatabaseId.APS) ?? 0;

        _output.WriteLine($"[0] PlanVersionId={planVersionId}, SourceScheduleRunId={sourceRunId}, 策略包版本={strategyVersionId}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await schedulingOrchestrator.RunSchedulingAsync(planVersionId, strategyVersionId, ct);
        sw.Stop();

        _output.WriteLine($"[1] RunSchedulingAsync: IsSuccess={result.IsSuccess}, Error={result.ErrorMessage ?? "(null)"}, "
            + $"Scheduled={result.ScheduledCount}, Unscheduled={result.UnscheduledCount}, 耗时={sw.ElapsedMilliseconds}ms");

        if (result.Metrics is { } m)
        {
            _output.WriteLine($"     Metrics: Demand={m.DemandCount}, LPD={m.LogicalProductionDemandCount}, "
                + $"FinalTask={m.FinalTaskCount} | ContextBuild={m.ContextBuildMs}ms, Pegging={m.PeggingMs}ms, "
                + $"Solver={m.SolverMs}ms, Persist={m.PersistMs}ms, Total={m.TotalMs}ms");
        }
        else
        {
            _output.WriteLine("     Metrics=(null)");
        }

        // 落库证据：无论成败都摊开（0 任务时正是要看现场）
        await DumpEvidenceAsync(sourceRunId, planVersionId, ct);

        // 本入口是**诊断**入口，不把「0 任务」判成测试失败 —— 求解器的丢弃原因在日志里，
        // 断言在此只会把「拿到了证据」误报成红。
    }

    /// <summary>
    /// 【2号位 · 夜间发令枪 seam 真跑】验 P1-03 抽出的 <c>ExecuteRunAsync</c> 在真实库上的行为：
    ///   ① 首次调用**真的跑完**整个域 —— 即重入守卫**不得误伤首跑**（本次改动最需证伪的风险点）；
    ///   ② 第二次调用**命中重入守卫、不重跑**。
    ///
    /// **为什么必须真跑、单测为什么不够**：`RunSchedulingAutoAsync`（本次被抽走方法体的那个）
    ///   **全仓没有任何测试调用它** —— 唯一调用方是 Hangfire 定时任务
    ///   （<c>HangfireServiceExtensions</c> 的 <c>RecurringJob</c>）。P1-03 把它的方法体搬进
    ///   <c>ExecuteRunAsync</c>，编译能过、单测全绿，但**那条路径此前一行都没被覆盖过**。
    ///   （同源教训：Fixture 顶掉生产 Provider ⇒ 生产能不能跑，集成测试证明不了。）
    ///
    /// 运行：<c>APS_REAL_DOMAIN_RUN=1 dotnet test --filter FullyQualifiedName~RunRealSeamAsync</c>
    /// </summary>
    [SkippableFact(DisplayName = "2号位: ExecuteRunAsync 夜间 seam 真跑 + 重入守卫生效（生产口径，结果不回滚）")]
    public async Task RunRealSeamAsync()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(GateEnvVar) == "1",
            $"真实域全量跑批默认关闭（会写 APS_Production 并跑满主链）。设 {GateEnvVar}=1 显式开启。");

        var ct = CancellationToken.None;
        var versionCode = $"SEAM_{DateTime.Today:yyyyMMdd}_{DomainKey}";

        using var provider = BuildProductionProvider();
        var connectionManager = provider.GetRequiredService<DatabaseConnectionManager>();
        var orchestrator = provider.GetRequiredService<SchedulingOrchestrator>();
        var orderLoadingService = provider.GetRequiredService<IOrderLoadingService>();
        _connectionManager = connectionManager;

        var strategyVersionId = await ResolveDefaultStrategyVersionIdAsync(ct);
        _output.WriteLine($"[0] 生产默认策略包版本 = {strategyVersionId}");

        var (runId, planVersionId, reused) = await EnsureRunAndPlanVersionAsync(strategyVersionId, ct, versionCode);
        _output.WriteLine($"[1] ScheduleRunId={runId}, PlanVersionId={planVersionId} ({(reused ? "复用既有" : "新建")})");

        // 复用既有 Run/PV 时把 PV 打回 'Created' —— 否则上一次留下的 'Computed' 会让**第 1 次**调用
        // 直接命中重入守卫（守卫行为正确，但本测试要验的是「首跑真的跑」，会被自己绊倒）。
        // Run 侧由 EnsureRunAndPlanVersionAsync 的复用分支打回 RUNNING。
        await _connectionManager.ExecuteAsync(
            "UPDATE PlanVersion SET Status = 'Created', ComputedAt = NULL WHERE Id = @Id",
            new { Id = planVersionId }, db: DatabaseId.APS);

        var loaded = await orderLoadingService.LoadOrdersToPartitionTableAsync(planVersionId, ct);
        var orderCount = await CountAsync("SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @Id", planVersionId, ct);
        _output.WriteLine($"[2] 订单装载: 本次新增 {loaded} 条，现 {orderCount} 行");
        Assert.True(orderCount > 0,
            $"PlanVersionId={planVersionId} 的 [Order] 为 0 行 —— 装载未生效，前置不满足。");

        // ── 调用 1：首次执行 ──────────────────────────────────────────────
        var first = await orchestrator.ExecuteRunAsync(runId, ct);
        _output.WriteLine($"[3] 第 1 次 ExecuteRunAsync: IsSuccess={first.IsSuccess}, "
            + $"ScheduledCount={first.ScheduledCount}, Error={first.ErrorMessage ?? "(null)"}");

        Assert.True(first.IsSuccess, $"首次 ExecuteRunAsync 未成功：{first.ErrorMessage}");
        Assert.True(first.ScheduledCount > 0,
            "首次 ExecuteRunAsync 的 ScheduledCount=0 —— 重入守卫**误伤首跑**，或求解未产出任务。"
            + " 这正是本次改动最需要证伪的风险点。");

        var pvStatusAfterFirst = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT Status FROM PlanVersion WHERE Id = @Id", new { Id = planVersionId }, db: DatabaseId.APS);
        var runStatusAfterFirst = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT Status FROM ScheduleRun WHERE Id = @Id", new { Id = runId }, db: DatabaseId.APS);
        _output.WriteLine($"[4] 首跑终态: PlanVersion.Status={pvStatusAfterFirst}, ScheduleRun.Status={runStatusAfterFirst}");
        Assert.Equal("Computed", pvStatusAfterFirst);
        Assert.Equal("COMPLETED", runStatusAfterFirst);

        // ── 调用 2：重入 —— 必须命中守卫、不重跑 ──────────────────────────
        var second = await orchestrator.ExecuteRunAsync(runId, ct);
        _output.WriteLine($"[5] 第 2 次 ExecuteRunAsync: IsSuccess={second.IsSuccess}, "
            + $"ScheduledCount={second.ScheduledCount}, Error={second.ErrorMessage ?? "(null)"}");

        Assert.Equal(0, second.ScheduledCount);
        Assert.NotNull(second.ErrorMessage);
        Assert.Contains("跳过重跑", second.ErrorMessage!);

        var runStatusAfterSecond = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT Status FROM ScheduleRun WHERE Id = @Id", new { Id = runId }, db: DatabaseId.APS);
        _output.WriteLine($"[6] 重入后 ScheduleRun.Status={runStatusAfterSecond}（应仍为 COMPLETED）");
        Assert.Equal("COMPLETED", runStatusAfterSecond);

        await DumpEvidenceAsync(runId, planVersionId, ct);
    }

    // ══════════════════════════════════════════════════════════════════
    // 辅助
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// 解析当前生产默认策略包版本：PUBLISHED + IsDefault=1 + 有效窗口内。
    /// 0 个 / 多个都**拒绝**（红线 #4：禁止盲目取第一个），与 RunLifecycleService 同口径。
    /// </summary>
    private async Task<long> ResolveDefaultStrategyVersionIdAsync(CancellationToken ct)
    {
        var rows = (await _connectionManager.QueryAsync<long>(
            @"SELECT Id
              FROM StrategyProfileVersion
              WHERE IsDefault = 1
                AND Status = 'PUBLISHED'
                AND (EffectiveFrom IS NULL OR EffectiveFrom <= GETDATE())
                AND (EffectiveTo   IS NULL OR EffectiveTo   >= GETDATE())",
            null, db: DatabaseId.APS)).ToList();

        Assert.False(rows.Count == 0,
            "当前无有效的默认 PUBLISHED 策略包版本（IsDefault=1 且窗口内）—— 跑批无策略可依，拒绝执行。");
        Assert.False(rows.Count > 1,
            $"存在 {rows.Count} 个有效默认 PUBLISHED 策略包版本（{string.Join(",", rows)}）—— 歧义，拒绝执行。");

        var resolved = rows[0];
        Assert.True(resolved == ExpectedStrategyProfileVersionId,
            $"生产默认策略包版本已漂移：库里 IsDefault=1 的是 {resolved}，本夹具常量写的 {ExpectedStrategyProfileVersionId}。"
            + " 请确认新版本内容后再改本夹具常量（别跟着改，先看差异）。");

        return resolved;
    }

    /// <summary>
    /// 保证存在本次跑批的 Run + PlanVersion。已存在则复用（同一天重跑不新建，避免 PV 堆积）。
    /// 两个 INSERT 的列集严格对齐 ScheduleRunRepository.CreateCandidateRunAsync。
    /// VersionCategory 用 'DAILY_BASELINE'（与夜间批同身份词表，**不用 TEST** —— 这不是测试残留，
    /// 是真实基线结果，别被残留清理脚本扫掉）。
    /// </summary>
    private async Task<(int RunId, int PlanVersionId, bool Reused)> EnsureRunAndPlanVersionAsync(
        long strategyVersionId, CancellationToken ct, string? versionCode = null)
    {
        // versionCode 可覆盖：夜间 seam 真跑测试用独立 VersionCode，避免与本测试的 MANUAL_ 批互相复用同一条 Run/PV。
        var code = versionCode ?? VersionCode;
        var existing = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            "SELECT Id FROM PlanVersion WHERE VersionCode = @Code",
            new { Code = code }, db: DatabaseId.APS);

        if (existing.HasValue)
        {
            var existingRunId = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
                "SELECT SourceScheduleRunId FROM PlanVersion WHERE Id = @Id",
                new { Id = existing.Value }, db: DatabaseId.APS);

            Assert.True(existingRunId.HasValue,
                $"既有 PlanVersionId={existing.Value} 的 SourceScheduleRunId 为空 —— 无法收口 Run，拒绝复用。");

            // 复用前把 Run 打回 RUNNING（上次跑完是 COMPLETED；重跑要重新计时）
            await _connectionManager.ExecuteAsync(
                "UPDATE ScheduleRun SET Status = 'RUNNING', StartedAt = GETUTCDATE(), CompletedAt = NULL, DurationSeconds = NULL WHERE Id = @Id",
                new { Id = existingRunId.Value }, db: DatabaseId.APS);

            return (existingRunId.Value, existing.Value, true);
        }

        var now = DateTime.Now;
        var expectedDomainKeysJson = System.Text.Json.JsonSerializer.Serialize(new[] { DomainKey });

        var runId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[ScheduleRun]
                  ([RunType], [Status], [TriggeredBy], [DataCutoffTime],
                   [BasePlanVersionId], [StrategyProfileVersionId], [ExpectedDomainKeysJson], [ScopeJson],
                   [StartedAt], [CreatedAt])
              OUTPUT INSERTED.[Id]
              VALUES ('FULL_SCHEDULE', 'RUNNING', @TriggeredBy, @DataCutoffTime,
                      NULL, @StrategyProfileVersionId, @ExpectedDomainKeysJson, NULL,
                      @StartedAt, @StartedAt)",
            new
            {
                TriggeredBy = TriggeredBy,
                DataCutoffTime = now,
                StrategyProfileVersionId = strategyVersionId,
                ExpectedDomainKeysJson = expectedDomainKeysJson,
                StartedAt = now,
            }, db: DatabaseId.APS);

        Assert.True(runId > 0, "ScheduleRun 写入未返回 Id");

        var planVersionId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[PlanVersion]
                  ([VersionCode], [VersionCategory], [DomainKey],
                   [PlanHorizonStart], [PlanHorizonEnd], [ComputeMode], [Status],
                   [SourceScheduleRunId], [CreatedBy], [CreatedAt])
              OUTPUT INSERTED.[Id]
              VALUES (@VersionCode, 'DAILY_BASELINE', @DomainKey,
                      @PlanHorizonStart, @PlanHorizonEnd, 'FULL', 'Created',
                      @SourceScheduleRunId, @CreatedBy, @StartedAt)",
            new
            {
                VersionCode = code,
                DomainKey = DomainKey,
                PlanHorizonStart = DateTime.Today,
                PlanHorizonEnd = DateTime.Today.AddDays(90),
                SourceScheduleRunId = runId,
                CreatedBy = TriggeredBy,
                StartedAt = now,
            }, db: DatabaseId.APS);

        Assert.True(planVersionId > 0, "PlanVersion 写入未返回 Id");
        return (runId, planVersionId, false);
    }

    /// <summary>把证据摊开打印 —— 验收 SQL 之前的现场快照。</summary>
    private async Task DumpEvidenceAsync(int runId, int planVersionId, CancellationToken ct)
    {
        var pv = new { Id = planVersionId };

        var pvStatus = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT Status FROM PlanVersion WHERE Id = @Id", pv, db: DatabaseId.APS);
        var runStatus = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT Status FROM ScheduleRun WHERE Id = @Id", new { Id = runId }, db: DatabaseId.APS);

        _output.WriteLine($"[4] 终态: PlanVersion.Status={pvStatus}, ScheduleRun.Status={runStatus}");

        foreach (var table in new[]
                 {
                     "[Order]", "[Task]", "[Pegging]", "PeggingSupplyAllocation",
                     "AllocationTaskShare", "ProductionInstructionPositionSnapshot",
                     "OrderScheduleSummary", "ExplainTrace", "FrozenZoneSnapshot",
                 })
        {
            var n = await CountAsync($"SELECT COUNT(*) FROM {table} WHERE PlanVersionId = @Id", planVersionId, ct);
            _output.WriteLine($"      {table,-38} = {n}");
        }

        // 排不下 / 失败原因分布 —— 「产能不足 ≠ 排不下」，Unscheduled 只兜真无解
        var taskStatus = (await _connectionManager.QueryAsync<(string? Status, int N)>(
            "SELECT Status, COUNT(*) AS N FROM [Task] WHERE PlanVersionId = @Id GROUP BY Status ORDER BY N DESC",
            pv, db: DatabaseId.APS)).ToList();
        foreach (var (status, n) in taskStatus)
        {
            _output.WriteLine($"      Task.Status={status ?? "(null)"} → {n}");
        }
    }

    private Task<int> CountAsync(string sql, int planVersionId, CancellationToken ct)
        => _connectionManager.QueryFirstOrDefaultAsync<int>(sql, new { Id = planVersionId }, db: DatabaseId.APS);
}
