using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// Explanation查询Repository实现
/// 直接读取ScheduleExplanationFact表
/// </summary>
public class ExplanationQueryRepository : IExplanationQueryRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public ExplanationQueryRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<ExplanationDto>> QueryAsync(
        int planVersionId,
        string? objectType = null,
        long? orderId = null,
        long? taskId = null,
        string? reasonCode = null,
        string? severity = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    Id,
    PlanVersionId,
    ScheduleRunId,
    ObjectType,
    OrderId,
    TaskId,
    ResourceId,
    StageCode,
    ReasonCode,
    Severity,
    ImpactHours,
    EvidenceJson,
    CreatedAt
FROM ScheduleExplanationFact
WHERE PlanVersionId = @PlanVersionId
    AND (@ObjectType IS NULL OR ObjectType = @ObjectType)
    AND (@OrderId IS NULL OR OrderId = @OrderId)
    AND (@TaskId IS NULL OR TaskId = @TaskId)
    AND (@ReasonCode IS NULL OR ReasonCode = @ReasonCode)
    AND (@Severity IS NULL OR Severity = @Severity)
ORDER BY
    CASE Severity WHEN 'ERROR' THEN 1 WHEN 'WARN' THEN 2 ELSE 3 END,
    ImpactHours DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            PlanVersionId = planVersionId,
            ObjectType = objectType,
            OrderId = orderId,
            TaskId = taskId,
            ReasonCode = reasonCode,
            Severity = severity,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<ExplanationDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<ExplanationSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken ct = default)
    {
        // 查询汇总统计
        var summarySql = @"
SELECT
    @PlanVersionId AS PlanVersionId,
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN Severity = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount,
    SUM(CASE WHEN Severity = 'WARN' THEN 1 ELSE 0 END) AS WarnCount,
    SUM(CASE WHEN Severity = 'INFO' THEN 1 ELSE 0 END) AS InfoCount
FROM ScheduleExplanationFact
WHERE PlanVersionId = @PlanVersionId";

        var summaryParams = new { PlanVersionId = planVersionId };
        var summary = (await _connectionManager.QueryAsync<ExplanationSummaryDto>(
            summarySql, summaryParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .FirstOrDefault() ?? new ExplanationSummaryDto { PlanVersionId = planVersionId };

        // 查询按ReasonCode分组统计
        var reasonCodeSql = @"
SELECT
    ReasonCode,
    COUNT(*) AS Count,
    SUM(ImpactHours) AS TotalImpactHours
FROM ScheduleExplanationFact
WHERE PlanVersionId = @PlanVersionId
GROUP BY ReasonCode
ORDER BY COUNT(*) DESC";

        var reasonCodeCounts = (await _connectionManager.QueryAsync<ReasonCodeCountDto>(
            reasonCodeSql, summaryParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .ToList();

        // 查询ERROR级别的Explanation列表
        var items = await QueryAsync(planVersionId, severity: "ERROR", take: 50, ct: ct);

        return new ExplanationSummaryDto
        {
            PlanVersionId = summary.PlanVersionId,
            TotalCount = summary.TotalCount,
            ErrorCount = summary.ErrorCount,
            WarnCount = summary.WarnCount,
            InfoCount = summary.InfoCount,
            ReasonCodeCounts = reasonCodeCounts,
            Items = items
        };
    }
}
