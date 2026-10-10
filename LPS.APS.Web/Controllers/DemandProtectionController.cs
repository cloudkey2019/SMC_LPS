using Microsoft.AspNetCore.Authorization;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// Demand Protection控制器（5号位提供给4号位）
///
/// 路由规范：
///   GET  /api/demand-protection                      - Demand Protection列表查询
///   GET  /api/demand-protection/summary              - Demand Protection汇总
///   POST /api/demand-protection/release              - 释放Demand Protection
///
/// 【职责边界 - 2026-09-12】
/// - 查看：5号位自己实现（直接查库）
/// - 释放：逐lock处理，部分成功；Allocation不动，下一轮Pegging自然回流
///
/// 【架构】通过 IDemandProtectionAppService（Application 层）中转，
///  与 ScheduleController / GovernanceController 分层一致。
/// </summary>
[Authorize(Policy = PermissionCodes.PlanView)]
[ApiController]
[Route("api/demand-protection")]
public class DemandProtectionController : ControllerBase
{
    private readonly IDemandProtectionAppService _appService;
    private readonly ILogger<DemandProtectionController> _logger;

    public DemandProtectionController(
        IDemandProtectionAppService appService,
        ILogger<DemandProtectionController> logger)
    {
        _appService = appService ?? throw new ArgumentNullException(nameof(appService));
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
            var result = await _appService.QueryAsync(
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
            var result = await _appService.GetSummaryAsync(demandKey, cancellationToken);
            return ApiResponse<DemandProtectionSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get demand protection summary");
            return ApiResponse<DemandProtectionSummaryDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 释放Demand Protection（逐lock处理，部分成功）
    /// 通过 IDemandProtectionAppService 中转（5号位只做权限/Scope校验与中转）
    /// </summary>
    [Authorize(Policy = PermissionCodes.DemandProtectionRelease)]
    [HttpPost("release")]
    public async Task<ApiResponse<List<DemandProtectionReleaseResult>>> Release(
        [FromBody] DemandProtectionReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _appService.ReleaseLocksAsync(
                request.LockIds, request.ReleasedBy, request.ReleaseReason, cancellationToken);

            return ApiResponse<List<DemandProtectionReleaseResult>>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<List<DemandProtectionReleaseResult>>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release demand protection");
            return ApiResponse<List<DemandProtectionReleaseResult>>.Fail(500, $"Release failed: {ex.Message}");
        }
    }
}
