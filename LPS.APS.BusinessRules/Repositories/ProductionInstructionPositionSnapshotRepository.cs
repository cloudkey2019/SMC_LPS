using Dapper;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// PI Position 快照仓储实现（Dapper + APS 库）。
/// 幂等保存：同一 (ScheduleRunId, PlanVersionId) 先 DELETE 再批量 INSERT（单事务）。
/// </summary>
public class ProductionInstructionPositionSnapshotRepository : IProductionInstructionPositionSnapshotRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public ProductionInstructionPositionSnapshotRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task SaveBatchAsync(
        int scheduleRunId,
        int planVersionId,
        IReadOnlyList<ProductionInstructionPositionSnapshot> snapshots,
        CancellationToken ct = default)
    {
        if (snapshots.Count == 0)
            return;

        await _connectionManager.ExecuteInTransactionAsync<int>(async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                @"DELETE FROM ProductionInstructionPositionSnapshot
                  WHERE ScheduleRunId = @ScheduleRunId AND PlanVersionId = @PlanVersionId",
                new { ScheduleRunId = scheduleRunId, PlanVersionId = planVersionId },
                transaction: tx);

            const string insertSql = @"
                INSERT INTO ProductionInstructionPositionSnapshot (
                    ScheduleRunId, PlanVersionId, ProductionInstructionNo,
                    MaterialId, MaterialCode, PositionType, Quantity,
                    CurrentStageCode, NextStageCode, AvailableTime,
                    SourceType, SourceKey, IssueCode, Confidence)
                VALUES (
                    @ScheduleRunId, @PlanVersionId, @ProductionInstructionNo,
                    @MaterialId, @MaterialCode, @PositionType, @Quantity,
                    @CurrentStageCode, @NextStageCode, @AvailableTime,
                    @SourceType, @SourceKey, @IssueCode, @Confidence)";

            foreach (var s in snapshots)
            {
                await conn.ExecuteAsync(insertSql, new
                {
                    s.ScheduleRunId,
                    s.PlanVersionId,
                    s.ProductionInstructionNo,
                    s.MaterialId,
                    s.MaterialCode,
                    s.PositionType,
                    s.Quantity,
                    s.CurrentStageCode,
                    s.NextStageCode,
                    s.AvailableTime,
                    s.SourceType,
                    s.SourceKey,
                    s.IssueCode,
                    s.Confidence
                }, transaction: tx);
            }

            return snapshots.Count;
        }, DatabaseId.APS);
    }
}
