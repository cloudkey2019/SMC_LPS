using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 订单装载服务（2号位职责）
/// 调用 sp_SyncOrdersToPartitionTable 将 Order_Canonical 装载到 Order 分区表
/// 
/// 数据路径：Order_Canonical → sp_SyncOrdersToPartitionTable → Order（分区表）
/// 补齐字段：MaterialId, ProductFamilyId, FactoryId, DomainKey, PriorityScore
/// 透传字段：TransportMode, CustomerName, CustomerSegment, SalesOrderCategory, DemandMaturityStatus, MTS_InstructionNo
/// </summary>
public class OrderLoadingService : IOrderLoadingService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<OrderLoadingService> _logger;

    public OrderLoadingService(
        DatabaseConnectionManager connectionManager,
        ILogger<OrderLoadingService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<int> LoadOrdersToPartitionTableAsync(int planVersionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始订单装载到分区表，PlanVersionId={PlanVersionId}", planVersionId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var parameters = new DynamicParameters();
            parameters.Add("@PlanVersionId", planVersionId, DbType.Int32);

            var result = await _connectionManager.QueryFirstOrDefaultAsync<OrderLoadingResultDto>(
                "sp_SyncOrdersToPartitionTable",
                parameters,
                CommandType.StoredProcedure,
                DatabaseId.APS);

            var insertCount = result?.InsertCount ?? 0;

            stopwatch.Stop();
            _logger.LogInformation(
                "订单装载完成：PlanVersionId={PlanVersionId}, 装载={InsertCount}条, 耗时={Elapsed}ms",
                planVersionId, insertCount, stopwatch.ElapsedMilliseconds);

            return insertCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "订单装载失败，PlanVersionId={PlanVersionId}", planVersionId);
            throw;
        }
    }

}
