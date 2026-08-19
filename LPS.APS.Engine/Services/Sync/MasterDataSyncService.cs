using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 主数据双源三表协同同步服务（2号位职责 — §2.4.3）
/// 
/// 调用 sp_SyncMasterData 存储过程，完成：
///   Material（物料主身份）→ MaterialMapping（来源映射 SCD Type 2）→ MaterialSupplyContext（仓库级供给 SCD Type 2）
/// 
/// 双源同构契约：
///   - ERP 和 MES 两个视图字段完全一致
///   - SP 逻辑零分叉，仅 @SourceType 参数区分
///   - MaterialType 由 APS 按 MaterialCode 前缀统一推导
/// 
/// 架构红线：
///   ❌ 禁止每天全量删除重建 Material 表
///   ✅ 增量 Upsert，物料ID稳定
///   ✅ 基于 MaterialMapping.IsCurrent=1 的记录同步
/// </summary>
public class MasterDataSyncService : IMasterDataSyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<MasterDataSyncService> _logger;

    public MasterDataSyncService(
        DatabaseConnectionManager connectionManager,
        ILogger<MasterDataSyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MasterDataSyncResultDto> SyncERPMasterDataAsync(CancellationToken cancellationToken = default)
    {
        return await SyncMasterDataAsync("ERP");
    }

    /// <inheritdoc />
    public async Task<MasterDataSyncResultDto> SyncMESMasterDataAsync(CancellationToken cancellationToken = default)
    {
        return await SyncMasterDataAsync("MES");
    }

    /// <summary>
    /// 统一调用 sp_SyncMasterData 存储过程
    /// </summary>
    private async Task<MasterDataSyncResultDto> SyncMasterDataAsync(string sourceType)
    {
        var batchNo = $"DAILY_{sourceType}_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("主数据同步开始: SourceType={SourceType}, BatchNo={BatchNo}", sourceType, batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@SourceType", sourceType);
            spParams.Add("@BatchNo", batchNo);
            spParams.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            // ⚠️ sp_SyncMasterData 处理主数据同步，可能数据量大，设置 5 分钟超时
            var connection = await _connectionManager.GetConnectionAsync(DatabaseId.APS);
            try
            {
                await connection.ExecuteAsync(
                    "sp_SyncMasterData",
                    spParams,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 300); // 5 分钟超时
            }
            finally
            {
                _connectionManager.ReleaseConnection(DatabaseId.APS);
            }

            var rowsAffected = spParams.Get<int>("@RowsAffected");
            var errorMessage = spParams.Get<string?>("@ErrorMessage");

            stopwatch.Stop();

            var result = new MasterDataSyncResultDto
            {
                SourceType = sourceType,
                BatchNo = batchNo,
                RowsAffected = rowsAffected,
                ErrorMessage = errorMessage
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "主数据同步完成: SourceType={SourceType}, BatchNo={BatchNo}, 影响行数={RowsAffected}, 耗时={Elapsed}ms",
                    sourceType, batchNo, rowsAffected, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogError(
                    "主数据同步SP返回错误: SourceType={SourceType}, BatchNo={BatchNo}, Error={ErrorMessage}",
                    sourceType, batchNo, errorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "主数据同步异常: SourceType={SourceType}, BatchNo={BatchNo}", sourceType, batchNo);
            throw;
        }
    }
}
