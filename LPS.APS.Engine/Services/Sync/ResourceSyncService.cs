using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 资源主数据同步服务实现（2号位职责 — 每日 00:15）
///
/// 调用 sp_SyncResourceData 存储过程，在 APS 库本地完成：
///   1. 从 ext_MES_APS_Resource_View 拉取 MES 设备主数据快照
///   2. 双字典映射：FactoryCode → Factory.Id + ProductionDeptCode → ProductionDepartment.Id
///   3. MERGE 新增/更新到 Resource 表（映射失败行跳过并登记日志）
///
/// SP 契约：APS_资源同步DDL补充_v1.0.sql
/// </summary>
public class ResourceSyncService : IResourceSyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<ResourceSyncService> _logger;

    public ResourceSyncService(
        DatabaseConnectionManager connectionManager,
        ILogger<ResourceSyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ResourceSyncResultDto> SyncAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"RESOURCE_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("资源主数据同步开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@SourceType", "MES");
            spParams.Add("@BatchNo", batchNo);
            spParams.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@Skipped", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_SyncResourceData",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS);

            stopwatch.Stop();

            var result = new ResourceSyncResultDto
            {
                BatchNo = batchNo,
                RowsAffected = spParams.Get<int>("@RowsAffected"),
                Skipped = spParams.Get<int>("@Skipped"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "资源主数据同步完成: BatchNo={BatchNo}, 耗时={Elapsed}ms | 影响行数={Rows}, 跳过={Skipped}",
                    batchNo, stopwatch.ElapsedMilliseconds,
                    result.RowsAffected, result.Skipped);

                if (result.Skipped > 0)
                {
                    _logger.LogWarning(
                        "资源主数据同步: 有 {Skipped} 行因 FactoryCode/ProductionDeptCode 映射失败被跳过，" +
                        "请检查 Factory 和 ProductionDepartment 字典表数据是否完整",
                        result.Skipped);
                }
            }
            else
            {
                _logger.LogError(
                    "资源主数据同步SP返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            await LogETLAsync(batchNo, "ResourceSync",
                $"Rows={result.RowsAffected}, Skipped={result.Skipped}, Elapsed={stopwatch.ElapsedMilliseconds}ms",
                result.IsSuccess ? "SUCCESS" : "FAILED");

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "资源主数据同步异常: BatchNo={BatchNo}", batchNo);

            await LogETLAsync(batchNo, "ResourceSync",
                $"资源主数据同步异常: {ex.Message}", "FAILED");

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
