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
[Authorize(Policy = PermissionCodes.PlanView)]
[ApiController]
[Route("api/pegging-trace")]
public class PeggingTraceController : ControllerBase
{
    private readonly PeggingTraceService _service;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<PeggingTraceController> _logger;

    public PeggingTraceController(
        PeggingTraceService service,
        IDataScopeService dataScopeService,
        ILogger<PeggingTraceController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>解析当前登录用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

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
            var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), cancellationToken);

            // 过滤：Pegging Trace 主对象 = Demand，Factory Scope = DemandFactoryCode（31-0 Q2 裁决）。
            // 空参按授权范围收窄结果集，避免越权返回全域数据。
            var allowedFactories = scope.GetValues(DataScopeTypes.Factory);

            var result = await _service.QueryAsync(
                planVersionId, materialCode, supplyType, commitmentStatus,
                orderNo, supplyDocumentNo, skip, take, cancellationToken,
                allowedFactories);

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
