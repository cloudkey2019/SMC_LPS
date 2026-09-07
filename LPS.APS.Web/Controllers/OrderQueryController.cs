using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 订单/需求计划查询控制器（5号位提供给4号位）
///
/// 路由规范：
///   GET /api/order-query?planVersionId=             - 订单列表查询
///   GET /api/order-query/{orderId}?planVersionId=   - 订单详情（含Pegging+生产计划）
///
/// 【职责边界】
/// - 5号位提供只读查询接口，直接读取APS事实表
/// - 不重算Pegging，不修改订单状态
/// </summary>
[ApiController]
[Route("api/order-query")]
public class OrderQueryController : ControllerBase
{
    private readonly OrderQueryService _service;
    private readonly ILogger<OrderQueryController> _logger;

    public OrderQueryController(
        OrderQueryService service,
        ILogger<OrderQueryController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询订单列表
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<List<OrderListItemDto>>> QueryOrders(
        [FromQuery] int planVersionId,
        [FromQuery] string? orderNo = null,
        [FromQuery] string? materialCode = null,
        [FromQuery] string? customerName = null,
        [FromQuery] string? factoryCode = null,
        [FromQuery] string? domainKey = null,
        [FromQuery] string? delayStatus = null,
        [FromQuery] string? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.QueryOrdersAsync(
                planVersionId, orderNo, materialCode, customerName,
                factoryCode, domainKey, delayStatus, status,
                skip, take, cancellationToken);

            return ApiResponse<List<OrderListItemDto>>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<List<OrderListItemDto>>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query orders");
            return ApiResponse<List<OrderListItemDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询订单详情（含Pegging承接+生产计划）
    /// </summary>
    [HttpGet("{orderId:long}")]
    public async Task<ApiResponse<OrderDetailDto>> GetOrderDetail(
        long orderId,
        [FromQuery] int planVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetOrderDetailAsync(planVersionId, orderId, cancellationToken);
            if (result == null)
                return ApiResponse<OrderDetailDto>.Fail(404, "Order not found");

            return ApiResponse<OrderDetailDto>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<OrderDetailDto>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order detail");
            return ApiResponse<OrderDetailDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
