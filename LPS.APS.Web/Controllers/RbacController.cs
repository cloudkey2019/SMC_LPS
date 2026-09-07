using System.Security.Claims;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog.Context;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// RBAC 管理控制器（F-G5，3号位）
/// 提供用户/角色/权限/业务范围策略的增删改与分配端点。
/// 所有端点要求 auth.manage 功能权限（PermissionAuthorizationHandler 对 DB 权威校验）。
/// </summary>
[ApiController]
[Route("api/rbac")]
[Authorize(Policy = PermissionCodes.AuthManage)]
public class RbacController : ControllerBase
{
    private readonly IRbacManagementService _service;
    private readonly ILogger<RbacController> _logger;

    public RbacController(IRbacManagementService service, ILogger<RbacController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ==================== 用户 ====================

    /// <summary>查询用户列表</summary>
    [HttpGet("users")]
    public Task<ApiResponse<IReadOnlyList<UserSummaryDto>>> GetUsers()
        => RunAsync(() => _service.GetUsersAsync());

    /// <summary>创建用户</summary>
    [HttpPost("users")]
    public Task<ApiResponse<UserSummaryDto>> CreateUser([FromBody] CreateUserRequest request)
        => RunAsync(() => _service.CreateUserAsync(request, GetCurrentUserId()), "创建用户成功");

    /// <summary>更新用户（含启用/停用）</summary>
    [HttpPut("users/{id:int}")]
    public Task<ApiResponse> UpdateUser(int id, [FromBody] UpdateUserRequest request)
        => RunAsync(() => _service.UpdateUserAsync(id, request, GetCurrentUserId()), "更新用户成功");

    /// <summary>删除用户（软删除）</summary>
    [HttpDelete("users/{id:int}")]
    public async Task<ApiResponse> DeleteUser(int id)
    {
        int currentUserId;
        try
        {
            currentUserId = GetCurrentUserId();
        }
        catch (InvalidOperationException)
        {
            return ApiResponse.Fail(401, "无法解析当前登录用户身份");
        }

        if (id == currentUserId)
            return ApiResponse.Fail(403, "不能删除当前登录账号");
        return await RunAsync(() => _service.DeleteUserAsync(id, currentUserId), "删除用户成功");
    }

    /// <summary>分配用户角色（覆盖式）</summary>
    [HttpPut("users/{id:int}/roles")]
    public Task<ApiResponse> AssignUserRoles(int id, [FromBody] AssignIdsRequest request)
        => RunAsync(() => _service.AssignUserRolesAsync(id, request.Ids, GetCurrentUserId()), "分配用户角色成功");

    /// <summary>分配用户业务范围（覆盖式）</summary>
    [HttpPut("users/{id:int}/scopes")]
    public Task<ApiResponse> AssignUserScopes(int id, [FromBody] AssignIdsRequest request)
        => RunAsync(() => _service.AssignUserScopesAsync(id, request.Ids, GetCurrentUserId()), "分配用户业务范围成功");

    // ==================== 角色 ====================

    /// <summary>查询角色列表</summary>
    [HttpGet("roles")]
    public Task<ApiResponse<IReadOnlyList<RoleSummaryDto>>> GetRoles()
        => RunAsync(() => _service.GetRolesAsync());

    /// <summary>创建角色</summary>
    [HttpPost("roles")]
    public Task<ApiResponse<RoleSummaryDto>> CreateRole([FromBody] CreateRoleRequest request)
        => RunAsync(() => _service.CreateRoleAsync(request, GetCurrentUserId()), "创建角色成功");

    /// <summary>更新角色（含启用/停用）</summary>
    [HttpPut("roles/{id:int}")]
    public Task<ApiResponse> UpdateRole(int id, [FromBody] UpdateRoleRequest request)
        => RunAsync(() => _service.UpdateRoleAsync(id, request, GetCurrentUserId()), "更新角色成功");

    /// <summary>删除角色（软删除）</summary>
    [HttpDelete("roles/{id:int}")]
    public Task<ApiResponse> DeleteRole(int id)
        => RunAsync(() => _service.DeleteRoleAsync(id, GetCurrentUserId()), "删除角色成功");

    /// <summary>分配角色权限（覆盖式）</summary>
    [HttpPut("roles/{id:int}/permissions")]
    public Task<ApiResponse> AssignRolePermissions(int id, [FromBody] AssignIdsRequest request)
        => RunAsync(() => _service.AssignRolePermissionsAsync(id, request.Ids, GetCurrentUserId()), "分配角色权限成功");

    /// <summary>分配角色业务范围（覆盖式）</summary>
    [HttpPut("roles/{id:int}/scopes")]
    public Task<ApiResponse> AssignRoleScopes(int id, [FromBody] AssignIdsRequest request)
        => RunAsync(() => _service.AssignRoleScopesAsync(id, request.Ids, GetCurrentUserId()), "分配角色业务范围成功");

    // ==================== 权限 ====================

    /// <summary>查询权限列表</summary>
    [HttpGet("permissions")]
    public Task<ApiResponse<IReadOnlyList<PermissionSummaryDto>>> GetPermissions()
        => RunAsync(() => _service.GetPermissionsAsync());

    /// <summary>创建权限</summary>
    [HttpPost("permissions")]
    public Task<ApiResponse<PermissionSummaryDto>> CreatePermission([FromBody] CreatePermissionRequest request)
        => RunAsync(() => _service.CreatePermissionAsync(request, GetCurrentUserId()), "创建权限成功");

    // ==================== 业务范围策略 ====================

    /// <summary>查询业务范围策略列表</summary>
    [HttpGet("scopes")]
    public Task<ApiResponse<IReadOnlyList<DataScopePolicyDto>>> GetScopes()
        => RunAsync(() => _service.GetDataScopePoliciesAsync());

    /// <summary>创建业务范围策略</summary>
    [HttpPost("scopes")]
    public Task<ApiResponse<DataScopePolicyDto>> CreateScope([FromBody] CreateDataScopePolicyRequest request)
        => RunAsync(() => _service.CreateDataScopePolicyAsync(request, GetCurrentUserId()), "创建业务范围策略成功");

    /// <summary>更新业务范围策略说明</summary>
    [HttpPut("scopes/{id:int}")]
    public Task<ApiResponse> UpdateScope(int id, [FromBody] UpdateDataScopePolicyRequest request)
        => RunAsync(() => _service.UpdateDataScopePolicyAsync(id, request.Description, GetCurrentUserId()), "更新业务范围策略成功");

    /// <summary>删除业务范围策略</summary>
    [HttpDelete("scopes/{id:int}")]
    public Task<ApiResponse> DeleteScope(int id)
        => RunAsync(() => _service.DeleteDataScopePolicyAsync(id, GetCurrentUserId()), "删除业务范围策略成功");

    // ==================== 私有辅助 ====================

    /// <summary>
    /// 从 JWT 声明解析当前操作用户 Id（用于审计 OperatedBy 与自删保护）。
    /// 解析失败或非法（≤0）时抛出 <see cref="InvalidOperationException"/>（fail-closed，避免落 OperatedBy="0" 的脏审计）。
    /// </summary>
    private int GetCurrentUserId()
    {
        if (int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id > 0)
            return id;
        throw new InvalidOperationException("无法解析当前登录用户身份");
    }

    /// <summary>仅用于日志显示的当前用户 Id（解析失败返回 0，不抛，避免日志行本身中断）。</summary>
    private int GetCurrentUserIdForLog()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>统一执行并映射异常到 <see cref="ApiResponse{T}"/>。</summary>
    private async Task<ApiResponse<T>> RunAsync<T>(Func<Task<T>> action, string message = "success")
    {
        using var auditScope = LogContext.PushProperty("Operator", $"{User.FindFirst(ClaimTypes.Name)?.Value ?? "?"}(Id={GetCurrentUserIdForLog()})");
        try
        {
            return ApiResponse<T>.Success(await action(), message);
        }
        catch (KeyNotFoundException ex)
        {
            return ApiResponse<T>.Fail(404, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse<T>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RBAC 管理操作失败");
            return ApiResponse<T>.Fail(500, "服务器内部错误");
        }
    }

    /// <summary>统一执行并映射异常到 <see cref="ApiResponse"/>（无返回数据）。</summary>
    private async Task<ApiResponse> RunAsync(Func<Task> action, string message = "操作成功")
    {
        using var auditScope = LogContext.PushProperty("Operator", $"{User.FindFirst(ClaimTypes.Name)?.Value ?? "?"}(Id={GetCurrentUserIdForLog()})");
        try
        {
            await action();
            return ApiResponse.Ok(message);
        }
        catch (KeyNotFoundException ex)
        {
            return ApiResponse.Fail(404, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ApiResponse.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RBAC 管理操作失败");
            return ApiResponse.Fail(500, "服务器内部错误");
        }
    }
}
