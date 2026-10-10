using System.Data;
using System.Text.Json;
using Dapper;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Models;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Models.Scheduling;
using LPS.APS.Engine.Data;
using LPS.APS.Scheduling.Algorithms;
using LPS.APS.Shared.Models;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 排程编排器（2号位职责 — §2.5.1 排程发令枪）
/// 
/// 每日02:00由Hangfire触发，完整编排流程：
///   阶段1: 装载排程沙盘（从APS库读取订单/BOM/物料/设备/库存 → SchedulingContext）
///   阶段2: Pegging（2号位PeggingOrchestrator生成Task + Allocation）
///   阶段3: 调用 FiniteCapacitySolver.Solve()（纯内存计算）
///   阶段4: 排程结果落盘
///   阶段5: PlanVersion 状态更新
///   阶段6: 快照封存（§2.6 SchedulingContext → .json.gz）
///
/// 架构位置：Application 层（桥接 Engine 数据层 + Scheduling 算法层）
/// </summary>
public class SchedulingOrchestrator : ISchedulingOrchestrator
{
    /// <summary>
    /// Dapper 把 `IN @List` 展开成**逐个值一个参数**，SQL Server 单次 RPC 参数上限 2100 ⇒ 取 2000 留余量。
    /// 与 `PeggingOrchestrator.SqlServerInParameterLimit` 同口径（两个类各持一份，避免跨类耦合）。
    /// </summary>
    private const int SqlServerInParameterLimit = 2000;

    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ISnapshotService _snapshotService;
    private readonly IPeggingOrchestrator _peggingOrchestrator;
    private readonly IScheduleRunService _scheduleRunService;
    private readonly LPS.APS.Engine.Services.Sync.IOrderLoadingService _orderLoadingService;
    private readonly LPS.APS.Engine.Services.Sync.IBomRealtimeExpandService _bomRealtimeExpandService;
    private readonly ILogger<SchedulingOrchestrator> _logger;

