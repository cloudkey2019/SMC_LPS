using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// Overview概览页控制器（5号位提供给4号位）
///
/// 路由规范：
///   GET /api/overview/active-plan           - 当前ACTIVE计划版本信息
///   GET /api/overview/task-summary          - 任务状态摘要
///   GET /api/overview/resource-bottleneck   - 资源负荷/Bottleneck
///   GET /api/overview/candidate-summary     - Candidate待处理摘要
///
/// 【职责边界】
/// - 5号位提供只读查询接口，直接读取APS事实表
/// - 不重算业务结果
/// </summary>
[ApiController]
[Route("api/overview")]
public class OverviewController : ControllerBase
{
    private readonly OverviewQueryService _service;
    private readonly ILogger<OverviewController> _logger;

    public OverviewController(
        OverviewQueryService service,
        ILogger<OverviewController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询当前ACTIVE计划版本信息
    /// </summary>
    [HttpGet("active-plan")]
    public async Task<ApiResponse<OverviewActivePlanDto?>> GetActivePlan(
        [FromQuery] string? domainKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetActivePlanAsync(domainKey, cancellationToken);
            return ApiResponse<OverviewActivePlanDto?>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active plan");
            return ApiResponse<OverviewActivePlanDto?>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询任务状态摘要
    /// </summary>
    [HttpGet("task-summary")]
    public async Task<ApiResponse<OverviewTaskSummaryDto>> GetTaskSummary(
        [FromQuery] int planVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (planVersionId <= 0)
                return ApiResponse<OverviewTaskSummaryDto>.Fail(400, "planVersionId is required");

            var result = await _service.GetTaskSummaryAsync(planVersionId, cancellationToken);
            return ApiResponse<OverviewTaskSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get task summary");
            return ApiResponse<OverviewTaskSummaryDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询资源负荷/Bottleneck
    /// </summary>
    [HttpGet("resource-bottleneck")]
    public async Task<ApiResponse<List<OverviewResourceBottleneckDto>>> GetResourceBottleneck(
        [FromQuery] int planVersionId,
        [FromQuery] int topN = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (planVersionId <= 0)
                return ApiResponse<List<OverviewResourceBottleneckDto>>.Fail(400, "planVersionId is required");

            var result = await _service.GetResourceBottleneckAsync(planVersionId, topN, cancellationToken);
            return ApiResponse<List<OverviewResourceBottleneckDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resource bottleneck");
            return ApiResponse<List<OverviewResourceBottleneckDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询Candidate待处理摘要
    /// </summary>
    [HttpGet("candidate-summary")]
    public async Task<ApiResponse<OverviewCandidateSummaryDto>> GetCandidateSummary(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetCandidateSummaryAsync(cancellationToken);
            return ApiResponse<OverviewCandidateSummaryDto>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get candidate summary");
            return ApiResponse<OverviewCandidateSummaryDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
