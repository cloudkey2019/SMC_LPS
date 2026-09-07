using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// Pegging Trace查询业务服务
///
/// 5号位提供给4号位的供需分配追溯查询
/// 直接读取PeggingSupplyAllocation表，不重算Allocation
/// </summary>
public class PeggingTraceService
{
    private readonly IPeggingTraceRepository _repository;

    public PeggingTraceService(IPeggingTraceRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 查询Pegging分配列表
    /// </summary>
    public async Task<List<PeggingTraceDto>> QueryAsync(
        int planVersionId,
        string? materialCode = null,
        string? supplyType = null,
        string? commitmentStatus = null,
        string? orderNo = null,
        string? supplyDocumentNo = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        if (planVersionId <= 0) throw new ArgumentException("planVersionId is required");
        if (take <= 0 || take > 500) take = 100;

        return await _repository.QueryAsync(
            planVersionId, materialCode, supplyType, commitmentStatus,
            orderNo, supplyDocumentNo, skip, take, ct);
    }

    /// <summary>
    /// 按订单查询Pegging分配
    /// </summary>
    public async Task<List<PeggingTraceDto>> QueryByOrderAsync(
        int planVersionId,
        long orderId,
        CancellationToken ct = default)
    {
        if (planVersionId <= 0) throw new ArgumentException("planVersionId is required");
        if (orderId <= 0) throw new ArgumentException("orderId is required");

        return await _repository.QueryByOrderAsync(planVersionId, orderId, ct);
    }

    /// <summary>
    /// 查询Pegging Trace汇总
    /// </summary>
    public async Task<PeggingTraceSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken ct = default)
    {
        if (planVersionId <= 0) throw new ArgumentException("planVersionId is required");

        return await _repository.GetSummaryAsync(planVersionId, ct);
    }
}
