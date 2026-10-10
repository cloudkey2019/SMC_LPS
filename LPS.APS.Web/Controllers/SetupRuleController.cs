using System.Security.Claims;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Setup;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Exceptions;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// Setup 换型规则维护 API（4号位 Setup 契约 §11.1，#1-13 端点）。
/// 路径前缀 /api/governance（与 <see cref="GovernanceController"/> 共享前缀，字面路由段不冲突）。
/// 权限：查看 aps.setup.view；维护 aps.setup.edit；发布 aps.setup.publish。
/// 错误映射：422 数据红线 / 404 不存在 / 400 状态红线与参数 / 403 权限不足（[Authorize]）。
/// </summary>
[ApiController]
[Route("api/governance")]
public class SetupRuleController : ControllerBase
{
    private readonly ISetupRuleService _setupRuleService;
    private readonly ILogger<SetupRuleController> _logger;

    public SetupRuleController(ISetupRuleService setupRuleService, ILogger<SetupRuleController> logger)
    {
        _setupRuleService = setupRuleService;
        _logger = logger;
    }

    /// <summary>解析当前登录用户 Id（无效时返回 0 → 审计可追溯性不降级原则下置空处理）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>解析当前登录用户工号（审计真实 Actor，P1-04 强制后端取身份，禁止信任请求体）</summary>
    private string GetCurrentUserCode()
        => User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    /// <summary>统一错误映射：422 数据红线 / 404 不存在 / 400 状态红线与参数；未知异常上抛由框架处理（500）</summary>
    private IActionResult HandleError(Exception ex, string action)
    {
        _logger.LogWarning(ex, "Setup 换型规则 {Action} 失败", action);
        return ex switch
        {
            SetupRuleDataRedLineException data => UnprocessableEntity(ApiResponse.Fail(422, data.Message)),
            ResourceNotFoundException missing => NotFound(ApiResponse.Fail(404, missing.Message)),
            InvalidOperationException state => BadRequest(ApiResponse.Fail(400, state.Message)),
            _ => throw ex,
        };
    }

    #region #1-4 明确规则（EXACT）

