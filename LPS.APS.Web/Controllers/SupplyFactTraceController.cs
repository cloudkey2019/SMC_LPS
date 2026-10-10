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
/// 供应事实原始追溯控制器（5号位提供给4号位）
///
/// 用于页面查看Procurement/VMI/Received/Transit原始供应事实
///
/// 路由规范：
///   GET /api/supply-fact-trace                  - 聚合查询所有供应事实
///
/// 【职责边界】
/// - 5号位提供原始供应事实查询
/// - 4号位用于Pegging行源事实查看
/// </summary>
[Authorize(Policy = PermissionCodes.PlanView)]
[ApiController]
[Route("api/supply-fact-trace")]
public class SupplyFactTraceController : ControllerBase
{
    private readonly SupplyFactTraceService _service;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<SupplyFactTraceController> _logger;

    public SupplyFactTraceController(
        SupplyFactTraceService service,
        IDataScopeService dataScopeService,
        ILogger<SupplyFactTraceController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>解析当前登录用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>
    /// 聚合查询所有供应事实
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<List<SupplyFactTraceDto>>> Query(
        [FromQuery] string? sourceType = null,
        [FromQuery] string? materialCode = null,
        [FromQuery] int? materialId = null,
        [FromQuery] string? factoryCode = null,
        [FromQuery] string? supplyType = null,
        [FromQuery] string? sourceDocumentNo = null,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), cancellationToken);

            // 校验：非空入参必须落在授权范围（Global 全放行，无范围全拒绝）
            if (!string.IsNullOrEmpty(factoryCode) && !scope.Allows(DataScopeTypes.Factory, factoryCode))
                return ApiResponse<List<SupplyFactTraceDto>>.Fail(403, "Factory 范围越界");

            // 过滤：空参按授权范围收窄结果集，避免越权返回全域数据
            var allowedFactories = scope.GetValues(DataScopeTypes.Factory);

            var result = await _service.QueryAllAsync(
                sourceType, materialCode, materialId, factoryCode,
                supplyType, sourceDocumentNo, activeOnly, skip, take, cancellationToken,
                allowedFactories);

            return ApiResponse<List<SupplyFactTraceDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query supply fact trace");
            return ApiResponse<List<SupplyFactTraceDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
