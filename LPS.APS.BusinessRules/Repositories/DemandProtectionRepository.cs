using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// Demand Protection查询Repository实现
/// 直接读取DemandSupplyHardLock表
/// </summary>
public class DemandProtectionRepository : IDemandProtectionRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public DemandProtectionRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<DemandProtectionDto>> QueryAsync(
        string? demandKey = null,
        string? supplyKey = null,
        string? lockType = null,
        string? status = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    Id,
    LockType,
    DemandType,
    DemandKey,
    SupplyType,
    SupplyKey,
    LockedQty,
    SourcePlanVersionId,
    SourceAllocationSequence,
    Status,
    CreatedAt,
    CreatedBy,
    ReleasedAt,
    ReleasedBy,
    ReleaseReason
FROM DemandSupplyHardLock
WHERE 1=1
    AND (@DemandKey IS NULL OR DemandKey LIKE '%' + @DemandKey + '%')
    AND (@SupplyKey IS NULL OR SupplyKey LIKE '%' + @SupplyKey + '%')
    AND (@LockType IS NULL OR LockType = @LockType)
    AND (@Status IS NULL OR Status = @Status)
ORDER BY
    CASE Status WHEN 'ACTIVE' THEN 1 WHEN 'RELEASED' THEN 2 ELSE 3 END,
    CreatedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            DemandKey = demandKey,
            SupplyKey = supplyKey,
            LockType = lockType,
            Status = status,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<DemandProtectionDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<DemandProtectionSummaryDto> GetSummaryAsync(
        string? demandKey = null,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN Status = 'ACTIVE' THEN 1 ELSE 0 END) AS ActiveCount,
    SUM(CASE WHEN Status = 'RELEASED' THEN 1 ELSE 0 END) AS ReleasedCount,
    SUM(CASE WHEN Status = 'BROKEN' THEN 1 ELSE 0 END) AS BrokenCount,
    SUM(CASE WHEN Status = 'ACTIVE' THEN LockedQty ELSE 0 END) AS TotalLockedQty
FROM DemandSupplyHardLock
WHERE (@DemandKey IS NULL OR DemandKey LIKE '%' + @DemandKey + '%')";

        var parameters = new { DemandKey = demandKey };

        var summary = (await _connectionManager.QueryAsync<DemandProtectionSummaryDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .FirstOrDefault() ?? new DemandProtectionSummaryDto();

        // 查询ACTIVE记录列表
        var items = await QueryAsync(demandKey: demandKey, status: "ACTIVE", take: 50, ct: ct);

        return new DemandProtectionSummaryDto
        {
            TotalCount = summary.TotalCount,
            ActiveCount = summary.ActiveCount,
            ReleasedCount = summary.ReleasedCount,
            BrokenCount = summary.BrokenCount,
            TotalLockedQty = summary.TotalLockedQty,
            Items = items
        };
    }
}
