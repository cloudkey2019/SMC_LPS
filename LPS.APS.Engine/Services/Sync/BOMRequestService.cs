using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// BOM展开请求服务（2号位职责）
///
/// 时序：每天00:00
///   Step 1: 调用 sp_GetActiveRootBOMNOs 统计活跃根集合（日志+统计用）
///   Step 2: 直接从 Order_Canonical 查询活跃订单（不再JOIN ERP_Order_Staging）
///   Step 3: 在 ODS 库插入 MES_API_BOM_Request 批次头（PENDING状态）
///   Step 4: 通过 SqlBulkCopy 推送订单级明细到 MES_API_BOM_Request_Detail
///   Step 5: SQL Agent Job 00:05 自动拾取 PENDING 批次执行 sp_ExpandBOMBatch_vNext
///
/// 【批次幂等红线】：
///   ✅ BatchNo 全局唯一（REQ_yyyyMMdd_xxxxxxxx）
///   ✅ MES_API_BOM_Request_Detail 有 (BatchNo, OrderCanonicalId) 唯一约束（v5.0.31改）
///   ✅ 2号位只推送基础字段，BOM入口解析由5号位负责
/// </summary>
public class BOMRequestService : IBOMRequestService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<BOMRequestService> _logger;

    public BOMRequestService(
        DatabaseConnectionManager connectionManager,
        ILogger<BOMRequestService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BOMRequestResult> PushBOMRequestToODSAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"REQ_{DateTime.Now:yyyyMMdd}_{Guid.NewGuid():N}"[..28];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@RootCount", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@TotalOrderCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

            await _connectionManager.QueryAsync<ActiveBOMNODto>(
                "sp_GetActiveRootBOMNOs",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS);

            var rootCount = spParams.Get<int>("@RootCount");
            var totalOrderCount = spParams.Get<int>("@TotalOrderCount");

            if (totalOrderCount == 0)
            {
                _logger.LogWarning("活跃订单为空，跳过BOM推送");
                return new BOMRequestResult { BatchNo = batchNo, RootCount = 0, TotalOrderCount = 0 };
            }

            var insertRequestSql = @"
                INSERT INTO MES_API_BOM_Request (BatchNo, Status, RootCount, CreatedAt, RetryCount)
                VALUES (@BatchNo, 'PENDING', @RootCount, GETDATE(), 0)";

            await _connectionManager.ExecuteAsync(
                insertRequestSql,
                new { BatchNo = batchNo, RootCount = totalOrderCount },
                db: DatabaseId.ODS);

            var detailSql = @"
                SELECT
                    oc.Id AS OrderCanonicalId,
                    oc.OrderNo,
                    oc.SourceSystem,
                    oc.SourceOrderId,
                    oc.MaterialCode,
                    oc.FactoryCode,
                    oc.OrderType,
                    oc.BOMNO AS RequestedBOMNO
                FROM Order_Canonical oc
                WHERE oc.Status IN ('Open', 'Released')
                  AND (
                      (oc.OrderType = 'SALES_ORDER'
                       AND oc.DueDate BETWEEN CAST(GETDATE() AS DATE) AND DATEADD(DAY, 90, CAST(GETDATE() AS DATE)))
                      OR
                      (oc.OrderType = 'PRODUCTION_INSTRUCTION')
                  )";

            var activeOrders = (await _connectionManager.QueryAsync<BOMRequestDetailDto>(
                detailSql, db: DatabaseId.APS, commandTimeout: 120)).ToList();

            if (activeOrders.Count == 0)
            {
                _logger.LogWarning("活跃订单明细为空");
                return new BOMRequestResult { BatchNo = batchNo, RootCount = rootCount, TotalOrderCount = 0 };
            }

            var dataTable = new DataTable("MES_API_BOM_Request_Detail");
            dataTable.Columns.Add("BatchNo", typeof(string));
            dataTable.Columns.Add("OrderCanonicalId", typeof(long));
            dataTable.Columns.Add("OrderNo", typeof(string));
            dataTable.Columns.Add("SourceSystem", typeof(string));
            dataTable.Columns.Add("SourceOrderId", typeof(string));
            dataTable.Columns.Add("MaterialCode", typeof(string));
            dataTable.Columns.Add("FactoryCode", typeof(string));
            dataTable.Columns.Add("OrderType", typeof(string));
            dataTable.Columns.Add("RequestedBOMNO", typeof(string));

            foreach (var order in activeOrders)
            {
                dataTable.Rows.Add(
                    batchNo,
                    order.OrderCanonicalId,
                    (object?)order.OrderNo ?? DBNull.Value,
                    (object?)order.SourceSystem ?? DBNull.Value,
                    (object?)order.SourceOrderId ?? DBNull.Value,
                    (object?)order.MaterialCode ?? DBNull.Value,
                    (object?)order.FactoryCode ?? DBNull.Value,
                    (object?)order.OrderType ?? DBNull.Value,
                    (object?)order.RequestedBOMNO ?? DBNull.Value);
            }

            await _connectionManager.BulkInsertAsync(dataTable, "MES_API_BOM_Request_Detail", DatabaseId.ODS);

            stopwatch.Stop();
            _logger.LogInformation(
                "BOM推送完成: BatchNo={BatchNo}, 订单数={Count}, 耗时={Elapsed}ms",
                batchNo, activeOrders.Count, stopwatch.ElapsedMilliseconds);

            return new BOMRequestResult
            {
                BatchNo = batchNo,
                RootCount = rootCount,
                TotalOrderCount = activeOrders.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BOM推送失败: BatchNo={BatchNo}", batchNo);
            throw;
        }
    }
}
