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

    /// <inheritdoc />
    public async Task<int> DetectUnassignedOrdersAsync(IReadOnlyList<int> planVersionIds, CancellationToken cancellationToken = default)
    {
        if (planVersionIds == null || planVersionIds.Count == 0)
            return 0;

        var rows = await _connectionManager.QueryAsync<UnassignedOrderRow>(
            @"SELECT oc.OrderNo, oc.MaterialCode, oc.FactoryCode, m.ProductFamilyId
              FROM Order_Canonical oc
              LEFT JOIN Material m ON oc.MaterialCode = m.MaterialCode
              WHERE oc.Status IN ('Open', 'Released')
                AND oc.DueDate BETWEEN GETDATE() AND DATEADD(DAY, 90, GETDATE())
                AND NOT EXISTS (
                    SELECT 1 FROM [Order] o
                    WHERE o.OrderNo = oc.OrderNo AND o.PlanVersionId IN @PlanVersionIds
                )",
            new { PlanVersionIds = planVersionIds },
            db: DatabaseId.APS);

        var unassigned = rows.ToList();
        if (unassigned.Count == 0)
        {
            _logger.LogInformation("归域失败捡漏：全部活跃订单均已归入某个 Domain，无落空订单");
            return 0;
        }

        _logger.LogWarning("归域失败捡漏：发现 {Count} 条未归域活跃订单（未匹配到任何 DomainDefinition）", unassigned.Count);

        // 登记 APS_ETL_Log（分批，避免单条 Message 过长），Status=WARN 标记数据问题
        const int batchSize = 100;
        foreach (var chunk in unassigned.Chunk(batchSize))
        {
            var detail = string.Join("; ",
                chunk.Select(r => $"OrderNo={r.OrderNo},Material={r.MaterialCode},Factory={r.FactoryCode ?? "NULL"},ProductFamilyId={r.ProductFamilyId?.ToString() ?? "NULL"}"));
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
                  VALUES ('SYNC', 'OrderDomainAssignment.Unassigned', @Message, 'WARN', GETDATE())",
                new { Message = detail },
                db: DatabaseId.APS);
        }

        return unassigned.Count;
    }

    private sealed class UnassignedOrderRow
    {
        public string OrderNo { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string? FactoryCode { get; set; }
        public int? ProductFamilyId { get; set; }
    }

}
