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
    private readonly IDomainDefinitionService _domainDefinitionService;
    private readonly ILogger<ScheduleRunService> _logger;

    public ScheduleRunService(
        DatabaseConnectionManager connectionManager,
        IDomainDefinitionService domainDefinitionService,
        ILogger<ScheduleRunService> logger)
    {
        _connectionManager      = connectionManager      ?? throw new ArgumentNullException(nameof(connectionManager));
        _domainDefinitionService = domainDefinitionService ?? throw new ArgumentNullException(nameof(domainDefinitionService));
        _logger                 = logger                 ?? throw new ArgumentNullException(nameof(logger));
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

        // 冻结预期 Domain 集合（ExpectedDomainKeysJson）：运行启动唯一权威来源。
        // FULL_SCHEDULE 须 ≥1 Domain；DomainDefinition 为空 = 治理未配置，属配置错误，响亮失败不静默降级。
        var domains = await _domainDefinitionService.GetActiveDomainsAsync(cancellationToken);
        if (domains.Count == 0)
            throw new InvalidOperationException("DomainDefinition 无有效（IsActive=1）Domain，无法创建 FULL_SCHEDULE ScheduleRun（预期 Domain 数须 ≥1）");

        var expectedDomainKeysJson = System.Text.Json.JsonSerializer.Serialize(
            domains.Select(d => d.DomainKey).ToList());

        // 默认策略包版本（FULL_SCHEDULE 的 IsDefault=1 且 PUBLISHED）：
        // 0 个 → 治理未配置，响亮失败不静默降级（否则 ScheduleRun 带 NULL 版本整晚空跑）；
        // 1 个 → 使用；多个 → 歧义报错（P0-06），不随机 TOP 1。
        var defaultVersionIds = (await _connectionManager.QueryAsync<long>(
            @"SELECT v.Id FROM StrategyProfileVersion v
              JOIN StrategyProfile p ON p.Id = v.StrategyProfileId
              WHERE v.Status = 'PUBLISHED' AND v.IsDefault = 1
                AND p.RunType = 'FULL_SCHEDULE' AND p.IsActive = 1
              ORDER BY v.PublishedAt DESC",
            db: DatabaseId.APS)).ToList();

        if (defaultVersionIds.Count == 0)
            throw new InvalidOperationException(
                "无默认 FULL_SCHEDULE 策略包版本（IsDefault=1 且 PUBLISHED），无法创建 ScheduleRun —— 请先在治理侧发布并标记默认版本");

        if (defaultVersionIds.Count > 1)
            throw new InvalidOperationException(
                $"FULL_SCHEDULE 存在多个默认策略包版本（StrategyProfileVersionId = {string.Join(", ", defaultVersionIds)}），默认版本必须唯一，无法创建 ScheduleRun");

        var strategyVersionId = defaultVersionIds[0];

        var id = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO ScheduleRun
                (RunType, Status, TriggeredBy, DataCutoffTime, StartedAt, CreatedAt, StrategyProfileVersionId, ExpectedDomainKeysJson)
              OUTPUT INSERTED.Id
              VALUES ('FULL_SCHEDULE', 'RUNNING', 'Hangfire', GETDATE(), GETDATE(), GETDATE(), @StrategyProfileVersionId, @ExpectedDomainKeysJson)",
            new { StrategyProfileVersionId = strategyVersionId, ExpectedDomainKeysJson = expectedDomainKeysJson },
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
    public async Task<IReadOnlyList<string>> GetExpectedDomainKeysAsync(int scheduleRunId, CancellationToken cancellationToken = default)
    {
        var json = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            @"SELECT ExpectedDomainKeysJson FROM ScheduleRun WHERE Id = @Id",
            new { Id = scheduleRunId },
            db: DatabaseId.APS);

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"ScheduleRun {scheduleRunId} 的 ExpectedDomainKeysJson 为空/缺失");

        try
        {
            var domains = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return domains.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException(
                $"ScheduleRun {scheduleRunId} 的 ExpectedDomainKeysJson 不是合法 JSON 数组：{ex.Message}", ex);
        }
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
    public async Task PartialSuccessAsync(int scheduleRunId, int durationSeconds, string errorMessage, CancellationToken cancellationToken = default)
    {
        await _connectionManager.ExecuteAsync(
            @"UPDATE ScheduleRun
              SET Status          = 'PARTIAL_SUCCESS',
                  CompletedAt     = GETDATE(),
                  DurationSeconds = @DurationSeconds,
                  ErrorMessage    = @ErrorMessage
              WHERE Id = @Id",
            new { Id = scheduleRunId, DurationSeconds = durationSeconds, ErrorMessage = errorMessage },
            db: DatabaseId.APS);

        _logger.LogInformation("ScheduleRun 部分成功: ScheduleRunId={Id}", scheduleRunId);
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