    /// <summary>#1 列出明确换型规则（EXACT）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("setup-rules/exact")]
    public async Task<IActionResult> ListExact([FromQuery] long ruleSetVersionId, CancellationToken ct)
    {
        if (ruleSetVersionId <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "缺少必填参数 ruleSetVersionId。"));
        }

        try
        {
            var data = await _setupRuleService.ListAsync(ruleSetVersionId, SetupTransitionRuleType.Exact, ct);
            return Ok(ApiResponse<IReadOnlyList<SetupRuleDto>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "列表查询");
        }
    }

    /// <summary>#2 新增明确换型规则（EXACT）</summary>
    [Authorize(Policy = PermissionCodes.SetupEdit)]
    [HttpPost("setup-rules/exact")]
    public async Task<IActionResult> CreateExact([FromBody] SetupRuleExactInput input, CancellationToken ct)
    {
        try
        {
            var dto = await _setupRuleService.CreateExactAsync(input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<SetupRuleDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "创建");
        }
    }

    /// <summary>#3 更新明确换型规则（EXACT）</summary>
    [Authorize(Policy = PermissionCodes.SetupEdit)]
    [HttpPut("setup-rules/exact/{id:long}")]
    public async Task<IActionResult> UpdateExact(long id, [FromBody] SetupRuleExactInput input, CancellationToken ct)
    {
        try
        {
            var dto = await _setupRuleService.UpdateExactAsync(id, input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<SetupRuleDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "更新");
        }
    }

    /// <summary>#4 删除明确换型规则（EXACT）</summary>
    [Authorize(Policy = PermissionCodes.SetupEdit)]
    [HttpDelete("setup-rules/exact/{id:long}")]
    public async Task<IActionResult> DeleteExact(long id, CancellationToken ct)
    {
        try
        {
            await _setupRuleService.DeleteAsync(id, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse.Ok("删除成功"));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "删除");
        }
    }

    #endregion

    #region #5-8 默认规则（DEFAULT）

    /// <summary>#5 列出默认换型规则（DEFAULT）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("setup-rules/default")]
    public async Task<IActionResult> ListDefault([FromQuery] long ruleSetVersionId, CancellationToken ct)
    {
        if (ruleSetVersionId <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "缺少必填参数 ruleSetVersionId。"));
        }

        try
        {
            var data = await _setupRuleService.ListAsync(ruleSetVersionId, SetupTransitionRuleType.Default, ct);
            return Ok(ApiResponse<IReadOnlyList<SetupRuleDto>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "列表查询");
        }
    }

    /// <summary>#6 新增默认换型规则（DEFAULT）</summary>
    [Authorize(Policy = PermissionCodes.SetupEdit)]
    [HttpPost("setup-rules/default")]
    public async Task<IActionResult> CreateDefault([FromBody] SetupRuleDefaultInput input, CancellationToken ct)
    {
        try
        {
            var dto = await _setupRuleService.CreateDefaultAsync(input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<SetupRuleDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "创建");
        }
    }

    /// <summary>#7 更新默认换型规则（DEFAULT）</summary>
    [Authorize(Policy = PermissionCodes.SetupEdit)]
    [HttpPut("setup-rules/default/{id:long}")]
    public async Task<IActionResult> UpdateDefault(long id, [FromBody] SetupRuleDefaultInput input, CancellationToken ct)
    {
        try
        {
            var dto = await _setupRuleService.UpdateDefaultAsync(id, input, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse<SetupRuleDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "更新");
        }
    }

    /// <summary>#8 删除默认换型规则（DEFAULT）</summary>
    [Authorize(Policy = PermissionCodes.SetupEdit)]
    [HttpDelete("setup-rules/default/{id:long}")]
    public async Task<IActionResult> DeleteDefault(long id, CancellationToken ct)
    {
        try
        {
            await _setupRuleService.DeleteAsync(id, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse.Ok("删除成功"));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "删除");
        }
    }

    #endregion

    #region #9-13 资源资格 / 版本列表 / 版本对比 / 发布

    /// <summary>#9 工序资源资格查询（当前 Operation 合法资源 ∩ 前后产品合法设备交集；materialId/toMaterialId 为物料主键 Material.Id）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("operation-resource-eligibility")]
    public async Task<IActionResult> GetOperationResourceEligibility(
        [FromQuery] string operationCode,
        [FromQuery] string materialId,
        [FromQuery] string? toMaterialId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(operationCode))
        {
            return BadRequest(ApiResponse.Fail(400, "缺少必填参数 operationCode。"));
        }

        if (!int.TryParse(materialId, out var materialIdValue) || materialIdValue <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "materialId 须为大于 0 的物料主键（Material.Id）。"));
        }

        int? toMaterialIdValue = null;
        if (!string.IsNullOrWhiteSpace(toMaterialId))
        {
            if (!int.TryParse(toMaterialId, out var parsedTo) || parsedTo <= 0)
            {
                return BadRequest(ApiResponse.Fail(400, "toMaterialId 须为大于 0 的物料主键（Material.Id）。"));
            }

            toMaterialIdValue = parsedTo;
        }

        try
        {
            var dto = await _setupRuleService.GetEligibilityAsync(operationCode.Trim(), materialIdValue, toMaterialIdValue, ct);
            return Ok(ApiResponse<OperationResourceEligibilityDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "工序资源资格查询");
        }
    }

    /// <summary>#11 规则集版本列表（可选按规则集/状态过滤）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("rule-set-versions")]
    public async Task<IActionResult> ListRuleSetVersions([FromQuery] long? ruleSetId, [FromQuery] string? status, CancellationToken ct)
    {
        try
        {
            var data = await _setupRuleService.ListRuleSetVersionsAsync(ruleSetId, status, ct);
            return Ok(ApiResponse<IReadOnlyList<SetupRuleSetVersionDto>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "规则集版本列表");
        }
    }

    /// <summary>#12 版本 Setup 换型规则差异（added/modified/removed + 计数）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("rule-set-versions/{id:long}/diff")]
    public async Task<IActionResult> GetDiff(long id, [FromQuery] long otherVersionId, CancellationToken ct)
    {
        if (otherVersionId <= 0)
        {
            return BadRequest(ApiResponse.Fail(400, "缺少必填参数 otherVersionId。"));
        }

        try
        {
            var dto = await _setupRuleService.GetDiffAsync(id, otherVersionId, ct);
            return Ok(ApiResponse<SetupDiffDto>.Success(dto));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "版本对比");
        }
    }

    /// <summary>#13 发布规则集版本（发布前 Setup 冲突全量预校验；权限 aps.setup.publish）</summary>
    [Authorize(Policy = PermissionCodes.SetupPublish)]
    [HttpPost("rule-set-versions/{id:long}/publish")]
    public async Task<IActionResult> Publish(long id, [FromBody] SetupPublishRequest? request, CancellationToken ct)
    {
        try
        {
            await _setupRuleService.PublishAsync(id, request?.ChangeReason, GetCurrentUserId(), GetCurrentUserCode(), ct);
            return Ok(ApiResponse.Ok("发布成功"));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "发布");
        }
    }

    #endregion

    #region 主数据 Code→Id 读（Setup 三 Dialog 数据源；3号位 直接读源表，红线 #4 返回列表）

    /// <summary>部门下拉（masterId=Id，code=DeptCode；search 模糊 DeptCode）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("lookups/departments")]
    public async Task<IActionResult> LookupDepartments([FromQuery] string? search, CancellationToken ct)
    {
        try
        {
            var data = await _setupRuleService.LookupDepartmentsAsync(search, ct);
            return Ok(ApiResponse<IReadOnlyList<DepartmentLookupItem>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "部门下拉查询");
        }
    }

    /// <summary>设备下拉（masterId=Id，code=ResourceCode；search 模糊 ResourceCode/ResourceName）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("lookups/resources")]
    public async Task<IActionResult> LookupResources([FromQuery] string? search, CancellationToken ct)
    {
        try
        {
            var data = await _setupRuleService.LookupResourcesAsync(search, ct);
            return Ok(ApiResponse<IReadOnlyList<ResourceLookupItem>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "设备下拉查询");
        }
    }

    /// <summary>物料下拉（masterId=Material.Id，code=MaterialCode；search 模糊 MaterialCode/MaterialName/Spec；activeOnly=true 仅活动）</summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("lookups/materials")]
    public async Task<IActionResult> LookupMaterials([FromQuery] string? search, CancellationToken ct, [FromQuery] bool activeOnly = false)
    {
        try
        {
            var data = await _setupRuleService.LookupMaterialsAsync(search, activeOnly, ct);
            return Ok(ApiResponse<IReadOnlyList<MaterialLookupItem>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "物料下拉查询");
        }
    }

    #endregion

    #region #14 uncovered-stats 规则缺失聚合查询（v1.4 §十一 KPI 监控）

    /// <summary>
    /// #14 规则缺失（0 分钟兜底）命中频次聚合（uncovered-stats）。
    /// 数据源 = [Task].SetupSource='SETUP_RULE_MISSING_ZERO_FALLBACK' 聚合；四级下钻 runId → 部门 → 工序 → 设备 → 产品。
    /// 各级过滤参数可选（未传不筛）；响应 Code 回带（部门/设备/物料）。只读查询，空结果返回空列表。
    /// 路由 setup-rules/uncovered-stats 与前端 setup.ts:424 一致。
    /// </summary>
    [Authorize(Policy = PermissionCodes.SetupView)]
    [HttpGet("setup-rules/uncovered-stats")]
    public async Task<IActionResult> GetUncoveredStats(
        [FromQuery] int? runId,
        [FromQuery] int? departmentId,
        [FromQuery] string? operationCode,
        [FromQuery] int? resourceId,
        [FromQuery] int? materialId,
        CancellationToken ct)
    {
        try
        {
            var data = await _setupRuleService.GetUncoveredStatsAsync(runId, departmentId, operationCode, resourceId, materialId, ct);
            return Ok(ApiResponse<IReadOnlyList<SetupUncoveredStatDto>>.Success(data));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "规则缺失聚合查询");
        }
    }

    #endregion
}
