using System.Data;
using Dapper;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Pegging;

public class VirtualInventoryBalanceRepository : IVirtualInventoryBalanceRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<VirtualInventoryBalanceRepository> _logger;

    public VirtualInventoryBalanceRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<VirtualInventoryBalanceRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<int> BulkInsertAsync(
        IEnumerable<VirtualInventoryBalance> balances,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO [dbo].[VirtualInventoryBalance]
                ([PlanVersionId], [MaterialId], [FactoryCode],
                 [SourceProductFamilyId], [TargetProductFamilyId],
                 [VirtualAvailableQuantity], [AllocatedQuantity], [RemainingQuantity],
                 [UOM], [AvailableAt], [UpstreamTaskId], [BomLevel],
                 [TopologicalOrder], [IsPropagated], [DependencyType],
                 [UpstreamFactoryCode], [DownstreamFactoryCode], [CrossFactoryMode],
                 [ComputedAt], [Remarks], [CreatedAt])
            VALUES
                (@PlanVersionId, @MaterialId, @FactoryCode,
                 @SourceProductFamilyId, @TargetProductFamilyId,
                 @VirtualAvailableQuantity, @AllocatedQuantity, @RemainingQuantity,
                 @UOM, @AvailableAt, @UpstreamTaskId, @BomLevel,
                 @TopologicalOrder, @IsPropagated, @DependencyType,
                 @UpstreamFactoryCode, @DownstreamFactoryCode, @CrossFactoryMode,
                 @ComputedAt, @Remarks, @CreatedAt)";

        var rows = balances.ToList();
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

    public async Task<IEnumerable<VirtualInventoryBalance>> GetByPlanVersionIdAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[VirtualInventoryBalance]
            WHERE [PlanVersionId] = @PlanVersionId
            ORDER BY [TopologicalOrder], [Id]";

        return await _connectionManager.QueryAsync<VirtualInventoryBalance>(
            sql, new { PlanVersionId = planVersionId });
    }

    public async Task<IEnumerable<VirtualInventoryBalance>> GetByTopologicalOrderAsync(
        int planVersionId,
        int topologicalOrder,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[VirtualInventoryBalance]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [TopologicalOrder] = @TopologicalOrder
            ORDER BY [Id]";

        return await _connectionManager.QueryAsync<VirtualInventoryBalance>(
            sql, new { PlanVersionId = planVersionId, TopologicalOrder = topologicalOrder });
    }

    public async Task<IEnumerable<VirtualInventoryBalance>> GetByProductFamilyAsync(
        int planVersionId,
        int sourceProductFamilyId,
        int targetProductFamilyId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[VirtualInventoryBalance]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [SourceProductFamilyId] = @SourceProductFamilyId
              AND [TargetProductFamilyId] = @TargetProductFamilyId
            ORDER BY [TopologicalOrder], [Id]";

        return await _connectionManager.QueryAsync<VirtualInventoryBalance>(
            sql, new { PlanVersionId = planVersionId, SourceProductFamilyId = sourceProductFamilyId, TargetProductFamilyId = targetProductFamilyId });
    }

    public async Task<IEnumerable<VirtualInventoryBalance>> GetByUpstreamTaskIdAsync(
        int planVersionId,
        long upstreamTaskId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[VirtualInventoryBalance]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [UpstreamTaskId] = @UpstreamTaskId
            ORDER BY [Id]";

        return await _connectionManager.QueryAsync<VirtualInventoryBalance>(
            sql, new { PlanVersionId = planVersionId, UpstreamTaskId = upstreamTaskId });
    }

    public async Task<int> UpdateAllocatedQuantityAsync(
        int planVersionId,
        long balanceId,
        decimal allocatedQuantity,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[VirtualInventoryBalance]
            SET [AllocatedQuantity] = @AllocatedQuantity,
                [RemainingQuantity] = [VirtualAvailableQuantity] - @AllocatedQuantity
            WHERE [PlanVersionId] = @PlanVersionId
              AND [Id] = @Id";

        return await _connectionManager.ExecuteAsync(
            sql, new { PlanVersionId = planVersionId, Id = balanceId, AllocatedQuantity = allocatedQuantity });
    }

    public async Task<int> MarkAsPropagatedAsync(
        int planVersionId,
        IEnumerable<long> balanceIds,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE [dbo].[VirtualInventoryBalance]
            SET [IsPropagated] = 1
            WHERE [PlanVersionId] = @PlanVersionId
              AND [Id] IN @Ids";

        return await _connectionManager.ExecuteAsync(
            sql, new { PlanVersionId = planVersionId, Ids = balanceIds });
    }

    public async Task<int> DeleteByPlanVersionIdAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM [dbo].[VirtualInventoryBalance] WHERE [PlanVersionId] = @PlanVersionId";

        return await _connectionManager.ExecuteAsync(sql, new { PlanVersionId = planVersionId });
    }

    public async Task<decimal> SumRemainingQuantityAsync(
        int planVersionId,
        int materialId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT ISNULL(SUM([RemainingQuantity]), 0)
            FROM [dbo].[VirtualInventoryBalance]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [MaterialId] = @MaterialId
              AND [IsPropagated] = 0";

        return await _connectionManager.QueryFirstOrDefaultAsync<decimal>(
            sql, new { PlanVersionId = planVersionId, MaterialId = materialId });
    }

    public async Task<IEnumerable<VirtualInventoryBalance>> GetUnpropagatedOrderedAsync(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[VirtualInventoryBalance]
            WHERE [PlanVersionId] = @PlanVersionId
              AND [IsPropagated] = 0
            ORDER BY [TopologicalOrder], [Id]";

        return await _connectionManager.QueryAsync<VirtualInventoryBalance>(
            sql, new { PlanVersionId = planVersionId });
    }
}
