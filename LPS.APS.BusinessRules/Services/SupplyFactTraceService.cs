using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// 供应事实原始追溯业务服务（5号位提供给4号位）
///
/// 用于页面查看Procurement/VMI/Received/Transit原始供应事实
/// 来源：SupplyFact_Pipeline + ext_ERP_Received_ByDocument_View
/// </summary>
public class SupplyFactTraceService
{
    private readonly ISupplyFactTraceRepository _repository;

    public SupplyFactTraceService(ISupplyFactTraceRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 聚合查询所有供应事实
    /// </summary>
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
        if (take <= 0 || take > 500) take = 100;

        return await _repository.QueryAllAsync(
            sourceType, materialCode, materialId, factoryCode,
            supplyType, sourceDocumentNo, activeOnly, skip, take, ct);
    }
}
