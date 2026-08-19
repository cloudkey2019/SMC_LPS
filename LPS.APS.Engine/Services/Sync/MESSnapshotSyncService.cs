using System.Data;
using Dapper;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// MES 生产进度快照同步服务（2号位职责）
/// ScheduleRun 由 IScheduleRunService 统一管理，本服务只读取当日记录
/// </summary>
public class MESSnapshotSyncService : IMESSnapshotSyncService
{
    private readonly IScheduleRunService _scheduleRunService;
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<MESSnapshotSyncService> _logger;

    private const int CommandTimeoutSeconds = 300;

    public MESSnapshotSyncService(
        IScheduleRunService scheduleRunService,
        DatabaseConnectionManager connectionManager,
        ILogger<MESSnapshotSyncService> logger)
    {
        _scheduleRunService = scheduleRunService ?? throw new ArgumentNullException(nameof(scheduleRunService));
        _connectionManager  = connectionManager  ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger             = logger             ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MESSnapshotSyncResultDto> SyncWorkOrderAsync(CancellationToken cancellationToken = default)
        => await SyncSnapshotAsync("sp_SyncMESWorkOrderSnapshot", "SyncWorkOrder");

    /// <inheritdoc />
    public async Task<MESSnapshotSyncResultDto> SyncOperationProgressAsync(CancellationToken cancellationToken = default)
        => await SyncSnapshotAsync("sp_SyncMESOperationProgressSnapshot", "SyncOperationProgress");

    /// <inheritdoc />
    public async Task<MESSnapshotSyncResultDto> SyncStageProgressAsync(CancellationToken cancellationToken = default)
        => await SyncSnapshotAsync("sp_SyncMESStageProgressSnapshot", "SyncStageProgress");

    private async Task<MESSnapshotSyncResultDto> SyncSnapshotAsync(string spName, string logStep)
    {
        _logger.LogInformation("{Step} 开始", logStep);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var run = await _scheduleRunService.GetCurrentRunAsync();
            if (run is null)
            {
                const string msg = "未找到当日 RUNNING ScheduleRun，请确认 NightlyBatch（00:30）已正常完成";
                _logger.LogError("{Step} 失败: {Msg}", logStep, msg);
                return new MESSnapshotSyncResultDto { ErrorMessage = msg };
            }

            var spParams = new DynamicParameters();
            spParams.Add("@ScheduleRunId",  run.Id);
            spParams.Add("@DataCutoffTime", run.DataCutoffTime);
            spParams.Add("@RowsAffected",   dbType: DbType.Int32, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                spName,
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS,
                commandTimeout: CommandTimeoutSeconds);

            stopwatch.Stop();
            var rows = spParams.Get<int>("@RowsAffected");

            _logger.LogInformation(
                "{Step} 完成: ScheduleRunId={Id}, rows={Rows}, elapsed={Elapsed}ms",
                logStep, run.Id, rows, stopwatch.ElapsedMilliseconds);

            return new MESSnapshotSyncResultDto
            {
                ScheduleRunId  = run.Id,
                DataCutoffTime = run.DataCutoffTime,
                RowsAffected   = rows
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "{Step} 异常: elapsed={Elapsed}ms", logStep, stopwatch.ElapsedMilliseconds);
            return new MESSnapshotSyncResultDto { ErrorMessage = ex.Message };
        }
    }
}

