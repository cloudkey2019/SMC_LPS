using System.Data;
using Dapper;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Pegging;

public class FrozenZoneSnapshotRepository : IFrozenZoneSnapshotRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<FrozenZoneSnapshotRepository> _logger;

    public FrozenZoneSnapshotRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<FrozenZoneSnapshotRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<int> BulkInsertAsync(
        IEnumerable<FrozenZoneSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO [dbo].[FrozenZoneSnapshot]
                ([PlanVersionId], [TaskId], [MaterialId], [FactoryCode], [ProductFamilyId],
                 [MESWorkOrderNo], [PlannedStartTime], [PlannedEndTime],
                 [FrozenWindowStart], [FrozenWindowEnd], [IsDispatched], [DispatchedAt],
                 [Quantity], [UOM], [ResourceId], [ResourceCode],
                 [FrozenReason], [CrossFactoryMode], [UpstreamFactoryCode],
                 [Remarks], [SnapshotAt], [CreatedAt])
            VALUES
                (@PlanVersionId, @TaskId, @MaterialId, @FactoryCode, @ProductFamilyId,
                 @MESWorkOrderNo, @PlannedStartTime, @PlannedEndTime,
                 @FrozenWindowStart, @FrozenWindowEnd, @IsDispatched, @DispatchedAt,
                 @Quantity, @UOM, @ResourceId, @ResourceCode,
                 @FrozenReason, @CrossFactoryMode, @UpstreamFactoryCode,
                 @Remarks, @SnapshotAt, @CreatedAt)";

        var rows = snapshots.ToList();
        if (rows.Count == 0) return 0;

        return await _connectionManager.ExecuteInTransactionAsync<int>(
            async (conn, tx) =>
            {
                var affected = 0;
                foreach (var batch in rows.Chunk(1000))
                {
                    affected += await conn.ExecuteAsync(sql, batch, tx);
                }
                return affected;
            },
            DatabaseId.APS);
    }

    public async Task<IEnumerable<FrozenZoneSnapshot>> GetByPlanVersionIdAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[FrozenZoneSnapshot]
            WHERE [PlanVersionId] = @PlanVersionId
            ORDER BY [FrozenWindowStart], [Id]";

        return await _connectionManager.QueryAsync<FrozenZoneSnapshot>(
            sql, new { PlanVersionId = planVersionId });
    }

    public async Task<FrozenZoneSnapshot?> GetByTaskIdAsync(
        int planVersionId,
        long taskId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP 1 * FROM [dbo].[FrozenZoneSnapshot]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [TaskId] = @TaskId";

        return await _connectionManager.QueryFirstOrDefaultAsync<FrozenZoneSnapshot>(
            sql, new { PlanVersionId = planVersionId, TaskId = taskId });
    }

    public async Task<IEnumerable<FrozenZoneSnapshot>> GetByFrozenWindowAsync(
        int planVersionId,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[FrozenZoneSnapshot]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [FrozenWindowStart] >= @WindowStart
              AND [FrozenWindowEnd] <= @WindowEnd
            ORDER BY [FrozenWindowStart], [Id]";

        return await _connectionManager.QueryAsync<FrozenZoneSnapshot>(
            sql, new { PlanVersionId = planVersionId, WindowStart = windowStart, WindowEnd = windowEnd });
    }

    public async Task<FrozenZoneSnapshot?> GetByMESWorkOrderNoAsync(
        string mesWorkOrderNo,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP 1 * FROM [dbo].[FrozenZoneSnapshot]
            WHERE [MESWorkOrderNo] = @MESWorkOrderNo
            ORDER BY [CreatedAt] DESC";

        return await _connectionManager.QueryFirstOrDefaultAsync<FrozenZoneSnapshot>(
            sql, new { MESWorkOrderNo = mesWorkOrderNo });
    }

    public async Task<IEnumerable<FrozenZoneSnapshot>> GetDispatchedAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[FrozenZoneSnapshot]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [IsDispatched] = 1
            ORDER BY [DispatchedAt], [Id]";

        return await _connectionManager.QueryAsync<FrozenZoneSnapshot>(
            sql, new { PlanVersionId = planVersionId });
    }

    public async Task<int> UpdateDispatchStatusAsync(
        int planVersionId,
        long taskId,
        bool isDispatched,
        DateTime? dispatchedAt = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[FrozenZoneSnapshot]
            SET [IsDispatched] = @IsDispatched,
                [DispatchedAt] = @DispatchedAt
            WHERE [PlanVersionId] = @PlanVersionId
              AND [TaskId] = @TaskId";

        return await _connectionManager.ExecuteAsync(
            sql, new { PlanVersionId = planVersionId, TaskId = taskId, IsDispatched = isDispatched, DispatchedAt = dispatchedAt });
    }

    public async Task<int> DeleteByPlanVersionIdAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM [dbo].[FrozenZoneSnapshot] WHERE [PlanVersionId] = @PlanVersionId";

        return await _connectionManager.ExecuteAsync(sql, new { PlanVersionId = planVersionId });
    }

    public async Task<int> CountInFrozenWindowAsync(
        int planVersionId,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM [dbo].[FrozenZoneSnapshot]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [PlannedStartTime] >= @WindowStart
              AND [PlannedStartTime] < @WindowEnd";

        return await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql, new { PlanVersionId = planVersionId, WindowStart = windowStart, WindowEnd = windowEnd });
    }
}
