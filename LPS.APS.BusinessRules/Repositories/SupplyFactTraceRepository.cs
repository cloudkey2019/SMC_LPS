using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// 供应事实原始追溯Repository实现
/// 从SupplyFact_Pipeline和ext_ERP_Received_ByDocument_View聚合查询
/// </summary>
public class SupplyFactTraceRepository : ISupplyFactTraceRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public SupplyFactTraceRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<SupplyFactTraceDto>> QueryPipelineAsync(
        string? materialCode = null,
        int? materialId = null,
        string? factoryCode = null,
        string? supplyType = null,
        string? sourceDocumentNo = null,
        bool activeOnly = true,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT
    'SUPPLY_PIPELINE' AS SourceType,
    SupplyType,
    MaterialCode,
    MaterialId,
    FactoryCode,
    StorageCode AS WarehouseCode,
    Quantity,
    ETA AS Eta,
    ReleaseDate,
    AvailableTime,
    CommitmentStatus,
    SourceDocumentNo,
    SourceDocumentLineNo,
    SourceSystem,
    SourceUpdatedAt,
    CAST(IsActive AS BIT) AS IsActive,
    SyncedAt,
    NULL AS DocumentType,
    NULL AS LastReceivedAt
FROM SupplyFact_Pipeline
WHERE 1=1
    AND (@ActiveOnly = 0 OR IsActive = 1)
    AND (@MaterialCode IS NULL OR MaterialCode = @MaterialCode)
    AND (@MaterialId IS NULL OR MaterialId = @MaterialId)
    AND (@FactoryCode IS NULL OR FactoryCode = @FactoryCode)
    AND (@SupplyType IS NULL OR SupplyType = @SupplyType)
    AND (@SourceDocumentNo IS NULL OR SourceDocumentNo = @SourceDocumentNo)
ORDER BY SyncedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            ActiveOnly = activeOnly ? 1 : 0,
            MaterialCode = materialCode,
            MaterialId = materialId,
            FactoryCode = factoryCode,
            SupplyType = supplyType,
            SourceDocumentNo = sourceDocumentNo,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<SupplyFactTraceDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<List<SupplyFactTraceDto>> QueryReceivedAsync(
        string? materialCode = null,
        int? materialId = null,
        string? factoryCode = null,
        string? documentType = null,
        string? documentNo = null,
        bool activeOnly = true,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        // 从APS包装视图读取Received事实
        var sql = @"
SELECT
    'RECEIVED' AS SourceType,
    DocumentType AS SupplyType,
    MaterialCode,
    ISNULL(MasterID, 0) AS MaterialId,
    FactoryCode,
    WarehouseCode,
    ReceivedQty AS Quantity,
    NULL AS Eta,
    NULL AS ReleaseDate,
    NULL AS AvailableTime,
    NULL AS CommitmentStatus,
    DocumentNo AS SourceDocumentNo,
    NULL AS SourceDocumentLineNo,
    NULL AS SourceSystem,
    SourceUpdatedAt,
    CAST(IsActive AS BIT) AS IsActive,
    NULL AS SyncedAt,
    DocumentType,
    LastReceivedAt
FROM ext_ERP_Received_ByDocument_View
WHERE 1=1
    AND (@ActiveOnly = 0 OR IsActive = 1)
    AND (@MaterialCode IS NULL OR MaterialCode = @MaterialCode)
    AND (@MaterialId IS NULL OR MasterID = @MaterialId)
    AND (@FactoryCode IS NULL OR FactoryCode = @FactoryCode)
    AND (@DocumentType IS NULL OR DocumentType = @DocumentType)
    AND (@DocumentNo IS NULL OR DocumentNo = @DocumentNo)
ORDER BY LastReceivedAt DESC
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            ActiveOnly = activeOnly ? 1 : 0,
            MaterialCode = materialCode,
            MaterialId = materialId,
            FactoryCode = factoryCode,
            DocumentType = documentType,
            DocumentNo = documentNo,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<SupplyFactTraceDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<List<SupplyFactTraceDto>> QueryAllAsync(
        string? sourceType = null,
        string? materialCode = null,
        int? materialId = null,
        string? factoryCode = null,
        string? supplyType = null,
        string? sourceDocumentNo = null,
        bool activeOnly = true,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        var allFacts = new List<SupplyFactTraceDto>();

        // SupplyFact_Pipeline（采购/Transit/VMI/已到厂未入库）
        if (string.IsNullOrEmpty(sourceType) || sourceType == "SUPPLY_PIPELINE")
        {
            var pipelineFacts = await QueryPipelineAsync(
                materialCode, materialId, factoryCode, supplyType, sourceDocumentNo,
                activeOnly, take: take, ct: ct);
            allFacts.AddRange(pipelineFacts);
        }

        // Received事实
        if (string.IsNullOrEmpty(sourceType) || sourceType == "RECEIVED")
        {
            // 将supplyType映射到documentType
            string? documentType = null;
            if (!string.IsNullOrEmpty(supplyType))
            {
                documentType = supplyType switch
                {
                    "SHIPPING_INSTRUCTION" => "SHIPPING_INSTRUCTION",
                    "PRODUCTION_INSTRUCTION" => "PRODUCTION_INSTRUCTION",
                    _ => null
                };
            }

            var receivedFacts = await QueryReceivedAsync(
                materialCode, materialId, factoryCode, documentType, sourceDocumentNo,
                activeOnly, take: take, ct: ct);
            allFacts.AddRange(receivedFacts);
        }

        return allFacts
            .OrderByDescending(f => f.SyncedAt ?? f.LastReceivedAt ?? f.SourceUpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
    }
}
