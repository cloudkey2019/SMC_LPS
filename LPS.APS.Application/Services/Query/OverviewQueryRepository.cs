using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.Application.Services.Query;

/// <summary>
/// Overview查询Repository实现
/// 直接读取APS_Production事实表
/// </summary>
public class OverviewQueryRepository : IOverviewQueryRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public OverviewQueryRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<OverviewActivePlanDto?> GetActivePlanAsync(
        string? domainKey = null,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedDomains = null)
    {
        // Dapper 列表参数只能出现一次（IN 内），不得兼作标量 "@X IS NULL" 检查——
        // 否则 Dapper 会把所有 @AllowedDomains 扩成 (@p1,@p2)，产生非法 "@(@p1,@p2) IS NULL" → SqlException 4145（4号位 pmc 视角 active-plan 500 根因）。
        // 分治：null=global(无域过滤)；空集=有 scope 无 Domain 授权(fail-closed 返空)；非空=IN 过滤。
        if (allowedDomains is { Count: 0 })
            return null;

        var baseSql = @"SELECT TOP 1
    Id AS PlanVersionId,
    VersionCode,
    DomainKey,
    PlanHorizonStart,
    PlanHorizonEnd,
    ActivatedAt,
    SourceScheduleRunId,
    TotalTasks,
    TotalOrders,
    CreatedAt
FROM PlanVersion
WHERE Status = 'ACTIVE'
    AND (@DomainKey IS NULL OR DomainKey = @DomainKey)";

        string sql;
        object parameters;
        if (allowedDomains is null)
        {
            sql = baseSql + @"
ORDER BY ActivatedAt DESC";
            parameters = new { DomainKey = domainKey };
        }
        else
        {
            sql = baseSql + @"
    AND DomainKey IN @AllowedDomains
ORDER BY ActivatedAt DESC";
            parameters = new { DomainKey = domainKey, AllowedDomains = allowedDomains };
        }

        var results = await _connectionManager.QueryAsync<OverviewActivePlanDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 10);

        return results.FirstOrDefault();
    }

    public async Task<OverviewTaskSummaryDto> GetTaskSummaryAsync(
        int planVersionId,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    @PlanVersionId AS PlanVersionId,
    COUNT(*) AS TotalTasks,
    SUM(CASE WHEN Status = 'COMPLETED' AND PlannedEndTime <= GETDATE() THEN 1 ELSE 0 END) AS OnTimeCount,
    SUM(CASE WHEN Status = 'COMPLETED' AND PlannedEndTime > GETDATE() THEN 1 ELSE 0 END) AS DelayedCount,
    SUM(CASE WHEN Status = 'IN_PROGRESS' AND IsCriticalPath = 1 THEN 1 ELSE 0 END) AS RiskCount,
    SUM(CASE WHEN Status = 'PENDING' OR Status = 'QUEUED' THEN 1 ELSE 0 END) AS UnscheduledCount,
    SUM(CASE WHEN Status = 'ESTIMATED' THEN 1 ELSE 0 END) AS EstimatedOnlyCount,
    SUM(CASE WHEN Status NOT IN ('COMPLETED', 'IN_PROGRESS', 'PENDING', 'QUEUED', 'ESTIMATED') THEN 1 ELSE 0 END) AS OtherCount
FROM Task
WHERE PlanVersionId = @PlanVersionId";

        var parameters = new { PlanVersionId = planVersionId };

        var results = await _connectionManager.QueryAsync<OverviewTaskSummaryDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.FirstOrDefault() ?? new OverviewTaskSummaryDto { PlanVersionId = planVersionId };
    }

    public async Task<List<OverviewResourceBottleneckDto>> GetResourceBottleneckAsync(
        int planVersionId,
        int topN = 10,
        CancellationToken ct = default)
    {
        // 注意：Bottleneck判定应由2号位正式Query提供，5号位不做二次计算
        // 此处仅返回资源任务数统计，不判定Bottleneck
        var sql = @"
SELECT TOP (@TopN)
    t.ResourceId,
    r.ResourceCode,
    r.ResourceName,
    r.ResourceType,
    COUNT(*) AS TaskCount,
    SUM(ISNULL(t.Duration, 0)) / 3600.0 AS TotalPlannedHours,
    NULL AS UtilizationRate,
    0 AS IsBottleneck
FROM Task t
INNER JOIN Resource r ON r.Id = t.ResourceId
WHERE t.PlanVersionId = @PlanVersionId
    AND t.ResourceId IS NOT NULL
GROUP BY t.ResourceId, r.ResourceCode, r.ResourceName, r.ResourceType
ORDER BY TotalPlannedHours DESC";

        var parameters = new { PlanVersionId = planVersionId, TopN = topN };

        var results = await _connectionManager.QueryAsync<OverviewResourceBottleneckDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<OverviewCandidateSummaryDto> GetCandidateSummaryAsync(
        CancellationToken ct = default)
    {
        // Candidate 列表 + 反查 ScheduleRun（Base 依 P0-04 权威落盘：SourceScheduleRunId → Run.BasePlanVersionId）
        // CanActivate = §12.3 前置：Candidate.BasePlanVersionId 仍为当前 Domain ACTIVE。
        // 边界：列表项仅承载候选元信息；Base vs Candidate 差集/Diff 由 3号位 ScheduleQueryService 提供，5号位不在此重复计算。
        var candidates = (await _connectionManager.QueryAsync<CandidateBriefDto>(@"
SELECT
    pv.Id                                      AS PlanVersionId,
    pv.VersionCode                             AS VersionCode,
    pv.DomainKey                               AS DomainKey,
    pv.Status                                  AS Status,
    pv.CreatedAt                               AS CreatedAt,
    pv.CreatedByUserName                       AS CreatedByUserName,
    pv.SourceScheduleRunId                     AS SourceScheduleRunId,
    sr.RunType                                 AS RunType,
    sr.BasePlanVersionId                       AS BasePlanVersionId,
    CAST(CASE WHEN sr.BasePlanVersionId IS NOT NULL
               AND EXISTS (SELECT 1 FROM PlanVersion b
                            WHERE b.Id = sr.BasePlanVersionId
                              AND b.Status = 'ACTIVE'
                              AND b.DomainKey = pv.DomainKey)
         THEN 1 ELSE 0 END AS BIT)             AS CanActivate
FROM PlanVersion pv
LEFT JOIN ScheduleRun sr ON sr.Id = pv.SourceScheduleRunId
WHERE pv.VersionCategory = 'CANDIDATE'
ORDER BY pv.CreatedAt DESC", null, CommandType.Text, DatabaseId.APS, commandTimeout: 10)).ToList();

        return new OverviewCandidateSummaryDto
        {
            PendingCount = candidates.Count,
            Candidates = candidates
        };
    }
}
