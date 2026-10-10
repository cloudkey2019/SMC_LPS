using System.Security.Claims;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Exceptions;
using LPS.APS.Application.Services;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 工序工艺属性治理 API（T1 · S2/S3 OPM 治理 API，4号位 配置页面消费）。
/// 路径前缀 /api/governance（与 SetupRuleController / GovernanceController 共享前缀，字面路由段不冲突）。
/// 权限：查看 aps.rule.view；维护 aps.rule.edit（复用 Rule 码，零权限种子变更）。
/// OperationPlanningMode（OPM）属 APS 工艺规划属性，治理侧直维护唯一主路径（0号位 2026-09-23 Q1 裁决）。
/// 错误映射：422 数据红线（三态非法）/ 404 不存在 / 400 参数 / 403 权限不足（[Authorize]）。
/// </summary>
[ApiController]
[Route("api/governance")]
public class RoutingOperationController : ControllerBase
{
    private readonly IRoutingOperationGovernanceService _service;
    private readonly ILogger<RoutingOperationController> _logger;

    public RoutingOperationController(
        IRoutingOperationGovernanceService service,
        ILogger<RoutingOperationController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>解析当前登录用户 Id（无效时返回 0 → 审计可追溯性不降级原则下置空处理）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>解析当前登录用户工号（审计真实 Actor，P1-04 强制后端取身份，禁止信任请求体）</summary>
    private string GetCurrentUserCode()
        => User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    /// <summary>统一错误映射：422 数据红线 / 404 不存在 / 400 参数；未知异常上抛由框架处理（500）</summary>
    private IActionResult HandleError(Exception ex, string action)
    {
        _logger.LogWarning(ex, "工序工艺属性治理 {Action} 失败", action);
        return ex switch
        {
            SetupRuleDataRedLineException data => UnprocessableEntity(ApiResponse.Fail(422, data.Message)),
            ResourceNotFoundException missing => NotFound(ApiResponse.Fail(404, missing.Message)),
            ArgumentException argument => BadRequest(ApiResponse.Fail(400, argument.Message)),
            _ => throw ex,
        };
    }

    /// <summary>列出指定物料的工序（含当前 OPM 值），供 4号位 选工序维护</summary>
    [Authorize(Policy = PermissionCodes.RuleView)]
    [HttpGet("routing-operations")]
    public async Task<IActionResult> ListOperations([FromQuery] int materialId, CancellationToken ct)
    {
        if (materialId <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "缺少必填参数 materialId（须大于 0）。"));
        }

        try
        {
            var data = await _service.ListOperationsAsync(materialId, ct);
            return Ok(ApiResponse<IReadOnlyList<RoutingOperationDto>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "列工序");
        }
    }

    /// <summary>维护指定工序的 OPM 值（三态校验在服务内执行，非法 → 422）</summary>
    [Authorize(Policy = PermissionCodes.RuleMaintain)]
    [HttpPut("routing-operations/{id:long}/planning-mode")]
    public async Task<IActionResult> UpdateOperationPlanningMode(
        long id,
        [FromBody] UpdateOperationPlanningModeRequest input,
        CancellationToken ct)
    {
        if (id <= 0 || input.OperationId != id)
        {
            return BadRequest(ApiResponse.Fail(400, "路径 id 与请求体 OperationId 不一致或非法。"));
        }

        try
        {
            var dto = await _service.UpdateOperationPlanningModeAsync(
                id, input.OperationPlanningMode, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<RoutingOperationDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "维护 OPM");
        }
    }
}