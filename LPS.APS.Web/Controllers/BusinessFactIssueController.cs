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
[Authorize(Policy = PermissionCodes.PlanView)]
[ApiController]
[Route("api/business-fact-issues")]
public class BusinessFactIssueController : ControllerBase
{
    private readonly BusinessFactIssueService _service;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<BusinessFactIssueController> _logger;

    public BusinessFactIssueController(
        BusinessFactIssueService service,
        IDataScopeService dataScopeService,
        ILogger<BusinessFactIssueController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>解析当前登录用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>
    /// 聚合查询所有ODS/复杂事实Issues
    ///
    /// Q5 三级 Scope 处理（31-0 裁决，33-0 红线）：
    /// ① Issue 有 Factory 关联 → 按 Factory Scope 过滤
    /// ② Factory 为空但可经 StageCode→ProcessCodeDict 派生 → 按派生 Factory 过滤
    /// ③ 真正系统级/全局 Issue → 仅 Global Scope 用户可见
    ///
    /// 实现：全链路下推 allowedFactories 到 SQL WHERE，null = Global 全放行。
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
            var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), cancellationToken);

            // 非空入参校验：用户指定的 factoryCode 必须在授权范围内
            if (!string.IsNullOrEmpty(factoryCode) && !scope.Allows(DataScopeTypes.Factory, factoryCode))
                return ApiResponse<List<BusinessFactIssueDto>>.Fail(403, "Factory 范围越界");

            // Global 全放行（allowedFactories=null → SQL 不追加过滤）；
            // 非 Global → 下推 allowedFactories 到 SQL（BOM Issues 按 ExpectedFactory/ActualFactory；
            //   MSC Issues 按 StageCode→ext_MES_ProcessCode_View.FactoryCode 派生）；
            // 均无 Factory 归属 → 仅 Global 用户可见（fail-closed）
            var allowedFactories = scope.GetValues(DataScopeTypes.Factory);

            var result = await _service.QueryAllAsync(
                source, materialCode, factoryCode, severity, reviewStatus,
                skip, take, cancellationToken, allowedFactories);

            return ApiResponse<List<BusinessFactIssueDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query business fact issues");
            return ApiResponse<List<BusinessFactIssueDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
