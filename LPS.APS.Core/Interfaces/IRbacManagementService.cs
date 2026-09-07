using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// RBAC 管理服务接口（F-G5，3号位）
/// 提供 User/Role/Permission/UserRole/RolePermission/DataScopePolicy 的增删改与分配能力。
/// 所有写入端点由 <c>[Authorize(Policy = PermissionCodes.AuthManage)]</c> 保护（Controller 层强制）。
/// 所有变更方法接收 operatorId（操作人用户 Id），用于审计留痕（GovernanceAuditLog.OperatedBy）。
/// </summary>
public interface IRbacManagementService
{
    // ==================== 用户 ====================

    /// <summary>查询用户列表（不含已删除用户）</summary>
    Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>创建用户（密码 PBKDF2-SHA256 哈希后落库）</summary>
    Task<UserSummaryDto> CreateUserAsync(CreateUserRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>更新用户（含启用/停用）</summary>
    Task UpdateUserAsync(int userId, UpdateUserRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>删除用户（软删除，Status='Deleted'）</summary>
    Task DeleteUserAsync(int userId, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>分配用户角色（覆盖式：先清空再写入）</summary>
    Task AssignUserRolesAsync(int userId, IReadOnlyList<int> roleIds, int operatorId, CancellationToken cancellationToken = default);

    // ==================== 角色 ====================

    /// <summary>查询角色列表（含停用角色）</summary>
    Task<IReadOnlyList<RoleSummaryDto>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>创建角色</summary>
    Task<RoleSummaryDto> CreateRoleAsync(CreateRoleRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>更新角色（含启用/停用）</summary>
    Task UpdateRoleAsync(int roleId, UpdateRoleRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>删除角色（软删除，IsActive=false）</summary>
    Task DeleteRoleAsync(int roleId, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>分配角色权限（覆盖式：先清空再写入）</summary>
    Task AssignRolePermissionsAsync(int roleId, IReadOnlyList<int> permissionIds, int operatorId, CancellationToken cancellationToken = default);

    // ==================== 权限 ====================

    /// <summary>查询权限列表（含停用权限）</summary>
    Task<IReadOnlyList<PermissionSummaryDto>> GetPermissionsAsync(CancellationToken cancellationToken = default);

    /// <summary>创建权限</summary>
    Task<PermissionSummaryDto> CreatePermissionAsync(CreatePermissionRequest request, int operatorId, CancellationToken cancellationToken = default);

    // ==================== 业务范围策略 ====================

    /// <summary>查询业务范围策略列表</summary>
    Task<IReadOnlyList<DataScopePolicyDto>> GetDataScopePoliciesAsync(CancellationToken cancellationToken = default);

    /// <summary>创建业务范围策略</summary>
    Task<DataScopePolicyDto> CreateDataScopePolicyAsync(CreateDataScopePolicyRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>更新业务范围策略说明</summary>
    Task UpdateDataScopePolicyAsync(int policyId, string? description, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>删除业务范围策略（先清引用再删除）</summary>
    Task DeleteDataScopePolicyAsync(int policyId, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>分配用户业务范围（覆盖式）</summary>
    Task AssignUserScopesAsync(int userId, IReadOnlyList<int> policyIds, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>分配角色业务范围（覆盖式）</summary>
    Task AssignRoleScopesAsync(int roleId, IReadOnlyList<int> policyIds, int operatorId, CancellationToken cancellationToken = default);
}
