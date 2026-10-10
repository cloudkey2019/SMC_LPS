using System.Security.Claims;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Exceptions;
using LPS.APS.Application.Services;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 执行批拆分规则治理 API（TaskSplitRuleConfig / Batch Policy，0号位 2026-10-07 裁决本轮落码，归 3号位 治理）。
/// 路径前缀 /api/governance（与 SetupRuleController / GovernanceController / StageLeadTimeParamController 共享前缀，字面路由段不冲突）。
/// 权限：查看 aps.rule.view；维护 aps.rule.edit（复用 Rule 码，零权限种子变更）。
/// 载体「两级承接」：主题表 CRUD；停用走 DELETE（软删 IsActive=0），不物理删。
/// 错误映射：422 数据红线 / 404 不存在 / 400 参数 / 403 权限不足（[Authorize]）。
/// </summary>
[ApiController]
[Route("api/governance")]
public class TaskSplitRuleConfigController : ControllerBase
{
    private readonly ITaskSplitRuleConfigGovernanceService _service;
    private readonly ILogger<TaskSplitRuleConfigController> _logger;

    public TaskSplitRuleConfigController(
        ITaskSplitRuleConfigGovernanceService service,
        ILogger<TaskSplitRuleConfigController> logger)
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
        _logger.LogWarning(ex, "执行批拆分规则治理 {Action} 失败", action);
        return ex switch
        {
            SetupRuleDataRedLineException data => UnprocessableEntity(ApiResponse.Fail(422, data.Message)),
            ScopeViolationException scope => StatusCode(403, ApiResponse.Fail(403, scope.Message)),
            ResourceNotFoundException missing => NotFound(ApiResponse.Fail(404, missing.Message)),
            ArgumentException argument => BadRequest(ApiResponse.Fail(400, argument.Message)),
            _ => throw ex,
        };
    }

    /// <summary>分页列出执行批拆分规则（可按物料/生产部门/启用态过滤；R2 标准分页契约，4号位 2026-10-08 提请，方案 A）。</summary>
    /// <param name="materialId">物料 Id（可选精确匹配）</param>
    /// <param name="productionDepartmentId">生产部门 Id（可选精确匹配）</param>
    /// <param name="isActive">启用态过滤（null = 全部）</param>
    /// <param name="pageIndex">页码（1 基，&lt; 1 归 1）</param>
    /// <param name="pageSize">每页条数（1~200，超限截断；默认 20）</param>
    /// <param name="ct">取消令牌</param>
    [Authorize(Policy = PermissionCodes.RuleView)]
    [HttpGet("task-split-rule-configs")]
    public async Task<IActionResult> List(
        [FromQuery] int? materialId,
        [FromQuery] int? productionDepartmentId,
        [FromQuery] bool? isActive,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        try
        {
            var data = await _service.ListAsync(materialId, productionDepartmentId, isActive, GetCurrentUserId(), pageIndex, pageSize, ct);
            return Ok(ApiResponse<PageResult<TaskSplitRuleConfigDto>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "列执行批拆分规则");
        }
    }

    /// <summary>新增执行批拆分规则。</summary>
    [Authorize(Policy = PermissionCodes.RuleMaintain)]
    [HttpPost("task-split-rule-configs")]
    public async Task<IActionResult> Create([FromBody] SaveTaskSplitRuleConfigRequest input, CancellationToken ct)
    {
        try
        {
            var dto = await _service.CreateAsync(input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<TaskSplitRuleConfigDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "新增执行批拆分规则");
        }
    }

    /// <summary>编辑执行批拆分规则（PUT 整对象替换，不改动 IsActive）。</summary>
    [Authorize(Policy = PermissionCodes.RuleMaintain)]
    [HttpPut("task-split-rule-configs/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveTaskSplitRuleConfigRequest input, CancellationToken ct)
    {
        if (id <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "路径 id 非法（须大于 0）。"));
        }

        try
        {
            var dto = await _service.UpdateAsync(id, input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<TaskSplitRuleConfigDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "编辑执行批拆分规则");
        }
    }

    /// <summary>停用执行批拆分规则（软删 IsActive=0，保留历史与生效窗口）。</summary>
    [Authorize(Policy = PermissionCodes.RuleMaintain)]
    [HttpDelete("task-split-rule-configs/{id:int}")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        if (id <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "路径 id 非法（须大于 0）。"));
        }

        try
        {
            var dto = await _service.DeactivateAsync(id, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<TaskSplitRuleConfigDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "停用执行批拆分规则");
        }
    }
}