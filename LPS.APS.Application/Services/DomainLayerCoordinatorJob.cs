using System.Text.Json;
using Hangfire;
using Hangfire.Storage;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 域层协调 Job（B2 分域并行方案）
///
/// 轮询当前层所有域 Job 完成后，enqueue 下一层或收尾。
/// 轮询间隔 5 秒，最大等待 30 分钟。
/// </summary>
public class DomainLayerCoordinatorJob : IDomainLayerCoordinatorJob
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxWaitTime     = TimeSpan.FromMinutes(30);

    private readonly IMonitoringApi _monitoringApi;
    private readonly IScheduleRunService _scheduleRunService;
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<DomainLayerCoordinatorJob> _logger;

    public DomainLayerCoordinatorJob(
        IMonitoringApi monitoringApi,
        IScheduleRunService scheduleRunService,
        DatabaseConnectionManager connectionManager,
        ILogger<DomainLayerCoordinatorJob> logger)
    {
        _monitoringApi      = monitoringApi      ?? throw new ArgumentNullException(nameof(monitoringApi));
        _scheduleRunService = scheduleRunService ?? throw new ArgumentNullException(nameof(scheduleRunService));
        _connectionManager  = connectionManager  ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger             = logger             ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    [AutomaticRetry(Attempts = 0)]
    public async Task CoordinateAsync(
        int planVersionId,
        int scheduleRunId,
        long strategyProfileVersionId,
        string serializedCurrentLayerJobIds,
        string serializedNextLayerDomains,
        int nextLayerIndex,
        int totalLayers)
    {
        var cancellationToken = CancellationToken.None;
        long? strategyProfileVersionIdNullable = strategyProfileVersionId == 0 ? null : strategyProfileVersionId;
        var currentLayerIndex = nextLayerIndex - 1;
        _logger.LogInformation(
            "[{PlanVersionId}] 层协调 Job 启动: 监控第 {Layer} 层，共 {Total} 层",
            planVersionId, currentLayerIndex, totalLayers);

        var jobIds      = JsonSerializer.Deserialize<string[]>(serializedCurrentLayerJobIds) ?? Array.Empty<string>();
        var nextDomains = string.IsNullOrEmpty(serializedNextLayerDomains)
            ? Array.Empty<DomainJobParam>()
            : JsonSerializer.Deserialize<DomainJobParam[]>(serializedNextLayerDomains) ?? Array.Empty<DomainJobParam>();

        var deadline = DateTime.UtcNow.Add(MaxWaitTime);
        while (DateTime.UtcNow < deadline)
        {
            var (allSucceeded, anyFailed, failedJobId) = CheckLayerStatus(jobIds);

            if (anyFailed)
            {
                _logger.LogError(
                    "[{PlanVersionId}] 第 {Layer} 层 Job {JobId} 失败，终止分域调度链",
                    planVersionId, currentLayerIndex, failedJobId);
                await FailPlanVersionAsync(planVersionId, scheduleRunId,
                    $"第 {currentLayerIndex} 层 Job {failedJobId} 执行失败", cancellationToken);
                return;
            }

            if (allSucceeded)
            {
                _logger.LogInformation(
                    "[{PlanVersionId}] 第 {Layer} 层全部完成，共 {Count} 个域",
                    planVersionId, currentLayerIndex, jobIds.Length);
                await AdvanceToNextLayerAsync(
                    planVersionId, scheduleRunId, strategyProfileVersionId,
                    nextDomains, nextLayerIndex, totalLayers, cancellationToken);
                return;
            }

            await Task.Delay(PollingInterval, cancellationToken);
        }

        _logger.LogError("[{PlanVersionId}] 第 {Layer} 层等待超时（30分钟）", planVersionId, currentLayerIndex);
        await FailPlanVersionAsync(planVersionId, scheduleRunId,
            $"第 {currentLayerIndex} 层域 Job 等待超时（30分钟）", cancellationToken);
    }

    // ── 状态检查 ─────────────────────────────────────────────────────────────

    private (bool allSucceeded, bool anyFailed, string? failedJobId) CheckLayerStatus(string[] jobIds)
    {
        foreach (var id in jobIds)
        {
            var details   = _monitoringApi.JobDetails(id);
            var lastState = details?.History.FirstOrDefault()?.StateName;
            if (lastState is "Failed" or "Deleted")
                return (false, true, id);
        }

        var allDone = jobIds.All(id =>
        {
            var details   = _monitoringApi.JobDetails(id);
            var lastState = details?.History.FirstOrDefault()?.StateName;
            return lastState == "Succeeded";
        });

        return (allDone, false, null);
    }

    // ── 推进下一层 ────────────────────────────────────────────────────────────

    private async Task AdvanceToNextLayerAsync(
        int planVersionId,
        int scheduleRunId,
        long? strategyProfileVersionId,
        DomainJobParam[] nextDomains,
        int nextLayerIndex,
        int totalLayers,
        CancellationToken cancellationToken)
    {
        if (nextLayerIndex >= totalLayers || nextDomains.Length == 0)
        {
            _logger.LogInformation("[{PlanVersionId}] 所有层完成，收尾排程", planVersionId);
            await CompletePlanVersionAsync(planVersionId, scheduleRunId, cancellationToken);
            return;
        }

        var nextJobIds = new List<string>();
        foreach (var domain in nextDomains)
        {
            var jobId = BackgroundJob.Enqueue<IDomainSchedulingJob>(j => j.ExecuteAsync(
                planVersionId, scheduleRunId, domain.DomainKey, domain.ProductFamilyId,
                nextLayerIndex, strategyProfileVersionId ?? 0L));

            nextJobIds.Add(jobId);

            await _connectionManager.ExecuteAsync(
                @"UPDATE DomainScheduleStatus SET HangfireJobId = @JobId
                  WHERE PlanVersionId = @PlanVersionId AND ProductFamilyId = @ProductFamilyId",
                new { JobId = jobId, PlanVersionId = planVersionId, ProductFamilyId = domain.ProductFamilyId },
                db: DatabaseId.APS);
        }

        _logger.LogInformation(
            "[{PlanVersionId}] 已 enqueue 第 {Layer} 层 {Count} 个域 Job",
            planVersionId, nextLayerIndex, nextJobIds.Count);

        var serializedNextJobIds = JsonSerializer.Serialize(nextJobIds.ToArray());
        var spvId = strategyProfileVersionId ?? 0L;
        var nextIdx = nextLayerIndex + 1;

        BackgroundJob.Enqueue<IDomainLayerCoordinatorJob>(j => j.CoordinateAsync(
            planVersionId, scheduleRunId, spvId,
            serializedNextJobIds,
            string.Empty,
            nextIdx,
            totalLayers));
    }

    // ── 收尾 / 失败 ───────────────────────────────────────────────────────────

    private async Task CompletePlanVersionAsync(
        int planVersionId, int scheduleRunId, CancellationToken cancellationToken)
    {
        await _connectionManager.ExecuteAsync(
            "UPDATE PlanVersion SET Status = 'Computed', ComputedAt = GETDATE() WHERE Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        if (scheduleRunId > 0)
            await _scheduleRunService.CompleteAsync(scheduleRunId, 0, cancellationToken);

        _logger.LogInformation("[{PlanVersionId}] PlanVersion 标记 Computed", planVersionId);
    }

    private async Task FailPlanVersionAsync(
        int planVersionId, int scheduleRunId, string reason, CancellationToken cancellationToken)
    {
        await _connectionManager.ExecuteAsync(
            "UPDATE PlanVersion SET Status = 'ComputeFailed' WHERE Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        if (scheduleRunId > 0)
            await _scheduleRunService.FailAsync(scheduleRunId, 0, reason, cancellationToken);

        _logger.LogError("[{PlanVersionId}] PlanVersion 标记 ComputeFailed: {Reason}", planVersionId, reason);
    }
}
