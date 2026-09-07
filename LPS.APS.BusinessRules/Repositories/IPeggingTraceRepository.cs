using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// Pegging Trace查询Repository接口
/// </summary>
public interface IPeggingTraceRepository
{
    /// <summary>
    /// 查询Pegging分配列表
    /// </summary>
    Task<List<PeggingTraceDto>> QueryAsync(
        int planVersionId,
        string? materialCode = null,
        string? supplyType = null,
        string? commitmentStatus = null,
        string? orderNo = null,
        string? supplyDocumentNo = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// 按订单查询Pegging分配
    /// </summary>
    Task<List<PeggingTraceDto>> QueryByOrderAsync(
        int planVersionId,
        long orderId,
        CancellationToken ct = default);

    /// <summary>
    /// 查询Pegging Trace汇总
    /// </summary>
    Task<PeggingTraceSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken ct = default);
}
