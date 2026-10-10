using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// MES 受控操作下发接口（骨架）。
/// 3号位 职责：挂权限码 + F-G4 业务范围（Factories）二次校验；核心下发能力归 2号位，接入前返回 501。
/// </summary>
/// <remarks>开发者：3号位</remarks>
[ApiController]
[Route("api/mes")]
public class MesDispatchController : ControllerBase
{
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<MesDispatchController> _logger;

    public MesDispatchController(IDataScopeService dataScopeService, ILogger<MesDispatchController> logger)
    {
        _dataScopeService = dataScopeService;
        _logger = logger;
    }

    /// <summary>解析当前登录用户 Id（无效时返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>发起 MES 受控操作下发——骨架：仅 scope 二次校验，核心下发待 2号位 接入</summary>
    /// <remarks>开发者：3号位（编排骨架）；核心下发能力归 2号位</remarks>
    [Authorize(Policy = PermissionCodes.MesControlledOperation)]
    [HttpPost("dispatch")]
    public async Task<IActionResult> Dispatch([FromBody] MesDispatchRequest request, CancellationToken ct)
    {
        try
        {
            // F-G4 fail-closed：Factories ∋ request.FactoryCode（Global 放行）
            await _dataScopeService.EnsureInScopeAsync(GetCurrentUserId(), DataScopeTypes.Factory, request.FactoryCode, ct);
        }
        catch (ScopeViolationException ex)
        {
            _logger.LogWarning(ex, "MES 下发业务范围越界：{FactoryCode}", request.FactoryCode);
            return StatusCode(403, ApiResponse.Fail(403, ex.Message));
        }

        // 核心 MES 受控操作下发归 2号位，接入前返回 501
        _logger.LogInformation("MES 下发端点（骨架）被调用：{FactoryCode}，核心能力待接入", request.FactoryCode);
        return StatusCode(501, ApiResponse.Fail(501, "MES 受控操作下发核心能力由 2号位 接入，暂未开放"));
    }
}

/// <summary>MES 下发请求体</summary>
public sealed record MesDispatchRequest
{
    /// <summary>目标工厂代码</summary>
    [Required(ErrorMessage = "FactoryCode 必填")]
    public required string FactoryCode { get; init; }
}