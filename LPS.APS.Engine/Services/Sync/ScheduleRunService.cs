using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// ScheduleRun 生命周期管理实现（Engine 层）
/// 原逻辑来自 NightlyBatchOrchestrator.CreateScheduleRunAsync（私有）
/// 和 MESSnapshotSyncService.CreateScheduleRunAsync（公开）的合并统一
/// </summary>
public class ScheduleRunService : IScheduleRunService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<ScheduleRunService> _logger;

    public ScheduleRunService(
        DatabaseConnectionManager connectionManager,
        ILogger<ScheduleRunService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<int> CreateScheduleRunAsync(CancellationToken cancellationToken = default)
    {
        // 幂等：当日已有 RUNNING 记录则直接复用
        var existing = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"SELECT TOP 1 Id FROM ScheduleRun
              WHERE Status = 'RUNNING'
                AND CAST(CreatedAt AS DATE) = CAST(GETDATE() AS DATE)
              ORDER BY CreatedAt DESC",
            db: DatabaseId.APS);

        if (existing > 0)
        {
            _logger.LogInformation("当日 ScheduleRun 已存在，复用: ScheduleRunId={Id}", existing);
            return existing;
        }

        var strategyVersionId = await _connectionManager.QueryFirstOrDefaultAsync<long?>(
            @"SELECT TOP 1 v.Id FROM StrategyProfileVersion v
              JOIN StrategyProfile p ON p.Id = v.StrategyProfileId
              WHERE v.Status = 'PUBLISHED' AND v.IsDefault = 1 AND p.RunType = 'FULL_SCHEDULE'
              ORDER BY v.PublishedAt DESC",
            db: DatabaseId.APS);

        var id = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO ScheduleRun
                (RunType, Status, TriggeredBy, DataCutoffTime, StartedAt, CreatedAt, StrategyProfileVersionId)
              OUTPUT INSERTED.Id
              VALUES ('FULL_SCHEDULE', 'RUNNING', 'Hangfire', GETDATE(), GETDATE(), GETDATE(), @StrategyProfileVersionId)",
            new { StrategyProfileVersionId = strategyVersionId },
            db: DatabaseId.APS);

        if (id <= 0)
            throw new InvalidOperationException("创建 ScheduleRun 失败");

        _logger.LogInformation("ScheduleRun 创建成功: ScheduleRunId={Id}, StrategyProfileVersionId={VersionId}",
            id, strategyVersionId);
        return id;
    }

    /// <inheritdoc />
    public async Task<ScheduleRunDto?> GetCurrentRunAsync(CancellationToken cancellationToken = default)
    {
        var row = await _connectionManager.QueryFirstOrDefaultAsync<ScheduleRunRow>(
            @"SELECT TOP 1 Id, DataCutoffTime, StrategyProfileVersionId FROM ScheduleRun
              WHERE Status = 'RUNNING'
                AND CAST(CreatedAt AS DATE) = CAST(GETDATE() AS DATE)
              ORDER BY CreatedAt DESC",
            db: DatabaseId.APS);

        return row is null ? null : new ScheduleRunDto(row.Id, row.DataCutoffTime, row.StrategyProfileVersionId);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(int scheduleRunId, int durationSeconds, CancellationToken cancellationToken = default)
    {
        await _connectionManager.ExecuteAsync(
            @"UPDATE ScheduleRun
              SET Status          = 'COMPLETED',
                  CompletedAt     = GETDATE(),
                  DurationSeconds = @DurationSeconds
              WHERE Id = @Id",
            new { Id = scheduleRunId, DurationSeconds = durationSeconds },
            db: DatabaseId.APS);

        _logger.LogInformation("ScheduleRun 完成: ScheduleRunId={Id}", scheduleRunId);
    }

    /// <inheritdoc />
    public async Task FailAsync(int scheduleRunId, int durationSeconds, string errorMessage, CancellationToken cancellationToken = default)
    {
        await _connectionManager.ExecuteAsync(
            @"UPDATE ScheduleRun
              SET Status          = 'FAILED',
                  CompletedAt     = GETDATE(),
                  DurationSeconds = @DurationSeconds,
                  ErrorMessage    = @ErrorMessage
              WHERE Id = @Id",
            new { Id = scheduleRunId, DurationSeconds = durationSeconds, ErrorMessage = errorMessage },
            db: DatabaseId.APS);

        _logger.LogInformation("ScheduleRun 失败: ScheduleRunId={Id}", scheduleRunId);
    }


    private sealed class ScheduleRunRow
    {
        public int Id { get; set; }
        public DateTime DataCutoffTime { get; set; }
        public long? StrategyProfileVersionId { get; set; }
    }
}
