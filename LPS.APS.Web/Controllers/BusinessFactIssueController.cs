using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// ODS/复杂事实Issue查询控制器（5号位提供给4号位）
///
/// 用于Explanation辅助、事实异常展示
/// 统一聚合来自多个5号位事实源的Issue（BOM Workset / MaterialStageDeptContext等）
///
/// 路由规范：
///   GET /api/business-fact-issues                  - 聚合查询所有Issue
///   GET /api/business-fact-issues/bom-workset      - 查询BOM Workset Issues
///   GET /api/business-fact-issues/material-stage   - 查询MaterialStageDeptContext Issues
///
/// 【职责边界】
/// - 5号位提供ODS/复杂事实Issue查询
/// - 4号位用于Explanation辅助和事实异常展示
/// </summary>
[ApiController]
[Route("api/business-fact-issues")]
public class BusinessFactIssueController : ControllerBase
{
    private readonly BusinessFactIssueService _service;
    private readonly ILogger<BusinessFactIssueController> _logger;

    public BusinessFactIssueController(
        BusinessFactIssueService service,
        ILogger<BusinessFactIssueController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 聚合查询所有ODS/复杂事实Issues
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<List<BusinessFactIssueDto>>> QueryAll(
        [FromQuery] string? source = null,
        [FromQuery] string? materialCode = null,
        [FromQuery] string? factoryCode = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? reviewStatus = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.QueryAllAsync(
                source, materialCode, factoryCode, severity, reviewStatus,
                skip, take, cancellationToken);

            return ApiResponse<List<BusinessFactIssueDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query business fact issues");
            return ApiResponse<List<BusinessFactIssueDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
