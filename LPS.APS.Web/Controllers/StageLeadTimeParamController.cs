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
/// 阶段提前期参数治理 API（StageLeadTimeParam，PM 2026-09-28 终裁归 3号位 规则参数体系）。
/// 路径前缀 /api/governance（与 SetupRuleController / GovernanceController / RoutingOperationController 共享前缀，字面路由段不冲突）。
/// 权限：查看 aps.rule.view；维护 aps.rule.edit（复用 Rule 码，零权限种子变更）。
/// 载体「直接生效（时间窗）」；停用走 DELETE（软删 IsActive=0），不物理删。
/// 错误映射：422 数据红线 / 404 不存在 / 400 参数 / 403 权限不足（[Authorize]）。
/// </summary>
[ApiController]
[Route("api/governance")]
public class StageLeadTimeParamController : ControllerBase
{
    private readonly IStageLeadTimeParamGovernanceService _service;
    private readonly ILogger<StageLeadTimeParamController> _logger;

    public StageLeadTimeParamController(
        IStageLeadTimeParamGovernanceService service,
        ILogger<StageLeadTimeParamController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>解析当前登录用户 Id（无效时返回 0 → 审计可追溯性不降级原则下置空处理）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>解析当前登录用户工号（审计真实 Actor，后端从身份取，禁止信任请求体）</summary>
    private string GetCurrentUserCode()
        => User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    /// <summary>统一错误映射：422 数据红线 / 404 不存在 / 400 参数；未知异常上抛由框架处理（500）</summary>
    private IActionResult HandleError(Exception ex, string action)
    {
        _logger.LogWarning(ex, "阶段提前期参数治理 {Action} 失败", action);
        return ex switch
        {
            SetupRuleDataRedLineException data => UnprocessableEntity(ApiResponse.Fail(422, data.Message)),
            ScopeViolationException scope => StatusCode(403, ApiResponse.Fail(403, scope.Message)),
            ResourceNotFoundException missing => NotFound(ApiResponse.Fail(404, missing.Message)),
            ArgumentException argument => BadRequest(ApiResponse.Fail(400, argument.Message)),
            _ => throw ex,
        };
    }

    /// <summary>列出阶段提前期参数（可按工厂/阶段/生产部门/启用态过滤），供 4号位 配置页面。</summary>
    /// <param name="factoryCode">工厂编码（可选）</param>
    /// <param name="stageCode">大工艺阶段码（可选）</param>
    /// <param name="productionDeptCode">生产部门编码（可选）</param>
    /// <param name="isActive">启用态（null = 全部）</param>
    /// <param name="ct">取消令牌</param>
    [Authorize(Policy = PermissionCodes.RuleView)]
    [HttpGet("stage-lead-time-params")]
    public async Task<IActionResult> List(
        [FromQuery] string? factoryCode,
        [FromQuery] string? stageCode,
        [FromQuery] string? productionDeptCode,
        [FromQuery] bool? isActive,
        CancellationToken ct)
    {
        try
        {
            var data = await _service.ListAsync(factoryCode, stageCode, productionDeptCode, isActive, GetCurrentUserId(), ct);
            return Ok(ApiResponse<IReadOnlyList<StageLeadTimeParamDto>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "列阶段提前期参数");
        }
    }

    /// <summary>新增阶段提前期参数。</summary>
    /// <param name="input">新增请求（整对象）</param>
    /// <param name="ct">取消令牌</param>
    [Authorize(Policy = PermissionCodes.RuleMaintain)]
    [HttpPost("stage-lead-time-params")]
    public async Task<IActionResult> Create([FromBody] SaveStageLeadTimeParamRequest input, CancellationToken ct)
    {
        try
        {
            var dto = await _service.CreateAsync(input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<StageLeadTimeParamDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "新增阶段提前期参数");
        }
    }

    /// <summary>编辑阶段提前期参数（PUT 整对象替换，不改动 IsActive）。</summary>
    /// <param name="id">参数主键（StageLeadTimeParam.Id）</param>
    /// <param name="input">编辑请求（整对象替换）</param>
    /// <param name="ct">取消令牌</param>
    [Authorize(Policy = PermissionCodes.RuleMaintain)]
    [HttpPut("stage-lead-time-params/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveStageLeadTimeParamRequest input, CancellationToken ct)
    {
        if (id <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "路径 id 非法（须大于 0）。"));
        }

        try
        {
            var dto = await _service.UpdateAsync(id, input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<StageLeadTimeParamDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "编辑阶段提前期参数");
        }
    }

    /// <summary>停用阶段提前期参数（软删 IsActive=0，保留历史与生效窗口）。</summary>
    /// <param name="id">参数主键（StageLeadTimeParam.Id）</param>
    /// <param name="ct">取消令牌</param>
    [Authorize(Policy = PermissionCodes.RuleMaintain)]
    [HttpDelete("stage-lead-time-params/{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        if (id <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "路径 id 非法（须大于 0）。"));
        }

        try
        {
            var dto = await _service.DeactivateAsync(id, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<StageLeadTimeParamDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "停用阶段提前期参数");
        }
    }
}