using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
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
[Authorize(Policy = PermissionCodes.PlanView)]
[ApiController]
[Route("api/order-query")]
public class OrderQueryController : ControllerBase
{
    private readonly OrderQueryService _service;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<OrderQueryController> _logger;

    public OrderQueryController(
        OrderQueryService service,
        IDataScopeService dataScopeService,
        ILogger<OrderQueryController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>解析当前登录用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

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
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 分页换算：兼容前端 pageIndex/pageSize（1 起）与后端 skip/take（0 起）。
            // pageIndex/pageSize 优先；均未传时回退 skip/take 默认。
            if (pageSize > 0) take = pageSize;
            if (pageIndex > 0)
            {
                skip = (pageIndex - 1) * take;
            }
            if (skip < 0) skip = 0;
            if (take < 1) take = 50;

            var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), cancellationToken);

            // 校验：非空入参必须落在授权范围（Global 全放行，无范围全拒绝）
            if (!string.IsNullOrEmpty(factoryCode) && !scope.Allows(DataScopeTypes.Factory, factoryCode))
                return ApiResponse<List<OrderListItemDto>>.Fail(403, "Factory 范围越界");
            if (!string.IsNullOrEmpty(domainKey) && !scope.Allows(DataScopeTypes.Domain, domainKey))
                return ApiResponse<List<OrderListItemDto>>.Fail(403, "Domain 范围越界");

            // 过滤：空参按授权范围收窄结果集，避免越权返回全域数据
            var allowedFactories = scope.GetValues(DataScopeTypes.Factory);
            var allowedDomains = scope.GetValues(DataScopeTypes.Domain);

            var result = await _service.QueryOrdersAsync(
                planVersionId, orderNo, materialCode, customerName,
                factoryCode, domainKey, delayStatus, status,
                skip, take, cancellationToken,
                allowedFactories, allowedDomains);

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

    /// <summary>
    /// 查询订单状态汇总（按 DelayStatus 聚合，供 Order.vue 顶部 KPI）
    /// </summary>
    [HttpGet("summary")]
    public async Task<ApiResponse<OrderSummaryDto>> GetSummary(
        [FromQuery] int planVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), cancellationToken);
            var allowedFactories = scope.GetValues(DataScopeTypes.Factory);
            var allowedDomains = scope.GetValues(DataScopeTypes.Domain);

            var result = await _service.GetSummaryAsync(
                planVersionId, allowedFactories, allowedDomains, cancellationToken);

            return ApiResponse<OrderSummaryDto>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<OrderSummaryDto>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order summary");
            return ApiResponse<OrderSummaryDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
