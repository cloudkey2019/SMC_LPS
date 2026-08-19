using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 库存快照同步服务（2号位职责 — 每日 00:35）
///
/// 调用 sp_SyncInventorySnapshot 六步 ETL，单事务完成：
///   Step 1-2: TRUNCATE+INSERT 全量刷新 InventoryFact_ERP / InventoryFact_MES
///   Step 3:   通过 MaterialMapping 桥接生成 InventorySupplyCandidate（白名单模式）
///   Step 4:   InventoryAvailabilityRule 规则裁决（胜出规则模式）
///   Step 5:   InventoryAvailableSupplyDetail → InventoryBalance 汇总
///   Step 6:   写入 ETL 成功日志
///
/// SP 契约：DDL v5.0.39 / v5.0.40
/// </summary>
public class InventorySyncService : IInventorySyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<InventorySyncService> _logger;

    private const int CommandTimeoutSeconds = 600;

    public InventorySyncService(
        DatabaseConnectionManager connectionManager,
        ILogger<InventorySyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InventorySyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"INVENTORY_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("库存快照同步开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@BatchNo", batchNo);
            spParams.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_SyncInventorySnapshot",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS,
                commandTimeout: CommandTimeoutSeconds);

            stopwatch.Stop();

            var result = new InventorySyncResultDto
            {
                BatchNo = batchNo,
                BalanceRows = spParams.Get<int>("@RowsAffected"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "库存快照同步完成: BatchNo={BatchNo}, 耗时={Elapsed}ms, InventoryBalance行数={BalanceRows}",
                    batchNo, stopwatch.ElapsedMilliseconds, result.BalanceRows);
            }
            else
            {
                _logger.LogError(
                    "库存快照同步SP返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            await LogETLAsync(batchNo, "InventorySync.CSharp",
                $"BalanceRows={result.BalanceRows}, Elapsed={stopwatch.ElapsedMilliseconds}ms",
                result.IsSuccess ? "SUCCESS" : "FAILED");

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "库存快照同步异常: BatchNo={BatchNo}", batchNo);

            await LogETLAsync(batchNo, "InventorySync.CSharp",
                $"库存快照同步异常: {ex.Message}", "FAILED");

            throw;
        }
    }

    private async Task LogETLAsync(string batchNo, string step, string message, string status)
    {
        try
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
                  VALUES (@BatchNo, @Step, @Message, @Status, GETDATE())",
                new { BatchNo = batchNo, Step = step, Message = message, Status = status },
                db: DatabaseId.APS);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入 ETL 日志失败（非致命）");
        }
    }
}
