using System.Security.Claims;
using LPS.APS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace LPS.APS.Web.Authorization;

/// <summary>功能权限点授权要求（携带目标 PermissionCode）</summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permissionCode)
    {
        PermissionCode = permissionCode;
    }

    /// <summary>目标权限码</summary>
    public string PermissionCode { get; }
}

/// <summary>
/// 功能权限点授权处理器（F-G3，3号位）
/// 从 JWT 取用户 Id，经 IPermissionService 校验目标权限码；缺用户或未命中 → 失败。
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissionService;

    public PermissionAuthorizationHandler(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId)
            && await _permissionService.HasPermissionAsync(userId, requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }
        // 未命中则不 Succeed，框架按授权失败处理
    }
}
