using System.Data;
using Dapper;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 夜间批次编排器（2号位职责）
///
/// 时序：
///   00:30  Step 1 — 全量订单同步（ERP → Staging → Order_Canonical）
///   00:30  Step 2 — 创建 ScheduleRun（DataCutoffTime 在此锁定，供 00:40~50 MES快照使用）
///   00:31  Step 3 — 创建 PlanVersion（SourceScheduleRunId = ScheduleRun.Id）
///   00:32  Step 4 — 订单装载到分区表（Order_Canonical → Order）
///   00:33  Step 5 — BOM展开结果接货（ODS.MES_APS_BOM_Workset → APS.APS_BOM_RAW）
///
/// 异常策略：任一步骤失败则中断，记录 APS_ETL_Log，不继续后续步骤
/// </summary>
public class NightlyBatchOrchestrator : INightlyBatchOrchestrator
{
    private readonly IERPOrderSyncService _orderSyncService;
    private readonly IOrderLoadingService _orderLoadingService;
    private readonly IBOMResultPullService _bomResultPullService;
    private readonly IScheduleRunService _scheduleRunService;
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<NightlyBatchOrchestrator> _logger;

    public NightlyBatchOrchestrator(
        IERPOrderSyncService orderSyncService,
        IOrderLoadingService orderLoadingService,
        IBOMResultPullService bomResultPullService,
        IScheduleRunService scheduleRunService,
        DatabaseConnectionManager connectionManager,
        ILogger<NightlyBatchOrchestrator> logger)
    {
        _orderSyncService    = orderSyncService    ?? throw new ArgumentNullException(nameof(orderSyncService));
        _orderLoadingService = orderLoadingService ?? throw new ArgumentNullException(nameof(orderLoadingService));
        _bomResultPullService = bomResultPullService ?? throw new ArgumentNullException(nameof(bomResultPullService));
        _scheduleRunService  = scheduleRunService  ?? throw new ArgumentNullException(nameof(scheduleRunService));
        _connectionManager   = connectionManager   ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger              = logger              ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"NIGHTLY_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("夜间批次开始: {BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // ═══════════════════════════════════════════
            // Step 1: 全量订单同步（ERP → Staging → Order_Canonical）
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{BatchNo}] Step 1: 全量订单同步", batchNo);
            await _orderSyncService.FullSyncAsync(cancellationToken);

            // ═══════════════════════════════════════════
            // Step 2: 创建 ScheduleRun（锁定 DataCutoffTime，供 00:40~50 MES快照使用）
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{BatchNo}] Step 2: 创建 ScheduleRun", batchNo);
            var scheduleRunId = await _scheduleRunService.CreateScheduleRunAsync(cancellationToken);
            _logger.LogInformation("[{BatchNo}] ScheduleRun 已创建: ScheduleRunId={ScheduleRunId}", batchNo, scheduleRunId);

            // ═══════════════════════════════════════════
            // Step 3: 创建计划版本（写入 SourceScheduleRunId）
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{BatchNo}] Step 3: 创建计划版本", batchNo);
            var planVersionId = await CreatePlanVersionAsync(batchNo, scheduleRunId, cancellationToken);
            _logger.LogInformation("[{BatchNo}] 计划版本已创建: PlanVersionId={PlanVersionId}", batchNo, planVersionId);

            // ═══════════════════════════════════════════
            // Step 4: 订单装载到分区表
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{BatchNo}] Step 4: 订单装载到分区表", batchNo);
            var loadedCount = await _orderLoadingService.LoadOrdersToPartitionTableAsync(planVersionId, cancellationToken);
            _logger.LogInformation("[{BatchNo}] 订单装载完成: {LoadedCount}条", batchNo, loadedCount);

            // ═══════════════════════════════════════════
            // Step 5: BOM展开结果接货（ODS → APS）
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{BatchNo}] Step 5: BOM展开结果接货", batchNo);
            var bomBatchNo = await _bomResultPullService.FindReadyBatchAsync(cancellationToken);
            var bomPulledCount = 0;

            if (bomBatchNo != null)
            {
                bomPulledCount = await _bomResultPullService.PullBOMResultFromODSAsync(bomBatchNo, planVersionId, cancellationToken);
                _logger.LogInformation("[{BatchNo}] BOM接货完成: BOMBatchNo={BOMBatchNo}, 行数={PulledCount}",
                    batchNo, bomBatchNo, bomPulledCount);
            }
            else
            {
                _logger.LogWarning("[{BatchNo}] 未找到READY状态的BOM批次，跳过接货（可能BOM展开尚未完成）", batchNo);
            }

            // ═══════════════════════════════════════════
            // 记录成功日志
            // ═══════════════════════════════════════════
            stopwatch.Stop();
            await LogETLAsync(batchNo, "NightlyBatch",
                $"夜间批次完成 | ScheduleRunId:{scheduleRunId} | PlanVersionId:{planVersionId} | 装载订单:{loadedCount} | BOM接货:{bomPulledCount}行 | 耗时:{stopwatch.ElapsedMilliseconds}ms",
                "SUCCESS");

            _logger.LogInformation("夜间批次完成: {BatchNo}, 耗时={Elapsed}ms", batchNo, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "夜间批次失败: {BatchNo}", batchNo);

            await LogETLAsync(batchNo, "NightlyBatch",
                $"夜间批次失败: {ex.Message}",
                "FAILED");

            throw;
        }
    }

    /// <summary>
    /// 创建计划版本，同时写入 SourceScheduleRunId
    /// VersionCode 格式: NIGHTLY_yyyyMMdd，PlanHorizon: 今天起 90 天
    /// </summary>
    private async Task<int> CreatePlanVersionAsync(string batchNo, int scheduleRunId, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var versionCode = $"NIGHTLY_{now:yyyyMMdd}";

        var sql = @"
            -- 如果今天已有同名版本，先标记为已归档
            UPDATE PlanVersion
            SET Status = 'Archived', ArchivedAt = GETDATE()
            WHERE VersionCode = @VersionCode AND Status <> 'Archived';

            INSERT INTO PlanVersion (
                VersionCode, VersionCategory, PlanHorizonStart, PlanHorizonEnd,
                ComputeMode, Status, BatchNo, SourceScheduleRunId, CreatedBy, CreatedAt
            )
            VALUES (
                @VersionCode, 'DAILY_BASELINE', @PlanHorizonStart, @PlanHorizonEnd,
                'FULL', 'Created', @BatchNo, @SourceScheduleRunId, 'SYSTEM', GETDATE()
            );

            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        var parameters = new DynamicParameters();
        parameters.Add("@VersionCode", versionCode);
        parameters.Add("@PlanHorizonStart", now.Date);
        parameters.Add("@PlanHorizonEnd", now.Date.AddDays(90));
        parameters.Add("@BatchNo", batchNo);
        parameters.Add("@SourceScheduleRunId", scheduleRunId);

        var planVersionId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql, parameters, db: DatabaseId.APS);

        if (planVersionId <= 0)
            throw new InvalidOperationException($"创建 PlanVersion 失败: VersionCode={versionCode}");

        return planVersionId;
    }

    /// <summary>
    /// 记录 ETL 日志到 APS_ETL_Log
    /// </summary>
    private async Task LogETLAsync(string batchNo, string step, string message, string status)
    {
        try
        {
            var sql = @"
                INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
                VALUES (@BatchNo, @Step, @Message, @Status, GETDATE())";

            await _connectionManager.ExecuteAsync(sql, new { BatchNo = batchNo, Step = step, Message = message, Status = status },
                db: DatabaseId.APS);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入ETL日志失败（非致命）");
        }
    }
}
