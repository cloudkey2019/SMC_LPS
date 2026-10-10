using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Ctp;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// CTP（承诺交期）评估接口（骨架）。
/// 3号位 职责：挂权限码 + domainKey resolve（缺省自动）+ F-G4 业务范围（Domains）二次校验；
/// 核心试算能力归 1号位/2号位，接入前返回 501。
/// 契约：APS V1 CTP 评估接口契约登记 v1.0（2026-09-30）。
/// 错误映射：resolve 失败 400 / scope 越界 403 / 核心未接入 501。
/// </summary>
/// <remarks>开发者：3号位</remarks>
[ApiController]
[Route("api/ctp")]
public class CtpController : ControllerBase
{
    private readonly ICtpService _ctpService;
    private readonly ILogger<CtpController> _logger;

    public CtpController(ICtpService ctpService, ILogger<CtpController> logger)
    {
        _ctpService = ctpService ?? throw new ArgumentNullException(nameof(ctpService));
        _logger = logger;
    }

    /// <summary>解析当前登录用户 Id（无效时返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>发起承诺交期评估（CTP 试算）——骨架：resolve + scope 二次校验，核心计算待 1号位/2号位 接入</summary>
    /// <remarks>开发者：3号位（编排骨架）；核心试算能力归 1号位/2号位</remarks>
    [Authorize(Policy = PermissionCodes.PlanCtp)]
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] CtpEvaluateRequest request, CancellationToken ct)
    {
        try
        {
            // resolve（显式/自动）+ F-G4 fail-closed 业务范围校验（Global 放行）
            var domainKey = await _ctpService.EvaluateAsync(request, GetCurrentUserId(), ct);

            // 核心承诺交期评估计算归 1号位/2号位，接入前返回 501
            _logger.LogInformation("CTP 评估端点（骨架）被调用：{DomainKey}，核心能力待接入", domainKey);
            return StatusCode(501, ApiResponse.Fail(501, "承诺交期评估（CTP 试算）核心能力由 1号位/2号位 接入，暂未开放"));
        }
        catch (ScopeViolationException ex)
        {
            _logger.LogWarning(ex, "CTP 评估业务范围越界：{DomainKey}", request.DomainKey);
            return StatusCode(403, ApiResponse.Fail(403, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            // resolve 无唯一匹配（缺 domainKey 且自动解析失败/多域歧义）→ 业务错 400（契约 §4；不 422）
            _logger.LogWarning(ex, "CTP 评估 domainKey 解析失败：{MaterialCode}/{FactoryCode}", request.MaterialCode, request.FactoryCode);
            return StatusCode(400, ApiResponse.Fail(400, ex.Message));
        }
    }
}