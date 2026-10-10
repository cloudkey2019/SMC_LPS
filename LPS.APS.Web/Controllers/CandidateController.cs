using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LPS.APS.Core.Authorization;
using LPS.APS.Application.Services.Query;
using LPS.APS.Application.Services.Query.Dto;
using LPS.APS.Shared.Models;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 白天候选统一入口 API（E：PM 2026-09-23 裁决 Candidate 统一入口；3号位 Web 层薄封装）。
/// 只做读查询 + 委托 IScheduleQueryService，不触碰排程编排（编排仍归 2号位 SchedulingOrchestrator）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
[ApiController]
[Route("api/candidate")]
public class CandidateController : ControllerBase
{
    private readonly IScheduleQueryService _queryService;
    private readonly ILogger<CandidateController> _logger;

    public CandidateController(IScheduleQueryService queryService, ILogger<CandidateController> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>解析当前登录用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>候选运行摘要（E-1：Status/创建时间/订单数/Task 数）</summary>
    /// <remarks>开发者：3号位</remarks>
    [Authorize(Policy = PermissionCodes.CandidateView)]
    [HttpGet("{candidateId}/summary")]
    public async Task<IActionResult> GetSummary(int candidateId, CancellationToken ct)
    {
        try
        {
            var result = await _queryService.GetCandidateSummaryAsync(GetCurrentUserId(), candidateId, ct);
            return Ok(ApiResponse<CandidateSummaryDto>.Success(result));
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "候选运行摘要失败：{CandidateId}", candidateId);
            return NotFound(ApiResponse<CandidateSummaryDto>.Fail(404, ex.Message));
        }
    }

    /// <summary>候选 vs 基础差异（E-1：最小集 addedOrders/removedOrders/changedTasks）</summary>
    /// <remarks>开发者：3号位</remarks>
    [Authorize(Policy = PermissionCodes.CandidateView)]
    [HttpGet("{candidateId}/comparison")]
    public async Task<IActionResult> GetComparison(int candidateId, CancellationToken ct)
    {
        try
        {
            var result = await _queryService.GetCandidateDiffAsync(GetCurrentUserId(), candidateId, ct);
            return Ok(ApiResponse<CandidateDiffDto>.Success(result));
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "候选差异查询失败：{CandidateId}", candidateId);
            return NotFound(ApiResponse<CandidateDiffDto>.Fail(404, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "候选差异查询失败：{CandidateId}", candidateId);
            return BadRequest(ApiResponse<CandidateDiffDto>.Fail(400, ex.Message));
        }
    }

    /// <summary>候选加速影响范围（E-2：宿主先行骨架端点；P1-2 数据就绪后填充真实影响范围）</summary>
    /// <remarks>开发者：3号位</remarks>
    [Authorize(Policy = PermissionCodes.CandidateView)]
    [HttpGet("{candidateId}/expedite-impact")]
    public IActionResult GetExpediteImpact(int candidateId)
    {
        // E-2：骨架占位——路由先行；真实影响范围（expeditedOrder/affectedOrders/protectedOrders/changes）待 P1-2 数据链路就绪后填充。
        return Ok(ApiResponse<object>.Success(
            new { CandidateId = candidateId, Ready = false, Message = "expedite-impact 待 P1-2 数据就绪后填充" },
            "expedite-impact 宿主已就绪（骨架）"));
    }
}