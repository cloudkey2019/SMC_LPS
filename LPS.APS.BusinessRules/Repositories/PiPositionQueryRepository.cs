using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// PI Position查询Repository实现
/// 直接读取ProductionInstructionPositionSnapshot表
/// </summary>
public class PiPositionQueryRepository : IPiPositionQueryRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public PiPositionQueryRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<PiPositionDto>> QueryAsync(
        int planVersionId,
        string? productionInstructionNo = null,
        string? materialCode = null,
        string? positionType = null,
        string? stageCode = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null)
    {
        // Dapper 列表参数只进 IN；@AllowedFactories IS NULL 标量判空会随列表一起扩成 (…) 导致 4145，用 Has 标志替代；空集 fail-closed 返空。
        if (allowedFactories is { Count: 0 })
            return new List<PiPositionDto>();
        var hasFactories = allowedFactories is { Count: > 0 };

        var sql = @"
SELECT
    p.Id,
    p.ScheduleRunId,
    p.PlanVersionId,
    p.ProductionInstructionNo,
    p.MaterialId,
    p.MaterialCode,
    p.PositionType,
    p.Quantity,
    p.CurrentStageCode,
    p.NextStageCode,
    p.AvailableTime,
    p.SourceType,
    p.SourceKey,
    p.IssueCode,
    p.Confidence,
    p.CreatedAt
FROM ProductionInstructionPositionSnapshot p
LEFT JOIN [Order] o
    ON o.PlanVersionId = p.PlanVersionId
   AND o.MTS_InstructionNo = p.ProductionInstructionNo
WHERE p.PlanVersionId = @PlanVersionId
    AND (@PINO IS NULL OR p.ProductionInstructionNo LIKE '%' + @PINO + '%')
    AND (@MaterialCode IS NULL OR p.MaterialCode LIKE '%' + @MaterialCode + '%')
    AND (@PositionType IS NULL OR p.PositionType = @PositionType)
    AND (@StageCode IS NULL OR p.CurrentStageCode = @StageCode OR p.NextStageCode = @StageCode)
    AND (@HasFactories = 0 OR o.SourceFactoryId IN @AllowedFactories)
ORDER BY p.ProductionInstructionNo, p.PositionType
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            PlanVersionId = planVersionId,
            PINO = productionInstructionNo,
            MaterialCode = materialCode,
            PositionType = positionType,
            StageCode = stageCode,
            HasFactories = hasFactories,
            AllowedFactories = allowedFactories,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<PiPositionDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<PiPositionSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken ct = default)
    {
        var summaryParams = new { PlanVersionId = planVersionId };

        // 汇总统计
        var summarySql = @"
SELECT
    @PlanVersionId AS PlanVersionId,
    COUNT(DISTINCT ProductionInstructionNo) AS PiCount,
    COUNT(*) AS TotalPositions
FROM ProductionInstructionPositionSnapshot
WHERE PlanVersionId = @PlanVersionId";

        var summary = (await _connectionManager.QueryAsync<PiPositionSummaryDto>(
            summarySql, summaryParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .FirstOrDefault() ?? new PiPositionSummaryDto { PlanVersionId = planVersionId };

        // PositionType统计
        var typeSql = @"
SELECT
    PositionType,
    COUNT(*) AS Count,
    SUM(Quantity) AS TotalQuantity
FROM ProductionInstructionPositionSnapshot
WHERE PlanVersionId = @PlanVersionId
GROUP BY PositionType
ORDER BY COUNT(*) DESC";

        var typeCounts = (await _connectionManager.QueryAsync<PositionTypeCountDto>(
            typeSql, summaryParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .ToList();

        return new PiPositionSummaryDto
        {
            PlanVersionId = summary.PlanVersionId,
            PiCount = summary.PiCount,
            TotalPositions = summary.TotalPositions,
            PositionTypeCounts = typeCounts
        };
    }

    public async Task<List<PiPositionDto>> GetByProductionInstructionAsync(
        int planVersionId,
        string productionInstructionNo,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    p.Id,
    p.ScheduleRunId,
    p.PlanVersionId,
    p.ProductionInstructionNo,
    p.MaterialId,
    p.MaterialCode,
    p.PositionType,
    p.Quantity,
    p.CurrentStageCode,
    p.NextStageCode,
    p.AvailableTime,
    p.SourceType,
    p.SourceKey,
    p.IssueCode,
    p.Confidence,
    p.CreatedAt
FROM ProductionInstructionPositionSnapshot p
WHERE p.PlanVersionId = @PlanVersionId
    AND p.ProductionInstructionNo = @ProductionInstructionNo
ORDER BY p.PositionType";

        var parameters = new
        {
            PlanVersionId = planVersionId,
            ProductionInstructionNo = productionInstructionNo
        };

        var results = await _connectionManager.QueryAsync<PiPositionDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 10);

        return results.ToList();
    }
}
