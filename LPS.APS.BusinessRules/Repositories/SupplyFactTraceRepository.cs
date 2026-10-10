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
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null)
    {
        // Dapper 列表参数只能出现在 IN 内；@AllowedFactories IS NULL(标量) 会随 @AllowedFactories 一起扩成 (@p1,@p2)
        // → "(@p1,@p2) IS NULL" 非法 SQL 4145。用 Has 标志替代标量判空；空集 fail-closed 返空，避免空集传入 IN ()。
        if (allowedFactories is { Count: 0 })
            return new List<SupplyFactTraceDto>();
        var hasFactories = allowedFactories is { Count: > 0 };

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
    AND (@HasFactories = 0 OR FactoryCode IN @AllowedFactories)
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
            HasFactories = hasFactories,
            AllowedFactories = allowedFactories,
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
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null)
    {
        // Dapper 列表参数只进 IN；@AllowedFactories IS NULL 标量判空会随列表一起扩成 (…) 导致 4145，用 Has 标志替代；空集 fail-closed 返空。
        if (allowedFactories is { Count: 0 })
            return new List<SupplyFactTraceDto>();
        var hasFactories = allowedFactories is { Count: > 0 };

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
    AND (@HasFactories = 0 OR FactoryCode IN @AllowedFactories)
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
            AllowedFactories = allowedFactories,
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
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null)
    {
        var allFacts = new List<SupplyFactTraceDto>();

        // SupplyFact_Pipeline（采购/Transit/VMI/已到厂未入库）
        if (string.IsNullOrEmpty(sourceType) || sourceType == "SUPPLY_PIPELINE")
        {
            var pipelineFacts = await QueryPipelineAsync(
                materialCode, materialId, factoryCode, supplyType, sourceDocumentNo,
                activeOnly, take: take, ct: ct, allowedFactories: allowedFactories);
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
                activeOnly, take: take, ct: ct, allowedFactories: allowedFactories);
            allFacts.AddRange(receivedFacts);
        }

        return allFacts
            .OrderByDescending(f => f.SyncedAt ?? f.LastReceivedAt ?? f.SourceUpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToList();
    }
}
