using Dapper;
using LPS.APS.Application.Services.Query.Dto;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services.Query;

/// <summary>
/// 排程结果查询服务实现（3号位）
///
/// 架构红线：
///   - 只读 APS_Production 库（已落盘数据），不触碰推演期内存沙盘
///   - 不包含任何排程/重排逻辑，纯查询投影
///   - Dapper 直连，避免 EF 跟踪开销（大结果集场景）
/// </summary>
public class ScheduleQueryService : IScheduleQueryService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<ScheduleQueryService> _logger;

    public ScheduleQueryService(
        DatabaseConnectionManager connectionManager,
        ILogger<ScheduleQueryService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlanVersionSummaryDto>> GetVersionsAsync(
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<PlanVersionSummaryDto>(
            @"SELECT TOP (@Take)
                Id, VersionCode, VersionCategory,
                PlanHorizonStart, PlanHorizonEnd,
                Status, ComputedAt,
                TotalTasks, CreatedAt
              FROM PlanVersion
              WHERE ArchivedAt IS NULL
              ORDER BY CreatedAt DESC",
            new { Take = take },
            db: DatabaseId.APS);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<GanttDataDto> GetGanttAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        // 1. 版本信息
        var version = await _connectionManager.QueryFirstOrDefaultAsync<(int Id, string VersionCode, DateTime PlanHorizonStart, DateTime PlanHorizonEnd)>(
            @"SELECT Id, VersionCode, PlanHorizonStart, PlanHorizonEnd
              FROM PlanVersion WHERE Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        if (version.Id == 0)
        {
            return new GanttDataDto { PlanVersionId = planVersionId };
        }

        // 2. 资源行（仅返回该版本里被 Task 使用到的资源，减少前端噪声）
        var resources = await _connectionManager.QueryAsync<GanttResourceDto>(
            @"SELECT DISTINCT
                r.Id AS ResourceId,
                r.ResourceCode,
                r.ResourceName,
                r.FactoryId,
                r.ProductionDepartmentId
              FROM Resource r
              INNER JOIN [Task] t ON t.ResourceId = r.Id
              WHERE t.PlanVersionId = @Id
              ORDER BY r.FactoryId, r.ProductionDepartmentId, r.ResourceCode",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        // 3. 任务条
        var tasks = await _connectionManager.QueryAsync<GanttTaskDto>(
            @"SELECT
                t.Id                AS TaskId,
                t.TaskNo,
                t.OrderId,
                o.OrderNo,
                t.MaterialId,
                m.MaterialCode,
                m.MaterialName,
                t.ResourceId,
                t.OperationCode,
                t.OperationSeq,
                t.Quantity,
                t.UOM,
                t.PlannedStartTime,
                t.PlannedEndTime,
                t.Status,
                CAST(CASE
                    WHEN t.PlannedEndTime IS NOT NULL
                     AND o.CustomerDueDate IS NOT NULL
                     AND t.PlannedEndTime > o.CustomerDueDate
                    THEN 1 ELSE 0
                END AS BIT) AS IsDelayed
              FROM [Task] t
              LEFT JOIN [Order] o ON o.Id = t.OrderId
              LEFT JOIN Material m ON m.Id = t.MaterialId
              WHERE t.PlanVersionId = @Id
              ORDER BY t.ResourceId, t.PlannedStartTime",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        return new GanttDataDto
        {
            PlanVersionId    = version.Id,
            VersionCode      = version.VersionCode,
            PlanHorizonStart = version.PlanHorizonStart,
            PlanHorizonEnd   = version.PlanHorizonEnd,
            Resources        = resources.ToList(),
            Tasks            = tasks.ToList()
        };
    }

    /// <inheritdoc />
    public async Task<ScheduleSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _connectionManager.QueryFirstOrDefaultAsync<ScheduleSummaryDto>(
            @"SELECT
                pv.Id                AS PlanVersionId,
                pv.VersionCode,
                pv.Status,
                pv.DurationSeconds   AS ComputeDurationSeconds,

                (SELECT COUNT(*) FROM [Task] WHERE PlanVersionId = @Id)
                    AS TotalTasks,
                (SELECT COUNT(*) FROM [Task]
                    WHERE PlanVersionId = @Id AND PlannedStartTime IS NOT NULL)
                    AS ScheduledTasks,
                (SELECT COUNT(*) FROM [Task]
                    WHERE PlanVersionId = @Id AND PlannedStartTime IS NULL)
                    AS UnscheduledTasks,
                (SELECT COUNT(*) FROM [Task] t
                    INNER JOIN [Order] o ON o.Id = t.OrderId
                    WHERE t.PlanVersionId = @Id
                      AND t.PlannedEndTime IS NOT NULL
                      AND o.CustomerDueDate IS NOT NULL
                      AND t.PlannedEndTime > o.CustomerDueDate)
                    AS DelayedTasks,

                (SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @Id)
                    AS TotalOrders,
                (SELECT COUNT(DISTINCT o.Id) FROM [Order] o
                    INNER JOIN [Task] t ON t.OrderId = o.Id
                    WHERE o.PlanVersionId = @Id
                      AND t.PlannedEndTime IS NOT NULL
                      AND o.CustomerDueDate IS NOT NULL
                      AND t.PlannedEndTime > o.CustomerDueDate)
                    AS DelayedOrders,

                (SELECT MIN(PlannedStartTime) FROM [Task] WHERE PlanVersionId = @Id)
                    AS FirstTaskStart,
                (SELECT MAX(PlannedEndTime) FROM [Task] WHERE PlanVersionId = @Id)
                    AS LastTaskEnd
              FROM PlanVersion pv
              WHERE pv.Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        return summary ?? new ScheduleSummaryDto { PlanVersionId = planVersionId };
    }
}
