using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// Demand Protection查看控制器（5号位提供给4号位）
///
/// 路由规范：
///   GET /api/demand-protection                      - Demand Protection列表查询
///   GET /api/demand-protection/summary              - Demand Protection汇总
///
/// 【职责边界】
/// - 查看：5号位自己实现（直接查库）
/// - 释放：必须通过2号位Application Service（暂未实现）
/// </summary>
[ApiController]
[Route("api/demand-protection")]
public class DemandProtectionController : ControllerBase
{
    private readonly DemandProtectionService _service;
    private readonly ILogger<DemandProtectionController> _logger;

    public DemandProtectionController(
        DemandProtectionService service,
        ILogger<DemandProtectionController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询Demand Protection列表
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<List<DemandProtectionDto>>> Query(
        [FromQuery] string? demandKey = null,
        [FromQuery] string? supplyKey = null,
        [FromQuery] string? lockType = null,
        [FromQuery] string? status = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.QueryAsync(
                demandKey, supplyKey, lockType, status, skip, take, cancellationToken);

            return ApiResponse<List<DemandProtectionDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query demand protection");
            return ApiResponse<List<DemandProtectionDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询Demand Protection汇总
    /// </summary>
    [HttpGet("summary")]
    public async Task<ApiResponse<DemandProtectionSummaryDto>> GetSummary(
        [FromQuery] string? demandKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetSummaryAsync(demandKey, cancellationToken);
            return ApiResponse<DemandProtectionSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get demand protection summary");
            return ApiResponse<DemandProtectionSummaryDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
