using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 供应商库存同步服务（2号位职责 — 每小时执行）
///
/// 调用 sp_SyncSupplierInventory，完成：
///   Step 1-2: 从采购系统读取供应商库存 + 在途采购单
///   Step 3-4: JOIN Material 映射到 APS 实体
///   Step 5:   合并两个数据源（SUPPLIER_STOCK + PO_IN_TRANSIT）
///   Step 6:   TRUNCATE + INSERT 全量刷新 SupplierInventorySnapshot
///
/// 数据来源：
///   - Procurement_SupplierInventory_View（供应商仓库库存）
///   - Procurement_PO_InTransit_View（在途采购单）
///
/// 调度频率：每小时执行（文档要求）
/// SP 契约：DDL v5.0.41
/// </summary>
public class SupplierInventorySyncService : ISupplierInventorySyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<SupplierInventorySyncService> _logger;

    private const int CommandTimeoutSeconds = 300; // 5分钟

    public SupplierInventorySyncService(
        DatabaseConnectionManager connectionManager,
        ILogger<SupplierInventorySyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SupplierInventorySyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"SUPPLIER_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("供应商库存同步开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@BatchNo", batchNo);
            spParams.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_SyncSupplierInventory",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS,
                commandTimeout: CommandTimeoutSeconds);

            stopwatch.Stop();

            var result = new SupplierInventorySyncResultDto
            {
                BatchNo = batchNo,
                RowsAffected = spParams.Get<int>("@RowsAffected"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "供应商库存同步完成: BatchNo={BatchNo}, 耗时={Elapsed}ms, 影响行数={RowsAffected}",
                    batchNo, stopwatch.ElapsedMilliseconds, result.RowsAffected);
            }
            else
            {
                _logger.LogError(
                    "供应商库存同步SP返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "供应商库存同步异常: BatchNo={BatchNo}, 耗时={Elapsed}ms",
                batchNo, stopwatch.ElapsedMilliseconds);

            return new SupplierInventorySyncResultDto
            {
                BatchNo = batchNo,
                RowsAffected = 0,
                ErrorMessage = ex.Message
            };
        }
    }
}
