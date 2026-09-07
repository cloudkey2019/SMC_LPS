using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 治理查询控制器（G4/G7：5号位中转，直接读取APS事实表）
///
/// 路由规范：
///   GET /api/governance-query/runs                    - 排程运行列表（G4）
///   GET /api/governance-query/domain-dependencies     - 排程域依赖（G7）
///
/// 【职责边界】
/// - 5号位提供只读查询接口
/// - 不修改、不重算业务结果
/// - ScheduleRun状态由2号位/3号位产生，5号位只读展示
/// </summary>
[ApiController]
[Route("api/governance-query")]
public class GovernanceQueryController : ControllerBase
{
    private readonly GovernanceQueryService _service;
    private readonly ILogger<GovernanceQueryController> _logger;

    public GovernanceQueryController(
        GovernanceQueryService service,
        ILogger<GovernanceQueryController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询排程运行列表（G4）
    /// </summary>
    [HttpGet("runs")]
    public async Task<ApiResponse<List<ScheduleRunGov>>> GetRuns(
        [FromQuery] string? status = null,
        [FromQuery] string? runType = null,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.QueryScheduleRunsAsync(status, runType, take, cancellationToken);
            return ApiResponse<List<ScheduleRunGov>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query schedule runs");
            return ApiResponse<List<ScheduleRunGov>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询排程域依赖（G7）
    /// </summary>
    [HttpGet("domain-dependencies")]
    public async Task<ApiResponse<List<DomainDependencyDto>>> GetDomainDependencies(
        [FromQuery] string? domainCode = null,
        [FromQuery] string? direction = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.QueryDomainDependenciesAsync(domainCode, direction, cancellationToken);
            return ApiResponse<List<DomainDependencyDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query domain dependencies");
            return ApiResponse<List<DomainDependencyDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
