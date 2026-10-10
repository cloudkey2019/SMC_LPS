using System.Security.Claims;
using LPS.APS.Application.Services.Query;
using LPS.APS.Application.Services.Query.Dto;
using LPS.APS.Core.Authorization;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 排程结果查询控制器（5号位中转，供4号位前端甘特图调用）
///
/// 路由规范：
///   GET /api/schedule/versions          - 计划版本列表
///   GET /api/schedule/gantt/{id}        - 指定版本的甘特图数据
///   GET /api/schedule/summary/{id}      - 指定版本的排程概要 KPI
///
/// 【职责边界】
/// - 5号位提供只读查询接口
/// - 数据来源：IScheduleQueryService（读取已落盘APS事实）
/// - V1不包含：触发重排/拖拽调整/解冻申请等写操作
/// F-G4：以当前登录用户 Id 为入口，由 IScheduleQueryService 内部解析业务范围并过滤（Domain/Factory 维度）。
/// V1 不包含：触发重排 / 拖拽调整 / 解冻申请 等写操作（后续阶段铺开）
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ScheduleController : ControllerBase
{
    private readonly IScheduleQueryService _queryService;
    private readonly ILogger<ScheduleController> _logger;

    public ScheduleController(
        IScheduleQueryService queryService,
        ILogger<ScheduleController> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    /// <summary>
    /// 获取计划版本列表（最新优先，默认 30 条，按当前用户业务范围过滤）
    /// </summary>
    [Authorize(Policy = PermissionCodes.PlanView)]
    [HttpGet("versions")]
    public async Task<ApiResponse<IReadOnlyList<PlanVersionSummaryDto>>> GetVersions(
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0 || take > 200) take = 30;

        var list = await _queryService.GetVersionsAsync(GetCurrentUserId(), take, cancellationToken);
        return ApiResponse<IReadOnlyList<PlanVersionSummaryDto>>.Success(list);
    }

    /// <summary>
    /// 获取指定计划版本的甘特图数据（资源行 + 任务条；越权返回空）
    /// </summary>
    [Authorize(Policy = PermissionCodes.PlanView)]
    [HttpGet("gantt/{planVersionId:int}")]
    public async Task<ApiResponse<GanttDataDto>> GetGantt(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        var data = await _queryService.GetGanttAsync(GetCurrentUserId(), planVersionId, cancellationToken);
        return ApiResponse<GanttDataDto>.Success(data);
    }

    /// <summary>
    /// 获取指定计划版本的排程概要 KPI（越权返回空）
    /// </summary>
    [Authorize(Policy = PermissionCodes.PlanView)]
    [HttpGet("summary/{planVersionId:int}")]
    public async Task<ApiResponse<ScheduleSummaryDto>> GetSummary(
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _queryService.GetSummaryAsync(GetCurrentUserId(), planVersionId, cancellationToken);
        return ApiResponse<ScheduleSummaryDto>.Success(summary);
    }

    /// <summary>解析当前登录用户 Id（无效时返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;
}
