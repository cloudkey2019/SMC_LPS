using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 订单验证与提升服务
/// 调用 sp_ValidateAndPromoteOrders 存储过程，将 ERP_Order_Staging 中 PENDING 记录
/// 校验后提升到 Order_Canonical
/// 
/// 状态机：PENDING → VALIDATED → PROCESSED（成功） / PENDING → FAILED（校验失败）
/// Upsert键：OrderNo（SourceOrderId 跨出荷/生产指示可重复，不能做唯一键）
/// </summary>
public class OrderPromotionService : IOrderPromotionService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<OrderPromotionService> _logger;

    public OrderPromotionService(
        DatabaseConnectionManager connectionManager,
        ILogger<OrderPromotionService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(int PromotedCount, int FailedCount)> ValidateAndPromoteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new DynamicParameters();
            parameters.Add("@PromotedCount", dbType: DbType.Int32, direction: ParameterDirection.Output);
            parameters.Add("@FailedCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_ValidateAndPromoteOrders",
                parameters,
                CommandType.StoredProcedure,
                DatabaseId.APS);

            var promoted = parameters.Get<int>("@PromotedCount");
            var failed = parameters.Get<int>("@FailedCount");

            if (failed > 0)
            {
                _logger.LogWarning("订单校验失败: {Failed} 条", failed);
                await LogFailedStagingRecordsAsync();
            }

            return (promoted, failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "sp_ValidateAndPromoteOrders 执行失败");
            throw;
        }
    }

    /// <summary>
    /// 记录Staging中校验失败的记录详情（用于问题排查）
    /// </summary>
    private async Task LogFailedStagingRecordsAsync()
    {
        try
        {
            var sql = @"
                SELECT TOP 50 
                    SourceOrderId, OrderNo, MaterialCode, BOMNO, 
                    ErrorMessage, SyncedAt
                FROM ERP_Order_Staging
                WHERE SyncStatus = 'FAILED'
                  AND SyncedAt >= DATEADD(HOUR, -2, GETDATE())
                ORDER BY SyncedAt DESC";

            var failedRecords = await _connectionManager.QueryAsync<dynamic>(sql, db: DatabaseId.APS);

            foreach (var record in failedRecords)
            {
                string srcId = record.SourceOrderId;
                string orderNo = record.OrderNo;
                string matCode = record.MaterialCode;
                string bomNo = record.BOMNO;
                string? errMsg = record.ErrorMessage;
                _logger.LogWarning(
                    "Staging校验失败: SourceOrderId={SourceOrderId}, OrderNo={OrderNo}, " +
                    "MaterialCode={MaterialCode}, BOMNO={BOMNO}, Error={Error}",
                    srcId, orderNo, matCode, bomNo, errMsg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "查询Staging失败记录详情时出错（非致命）");
        }
    }
}
