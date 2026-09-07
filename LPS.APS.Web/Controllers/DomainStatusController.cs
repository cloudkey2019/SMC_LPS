using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 排程域状态查询控制器（G8：5号位中转，3号位真源）
///
/// 职责边界（0号位裁定）：
/// - 5号位提供前端接口接入
/// - 3号位RunLifecycleService提供状态真源
/// - 5号位不自行聚合状态
///
/// 路由规范：
///   GET /api/domain-status/{scheduleRunId}  - 查询排程域状态
/// </summary>
[ApiController]
[Route("api/domain-status")]
public class DomainStatusController : ControllerBase
{
    private readonly IRunLifecycleService _runLifecycleService;
    private readonly ILogger<DomainStatusController> _logger;

    public DomainStatusController(
        IRunLifecycleService runLifecycleService,
        ILogger<DomainStatusController> logger)
    {
        _runLifecycleService = runLifecycleService ?? throw new ArgumentNullException(nameof(runLifecycleService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询排程域状态（G8）
    /// </summary>
    /// <param name="scheduleRunId">排程运行ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    [HttpGet("{scheduleRunId:int}")]
    public async Task<ApiResponse<IReadOnlyList<RunDomainStatusDto>>> GetRunDomainStatus(
        int scheduleRunId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _runLifecycleService.GetRunDomainStatusAsync(scheduleRunId, cancellationToken);
            return ApiResponse<IReadOnlyList<RunDomainStatusDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get domain status for ScheduleRun {ScheduleRunId}", scheduleRunId);
            return ApiResponse<IReadOnlyList<RunDomainStatusDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
