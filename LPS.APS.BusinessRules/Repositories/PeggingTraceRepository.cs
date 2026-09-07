using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// Pegging Trace查询Repository实现
/// 直接读取PeggingSupplyAllocation表
/// </summary>
public class PeggingTraceRepository : IPeggingTraceRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public PeggingTraceRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<PeggingTraceDto>> QueryAsync(
        int planVersionId,
        string? materialCode = null,
        string? supplyType = null,
        string? commitmentStatus = null,
        string? orderNo = null,
        string? supplyDocumentNo = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    Id,
    PlanVersionId,
    ScheduleRunId,
    AllocationSequence,
    RootOrderId,
    RootOrderNo,
    CurrentOrderId,
    CurrentOrderNo,
    OrderType,
    MaterialCode,
    MaterialId,
    DemandFactoryCode,
    DemandStageCode,
    DemandQty,
    AllocatedQty,
    SupplyType,
    SupplyFactoryCode,
    SupplyWarehouseCode,
    ERPProperty,
    SupplyDocumentType,
    SupplyDocumentNo,
    SupplyMode,
    CrossFactoryEdgeId,
    TransportLeadTimeHours,
    ETA,
    KnownAvailableTime,
    CommitmentStatus,
    AttachStageCode,
    CompletedStageCode,
    NextRequiredStageCode,
    CreatedAt
FROM PeggingSupplyAllocation
WHERE PlanVersionId = @PlanVersionId
    AND (@MaterialCode IS NULL OR MaterialCode LIKE '%' + @MaterialCode + '%')
    AND (@SupplyType IS NULL OR SupplyType = @SupplyType)
    AND (@CommitmentStatus IS NULL OR CommitmentStatus = @CommitmentStatus)
    AND (@OrderNo IS NULL OR RootOrderNo LIKE '%' + @OrderNo + '%' OR CurrentOrderNo LIKE '%' + @OrderNo + '%')
    AND (@SupplyDocumentNo IS NULL OR SupplyDocumentNo LIKE '%' + @SupplyDocumentNo + '%')
ORDER BY AllocationSequence
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            PlanVersionId = planVersionId,
            MaterialCode = materialCode,
            SupplyType = supplyType,
            CommitmentStatus = commitmentStatus,
            OrderNo = orderNo,
            SupplyDocumentNo = supplyDocumentNo,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<PeggingTraceDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<List<PeggingTraceDto>> QueryByOrderAsync(
        int planVersionId,
        long orderId,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    Id,
    PlanVersionId,
    ScheduleRunId,
    AllocationSequence,
    RootOrderId,
    RootOrderNo,
    CurrentOrderId,
    CurrentOrderNo,
    OrderType,
    MaterialCode,
    MaterialId,
    DemandFactoryCode,
    DemandStageCode,
    DemandQty,
    AllocatedQty,
    SupplyType,
    SupplyFactoryCode,
    SupplyWarehouseCode,
    ERPProperty,
    SupplyDocumentType,
    SupplyDocumentNo,
    SupplyMode,
    CrossFactoryEdgeId,
    TransportLeadTimeHours,
    ETA,
    KnownAvailableTime,
    CommitmentStatus,
    AttachStageCode,
    CompletedStageCode,
    NextRequiredStageCode,
    CreatedAt
FROM PeggingSupplyAllocation
WHERE PlanVersionId = @PlanVersionId
    AND (RootOrderId = @OrderId OR CurrentOrderId = @OrderId)
ORDER BY AllocationSequence";

        var parameters = new { PlanVersionId = planVersionId, OrderId = orderId };

        var results = await _connectionManager.QueryAsync<PeggingTraceDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<PeggingTraceSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken ct = default)
    {
        var summaryParams = new { PlanVersionId = planVersionId };

        // 总数统计
        var totalSql = @"
SELECT COUNT(*) AS TotalAllocations
FROM PeggingSupplyAllocation
WHERE PlanVersionId = @PlanVersionId";

        var total = (await _connectionManager.QueryAsync<int>(
            totalSql, summaryParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .FirstOrDefault();

        // SupplyType统计
        var supplyTypeSql = @"
SELECT
    SupplyType,
    COUNT(*) AS Count,
    SUM(AllocatedQty) AS TotalAllocatedQty
FROM PeggingSupplyAllocation
WHERE PlanVersionId = @PlanVersionId
GROUP BY SupplyType
ORDER BY COUNT(*) DESC";

        var supplyTypeCounts = (await _connectionManager.QueryAsync<SupplyTypeCountDto>(
            supplyTypeSql, summaryParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .ToList();

        // CommitmentStatus统计
        var commitmentSql = @"
SELECT
    ISNULL(CommitmentStatus, 'UNKNOWN') AS CommitmentStatus,
    COUNT(*) AS Count,
    SUM(AllocatedQty) AS TotalAllocatedQty
FROM PeggingSupplyAllocation
WHERE PlanVersionId = @PlanVersionId
GROUP BY CommitmentStatus
ORDER BY COUNT(*) DESC";

        var commitmentCounts = (await _connectionManager.QueryAsync<CommitmentStatusCountDto>(
            commitmentSql, summaryParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .ToList();

        return new PeggingTraceSummaryDto
        {
            PlanVersionId = planVersionId,
            TotalAllocations = total,
            SupplyTypeCounts = supplyTypeCounts,
            CommitmentStatusCounts = commitmentCounts
        };
    }
}
