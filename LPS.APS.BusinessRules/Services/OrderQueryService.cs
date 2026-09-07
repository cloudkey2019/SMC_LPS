using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// 订单查询业务服务
///
/// 5号位提供给4号位的订单/需求计划查询
/// 直接读取APS_Production事实表，不重算Pegging
/// </summary>
public class OrderQueryService
{
    private readonly IOrderQueryRepository _repository;

    public OrderQueryService(IOrderQueryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 查询订单列表
    /// </summary>
    public async Task<List<OrderListItemDto>> QueryOrdersAsync(
        int planVersionId,
        string? orderNo = null,
        string? materialCode = null,
        string? customerName = null,
        string? factoryCode = null,
        string? domainKey = null,
        string? delayStatus = null,
        string? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default)
    {
        if (planVersionId <= 0) throw new ArgumentException("planVersionId is required");
        if (take <= 0 || take > 200) take = 50;

        return await _repository.QueryOrdersAsync(
            planVersionId, orderNo, materialCode, customerName,
            factoryCode, domainKey, delayStatus, status,
            skip, take, ct);
    }

    /// <summary>
    /// 查询订单详情（含Pegging和生产计划）
    /// </summary>
    public async Task<OrderDetailDto?> GetOrderDetailAsync(
        int planVersionId,
        long orderId,
        CancellationToken ct = default)
    {
        if (planVersionId <= 0) throw new ArgumentException("planVersionId is required");
        if (orderId <= 0) throw new ArgumentException("orderId is required");

        return await _repository.GetOrderDetailAsync(planVersionId, orderId, ct);
    }
}
