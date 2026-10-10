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
    public string ActionType { get; set; } = string.Empty;
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
    string? PhoneNumber = null);

/// <summary>更新用户请求（Status 取值 Active / Deleted）</summary>
public sealed record UpdateUserRequest(
    string UserName,
    string Status,
    string? Email = null,
    string? PhoneNumber = null);

/// <summary>创建角色请求（RoleCode 须 aps. 前缀，否则 400 而非 500 SQL CHECK 冲突）</summary>
public sealed record CreateRoleRequest(
    [System.ComponentModel.DataAnnotations.RegularExpression("^aps\\..+", ErrorMessage = "角色编码必须以 aps. 前缀开头")]
    string RoleCode,
    string RoleName,
    string? Description = null);

/// <summary>更新角色请求（IsActive 用于启用/停用）</summary>
public sealed record UpdateRoleRequest(
    string RoleName,
    bool IsActive,
    string? Description = null);

/// <summary>创建权限请求（PermissionCode 须 aps. 前缀，否则 400 而非 500 SQL CHECK 冲突）</summary>
public sealed record CreatePermissionRequest(
    [System.ComponentModel.DataAnnotations.RegularExpression("^aps\\..+", ErrorMessage = "权限编码必须以 aps. 前缀开头")]
    string PermissionCode,
    string PermissionName,
    string Module,
    string ActionType,
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

/// <summary>测试数据清理结果（R1 方案 a 响应）</summary>
public class TestDataCleanupResult
{
    public int Users { get; set; }
    public int Roles { get; set; }
    public int Permissions { get; set; }
    public IReadOnlyList<TestDataCleanupFailure> Failed { get; set; } = Array.Empty<TestDataCleanupFailure>();
}

/// <summary>测试数据清理失败项（kind=user/role/permission）</summary>
public sealed record TestDataCleanupFailure(string Kind, int Id, string Reason);

/// <summary>批量删除请求</summary>
public sealed record BatchDeleteRequest(IReadOnlyList<int> Ids);

/// <summary>批量删除结果（succeeded=成功 Id，failed=逐条失败原因）</summary>
public class BatchDeleteResult
{
    public IReadOnlyList<int> Succeeded { get; set; } = Array.Empty<int>();
    public IReadOnlyList<BatchDeleteFailure> Failed { get; set; } = Array.Empty<BatchDeleteFailure>();
}

/// <summary>批量删除失败项（kind=user/role/permission/scope，对齐 cleanup 的 {kind,id,reason} 结构）</summary>
public sealed record BatchDeleteFailure(string Kind, int Id, string Reason);
