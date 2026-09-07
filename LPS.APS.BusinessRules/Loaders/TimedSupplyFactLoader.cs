using Dapper;
using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;

namespace LPS.APS.BusinessRules.Loaders;

/// <summary>
/// Timed Supply事实加载器（采购/VMI供给）
/// 从SupplyFact_Pipeline装载原始采购事实
/// 职责边界：5号位仅负责加载Raw事实，不负责SupplyPool集成（2号位职责）
/// </summary>
public class TimedSupplyFactLoader : ITimedSupplyFactLoader
{
    private readonly DatabaseConnectionManager _connectionManager;

    public TimedSupplyFactLoader(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    /// <summary>
    /// 加载原始采购事实
    /// </summary>
    /// <param name="scope">供给事实范围（物料ID列表、工厂ID列表）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>原始采购事实列表</returns>
    public async Task<IReadOnlyList<RawProcurementFact>> LoadRawFactsAsync(
        SupplyFactScope scope,
        CancellationToken ct)
    {
        var sql = @"
            SELECT
                sfp.MaterialCode,
                sfp.MaterialId,
                sfp.FactoryId,
                sfp.FactoryCode,
                sfp.Quantity AS RemainingQty,
                sfp.ETA AS Eta,
                sfp.ReleaseDate,
                sfp.StorageCode,
                sfp.SupplyType,
                sfp.CommitmentStatus,
                sfp.SourceDocumentNo,
                sfp.SourceDocumentNo AS PhysicalSourceKey,
                sfp.SourceDocumentLineNo,
                sfp.SourceUpdatedAt
            FROM SupplyFact_Pipeline sfp
            WHERE sfp.IsActive = 1
              AND sfp.Quantity > 0
              AND sfp.SupplyType IN ('PURCHASE_IN_TRANSIT', 'OPEN_PO_REMAINING',
                                      'ARRIVED_NOT_RECEIVED')
              AND (@MaterialIds IS NULL OR sfp.MaterialId IN @MaterialIds)
              AND (@FactoryIds IS NULL OR sfp.FactoryId IN @FactoryIds)
            ORDER BY sfp.MaterialId, sfp.FactoryId, sfp.AvailableTime";

        var parameters = new
        {
            MaterialIds = scope.MaterialIds?.Count > 0 ? scope.MaterialIds : null,
            FactoryIds = scope.FactoryIds?.Count > 0 ? scope.FactoryIds : null
        };

        var rows = await _connectionManager.QueryAsync<RawProcurementFact>(
            sql, parameters, db: DatabaseId.APS);

        return rows.ToList();
    }
}
