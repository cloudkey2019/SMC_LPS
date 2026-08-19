global using Task = System.Threading.Tasks.Task;
using System.Data;
using System.Text.Json;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// ERP订单同步服务（2号位职责）
/// 遵循 Socket-Plug 模式：通过 ODS v_APS_SalesOrder 视图间接拉取，不直连ERP
/// 数据路径：ODS.v_APS_SalesOrder → APS.ERP_Order_Staging → sp_ValidateAndPromoteOrders → APS.Order_Canonical
/// 
/// 同步策略：
/// - 全量同步：每日凌晨，拉取所有未取消/未完成订单（日期窗口过滤延迟到ETL层）
/// - 增量同步：每小时，基于 UpdatedAt 水位线拉取变更订单
/// - 新BOMNO嗅探：增量同步时检测 Order_Canonical 中不存在的BOMNO，触发实时BOM展开
/// </summary>
public class ERPOrderSyncService : IERPOrderSyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IOrderPromotionService _promotionService;
    private readonly ILogger<ERPOrderSyncService> _logger;

    public ERPOrderSyncService(
        DatabaseConnectionManager connectionManager,
        IOrderPromotionService promotionService,
        ILogger<ERPOrderSyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _promotionService = promotionService ?? throw new ArgumentNullException(nameof(promotionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task FullSyncAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await CleanupOldStagingRecordsAsync();

            var orders = await QueryOrdersFromODSAsync(
                whereClause: "",
                parameters: null);

            if (orders.Count == 0)
            {
                _logger.LogWarning("全量同步：ODS视图未返回任何订单");
                return;
            }

            await BulkWriteToStagingAsync(orders);
            var (promoted, failed) = await _promotionService.ValidateAndPromoteAsync(cancellationToken);
            var newBOMNOs = await DetectNewBOMNOsAsync();

            stopwatch.Stop();
            _logger.LogInformation(
                "全量同步完成: 拉取={Fetched}, 提升={Promoted}, 失败={Failed}, 新BOMNO={NewBOMNO}, 耗时={Elapsed}ms",
                orders.Count, promoted, failed, newBOMNOs.Count, stopwatch.ElapsedMilliseconds);

            if (newBOMNOs.Count > 0)
            {
                _logger.LogWarning("新BOMNO需实时展开: {BOMNOs}",
                    string.Join(", ", newBOMNOs.Take(20)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "全量同步失败");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task IncrementalSyncAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var lastSyncTime = await GetLastSyncWatermarkAsync();

            var orders = await QueryOrdersFromODSAsync(
                whereClause: "WHERE UpdatedAt > @LastSyncTime",
                parameters: new { LastSyncTime = lastSyncTime });

            if (orders.Count == 0)
            {
                await UpdateSyncWatermarkAsync(DateTime.Now);
                return;
            }

            var maxUpdatedAt = orders.Max(o => o.UpdatedAt);

            try
            {
                await BulkWriteToStagingAsync(orders);
                var (promoted, failed) = await _promotionService.ValidateAndPromoteAsync(cancellationToken);
                var newBOMNOs = await DetectNewBOMNOsAsync();

                stopwatch.Stop();
                _logger.LogInformation(
                    "增量同步完成: 拉取={Fetched}, 提升={Promoted}, 失败={Failed}, 耗时={Elapsed}ms",
                    orders.Count, promoted, failed, stopwatch.ElapsedMilliseconds);

                if (newBOMNOs.Count > 0)
                {
                    _logger.LogWarning("新BOMNO需实时展开: {BOMNOs}",
                        string.Join(", ", newBOMNOs.Take(20)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "增量同步提升失败，水位线仍会更新");
            }
            finally
            {
                await UpdateSyncWatermarkAsync(maxUpdatedAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "增量同步失败");
            throw;
        }
    }

    /// <summary>
    /// 直接查询 ODS 库的 v_APS_SalesOrder 视图
    /// 遵守 Socket-Plug 红线：2号位不直连ERP，通过 ODS 视图拉取
    /// ⚠️ 原方案通过 APS SYNONYM → ODS 跨库查询，22万条耗时3分钟+
    ///    改为直连 ODS 库查询，耗时降至约10秒
    /// ⚠️ 全量同步时数据量大（22万条），设置5分钟超时
    /// </summary>
    private async Task<List<ERPOrderDto>> QueryOrdersFromODSAsync(string whereClause, object? parameters, int commandTimeout = 300)
    {
        var sql = $@"
            SELECT
                SourceOrderId,
                OrderNo,
                OrderType,
                MaterialCode,
                BOMNO,
                FactoryCode,
                Quantity,
                UOM,
                DueDate,
                OriginalDueDate,
                ReceivedQty,
                Priority,
                Status,
                SourceSystem,
                SourceMasterID,
                TransportMode,
                CustomerName,
                MTS_InstructionNo,
                CustomerCode,
                JPOrderNo,
                SalesOrderCategory,
                DemandMaturityStatus,
                CreatedAt,
                UpdatedAt
            FROM v_APS_SalesOrder
            {whereClause}
            ORDER BY UpdatedAt";

        var result = await _connectionManager.QueryAsync<ERPOrderDto>(
            sql,
            parameters,
            db: DatabaseId.ODS,
            commandTimeout: commandTimeout);

        return result.ToList();
    }

    /// <summary>
    /// 批量写入 ERP_Order_Staging 表（SyncStatus = PENDING）
    /// 使用 SqlBulkCopy 高性能写入
    /// ⚠️ 写入前按 OrderNo 去重，避免 ODS 视图返回重复数据导致违反唯一约束
    /// </summary>
    private async Task BulkWriteToStagingAsync(List<ERPOrderDto> orders)
    {
        // 按 OrderNo 去重（OrderNo 是 APS 业务主键），保留 UpdatedAt 最新的记录
        var distinctOrders = orders
            .GroupBy(o => o.OrderNo)
            .Select(g => g.OrderByDescending(o => o.UpdatedAt).First())
            .ToList();

        if (distinctOrders.Count < orders.Count)
        {
            _logger.LogWarning(
                "ODS视图存在重复OrderNo，已去重: 原始={Original}, 去重后={Distinct}",
                orders.Count, distinctOrders.Count);
        }

        var dataTable = new DataTable("ERP_Order_Staging");
        dataTable.Columns.Add("SourceOrderId", typeof(string));
        dataTable.Columns.Add("SourceSystem", typeof(string));
        dataTable.Columns.Add("SourceMasterID", typeof(int));
        dataTable.Columns.Add("OrderNo", typeof(string));
        dataTable.Columns.Add("OrderType", typeof(string));
        dataTable.Columns.Add("MaterialCode", typeof(string));
        dataTable.Columns.Add("FactoryCode", typeof(string));
        dataTable.Columns.Add("Quantity", typeof(decimal));
        dataTable.Columns.Add("UOM", typeof(string));
        dataTable.Columns.Add("DueDate", typeof(DateTime));
        dataTable.Columns.Add("OriginalDueDate", typeof(DateTime));
        dataTable.Columns.Add("ReceivedQty", typeof(decimal));
        dataTable.Columns.Add("Priority", typeof(int));
        dataTable.Columns.Add("BOMNO", typeof(string));
        dataTable.Columns.Add("Status", typeof(string));
        // v5.0.3 源事实字段
        dataTable.Columns.Add("TransportMode", typeof(string));
        dataTable.Columns.Add("CustomerName", typeof(string));
        dataTable.Columns.Add("MTS_InstructionNo", typeof(string));
        // v5.0.24 原始字段（由 sp_ValidateAndPromoteOrders 派生标准化）
        dataTable.Columns.Add("CustomerCode", typeof(string));
        dataTable.Columns.Add("JPOrderNo", typeof(string));
        dataTable.Columns.Add("SalesOrderCategory", typeof(string));
        dataTable.Columns.Add("DemandMaturityStatus", typeof(string));
        dataTable.Columns.Add("RawData", typeof(string));
        dataTable.Columns.Add("SyncStatus", typeof(string));
        dataTable.Columns.Add("SyncedAt", typeof(DateTime));

        var now = DateTime.Now;
        foreach (var order in distinctOrders)  // 使用去重后的数据
        {
            dataTable.Rows.Add(
                order.SourceOrderId,
                order.SourceSystem ?? "ERP",
                order.SourceMasterID.HasValue ? (object)order.SourceMasterID.Value : DBNull.Value,
                order.OrderNo,
                order.OrderType,
                order.MaterialCode,
                order.FactoryCode,
                order.Quantity,
                order.UOM,
                order.DueDate,
                order.OriginalDueDate,
                order.ReceivedQty,
                order.Priority,
                order.BOMNO,
                order.Status ?? "Open",
                // v5.0.3 源事实字段
                (object?)order.TransportMode ?? DBNull.Value,
                (object?)order.CustomerName ?? DBNull.Value,
                (object?)order.MTS_InstructionNo ?? DBNull.Value,
                // v5.0.24 原始字段（由 sp_ValidateAndPromoteOrders 派生标准化）
                (object?)order.CustomerCode ?? DBNull.Value,
                (object?)order.JPOrderNo ?? DBNull.Value,
                (object?)order.SalesOrderCategory ?? DBNull.Value,
                (object?)order.DemandMaturityStatus ?? DBNull.Value,
                JsonSerializer.Serialize(order),
                "PENDING",
                now
            );
        }

        await _connectionManager.BulkInsertAsync(dataTable, "ERP_Order_Staging", DatabaseId.APS);
    }

    /// <summary>
    /// 检测新BOMNO（紧急插单支持）
    /// 对比最新Staging批次中的BOMNO与现有Order_Canonical，找出新增的BOMNO
    /// </summary>
    private async Task<List<string>> DetectNewBOMNOsAsync()
    {
        var sql = @"
            SELECT DISTINCT stg.BOMNO
            FROM ERP_Order_Staging stg
            WHERE stg.SyncStatus IN ('VALIDATED', 'PROCESSED')
              AND stg.ProcessedAt >= DATEADD(HOUR, -2, GETDATE())
              AND stg.BOMNO IS NOT NULL
              AND stg.BOMNO <> ''
              AND NOT EXISTS (
                  SELECT 1 FROM Order_Canonical oc 
                  WHERE oc.BOMNO = stg.BOMNO 
                    AND oc.CreatedAt < DATEADD(HOUR, -2, GETDATE())
              )";

        var result = await _connectionManager.QueryAsync<string>(sql, db: DatabaseId.APS);
        return result.ToList();
    }

    /// <summary>
    /// 获取上次同步水位线（从APS_SyncWatermark表读取）
    /// ⚠️ 首次同步：返回一个很早的日期（2020-01-01），确保全量拉取所有历史订单
    /// ⚠️ 后续同步：返回上次水位线，拉取所有变更（不限制回溯时间）
    /// </summary>
    private async Task<DateTime> GetLastSyncWatermarkAsync()
    {
        try
        {
            var sql = @"
                SELECT TOP 1 LastSyncTime
                FROM APS_SyncWatermark
                WHERE SyncType = 'ERP_Order'
                ORDER BY LastSyncTime DESC";

            var result = await _connectionManager.QueryFirstOrDefaultAsync<DateTime?>(sql, db: DatabaseId.APS);

            if (result == null)
            {
                _logger.LogWarning("未找到同步水位线，将全量拉取");
                return new DateTime(2020, 1, 1);
            }

            return result.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "水位线查询失败，回退全量拉取");
            return new DateTime(2020, 1, 1);
        }
    }

    /// <summary>
    /// 更新同步水位线
    /// </summary>
    private async Task UpdateSyncWatermarkAsync(DateTime syncTime)
    {
        try
        {
            var sql = @"
                IF EXISTS (SELECT 1 FROM APS_SyncWatermark WHERE SyncType = 'ERP_Order')
                    UPDATE APS_SyncWatermark
                    SET LastSyncTime = @SyncTime, UpdatedAt = GETDATE()
                    WHERE SyncType = 'ERP_Order'
                ELSE
                    INSERT INTO APS_SyncWatermark (SyncType, LastSyncTime, UpdatedAt)
                    VALUES ('ERP_Order', @SyncTime, GETDATE())";

            await _connectionManager.ExecuteAsync(sql, new { SyncTime = syncTime }, db: DatabaseId.APS);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新同步水位线失败（非致命错误，下次同步将使用旧水位线）");
        }
    }

    /// <summary>
    /// 清理Staging表历史数据
    /// 策略：
    /// - 全量同步前清理所有残留 PENDING 记录（马上要重新拉全量，旧的无意义）
    /// - 保留7天内已处理(PROCESSED)的记录用于审计追溯
    /// - 保留所有失败(FAILED)记录用于问题排查
    /// - 删除7天前已处理的记录，避免表无限增长
    /// 调用时机：全量同步开始前
    /// </summary>
    private async Task CleanupOldStagingRecordsAsync()
    {
        try
        {
            var sql = @"
                DELETE FROM ERP_Order_Staging
                WHERE SyncStatus = 'PENDING';

                DELETE FROM ERP_Order_Staging
                WHERE SyncStatus = 'PROCESSED'
                  AND ProcessedAt < DATEADD(DAY, -7, GETDATE())";

            var deletedCount = await _connectionManager.ExecuteAsync(sql, db: DatabaseId.APS);

            if (deletedCount > 0)
            {
                _logger.LogDebug("清理Staging历史: {Count} 条（含残留PENDING）", deletedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理Staging历史记录失败（非致命）");
        }
    }
}
