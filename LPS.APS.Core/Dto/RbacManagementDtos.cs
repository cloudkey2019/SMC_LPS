namespace LPS.APS.Core.Dto;

/// <summary>
/// RBAC 管理端点 DTO（F-G5，3号位）
/// 响应 DTO 用可写类以便 Dapper 投影；请求 DTO 用不可变 record 绑定 JSON。
/// 响应 DTO 一律不含 PasswordHash，避免密码哈希泄露到前端。
/// </summary>

/// <summary>用户摘要 DTO（不含密码哈希）</summary>
public class UserSummaryDto
{
    public int Id { get; set; }
    public string UserCode { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public int? FactoryId { get; set; }
    public int? DepartmentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastLoginTime { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>角色摘要 DTO</summary>
public class RoleSummaryDto
{
    public int Id { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>权限摘要 DTO</summary>
public class PermissionSummaryDto
{
    public int Id { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>业务范围策略摘要 DTO</summary>
public class DataScopePolicyDto
{
    public int Id { get; set; }
    public string ScopeType { get; set; } = string.Empty;
    public string ScopeValue { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>创建用户请求</summary>
public sealed record CreateUserRequest(
    string UserCode,
    string UserName,
    string Password,
    string? Email = null,
    string? PhoneNumber = null,
    int? FactoryId = null,
    int? DepartmentId = null);

/// <summary>更新用户请求（Status 取值 Active / Deleted）</summary>
public sealed record UpdateUserRequest(
    string UserName,
    string Status,
    string? Email = null,
    string? PhoneNumber = null,
    int? FactoryId = null,
    int? DepartmentId = null);

/// <summary>创建角色请求</summary>
public sealed record CreateRoleRequest(
    string RoleCode,
    string RoleName,
    string? Description = null);

/// <summary>更新角色请求（IsActive 用于启用/停用）</summary>
public sealed record UpdateRoleRequest(
    string RoleName,
    bool IsActive,
    string? Description = null);

/// <summary>创建权限请求</summary>
public sealed record CreatePermissionRequest(
    string PermissionCode,
    string PermissionName,
    string Module,
    string Category,
    string? Description = null);

/// <summary>创建业务范围策略请求</summary>
public sealed record CreateDataScopePolicyRequest(
    string ScopeType,
    string ScopeValue,
    string? Description = null);

/// <summary>批量 Id 分配请求（角色 / 权限 / 业务范围策略）</summary>
public sealed record AssignIdsRequest(IReadOnlyList<int> Ids);

/// <summary>更新业务范围策略请求（仅说明可改）</summary>
public sealed record UpdateDataScopePolicyRequest(string? Description = null);
