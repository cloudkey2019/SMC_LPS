using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// Supply/Pegging Trace供需追溯控制器（5号位提供给4号位）
///
/// 路由规范：
///   GET /api/pegging-trace?planVersionId=              - Pegging分配列表查询
///   GET /api/pegging-trace/order/{orderId}?planVersionId=  - 按订单查询Pegging分配
///   GET /api/pegging-trace/summary?planVersionId=      - Pegging Trace汇总
///
/// 【职责边界】
/// - 5号位提供只读查询接口，直接读取PeggingSupplyAllocation表
/// - 不重算Allocation，不修改分配结果
/// </summary>
[ApiController]
[Route("api/pegging-trace")]
public class PeggingTraceController : ControllerBase
{
    private readonly PeggingTraceService _service;
    private readonly ILogger<PeggingTraceController> _logger;

    public PeggingTraceController(
        PeggingTraceService service,
        ILogger<PeggingTraceController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询Pegging分配列表
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<List<PeggingTraceDto>>> Query(
        [FromQuery] int planVersionId,
        [FromQuery] string? materialCode = null,
        [FromQuery] string? supplyType = null,
        [FromQuery] string? commitmentStatus = null,
        [FromQuery] string? orderNo = null,
        [FromQuery] string? supplyDocumentNo = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.QueryAsync(
                planVersionId, materialCode, supplyType, commitmentStatus,
                orderNo, supplyDocumentNo, skip, take, cancellationToken);

            return ApiResponse<List<PeggingTraceDto>>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<List<PeggingTraceDto>>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query pegging trace");
            return ApiResponse<List<PeggingTraceDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 按订单查询Pegging分配
    /// </summary>
    [HttpGet("order/{orderId:long}")]
    public async Task<ApiResponse<List<PeggingTraceDto>>> QueryByOrder(
        long orderId,
        [FromQuery] int planVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.QueryByOrderAsync(planVersionId, orderId, cancellationToken);
            return ApiResponse<List<PeggingTraceDto>>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<List<PeggingTraceDto>>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query pegging trace by order");
            return ApiResponse<List<PeggingTraceDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询Pegging Trace汇总
    /// </summary>
    [HttpGet("summary")]
    public async Task<ApiResponse<PeggingTraceSummaryDto>> GetSummary(
        [FromQuery] int planVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetSummaryAsync(planVersionId, cancellationToken);
            return ApiResponse<PeggingTraceSummaryDto>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<PeggingTraceSummaryDto>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pegging trace summary");
            return ApiResponse<PeggingTraceSummaryDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
