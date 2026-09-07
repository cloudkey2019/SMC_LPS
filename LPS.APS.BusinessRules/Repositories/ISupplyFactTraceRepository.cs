using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// 供应事实原始追溯Repository接口
/// 从SupplyFact_Pipeline和ext_ERP_Received_ByDocument_View聚合查询
/// </summary>
public interface ISupplyFactTraceRepository
{
    /// <summary>
    /// 查询SupplyFact_Pipeline（采购/Transit/VMI/已到厂未入库）
    /// </summary>
    Task<List<SupplyFactTraceDto>> QueryPipelineAsync(
        string? materialCode = null,
        int? materialId = null,
        string? factoryCode = null,
        string? supplyType = null,
        string? sourceDocumentNo = null,
        bool activeOnly = true,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// 查询Received事实（SH/PI Received）
    /// </summary>
    Task<List<SupplyFactTraceDto>> QueryReceivedAsync(
        string? materialCode = null,
        int? materialId = null,
        string? factoryCode = null,
        string? documentType = null,
        string? documentNo = null,
        bool activeOnly = true,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// 聚合查询所有供应事实
    /// </summary>
    Task<List<SupplyFactTraceDto>> QueryAllAsync(
        string? sourceType = null,
        string? materialCode = null,
        int? materialId = null,
        string? factoryCode = null,
        string? supplyType = null,
        string? sourceDocumentNo = null,
        bool activeOnly = true,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);
}
