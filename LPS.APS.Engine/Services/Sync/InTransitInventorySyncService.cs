using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 在途库存同步服务（2号位职责）
///
/// 调用 sp_SyncInTransitInventory，完成：
///   Step 1: 从 ODS.ERP_InterplantInTransit_View 读取在途数据
///   Step 2: JOIN Material / Factory 映射到 APS 实体
///   Step 3: MERGE 到 InTransitInventoryFact（UPSERT）
///   Step 4: 清理已到货历史数据（保留7天）
///
/// 调度频率：建议每30分钟执行
/// SP 契约：DDL v5.0.41
/// </summary>
public class InTransitInventorySyncService : IInTransitInventorySyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<InTransitInventorySyncService> _logger;

    private const int CommandTimeoutSeconds = 300; // 5分钟

    public InTransitInventorySyncService(
        DatabaseConnectionManager connectionManager,
        ILogger<InTransitInventorySyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InTransitInventorySyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"INTRANSIT_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("在途库存同步开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@BatchNo", batchNo);
            spParams.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_SyncInTransitInventory",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS,
                commandTimeout: CommandTimeoutSeconds);

            stopwatch.Stop();

            var result = new InTransitInventorySyncResultDto
            {
                BatchNo = batchNo,
                RowsAffected = spParams.Get<int>("@RowsAffected"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "在途库存同步完成: BatchNo={BatchNo}, 耗时={Elapsed}ms, 影响行数={RowsAffected}",
                    batchNo, stopwatch.ElapsedMilliseconds, result.RowsAffected);
            }
            else
            {
                _logger.LogError(
                    "在途库存同步SP返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "在途库存同步异常: BatchNo={BatchNo}, 耗时={Elapsed}ms",
                batchNo, stopwatch.ElapsedMilliseconds);

            return new InTransitInventorySyncResultDto
            {
                BatchNo = batchNo,
                RowsAffected = 0,
                ErrorMessage = ex.Message
            };
        }
    }
}
