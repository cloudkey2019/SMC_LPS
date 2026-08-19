using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 管道供给同步服务（2号位职责 — 每日 00:55）
///
/// V1：调用 sp_SyncPipelineSupply，仅 TRUNCATE + SUCCESS 日志
/// V1.1：sp 改为从 ext_PipelineSupply_Source_View 全量装载后，此处无需改动
/// </summary>
public class PipelineSupplySyncService : IPipelineSupplySyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<PipelineSupplySyncService> _logger;

    private const int CommandTimeoutSeconds = 300;

    public PipelineSupplySyncService(
        DatabaseConnectionManager connectionManager,
        ILogger<PipelineSupplySyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PipelineSupplySyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"PIPELINE_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("管道供给同步开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@BatchNo", batchNo);
            spParams.Add("@DataCutoffTime", DateTime.Now);
            spParams.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_SyncPipelineSupply",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS,
                commandTimeout: CommandTimeoutSeconds);

            stopwatch.Stop();

            var result = new PipelineSupplySyncResultDto
            {
                BatchNo      = batchNo,
                RowsAffected = spParams.Get<int>("@RowsAffected"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "管道供给同步完成: BatchNo={BatchNo}, 耗时={Elapsed}ms, RowsAffected={Rows}",
                    batchNo, stopwatch.ElapsedMilliseconds, result.RowsAffected);
            }
            else
            {
                _logger.LogError(
                    "管道供给同步SP返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            await LogETLAsync(batchNo, "PipelineSupplySync.CSharp",
                $"RowsAffected={result.RowsAffected}, Elapsed={stopwatch.ElapsedMilliseconds}ms",
                result.IsSuccess ? "SUCCESS" : "FAILED");

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "管道供给同步异常: BatchNo={BatchNo}", batchNo);

            await LogETLAsync(batchNo, "PipelineSupplySync.CSharp",
                $"管道供给同步异常: {ex.Message}", "FAILED");

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