    public SchedulingOrchestrator(
        DatabaseConnectionManager connectionManager,
        ISnapshotService snapshotService,
        IPeggingOrchestrator peggingOrchestrator,
        IScheduleRunService scheduleRunService,
        LPS.APS.Engine.Services.Sync.IOrderLoadingService orderLoadingService,
        LPS.APS.Engine.Services.Sync.IBomRealtimeExpandService bomRealtimeExpandService,
        ILogger<SchedulingOrchestrator> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _peggingOrchestrator = peggingOrchestrator ?? throw new ArgumentNullException(nameof(peggingOrchestrator));
        _scheduleRunService = scheduleRunService ?? throw new ArgumentNullException(nameof(scheduleRunService));
        _orderLoadingService = orderLoadingService ?? throw new ArgumentNullException(nameof(orderLoadingService));
        _bomRealtimeExpandService = bomRealtimeExpandService ?? throw new ArgumentNullException(nameof(bomRealtimeExpandService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SchedulingRunResult> RunSchedulingAutoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("排程发令枪：自动查找待排 FULL_SCHEDULE ScheduleRun（RUNNING + 有 Created PlanVersion）");

        // 原子领取：领取粒度从「单个 PlanVersion」上移为「ScheduleRun」。
        // 分域后一个 ScheduleRun 对应多个 PlanVersion（每 Domain 一个，由 NightlyBatch Step3 创建），
        // 此处锁定 RUNNING 且仍有 Created PlanVersion 的 ScheduleRun，避免多 Worker 重复领取同一 Run。
        // RunType='FULL_SCHEDULE' 过滤：仅领夜间正式排程；白天候选（MANUAL_RESCHEDULE 等）由 3号位 显式触发，不得被发令枪误领。
        var scheduleRun = await _connectionManager.QueryFirstOrDefaultAsync<ScheduleRunQueryDto>(
            @"SELECT TOP 1 sr.Id, sr.DataCutoffTime, sr.StrategyProfileVersionId
              FROM ScheduleRun sr WITH (UPDLOCK, READPAST, ROWLOCK)
              WHERE sr.Status = 'RUNNING'
                AND sr.RunType = 'FULL_SCHEDULE'
                AND EXISTS (
                    SELECT 1 FROM PlanVersion pv
                    WHERE pv.SourceScheduleRunId = sr.Id AND pv.Status = 'Created'
                )
              ORDER BY sr.CreatedAt DESC",
            db: DatabaseId.APS);

        if (scheduleRun == null)
        {
            _logger.LogInformation("未找到待排 FULL_SCHEDULE ScheduleRun（RUNNING + 有 Created PlanVersion），跳过本次触发");
            return new SchedulingRunResult { IsSuccess = true, ErrorMessage = "无待排 ScheduleRun" };
        }

        _logger.LogInformation("成功领取 ScheduleRun: ScheduleRunId={RunId}", scheduleRun.Id);

        return await ExecuteRunAsync(scheduleRun.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SchedulingRunResult> ExecuteRunAsync(int scheduleRunId, CancellationToken cancellationToken = default)
    {
        // 【2026-09-29 P1-03】本方法体原属 RunSchedulingAutoAsync，抽出后由两个调用方共用：
        //   ① 夜间发令枪（RunSchedulingAutoAsync 领取后委托）② FAILED 恢复（3号位 建壳后直接调）。
        //   执行基线（DataCutoffTime / 策略包版本）由本方法**自取**而非调用方传参 ⇒ 两条路径的参数口径
        //   不可能漂移；调用方只负责判定「这个 Run 该不该跑」，本方法不重复校验 RunType/Status。
        var run = await _connectionManager.QueryFirstOrDefaultAsync<ScheduleRunQueryDto>(
            @"SELECT Id, DataCutoffTime, StrategyProfileVersionId
              FROM ScheduleRun WHERE Id = @Id",
            new { Id = scheduleRunId },
            db: DatabaseId.APS);

        if (run == null)
        {
            _logger.LogError("ScheduleRun {RunId} 不存在，无法执行", scheduleRunId);
            return new SchedulingRunResult { IsSuccess = false, ErrorMessage = $"ScheduleRun 不存在：{scheduleRunId}" };
        }

        // 读冻结预期 Domain 集合（运行启动唯一权威来源；FULL_SCHEDULE 须 ≥1 Domain）
        IReadOnlyList<string> domainKeys;
        try
        {
            domainKeys = await _scheduleRunService.GetExpectedDomainKeysAsync(scheduleRunId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取 ExpectedDomainKeysJson 失败: ScheduleRunId={RunId}", scheduleRunId);
            await _scheduleRunService.FailAsync(scheduleRunId, 0, $"读取 ExpectedDomainKeysJson 失败: {ex.Message}", cancellationToken);
            return new SchedulingRunResult { IsSuccess = false, ErrorMessage = ex.Message };
        }

        if (domainKeys.Count == 0)
        {
            _logger.LogError("ScheduleRun {RunId} 的 ExpectedDomainKeysJson 解析结果为空 Domain 集合", scheduleRunId);
            await _scheduleRunService.FailAsync(scheduleRunId, 0, "ExpectedDomainKeysJson 解析结果为空 Domain 集合", cancellationToken);
            return new SchedulingRunResult { IsSuccess = false, ErrorMessage = "ExpectedDomainKeysJson 为空 Domain 集合" };
        }

        // 【2026-09-29 重入守卫，对应回执 §5.3】本方法现有两个调用方 ⇒ 同一 Run 可能被先后执行两次
        //   （例：发令枪已跑完，恢复又显式调一次）。若**全部**预期 Domain 的 PlanVersion 已达终态，
        //   则跳过整段 Domain 执行，只补一次收口 —— 这同时兜住「全部 Domain 跑完但收口前崩溃」导致
        //   Run 永久卡在 RUNNING 的情形（否则那种 Run 再也领不走）。
        //   · 为什么是「全终态才跳过」而不是「逐域跳过已达终态的 Domain」：逐域跳过会丢掉已成功域的
        //     sharedResourceOccupancy（FULL §9 跨域资源占用），后续 Domain 会排进已被占用的窗口 —— 那比
        //     重跑更糟。全终态跳过时一个 Domain 都不执行，该上下文无人消费，故无此副作用。
        //   · ⚠️ 边界：只覆盖「先后调用」，**不覆盖两个调用方同时在飞的并发**（那需要执行级锁，见回执 §5.3）。
        var terminalPlanVersions = (await _connectionManager.QueryAsync<PlanVersionInfoDto>(
            @"SELECT DomainKey, Status FROM PlanVersion
              WHERE SourceScheduleRunId = @RunId AND Status IN ('Computed', 'ComputeFailed')",
            new { RunId = scheduleRunId },
            db: DatabaseId.APS)).ToList();

        var runStopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 只认**预期 Domain** 的终态 PV（若 Run 上存在多余/陈旧 PV，不得据此判定「已跑完」）。
        // 用 HashSet 在内存里取交集，不用 SQL 的 IN @list —— 规避装载层踩过的 2100 参数上限坑。
        var expectedDomainSet = new HashSet<string>(domainKeys, StringComparer.OrdinalIgnoreCase);
        var computedDomains = terminalPlanVersions
            .Where(p => p.Status == "Computed" && expectedDomainSet.Contains(p.DomainKey))
            .Select(p => p.DomainKey).Distinct().ToList();
        var terminalFailed = terminalPlanVersions
            .Where(p => p.Status != "Computed" && expectedDomainSet.Contains(p.DomainKey))
            .Select(p => p.DomainKey).Distinct().ToList();

        if (computedDomains.Count + terminalFailed.Count >= domainKeys.Count)
        {
            // 从终态 PV 反推成功/失败数，走**同一个**收口方法 ⇒ 收口语义与正常路径单点、不会漂移
            _logger.LogWarning(
                "ScheduleRun {RunId} 的全部 {Total} 个 Domain 已达终态（Computed={Computed}, 失败={Failed}），跳过重跑、仅补收口",
                scheduleRunId, domainKeys.Count, computedDomains.Count, terminalFailed.Count);

            runStopwatch.Stop();
            var reentryDurationSeconds = (int)(runStopwatch.ElapsedMilliseconds / 1000);
            await CloseOutRunAsync(scheduleRunId, domainKeys.Count, computedDomains.Count, terminalFailed, reentryDurationSeconds, cancellationToken);

            return new SchedulingRunResult
            {
                IsSuccess        = computedDomains.Count == domainKeys.Count,
                ScheduledCount   = 0,
                UnscheduledCount = terminalFailed.Count,
                ElapsedMs        = runStopwatch.ElapsedMilliseconds,
                ErrorMessage     = computedDomains.Count == domainKeys.Count
                    ? "该 ScheduleRun 的全部 Domain 已达终态，已跳过重跑"
                    : $"该 ScheduleRun 已达终态：成功 {computedDomains.Count}/{domainKeys.Count}，失败 Domain={string.Join(",", terminalFailed)}"
            };
        }

        var domainResults = new List<SchedulingRunResult>(domainKeys.Count);
        var succeededCount = 0;
        var failedDomainKeys = new List<string>();

        // 域间依赖（真实 DomainKey 口径，§5/§7/§15/§23）：依赖边 → 拓扑执行序 + 传递上游闭包。
        // Domain_Dependency 空（V1 现状）时两者均退化：执行序=冻结顺序、闭包为空（不阻断）。
        var dependencyEdges = await LoadDomainDependencyEdgesAsync(domainKeys, cancellationToken);
        var orderedDomainKeys = OrderDomainsTopologically(domainKeys, dependencyEdges);
        var upstreamClosure = BuildUpstreamClosure(dependencyEdges, domainKeys);

        // FULL §9：前序 Domain 成功后的共享 Resource 占用块（逐 Domain 累积，传给后续 Domain 作不可用时间窗）
        var sharedResourceOccupancy = new List<ResourceBlock>();

        // 壳盘点（**只作归因诊断，不参与执行决策**）。
        // 为何要单独盘点：缺壳的真实因由**随路径而异** ——
        //   夜间/候选路径：装载阶段就没建壳；恢复路径（P1-03）：3号位 建壳未按「一域一壳 + 与 Run 同事务」建全。
        // 原措辞一律记「装载缺口」，会把排查引向错误阶段（恢复缺壳与装载阶段无关）。见 §5.4 同族的 P1-03 接线。
        // 该查询同时暴露「同一 DomainKey 多个壳」：下面执行侧取 `TOP 1 … ORDER BY Id DESC`，
        // 多出来的壳不会被任何一次执行认领 ⇒ **永久停在 Created**（建壳若不幂等就会踩到）。
        var shellCounts = (await _connectionManager.QueryAsync<DomainShellCount>(
            @"SELECT DomainKey, COUNT(*) AS ShellCount
              FROM PlanVersion
              WHERE SourceScheduleRunId = @RunId
              GROUP BY DomainKey",
            new { RunId = scheduleRunId },
            db: DatabaseId.APS)).ToList();

        var shelledDomainSet = new HashSet<string>(
            shellCounts.Select(s => s.DomainKey!).Where(k => !string.IsNullOrEmpty(k)),
            StringComparer.OrdinalIgnoreCase);

        var missingShellDomains = orderedDomainKeys.Where(d => !shelledDomainSet.Contains(d)).ToList();
        var duplicateShellDomains = shellCounts
            .Where(s => s.ShellCount > 1 && !string.IsNullOrEmpty(s.DomainKey))
            .Select(s => $"{s.DomainKey}×{s.ShellCount}")
            .ToList();

        if (missingShellDomains.Count > 0 || duplicateShellDomains.Count > 0)
        {
            _logger.LogError(
                "[{RunId}] PlanVersion 壳盘点异常：预期 {Total} 个 Domain，壳行数={ShellRows}，缺壳={Missing}，重复壳={Dup}。" +
                "请核对建壳步骤（一域一壳 + 与 Run 同事务）",
                scheduleRunId, orderedDomainKeys.Count, shellCounts.Sum(s => s.ShellCount),
                missingShellDomains.Count == 0 ? "无" : string.Join(",", missingShellDomains),
                duplicateShellDomains.Count == 0 ? "无" : string.Join(",", duplicateShellDomains));
        }

        // 逐 Domain 串行执行（依赖顺序 = Domain_Dependency 拓扑序；无依赖边时降级为 ExpectedDomainKeysJson 冻结顺序）
        foreach (var domainKey in orderedDomainKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var planVersion = await _connectionManager.QueryFirstOrDefaultAsync<PlanVersionInfoDto>(
                @"SELECT TOP 1 Id, VersionCode, DomainKey, PlanHorizonStart, PlanHorizonEnd
                  FROM PlanVersion
                  WHERE SourceScheduleRunId = @RunId AND DomainKey = @DomainKey
                  ORDER BY Id DESC",
                new { RunId = scheduleRunId, DomainKey = domainKey },
                db: DatabaseId.APS);

            if (planVersion == null)
            {
                // 预期 Domain 无对应 PlanVersion 壳。**不再武断归因为「装载缺口」** —— 因由随路径而异
                // （装载阶段未建 / 恢复建壳未建全），上方壳盘点已给出该 Run 的权威诊断，这里只引用它。
                _logger.LogError(
                    "[{RunId}] Domain {DomainKey} 无对应 PlanVersion 壳 ⇒ 计入失败（本 Run 缺壳={Missing}，诊断见上方壳盘点日志）",
                    scheduleRunId, domainKey,
                    missingShellDomains.Count == 0 ? "无" : string.Join(",", missingShellDomains));
                failedDomainKeys.Add(domainKey);
                continue;
            }

            // 依赖感知阻断：仅当本 Domain 直接/间接依赖某个已失败上游时才阻断（§5/§7/§15/§23），
            // 而非一刀切阻断所有后续 Domain。
            if (failedDomainKeys.Count > 0
                && upstreamClosure.TryGetValue(domainKey, out var upstreams)
                && upstreams.Overlaps(failedDomainKeys))
            {
                await BlockDomainAsync(planVersion.Id);
                _logger.LogWarning(
                    "[{RunId}] Domain {DomainKey} 因上游失败被阻断: PlanVersionId={PlanVersionId}，失败上游={Upstream}",
                    scheduleRunId, domainKey, planVersion.Id, string.Join(",", failedDomainKeys));
                domainResults.Add(new SchedulingRunResult
                {
                    PlanVersionId = planVersion.Id,
                    VersionCode   = planVersion.VersionCode,
                    IsSuccess     = false,
                    ErrorMessage  = $"上游 Domain 失败被阻断（失败上游={string.Join(",", failedDomainKeys)}）"
                });
                failedDomainKeys.Add(domainKey);
                continue;
            }

            var result = await ExecuteDomainAsync(
                planVersion.Id, scheduleRunId, run.DataCutoffTime, run.StrategyProfileVersionId,
                sharedResourceOccupancy, sourcePlanVersionId: null, scope: null, cancellationToken: cancellationToken);
            domainResults.Add(result);

            if (result.IsSuccess)
            {
                succeededCount++;
                // FULL §9：成功 Domain 的 FinalTask 共享 Resource 占用区间，累积进 Run 级上下文
                if (result.EmittedResourceBlocks.Count > 0)
                    sharedResourceOccupancy.AddRange(result.EmittedResourceBlocks);
            }
            else
            {
                failedDomainKeys.Add(domainKey);
            }
        }

        runStopwatch.Stop();
        var durationSeconds = (int)(runStopwatch.ElapsedMilliseconds / 1000);

        // ScheduleRun 终态收口（与重入路径共用 CloseOutRunAsync ⇒ 三态语义单点，不会两处漂移）
        await CloseOutRunAsync(scheduleRunId, domainKeys.Count, succeededCount, failedDomainKeys, durationSeconds, cancellationToken);

        _logger.LogInformation(
            "排程执行完成: ScheduleRunId={RunId}, Domain={DomainCount}, 成功={Succeeded}/{Total}, 耗时={Elapsed}ms",
            scheduleRunId, domainKeys.Count, succeededCount, domainKeys.Count, runStopwatch.ElapsedMilliseconds);

        return new SchedulingRunResult
        {
            IsSuccess        = succeededCount == domainKeys.Count,
            ScheduledCount   = domainResults.Where(r => r.IsSuccess).Sum(r => r.ScheduledCount),
            UnscheduledCount = domainResults.Count(r => !r.IsSuccess),
            ElapsedMs        = runStopwatch.ElapsedMilliseconds,
            ErrorMessage     = succeededCount == domainKeys.Count ? null : $"部分 Domain 失败：成功 {succeededCount}/{domainKeys.Count}"
        };
    }

    /// <summary>
    /// ScheduleRun 终态收口（三态各一次）：全成功 COMPLETED / 部分成功 PARTIAL_SUCCESS / 全失败 FAILED。
    /// 抽为私有方法，供「正常执行完」与「重入全终态跳过」两条路径共用 —— 收口语义必须单点，
    /// 否则两条路径的成功判定口径会各自演化（这正是本次抽 ExecuteRunAsync 要消灭的那类漂移）。
    /// </summary>
    /// <param name="scheduleRunId">目标 ScheduleRun</param>
    /// <param name="totalDomainCount">预期 Domain 总数（= ExpectedDomainKeysJson 元素数，作分母）</param>
    /// <param name="succeededCount">成功 Domain 数</param>
    /// <param name="failedDomainKeys">失败/被阻断的 DomainKey（写入收口原因）</param>
    /// <param name="durationSeconds">本次执行耗时（秒）</param>
    private async Task CloseOutRunAsync(
        int scheduleRunId,
        int totalDomainCount,
        int succeededCount,
        IReadOnlyList<string> failedDomainKeys,
        int durationSeconds,
        CancellationToken cancellationToken)
    {
        if (succeededCount == totalDomainCount)
        {
            await _scheduleRunService.CompleteAsync(scheduleRunId, durationSeconds, cancellationToken);
        }
        else if (succeededCount > 0)
        {
            await _scheduleRunService.PartialSuccessAsync(
                scheduleRunId, durationSeconds,
                $"部分 Domain 失败/被阻断：成功 {succeededCount}/{totalDomainCount}，失败/阻断 Domain={string.Join(",", failedDomainKeys)}",
                cancellationToken);
        }
        else
        {
            await _scheduleRunService.FailAsync(
                scheduleRunId, durationSeconds,
                $"全部 Domain 失败/被阻断（{totalDomainCount} 个），失败/阻断 Domain={string.Join(",", failedDomainKeys)}",
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<SchedulingRunResult> RunSchedulingAsync(int planVersionId, CancellationToken cancellationToken = default)
        => await ExecuteDomainAsync(planVersionId, scheduleRunId: 0, dataCutoffTime: null, strategyProfileVersionId: null, upstreamResourceBlocks: null, sourcePlanVersionId: null, scope: null, cancellationToken: cancellationToken);

    /// <summary>
    /// 手动/联调入口：显式指定策略包版本（测试/联调场景，绕过 RunSchedulingAutoAsync 的自动领取与版本绑定）。
    /// </summary>
    public Task<SchedulingRunResult> RunSchedulingAsync(int planVersionId, long strategyProfileVersionId, CancellationToken cancellationToken = default)
        => ExecuteDomainAsync(planVersionId, scheduleRunId: 0, dataCutoffTime: null, strategyProfileVersionId, upstreamResourceBlocks: null, sourcePlanVersionId: null, scope: null, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<SchedulingRunResult> RunSchedulingAndFinalizeAsync(
        int planVersionId,
        int scheduleRunId,
        long strategyProfileVersionId,
        CancellationToken cancellationToken = default)
    {
        // 反查 Run 冻结基线（§5.3.1）：候选需求侧订单数据源钉 BasePlanVersionId，不随执行时刻 ACTIVE 漂移。
        // StrategyProfileVersionId 以 Run 冻结值优先，入参 strategyProfileVersionId 作回退。
        var run = await _connectionManager.QueryFirstOrDefaultAsync<ScheduleRunQueryDto>(
            @"SELECT Id, DataCutoffTime, StrategyProfileVersionId, BasePlanVersionId, ScopeJson
              FROM ScheduleRun WHERE Id = @Id",
            new { Id = scheduleRunId },
            db: DatabaseId.APS);

        if (run == null)
            throw new InvalidOperationException($"ScheduleRun 不存在: ScheduleRunId={scheduleRunId}");

        // M4：反查 Run 冻结 ScopeJson（白天候选局部重排范围载荷），反序列化为强类型 ScopeJsonV2。
        // null/空 = FULL 语义（旧调用兼容，不校验）；序列化口径 = 字符串码（JsonStringEnumConverter），3号位 写入。
        var scope = string.IsNullOrWhiteSpace(run.ScopeJson)
            ? null
            : JsonSerializer.Deserialize<ScopeJsonV2>(run.ScopeJson);

        // 【白天候选】订单池准备（2026-09-28）：Candidate 壳（RunLifecycleService.CreateCandidateRunAsync）
        // 只建 ScheduleRun + PlanVersion、**不装订单**；而 ExecuteDomainAsync 是按本 planVersionId 装载订单的，
        // 故跑之前必须把活跃订单压进本 Candidate PlanVersion（否则订单为空 → 空结果）。
        // 幂等：sp_SyncOrdersToPartitionTable 有 NOT EXISTS(OrderNo, PlanVersionId) 保护，重复调用只补增量。
        // 失败不静默：记错误日志后继续（下游会因无订单产出空结果，日志可定位）。
        try
        {
            var loadedOrders = await _orderLoadingService.LoadOrdersToPartitionTableAsync(planVersionId, cancellationToken);
            _logger.LogInformation(
                "白天候选订单池准备：PlanVersionId={PlanVersionId}，本次装载 {Loaded} 条订单",
                planVersionId, loadedOrders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "白天候选订单池准备失败：PlanVersionId={PlanVersionId}（候选将因无订单而产出空结果，请检查该 PV 的 DomainKey/DomainDefinition）",
                planVersionId);
        }

        // 【白天候选】BOM 实时展开接线（2026-10-08，2号位）。
        //   口径 = 5号位 回执 v1.0（2026-09-24）+ 本号位《白天BOM实时展开方案致5号位》§一：
        //     ① 范围 = ScopeJson.OrderTargets[].OrderCanonicalId（本次局部重排的对象集），**只补缺口**
        //        （服务内部先按白天天次查 MES_API_BOM_Request_Detail、已存在的跳过 ⇒ 幂等）；
        //     ② 5号位 加护1：`sp_ExpandBOMRealtime_vNext` 前置校验 `MES_BOM_Edge_RefreshLog` 非 COMPLETED 会 RAISERROR ——
        //        **必须捕获转可解释结果、不得硬失败** ⇒ 此处 try/catch 记日志后继续（Pegging 仍可用夜间存量 BOM）。
        //   ⚠️ 边界：本方法仅为「白天候选」入口；夜间正式排程的 BOM 由 NightlyBatchOrchestrator 走接货链，不经此处。
        try
        {
            var targetOrderCanonicalIds = scope?.OrderTargets
                .Select(t => t.OrderCanonicalId)
                .Where(id => id > 0)
                .Distinct()
                .ToList() ?? new List<long>();

            if (targetOrderCanonicalIds.Count > 0)
            {
                var expandResult = await _bomRealtimeExpandService.EnsureExpandedAsync(targetOrderCanonicalIds, cancellationToken);
                _logger.LogInformation(
                    "白天候选 BOM 实时展开：目标订单={Requested}，补写 Detail={Inserted}，展开={Expanded}，结果行={Rows}，Issue={Issues}",
                    expandResult.RequestedOrders, expandResult.DetailInserted,
                    expandResult.ExpandedOrders, expandResult.ExpandedRowCount, expandResult.Issues.Count);
                if (expandResult.Issues.Count > 0)
                    _logger.LogWarning("白天候选 BOM 实时展开 Issue（不阻断）：{Issues}", string.Join(" | ", expandResult.Issues));
            }
            else
            {
                _logger.LogInformation(
                    "白天候选 BOM 实时展开：ScopeJson.OrderTargets 为空（非局部重排场景），跳过实时展开、沿用夜间已展开 BOM。PlanVersionId={PlanVersionId}",
                    planVersionId);
            }
        }
        catch (Exception ex)
        {
            // 加护1：不得硬失败 —— 实时展开失败不阻断候选（沿用夜间存量 BOM），记错便于定位。
            _logger.LogError(ex, "白天候选 BOM 实时展开失败（不阻断候选，沿用夜间已展开 BOM）。PlanVersionId={PlanVersionId}", planVersionId);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await ExecuteDomainAsync(
            planVersionId, scheduleRunId, run.DataCutoffTime, run.StrategyProfileVersionId ?? strategyProfileVersionId,
            upstreamResourceBlocks: null, sourcePlanVersionId: run.BasePlanVersionId, scope, cancellationToken);
        stopwatch.Stop();
        var durationSeconds = (int)(stopwatch.ElapsedMilliseconds / 1000);

        if (result.IsSuccess)
        {
            await _scheduleRunService.CompleteAsync(scheduleRunId, durationSeconds, cancellationToken);
        }
        else
        {
            await _scheduleRunService.FailAsync(scheduleRunId, durationSeconds, result.ErrorMessage ?? "排程失败", cancellationToken);
        }

        _logger.LogInformation(
            "白天候选执行收口: ScheduleRunId={RunId}, PlanVersionId={PlanVersionId}, IsSuccess={Success}, 耗时={Elapsed}ms",
            scheduleRunId, planVersionId, result.IsSuccess, stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// 单 Domain 执行单元（分域求解边界）。
    /// 职责：装载本 Domain PlanVersion 的沙盘 → Pegging → 1号位求解 → 结果落盘 → PlanVersion 终态（Computed/ComputeFailed）。
    /// 边界：不处理 ScheduleRun 终态（COMPLETED/PARTIAL_SUCCESS/FAILED），终态由 RunSchedulingAutoAsync 逐 Domain 循环后统一收口；
    ///       失败时返回 IsSuccess=false（不向上抛），供上层做「上游失败阻断」传播。
    /// </summary>
    private async Task<SchedulingRunResult> ExecuteDomainAsync(
        int planVersionId,
        int scheduleRunId,
        DateTime? dataCutoffTime,
        long? strategyProfileVersionId,
        IReadOnlyList<ResourceBlock>? upstreamResourceBlocks,
        int? sourcePlanVersionId = null,
        ScopeJsonV2? scope = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("排程开始: PlanVersionId={PlanVersionId}, ScheduleRunId={RunId}",
            planVersionId, scheduleRunId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 获取计划版本信息
            var planVersion = await _connectionManager.QueryFirstOrDefaultAsync<PlanVersionInfoDto>(
                "SELECT Id, VersionCode, DomainKey, PlanHorizonStart, PlanHorizonEnd FROM PlanVersion WHERE Id = @Id",
                new { Id = planVersionId },
                db: DatabaseId.APS);

            if (planVersion == null)
                throw new InvalidOperationException($"计划版本不存在: PlanVersionId={planVersionId}");

            // 更新状态为 Computing
            await _connectionManager.ExecuteAsync(
                "UPDATE PlanVersion SET Status = 'Computing' WHERE Id = @Id",
                new { Id = planVersionId },
                db: DatabaseId.APS);

            // 幂等清理：删除上次运行留下的脏数据（按 FK 依赖顺序）
            await _connectionManager.ExecuteAsync(
                @"DELETE FROM PeggingSupplyAllocation WHERE PlanVersionId = @Id;
                  DELETE FROM [Pegging]               WHERE PlanVersionId = @Id;
                  DELETE FROM AllocationTaskShare     WHERE PlanVersionId = @Id;
                  DELETE FROM [Task]                  WHERE PlanVersionId = @Id;",
                new { Id = planVersionId },
                db: DatabaseId.APS);

            // ═══════════════════════════════════════════
            // 阶段1: 装载排程沙盘
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{PlanVersionId}] 阶段1: 装载排程沙盘", planVersionId);
            var contextStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var context = await LoadSchedulingContextAsync(planVersion, scheduleRunId, cancellationToken);
            context.StrategyProfileVersionId = strategyProfileVersionId;
            // P0-03：DataCutoffTime 是本 Run 冻结事实时点（ScheduleRun.DataCutoffTime），落到 context 供下游
            // Pegging 供给快照 + DomainSolveRequest.DataCutoffTime 使用；手动入口为 null 时下游回退 Now。
            context.DataCutoffTime = dataCutoffTime;
            if (strategyProfileVersionId.HasValue)
                await LoadStrategyConfigAsync(context, strategyProfileVersionId.Value, cancellationToken);
            contextStopwatch.Stop();
            _logger.LogInformation(
                "[{PlanVersionId}] 沙盘装载完成: Tasks={TaskCount}, Resources={ResourceCount}, MESKeys={MESCount}, 耗时={ContextMs}ms",
                planVersionId, context.Tasks.Count, context.Resources.Count, context.MESRemainingQty.Count, contextStopwatch.ElapsedMilliseconds);

            // ═══════════════════════════════════════════
            // 阶段2: Pegging — 供需挂钩 + 冻结区保护
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{PlanVersionId}] 阶段2: Pegging 供需挂钩", planVersionId);
            // 事项四（§5.3.1 基线快照）：候选需求侧订单数据源钉 BasePlanVersionId（sourcePlanVersionId），
            // 不随执行时刻 ACTIVE 漂移；FULL 场景 sourcePlanVersionId=null，回退为自身 planVersionId。
            var demandSourcePlanVersionId = sourcePlanVersionId ?? planVersionId;
            var allOrderIds = (await _connectionManager.QueryAsync<long>(
                "SELECT Id FROM [Order] WHERE PlanVersionId = @PlanVersionId",
                new { PlanVersionId = demandSourcePlanVersionId },
                db: DatabaseId.APS)).ToList();

            // M4：OrderTargets[].OrderCanonicalId ↔ 本 Run 装载订单集（demand 侧 [Order].OrderCanonicalId）一致性校验。
            // 任一目标订单不在装载集内 → fail-closed 拒绝执行，不静默降级。
            if (scope?.OrderTargets is { Count: > 0 } orderTargets)
            {
                var loadedOrderCanonicalIds = (await _connectionManager.QueryAsync<long>(
                    "SELECT OrderCanonicalId FROM [Order] WHERE PlanVersionId = @PlanVersionId AND OrderCanonicalId IS NOT NULL",
                    new { PlanVersionId = demandSourcePlanVersionId },
                    db: DatabaseId.APS)).ToHashSet();
                ScopeJsonV2Validator.ValidateOrderTargetsConsistency(orderTargets, loadedOrderCanonicalIds);
            }

            // P0-04：Candidate 判定 = 本次 Run 冻结了 BasePlanVersionId（sourcePlanVersionId != null）。
            // Candidate 场景装载「其它 Domain 当前 ACTIVE 在共享 Resource 上的占用」作为 ExternalDomainResourceBlocks（§11）。
            var isCandidate = sourcePlanVersionId.HasValue;
            IReadOnlyList<ResourceBlock>? externalDomainResourceBlocks = null;
            if (isCandidate)
            {
                externalDomainResourceBlocks = await LoadExternalDomainResourceBlocksAsync(
                    planVersion.DomainKey, context, sourcePlanVersionId!.Value, planVersionId, cancellationToken);
                _logger.LogInformation(
                    "[{PlanVersionId}] Candidate 外部 Domain 占用块: {Count} 条", planVersionId, externalDomainResourceBlocks.Count);
            }

            var peggingRequest = BuildPeggingRequest(
                planVersionId, planVersion.DomainKey, allOrderIds, context, upstreamResourceBlocks,
                isCandidate, sourcePlanVersionId, externalDomainResourceBlocks);
            var peggingResults = (await _peggingOrchestrator.ExecuteBatchPeggingWorkflowAsync(
                peggingRequest, cancellationToken)).ToList();

            var peggingFailed = peggingResults.Where(r => !r.IsSuccess).ToList();
            if (peggingFailed.Count > 0)
            {
                _logger.LogWarning(
                    "[{PlanVersionId}] Pegging 部分失败: {FailCount}/{Total}，继续排程",
                    planVersionId, peggingFailed.Count, peggingResults.Count);
                foreach (var f in peggingFailed)
                    _logger.LogWarning("[{PlanVersionId}] Pegging 失败 OrderId={OrderId}: {Err}",
                        planVersionId, f.OrderId, f.ErrorMessage);
            }

            var totalTasks   = peggingResults.Sum(r => r.GeneratedTasks.Count);
            var totalAlloc   = peggingResults.Sum(r => r.SupplyAllocationCount);
            var logicalProductionDemandCount = peggingResults.Sum(r => r.Voucher?.LogicalProductionDemands?.Count ?? 0);
            var peggingMs    = peggingResults.Sum(r => r.PeggingMs);
            var solverMs     = peggingResults.Sum(r => r.SolverMs);
            var persistMs    = peggingResults.Sum(r => r.PersistMs);
            _logger.LogInformation(
                "[{PlanVersionId}] Pegging 完成: 生成Task={Tasks}, 分配={Alloc}",
                planVersionId, totalTasks, totalAlloc);

            // V1.2：Pegging已完成Task生成并持久化，无需回填context或再次调用Solver
            // 阶段3已在PeggingOrchestrator内部完成：LogicalProductionDemands → TaskDrafts → SolveAsync → FinalTasks
            _logger.LogInformation("[{PlanVersionId}] 阶段3: 有限产能排程已在Pegging阶段完成", planVersionId);
            _logger.LogInformation("[{PlanVersionId}] 阶段4: 排程结果已在Pegging阶段持久化", planVersionId);

            // ═══════════════════════════════════════════
            // 阶段5: 更新PlanVersion状态 + 更新 ScheduleRun
            // ═══════════════════════════════════════════
            var isSuccess = peggingFailed.Count == 0;
            var finalStatus = isSuccess ? "Computed" : "ComputeFailed";
            await _connectionManager.ExecuteAsync(
                "UPDATE PlanVersion SET Status = @Status, ComputedAt = GETDATE() WHERE Id = @Id",
                new { Id = planVersionId, Status = finalStatus },
                db: DatabaseId.APS);

            // ═══════════════════════════════════════════
            // 阶段6: 快照封存（§2.6）
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{PlanVersionId}] 阶段6: 快照封存", planVersionId);
            try
            {
                var snapshotInfo = await _snapshotService.SaveAsync(context, planVersionId, cancellationToken);
                _logger.LogInformation(
                    "[{PlanVersionId}] 快照封存完成: 压缩后={CompressedMB:F1}MB, SHA256={Hash}",
                    planVersionId, snapshotInfo.CompressedSize / 1048576.0, snapshotInfo.FileHash[..12] + "...");
            }
            catch (Exception snapshotEx)
            {
                _logger.LogWarning(snapshotEx, "[{PlanVersionId}] 快照封存失败（非致命，不影响排程结果）", planVersionId);
            }

            stopwatch.Stop();

            await LogETLAsync(planVersion.VersionCode, "Scheduling",
                $"排程完成 | 已排:{totalTasks} | 失败:{peggingFailed.Count} | 耗时:{stopwatch.ElapsedMilliseconds}ms",
                isSuccess ? "SUCCESS" : "PARTIAL");

            var result = new SchedulingRunResult
            {
                PlanVersionId    = planVersionId,
                VersionCode      = planVersion.VersionCode,
                IsSuccess        = isSuccess,
                // 【2026-09-29 修复】此处原**不设 `ErrorMessage`** ⇒ 「优雅失败」（Pegging 未抛异常、由 `isSuccess =
                //   peggingFailed.Count == 0` 判出的 false）返回的 `ErrorMessage` 恒为 null。
                //   后果链：`RunSchedulingAndFinalizeAsync` 拿 `result.ErrorMessage ?? "排程失败"` 回写 `ScheduleRun.ErrorMessage`
                //   ⇒ **库里只留「排程失败」四个字**，真实原因（例：「DemandPriority 配置错误：未知字段 'OrderId'」）
                //   只存在于 `_logger.LogWarning`（见上方 409 行同源），**失败现场无法从库自证**。
                //   2026-09-29 首次真跑（PV632/Run818）即栽在此：IsSuccess=False 且 ErrorMessage 为空，只能挖日志。
                //   现把 Pegging 失败原因**摘要**带上（前 5 条含 OrderId，超出部分指向日志），与日志同源、不新增取值路径。
                ErrorMessage     = isSuccess
                    ? null
                    : $"Pegging 失败 {peggingFailed.Count}/{peggingResults.Count}："
                      + string.Join("；", peggingFailed.Take(5).Select(f => $"OrderId={f.OrderId}:{f.ErrorMessage}"))
                      + (peggingFailed.Count > 5 ? $"…（其余 {peggingFailed.Count - 5} 条见日志）" : ""),
                ScheduledCount   = totalTasks,
                UnscheduledCount = peggingFailed.Count,
                ElapsedMs        = stopwatch.ElapsedMilliseconds,
                // FULL §9：成功 Domain 的 FinalTask 共享 Resource 占用区间，供后续 Domain 作不可用时间窗
                EmittedResourceBlocks = isSuccess ? ExtractResourceOccupancyBlocks(peggingResults) : new List<ResourceBlock>(),
                // §16/§21.6/D18：Domain 级性能埋点
                Metrics = new DomainPerformanceMetrics
                {
                    DomainKey                    = planVersion.DomainKey,
                    DemandCount                  = allOrderIds.Count,
                    LogicalProductionDemandCount = logicalProductionDemandCount,
                    FinalTaskCount               = totalTasks,
                    ContextBuildMs               = contextStopwatch.ElapsedMilliseconds,
                    PeggingMs                    = peggingMs,
                    SolverMs                     = solverMs,
                    PersistMs                    = persistMs,
                    TotalMs                      = stopwatch.ElapsedMilliseconds
                }
            };

            _logger.LogInformation(
                "[{PlanVersionId}] Domain 性能指标: DomainKey={DomainKey}, Demand={Demand}, LPD={Lpd}, FinalTask={FinalTask}, ContextBuild={ContextBuildMs}ms, Pegging={PeggingMs}ms, Solver={SolverMs}ms, Persist={PersistMs}ms, Total={TotalMs}ms",
                planVersionId, planVersion.DomainKey, allOrderIds.Count, logicalProductionDemandCount, totalTasks,
                contextStopwatch.ElapsedMilliseconds, peggingMs, solverMs, persistMs, stopwatch.ElapsedMilliseconds);

            _logger.LogInformation(
                "排程完成: PlanVersionId={PlanVersionId}, 已排={Scheduled}, 未排={Unscheduled}, 耗时={Elapsed}ms",
                planVersionId, totalTasks, peggingFailed.Count, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "排程失败: PlanVersionId={PlanVersionId}", planVersionId);

            try
            {
                await _connectionManager.ExecuteAsync(
                    "UPDATE PlanVersion SET Status = 'ComputeFailed' WHERE Id = @Id",
                    new { Id = planVersionId },
                    db: DatabaseId.APS);

                await LogETLAsync($"PV-{planVersionId}", "Scheduling",
                    $"排程失败: {ex.Message}", "FAILED");
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "排程失败后回写状态异常（非致命）");
            }

            // 单 Domain 失败不向上抛：返回失败结果，交由 RunSchedulingAutoAsync 做「上游失败阻断」+ ScheduleRun 终态收口
            return new SchedulingRunResult
            {
                PlanVersionId = planVersionId,
                IsSuccess     = false,
                ErrorMessage  = ex.Message,
                ElapsedMs     = stopwatch.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// 将本 Domain 的 PlanVersion 标记为「因上游失败被阻断」（ComputeFailed，与真实计算失败同终态；阻断原因记录在日志与 ScheduleRun.ErrorMessage）。
    /// </summary>
    private async Task BlockDomainAsync(int planVersionId)
    {
        await _connectionManager.ExecuteAsync(
            "UPDATE PlanVersion SET Status = 'ComputeFailed' WHERE Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);
    }

    /// <summary>
    /// 阶段1: 装载排程沙盘（从APS库读取数据组装 SchedulingContext）
    /// 
    /// 子步骤（2号位端到端通路）：
    ///   1.1 订单加载          → context.Orders（供2号位Pegging使用）
    ///   1.2 BOM 加载          → 保留接口（V1 不真装载 BOM，由 5号位 Pegging 真正消费）
    ///   1.3 物料属性           → 订单加载时 JOIN Material 取得 ProductFamilyId/LLC
    ///   1.4 资源 + 日历        → context.Resources / context.ResourceCalendars（日历 V1 默认 7x24）
    ///   1.5 库存装载           → context.InventorySupplies（从 InventoryBalance）
    ///   1.6 Task 拆批          → 按 RoutingOperation 把每个 Order 拆为 N 个 SchedulingTask
    ///                           + 批量 INSERT 到 [Task] 表（阶段4 回写需要真实 TaskId）
    /// 
    /// V1 保守策略（5号位 Pegging 接入后可替换）：
    ///   - 无真 Pegging：Order 数量直接分配到 Task（一 Order 一 Task 链）
    ///   - 无 BOM 下钻：只处理订单物料自身的工艺路线
    ///   - 无批量切分：整订单数量 × StandardDuration 作为 Task 工时
    ///   - 资源指派：按 OperationResourceEligibility 取默认资源
    /// </summary>
    private async Task<SchedulingContext> LoadSchedulingContextAsync(
        PlanVersionInfoDto planVersion,
        int scheduleRunId,
        CancellationToken cancellationToken)
    {
        var context = new SchedulingContext
        {
            PlanVersionId    = planVersion.Id.ToString(),
            ScheduleRunId    = scheduleRunId,
            PlanHorizonStart = planVersion.PlanHorizonStart,
            PlanHorizonEnd   = planVersion.PlanHorizonEnd
        };

        // 1.4 资源 + 日历
        await LoadResourcesAndCalendarAsync(context, cancellationToken);
        _logger.LogInformation("[{PlanVersionId}] 1.4 资源加载: {Count}", planVersion.Id, context.Resources.Count);

        // 1.5 库存双源汇聚（§2.5.2）
        await LoadInventoryAsync(context, cancellationToken);
        _logger.LogInformation("[{PlanVersionId}] 1.5 库存加载: {Count} 个(物料+产品族+工厂) 组合",
            planVersion.Id, context.InventorySupplies.Count);

        // 1.6 Task 拆批 → V1.2 已废弃：Task由2号位Pegging在供需挂钩后生成，不再预拆批
        // v5.1.2冻结设计（§3.1）：批次拆分由1号位IFiniteCapacityScheduler执行
        _logger.LogInformation("[{PlanVersionId}] 1.6 跳过预拆批（Task将由Pegging生成）", planVersion.Id);

        // 1.7 MES 进度快照装载（按 ScheduleRunId 从 StageProgressSnapshot 读 RemainingQty）
        if (scheduleRunId > 0)
        {
            await LoadMESProgressAsync(context, scheduleRunId, cancellationToken);
            _logger.LogInformation("[{PlanVersionId}] 1.7 MES进度装载: {Count} 条",
                planVersion.Id, context.MESRemainingQty.Count);
        }

        return context;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 阶段1 各子步骤实现
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 1.7 从 StageProgressSnapshot 装载 MES 进度（按 ScheduleRunId 分区）
    /// 1号位用 MESRemainingQty 决定每道工序实际还需排多少量
    /// </summary>
    private async Task LoadMESProgressAsync(SchedulingContext context, int scheduleRunId, CancellationToken ct)
    {
        var rows = await _connectionManager.QueryAsync<MESProgressLoadDto>(
            @"SELECT ProductionInstructionNo, MaterialCode, StageCode, RemainingQty
              FROM StageProgressSnapshot
              WHERE ScheduleRunId = @ScheduleRunId
                AND RemainingQty  > 0",
            new { ScheduleRunId = scheduleRunId },
            db: DatabaseId.APS);

        foreach (var r in rows)
        {
            var key = SchedulingContext.BuildMESKey(r.ProductionInstructionNo, r.MaterialCode, r.StageCode);
            context.MESRemainingQty[key] = r.RemainingQty;
        }
    }


    /// <summary>
    /// 1.4 加载资源 + 日历
    /// V1 日历策略：7x24 连续可用（计划期全覆盖）；待专门的 ResourceCalendar 装载服务落地后替换
    /// </summary>
    private async Task LoadResourcesAndCalendarAsync(SchedulingContext context, CancellationToken ct)
    {
        var resources = await _connectionManager.QueryAsync<ResourceLoadDto>(
            @"SELECT
                r.Id              AS ResourceId,
                r.ResourceCode,
                r.ResourceName,
                r.FactoryId,
                r.ProductionDepartmentId,
                r.CapacityFactor,
                ISNULL(rpc.DispatchPriority, 100) AS DispatchPriority,
                ISNULL(rpc.LocalDisableFlag, 0)   AS LocalDisableFlag
              FROM Resource r
              LEFT JOIN ResourcePlanningContext rpc
                     ON rpc.ResourceId = r.Id
                    AND (rpc.EffectiveTo IS NULL OR rpc.EffectiveTo >= CAST(GETDATE() AS DATE))
                    AND rpc.EffectiveFrom <= CAST(GETDATE() AS DATE)
              WHERE r.IsActive = 1
                AND r.Status = 'AVAILABLE'",
            db: DatabaseId.APS);

        // PM 0923 §九：取消 730 天 7×24 合成；只加载 ResourceCalendarSlot 真实可用窗口，无日历行=该资源不可用。
        var calendarRows = await _connectionManager.QueryAsync<ResourceCalendarSlotLoadDto>(
            @"SELECT ResourceId, StartTime, EndTime, AvailableFlag
                FROM ResourceCalendarSlot
               WHERE AvailableFlag = 1",
            db: DatabaseId.APS);
        var realCalendars = calendarRows
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(c => new TimeWindow(c.StartTime, c.EndTime)).ToList());

        foreach (var r in resources)
        {
            if (r.LocalDisableFlag) continue;

            var resIdStr = r.ResourceId.ToString();
            context.Resources.Add(new SchedulingResource
            {
                ResourceId       = resIdStr,
                ResourceName     = r.ResourceName,
                ResourceCode     = r.ResourceCode,
                FactoryId        = r.FactoryId.ToString(),
                ProductionDepartmentId = r.ProductionDepartmentId,
                CapacityFactor   = r.CapacityFactor,
                DispatchPriority = r.DispatchPriority,
                IsAvailable      = true
            });

            // PM 0923 §九：无有效日历=不可用（设备+人工统一）；铺天数=5号位，2号位 只加载不合成。
            // 有 ResourceCalendarSlot 真实可用窗口才写入；无日历行则该资源不产出日历（=不可用）。
            if (realCalendars.TryGetValue(r.ResourceId, out var windows) && windows.Count > 0)
            {
                context.ResourceCalendars[resIdStr] = windows;
            }
        }

        // ── 人工能力槽装载 + 运行时投影（PM 0923 资源模型裁决）──
        // ManualCapacitySlot 不进入正式 Resource 主数据；装载时投影为合成 SolverResource
        //（合成 ResourceId = ManualSlotResourceOffset + ManualSlotId，运行时映射键，不落库、不暴露负号）。
        // PM §十二：1 槽=1 并行能力单元，CapacityFactor 固定 1（不引入 CapacityFactor=人数）。
        var manualSlots = await _connectionManager.QueryAsync<ManualCapacitySlotLoadDto>(
            @"SELECT ManualSlotId, ProductionDepartmentId, OperationName, SlotCode
                FROM ManualCapacitySlot
               WHERE IsActive = 1",
            db: DatabaseId.APS);

        var manualSlotCalendarRows = await _connectionManager.QueryAsync<ManualCapacitySlotCalendarLoadDto>(
            @"SELECT ManualSlotId, StartTime, EndTime, AvailableFlag
                FROM ManualCapacitySlotCalendar
               WHERE AvailableFlag = 1",
            db: DatabaseId.APS);
        var manualCalendars = manualSlotCalendarRows
            .GroupBy(c => c.ManualSlotId)
            .ToDictionary(g => g.Key, g => g.Select(c => new TimeWindow(c.StartTime, c.EndTime)).ToList());

        foreach (var slot in manualSlots)
        {
            var syntheticIdStr = (SolverResourceProjection.ManualSlotResourceOffset + slot.ManualSlotId).ToString();

            context.Resources.Add(new SchedulingResource
            {
                ResourceId       = syntheticIdStr,
                ResourceName     = SolverResourceProjection.ManualSlotResourceCodePrefix + slot.OperationName + ":" + slot.SlotCode,
                ResourceCode     = SolverResourceProjection.ManualSlotResourceCodePrefix + slot.OperationName + ":" + slot.SlotCode,
                FactoryId        = string.Empty,
                ProductionDepartmentId = slot.ProductionDepartmentId,
                CapacityFactor   = 1.0m,
                DispatchPriority = 100,
                IsAvailable      = true
            });

            // 无 ManualCapacitySlotCalendar 行 = 不可用（与设备同口径，PM §九）。
            if (manualCalendars.TryGetValue(slot.ManualSlotId, out var windows) && windows.Count > 0)
            {
                context.ResourceCalendars[syntheticIdStr] = windows;
            }
        }

        // 1号位 回执 §四 提请：S4 取消 730 天合成后，真实日历覆盖 = 正排物理上限。
        // 软边界（正排不受 PlanningEnd 约束）要求日历铺到「兜底日期 = PlanHorizonEnd + 180 天」；
        // 覆盖不足则正排末端找不到槽 → 静默落 Unscheduled。此处只打诊断日志（非硬报错），
        // 铺天数属 5号位 数据维护（PM 0923 §九/§十），2号位 只加载 + 出证据。
        const int softBoundaryCalendarBufferDays = 180;
        var horizonFloor = context.PlanHorizonEnd.AddDays(softBoundaryCalendarBufferDays);
        var allCalendarWindows = context.ResourceCalendars.Values.SelectMany(w => w).ToList();
        if (allCalendarWindows.Count == 0)
        {
            _logger.LogWarning(
                "[ResourceCalendar] 本次运行无任何资源日历可用窗口（设备 + 人工均空），正排将无处放槽、可能全部落 Unscheduled。铺天数=5号位 数据维护，2号位 只加载不合成（PM 0923 §九）。");
        }
        else
        {
            var maxCalEnd = allCalendarWindows.Max(w => w.End);
            if (maxCalEnd < context.PlanHorizonEnd)
            {
                _logger.LogWarning(
                    "[ResourceCalendar] 资源日历最大末端 {MaxCalEnd:O} 未覆盖计划期结束 {PlanHorizonEnd:O}，窗口内任务即可能无槽；请 5号位 核对铺天数。",
                    maxCalEnd, context.PlanHorizonEnd);
            }
            else if (maxCalEnd < horizonFloor)
            {
                _logger.LogWarning(
                    "[ResourceCalendar] 资源日历最大末端 {MaxCalEnd:O} 未覆盖软边界兜底日期 {HorizonFloor:O}（PlanHorizonEnd+{BufferDays} 天，正排不受 PlanningEnd 约束）；可能静默落 Unscheduled（1号位 回执 §四）。请 5号位 核对铺天数。",
                    maxCalEnd, horizonFloor, softBoundaryCalendarBufferDays);
            }
        }
    }

    /// <summary>
    /// 1.5 从 InventoryBalance + SupplyFact_Pipeline 全量装载库存到 SchedulingContext.InventorySupplies
    ///
    /// 装载内容（2号位职责）：
    ///   1. INVENTORY：从 InventoryBalance 读取现有库存（ERP + MES 合并后的可用量）
    ///   2. PIPELINE：从 SupplyFact_Pipeline 读取在途/管道供给（v5.1.3 统一供给事实层）
    ///
    /// ⚠️ 【待对齐 5号位 — B 项越界】
    ///   当前 sp_SyncInventorySnapshot 里硬编码了"双源互斥判定 + InventoryAvailabilityRule 筛选"，
    ///   这部分是 5号位业务规则引擎的职责。后续应改为：
    ///     - 2号位 SP 只做 L2→L3→L4 管道流转（读规则表 + JOIN 应用）
    ///     - 5号位负责 InventoryAvailabilityRule 表内容维护 + Pegging 时的运行时判定
    ///   V1.2: 5号位接入前，SP 里的规则逻辑保持不动。
    /// </summary>
    private async Task LoadInventoryAsync(SchedulingContext context, CancellationToken ct)
    {
        // ── 1. 装载现有库存（INVENTORY）──
        var balances = await _connectionManager.QueryAsync<InventoryLoadDto>(
            @"SELECT MaterialCode, ProductFamilyId, FactoryId, AvailableQty
              FROM InventoryBalance
              WHERE AvailableQty > 0",
            db: DatabaseId.APS);

        foreach (var b in balances)
        {
            var key = SchedulingContext.BuildInventoryKey(b.MaterialCode, b.ProductFamilyId, b.FactoryId);
            context.InventorySupplies[key] = b.AvailableQty;
        }

        // ── 2. 装载管道供给（PIPELINE）——来源：SupplyFact_Pipeline ──
        // AvailableTime 已改由 2号位运行时 EtaInvariant 三级链计算（sp_SyncPipelineSupply 不再落库 AvailableTime）。
        // V1 排程装载侧暂以原始 ETA 事实做计划期过滤（Manual ETA / Arrival-to-Usable Offset 精算待与 Pegging 对齐后接入）。
        var inTransits = await _connectionManager.QueryAsync<InTransitLoadDto>(
            @"SELECT
                  sfp.MaterialCode,
                  sfp.ProductFamilyId,
                  sfp.FactoryId,
                  sfp.Quantity       AS AvailableQty,
                  sfp.ETA            AS EstimatedArrivalTime
              FROM SupplyFact_Pipeline sfp
              WHERE sfp.IsActive = 1
                AND sfp.Quantity > 0
                AND (sfp.ETA IS NULL OR sfp.ETA <= @PlanHorizonEnd)",
            new { PlanHorizonEnd = context.PlanHorizonEnd },
            db: DatabaseId.APS);

        foreach (var it in inTransits)
        {
            var key = SchedulingContext.BuildInventoryKey(it.MaterialCode, it.ProductFamilyId, it.FactoryId);

            if (context.InventorySupplies.ContainsKey(key))
                context.InventorySupplies[key] += it.AvailableQty;
            else
                context.InventorySupplies[key] = it.AvailableQty;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // v5.1.2 架构变更说明：
    // GenerateAndPersistTasksAsync 和 BulkInsertTasksAsync 方法已废弃
    //
    // 原因：Task 表的 INSERT 职责已转移至 PeggingOrchestrator（2号位）
    //       - PeggingOrchestrator 在供需匹配阶段生成 TaskDraft 并落库
    //       - SchedulingOrchestrator 只负责 UPDATE Task 表（填充时间/资源分配结果）
    //       - 这样避免了重复落库，保证了 Task 与 PeggingSupplyAllocation 的事务一致性
    //
    // 详见：PeggingOrchestrator.PersistDomainAndPeggingInTransactionAsync (line 238-280)
    // ═══════════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════════
    // 阶段4 结果落盘 — 已废弃：Task 落盘已移入 Pegging 阶段（PersistDomainAndPeggingInTransactionAsync）
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task LoadStrategyConfigAsync(SchedulingContext context, long strategyProfileVersionId, CancellationToken ct)
    {
        var rows = (await _connectionManager.QueryAsync<StrategyConfigLoadDto>(
            @"SELECT rs.RuleSetCode, rs.RuleSetName, rsv.VersionCode AS RuleSetVersionCode,
                     ps.ParameterSetCode, ps.ParameterSetName, psv.VersionCode AS ParameterSetVersionCode,
                     spv.RuleSetVersionId, spv.ParameterSetVersionId
              FROM StrategyProfileVersion spv
              JOIN RuleSetVersion     rsv ON rsv.Id = spv.RuleSetVersionId
              JOIN RuleSet            rs  ON rs.Id  = rsv.RuleSetId
              JOIN ParameterSetVersion psv ON psv.Id = spv.ParameterSetVersionId
              JOIN ParameterSet        ps  ON ps.Id  = psv.ParameterSetId
              WHERE spv.Id = @Id",
            new { Id = strategyProfileVersionId },
            db: DatabaseId.APS)).ToList();

        if (rows.Count == 0) return;

        context.RuleConfigs = rows.Select(r => new RuleConfig
        {
            RuleSetVersionId = r.RuleSetVersionId,
            RuleSetCode      = r.RuleSetCode,
            RuleSetName      = r.RuleSetName,
            VersionCode      = r.RuleSetVersionCode
        }).ToList();

        context.SchedulingParamsList = rows.Select(r => new SchedulingParamConfig
        {
            ParameterSetVersionId = r.ParameterSetVersionId,
            ParameterSetCode      = r.ParameterSetCode,
            ParameterSetName      = r.ParameterSetName,
            VersionCode           = r.ParameterSetVersionCode
        }).ToList();

        _logger.LogInformation("策略配置加载完成: StrategyProfileVersionId={Id}, RuleConfigs={RC}, Params={PC}",
            strategyProfileVersionId, context.RuleConfigs.Count, context.SchedulingParamsList.Count);
    }

    /// <summary>
    /// 记录 ETL 日志
    /// </summary>
    private async Task LogETLAsync(string batchNo, string step, string message, string status)
    {
        try
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
                  VALUES (@BatchNo, @Step, @Message, @Status, GETDATE())",
                new { BatchNo = batchNo, Step = step, Message = message, Status = status },
                db: DatabaseId.APS);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入ETL日志失败（非致命）");
        }
    }

    /// <summary>
    /// 构建 PeggingExecutionRequest（从 SchedulingContext 提取必要字段）
    ///
    /// 冻结区规则（文档 §2.3）：
    ///   当前时间起的 2 小时为滑动冻结窗口；MES 已下发任务不可重排
    ///
    /// 虚拟库存：
    ///   从 context.InventorySupplies 里提取（当前 V1 不含跨域供给，由后续3号位扫描补充）
    /// </summary>
    private static PeggingExecutionRequest BuildPeggingRequest(
        int planVersionId,
        string domainKey,
        List<long> allOrderIds,
        SchedulingContext context,
        IReadOnlyList<ResourceBlock>? upstreamResourceBlocks,
        bool isCandidate,
        int? basePlanVersionId,
        IReadOnlyList<ResourceBlock>? externalDomainResourceBlocks)
    {
        var now = DateTime.Now;
        var orderIds = allOrderIds;

        return new PeggingExecutionRequest
        {
            PlanVersionId     = planVersionId,
            DomainKey         = domainKey,
            OrderIds          = orderIds,
            // P0-03：SnapshotAt（供给快照基准）改用 ScheduleRun 冻结 DataCutoffTime，不再 DateTime.Now；
            // 手动入口 DataCutoffTime 为 null 时回退 now。
            SnapshotAt        = context.DataCutoffTime ?? now,
            FrozenWindowStart = now,
            FrozenWindowEnd   = now.AddHours(2),   // §2.3 滑动冻结窗口
            AllowCrossFactory = false,
            DefaultStrategy   = Core.Enum.PeggingStrategyType.FIFO,
            ProductFamilyIds  = context.Tasks
                .Select(t => int.TryParse(t.MaterialId, out var mid) ? mid : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList(),
            UpstreamResourceBlocks = upstreamResourceBlocks,
            MaxBomDepth       = 10,
            TimeoutSeconds    = 300,
            // P0-04：Candidate 模式（sourcePlanVersionId != null）→ CANDIDATE；否则 FULL_RUN。
            ExecutionMode     = isCandidate ? "CANDIDATE" : "FULL_RUN",
            IsCandidate       = isCandidate,
            BasePlanVersionId = basePlanVersionId,
            ExternalDomainResourceBlocks = externalDomainResourceBlocks,
            SchedulingContext = context  // V1.2：传递完整沙盘上下文供1号位使用
        };
    }

    /// <summary>
    /// FULL §9：从已排 Task 提取共享 Resource 占用区间（ResourceId + 起止时间）。
    /// 只收 ResourceId 与 PlannedStart/End 均齐全的 FinalTask；占用块累积后作为后续 Domain 的不可用时间窗。
    /// 不在此区分「共享 vs 本域独享」——把全部占用区间传给后续 Domain，后续 Domain 只用其中自己真正使用的 Resource，
    /// 语义上与 Candidate 的 ExternalDomainResourceBlocks 一致（§11）。
    /// </summary>
    private static List<ResourceBlock> ExtractResourceOccupancyBlocks(IEnumerable<PeggingOrchestrationResult> peggingResults)
    {
        var blocks = new List<ResourceBlock>();
        foreach (var r in peggingResults)
        {
            foreach (var t in r.GeneratedTasks)
            {
                if (t.ResourceId.HasValue && t.PlannedStartTime.HasValue && t.PlannedEndTime.HasValue)
                {
                    blocks.Add(new ResourceBlock
                    {
                        ResourceId = t.ResourceId.Value,
                        StartTime  = t.PlannedStartTime.Value,
                        EndTime    = t.PlannedEndTime.Value,
                        Reason     = "UPSTREAM_DOMAIN_FULL"
                    });
                }
            }
        }
        return blocks;
    }

    /// <summary>
    /// P0-04 §11：装载「其它 Domain 当前 ACTIVE 在共享 Resource 上的不可移动占用」作为 Candidate 的 ExternalDomainResourceBlocks。
    /// 语义与 FULL 的 UpstreamDomainResourceBlocks 不同：这里不看 Quantity-Time，只看已排 Task 的 ResourceId + 起止时间。
    /// 排除自身候选 PlanVersion（尚未生成终态）与 Base PlanVersion（比较基线，非外部 Domain）。
    /// </summary>
    private async Task<IReadOnlyList<ResourceBlock>> LoadExternalDomainResourceBlocksAsync(
        string currentDomainKey,
        SchedulingContext context,
        int basePlanVersionId,
        int currentPlanVersionId,
        CancellationToken cancellationToken)
    {
        var resourceIds = context.Resources.Select(r => r.ResourceId).Distinct().ToList();
        if (resourceIds.Count == 0)
            return Array.Empty<ResourceBlock>();

        // VersionCategory = 'ACTIVE' 为身份口径（D7 词表：身份看 VersionCategory），Status 只表达执行进度。
        //
        // 【2026-09-29 分片】`resourceIds` = 本 Domain 的资源清单。FACTORY_FAMILY 域可覆盖到接近 `Resource` 全表量级
        //   （实测 `Resource` 共 7,768 行）⇒ 远超 2100。Dapper 把 `IN @ResourceIds` 展开成**逐个参数**，
        //   SQL Server 单次 RPC 上限 2100 ⇒ 不分片就是运行时硬失败（不是慢，是报错）。
        //   分片键 = ResourceId：各批结果取并集与单条 `IN` 等价（无聚合、无跨行 ORDER BY 语义）。
        var rows = new List<ExternalDomainBlockRow>();
        for (var offset = 0; offset < resourceIds.Count; offset += SqlServerInParameterLimit)
        {
            var chunk = resourceIds.Skip(offset).Take(SqlServerInParameterLimit).ToList();
            rows.AddRange(await _connectionManager.QueryAsync<ExternalDomainBlockRow>(
                @"SELECT t.ResourceId, t.PlannedStartTime, t.PlannedEndTime, pv.DomainKey, pv.Id AS SourcePlanVersionId
                  FROM [Task] t
                  JOIN PlanVersion pv ON pv.Id = t.PlanVersionId
                  WHERE pv.VersionCategory = 'ACTIVE'
                    AND pv.Id NOT IN (@CurrentPlanVersionId, @BasePlanVersionId)
                    AND pv.DomainKey <> @CurrentDomainKey
                    AND t.ResourceId IN @ResourceIds
                    AND t.PlannedStartTime IS NOT NULL
                    AND t.PlannedEndTime IS NOT NULL",
                new
                {
                    CurrentPlanVersionId = currentPlanVersionId,
                    BasePlanVersionId    = basePlanVersionId,
                    CurrentDomainKey     = currentDomainKey,
                    ResourceIds          = chunk,
                },
                db: DatabaseId.APS));
        }

        // D8/R17/T18 双保险：SQL WHERE 已排除本域/自身版本/基线版本，此处再以纯函数兜底过滤，
        // 保证「同TaskNo/本域旧块」在任何情况下都不作为外部 ResourceBlock 阻挡自己（供 T18 单测覆盖）。
        return rows
            .Where(r => !IsSelfOrBaselineExternalBlock(
                r.DomainKey,
                r.SourcePlanVersionId,
                currentDomainKey,
                currentPlanVersionId,
                basePlanVersionId))
            .Select(r => new ResourceBlock
            {
                ResourceId          = r.ResourceId,
                StartTime           = r.PlannedStartTime,
                EndTime             = r.PlannedEndTime,
                Reason              = "EXTERNAL_DOMAIN_ACTIVE",
                SourceDomainKey     = r.DomainKey,
                SourcePlanVersionId = r.SourcePlanVersionId,
                Immutable           = true,
            })
            .ToList();
    }

    /// <summary>
    /// D8/R17/T18（同TaskNo自阻挡）：判定某外部 ACTIVE 块是否为「本域 / 自身版本 / 基线版本」旧块。
    /// 三类必须从 ExternalDomainResourceBlocks 排除——本域旧块应走锚点（ExecutionConstraint）而非外部阻挡，
    /// 自身/基线版本是当前求解与比较基线、非外部域。返回 true = 排除（不得作为外部块）。
    /// </summary>
    internal static bool IsSelfOrBaselineExternalBlock(
        string blockDomainKey, int blockSourcePlanVersionId,
        string currentDomainKey, int currentPlanVersionId, int basePlanVersionId)
    {
        if (string.Equals(blockDomainKey, currentDomainKey, StringComparison.OrdinalIgnoreCase))
            return true;
        if (blockSourcePlanVersionId == currentPlanVersionId || blockSourcePlanVersionId == basePlanVersionId)
            return true;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 域间依赖拓扑（§5/§7/§15/§23 — 读 Domain_Dependency 表，真实 DomainKey 口径）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从 Domain_Dependency 读取跨域血缘边（真实 DomainKey 口径，不再 JOIN ProductFamily.Code 冒充）。
    /// 只保留两端均落在本次 Run 的 DomainKey 集合内的边；表空（V1 现状）返回空集合。
    /// </summary>
    private async Task<List<(string Upstream, string Downstream)>> LoadDomainDependencyEdgesAsync(
        IReadOnlyList<string> domainKeys,
        CancellationToken cancellationToken)
    {
        var rows = (await _connectionManager.QueryAsync<DomainDependencyRow>(
            @"SELECT UpstreamDomainCode, DownstreamDomainCode
              FROM Domain_Dependency
              WHERE UpstreamDomainCode <> DownstreamDomainCode",
            db: DatabaseId.APS)).ToList();

        if (rows.Count == 0)
            return new List<(string, string)>();

        var keySet = domainKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rows
            .Where(r => keySet.Contains(r.UpstreamDomainCode) && keySet.Contains(r.DownstreamDomainCode))
            .Select(r => (r.UpstreamDomainCode, r.DownstreamDomainCode))
            .ToList();
    }

    /// <summary>
    /// 对本次 Run 的 DomainKey 集合做 Kahn 拓扑排序（依赖边 = Domain_Dependency，真实 DomainKey 口径）。
    /// 无有效依赖边（V1 现状）或检测到环时，降级为 ExpectedDomainKeysJson 冻结顺序（数组序）并记日志。
    /// </summary>
    private List<string> OrderDomainsTopologically(
        IReadOnlyList<string> domainKeys,
        List<(string Upstream, string Downstream)> edges)
    {
        if (edges.Count == 0)
        {
            _logger.LogInformation(
                "Domain_Dependency 无本次 Run 内有效依赖边，按 ExpectedDomainKeysJson 冻结顺序执行（{Count} 域）",
                domainKeys.Count);
            return domainKeys.ToList();
        }

        var nodes = domainKeys.ToList();
        var edgePairs = edges.Select(e => (From: e.Upstream, To: e.Downstream)).ToList();

        List<List<string>> layers;
        try
        {
            layers = TopologicalSort.SortByLayers<string>(nodes, edgePairs);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Domain_Dependency 存在循环依赖，拓扑排序失败，降级为 ExpectedDomainKeysJson 冻结顺序");
            return domainKeys.ToList();
        }

        // 冻结顺序索引（DomainKey → 数组下标），分层内稳定排序用
        var frozenIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < domainKeys.Count; i++)
            frozenIndex[domainKeys[i]] = i;

        var ordered = new List<string>(domainKeys.Count);
        foreach (var layer in layers)
        {
            ordered.AddRange(layer.OrderBy(d => frozenIndex[d]));
        }

        // 防御：Kahn 未覆盖的孤立节点（nodes 已含全部 domainKeys，理论上不会发生）补到末尾
        foreach (var dk in domainKeys)
        {
            if (!ordered.Contains(dk))
                ordered.Add(dk);
        }

        return ordered;
    }

    /// <summary>
    /// 构建每个 Domain 的传递上游闭包（直接 + 间接上游），用于失败阻断按依赖边传播。
    /// 无依赖边时返回空字典（不阻断，全部按冻结顺序执行）。
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildUpstreamClosure(
        List<(string Upstream, string Downstream)> edges,
        IReadOnlyList<string> domainKeys)
    {
        var closure = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (edges.Count == 0)
            return closure;

        foreach (var dk in domainKeys)
            closure[dk] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var dk in domainKeys)
            adjacency[dk] = new List<string>();
        foreach (var (up, dn) in edges)
            adjacency[up].Add(dn);

        // 对每个源节点 BFS，把源标记为其所有可达下游的传递上游
        foreach (var dk in domainKeys)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(dk);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var next in adjacency[cur])
                {
                    if (visited.Add(next))
                    {
                        closure[next].Add(dk);
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return closure;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 内部 DTO（仅本类 Dapper 投影使用，不对外暴露）
    // ═══════════════════════════════════════════════════════════════════════════

    private class ResourceLoadDto
    {
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; } = string.Empty;
        public string ResourceName { get; set; } = string.Empty;
        public int FactoryId { get; set; }
        public int ProductionDepartmentId { get; set; }
        public decimal CapacityFactor { get; set; }
        public int DispatchPriority { get; set; }
        public bool LocalDisableFlag { get; set; }
    }

    private class ResourceCalendarSlotLoadDto
    {
        public int ResourceId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool AvailableFlag { get; set; }
    }

    private class ManualCapacitySlotLoadDto
    {
        public int ManualSlotId { get; set; }
        public int ProductionDepartmentId { get; set; }
        public string OperationName { get; set; } = string.Empty;
        public string SlotCode { get; set; } = string.Empty;
    }

    private class ManualCapacitySlotCalendarLoadDto
    {
        public int ManualSlotId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool AvailableFlag { get; set; }
    }

    private class InventoryLoadDto
    {
        public string MaterialCode { get; set; } = string.Empty;
        public int ProductFamilyId { get; set; }
        public int FactoryId { get; set; }
        public decimal AvailableQty { get; set; }
    }

    private class InTransitLoadDto
    {
        public string MaterialCode { get; set; } = string.Empty;
        public int ProductFamilyId { get; set; }
        public int FactoryId { get; set; }
        public decimal AvailableQty { get; set; }
        public DateTime? EstimatedArrivalTime { get; set; }
    }

    private class OperationEligibilityDto
    {
        public int MaterialId { get; set; }
        public string OperationCode { get; set; } = string.Empty;
        public int ResourceId { get; set; }
        public int Priority { get; set; }
    }

    private class TaskInsertDto
    {
        public int PlanVersionId { get; set; }
        public string TaskNo { get; set; } = string.Empty;
        public long OrderId { get; set; }
        public int MaterialId { get; set; }
        public int OperationSeq { get; set; }
        public string OperationCode { get; set; } = string.Empty;
        public int? ResourceId { get; set; }
        public string RouteCode { get; set; } = "DEFAULT";
        public int PathId { get; set; } = 1;
        public decimal Quantity { get; set; }
        public string UOM { get; set; } = string.Empty;
        public decimal? Duration { get; set; }
        public string Status { get; set; } = "Pending";
        public string TaskType { get; set; } = "PRODUCTION";
    }

    private class ScheduleRunQueryDto
    {
        public int Id { get; set; }
        public DateTime DataCutoffTime { get; set; }
        public long? StrategyProfileVersionId { get; set; }
        public int? BasePlanVersionId { get; set; }
        public string? ScopeJson { get; set; }
    }

    private class DomainDependencyRow
    {
        public string UpstreamDomainCode { get; set; } = string.Empty;
        public string DownstreamDomainCode { get; set; } = string.Empty;
    }

    /// <summary>壳盘点投影：每个 DomainKey 有几个 PlanVersion 壳（见 ExecuteRunAsync 的壳盘点注释）。</summary>
    private class DomainShellCount
    {
        public string? DomainKey { get; set; }
        public int ShellCount { get; set; }
    }

    private class ExternalDomainBlockRow
    {
        public int ResourceId { get; set; }
        public DateTime PlannedStartTime { get; set; }
        public DateTime PlannedEndTime { get; set; }
        public string DomainKey { get; set; } = string.Empty;
        public int SourcePlanVersionId { get; set; }
    }

    private class MESProgressLoadDto
    {
        public string ProductionInstructionNo { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string StageCode { get; set; } = string.Empty;
        public decimal RemainingQty { get; set; }
    }

    private class StrategyConfigLoadDto
    {
        public long RuleSetVersionId { get; set; }
        public string RuleSetCode { get; set; } = string.Empty;
        public string RuleSetName { get; set; } = string.Empty;
        public string RuleSetVersionCode { get; set; } = string.Empty;
        public long ParameterSetVersionId { get; set; }
        public string ParameterSetCode { get; set; } = string.Empty;
        public string ParameterSetName { get; set; } = string.Empty;
        public string ParameterSetVersionCode { get; set; } = string.Empty;
    }
}
