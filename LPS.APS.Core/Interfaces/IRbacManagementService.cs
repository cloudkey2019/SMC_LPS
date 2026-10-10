using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// RBAC 管理服务接口（F-G5，3号位）
/// 提供 User/Role/Permission/UserRole/RolePermission/DataScopePolicy 的增删改与分配能力。
/// 所有写入端点由 <c>[Authorize(Policy = PermissionCodes.AuthManage)]</c> 保护（Controller 层强制）。
/// 所有变更方法接收 operatorId（操作人用户 Id），用于审计留痕（AuditLog.UserCode）。
/// </summary>
public interface IRbacManagementService
{
    // ==================== 用户 ====================

    /// <summary>查询用户列表（分页；不含已删除用户。R2 契约）</summary>
    Task<PageResult<UserSummaryDto>> GetUsersPagedAsync(int page, int pageSize, string? keyword, string? status, CancellationToken cancellationToken = default);

    /// <summary>创建用户（密码 PBKDF2-SHA256 哈希后落库）</summary>
    Task<UserSummaryDto> CreateUserAsync(CreateUserRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>更新用户（含启用/停用）</summary>
    Task UpdateUserAsync(int userId, UpdateUserRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>删除用户（软删除，Status='Deleted'）</summary>
    Task DeleteUserAsync(int userId, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>分配用户角色（覆盖式：先清空再写入）</summary>
    Task AssignUserRolesAsync(int userId, IReadOnlyList<int> roleIds, int operatorId, CancellationToken cancellationToken = default);

    // ==================== 角色 ====================

    /// <summary>查询角色列表（分页；含停用角色。R2 契约）</summary>
    Task<PageResult<RoleSummaryDto>> GetRolesPagedAsync(int page, int pageSize, string? keyword, bool? isSystem, CancellationToken cancellationToken = default);

    /// <summary>创建角色</summary>
    Task<RoleSummaryDto> CreateRoleAsync(CreateRoleRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>更新角色（含启用/停用）</summary>
    Task UpdateRoleAsync(int roleId, UpdateRoleRequest request, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>删除角色（软删除，IsActive=false）</summary>
    Task DeleteRoleAsync(int roleId, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>分配角色权限（覆盖式：先清空再写入）</summary>
    Task AssignRolePermissionsAsync(int roleId, IReadOnlyList<int> permissionIds, int operatorId, CancellationToken cancellationToken = default);

    // ==================== 权限 ====================

    /// <summary>查询权限列表（分页；含停用权限。R2 契约）</summary>
    Task<PageResult<PermissionSummaryDto>> GetPermissionsPagedAsync(int page, int pageSize, string? module, string? actionType, string? keyword, CancellationToken cancellationToken = default);

    /// <summary>创建权限</summary>
    Task<PermissionSummaryDto> CreatePermissionAsync(CreatePermissionRequest request, int operatorId, CancellationToken cancellationToken = default);

    // ==================== 业务范围策略 ====================

    /// <summary>查询业务范围策略列表（分页；仅启用的策略。R2 契约）</summary>
    Task<PageResult<DataScopePolicyDto>> GetScopesPagedAsync(int page, int pageSize, string? scopeType, string? keyword, CancellationToken cancellationToken = default);

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

    // ==================== 读回当前分配 ====================

    /// <summary>读回用户当前角色分配（仅 IsActive=1 角色）</summary>
    Task<IReadOnlyList<RoleSummaryDto>> GetUserRolesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>读回用户当前业务范围分配（仅 IsEnabled=1 策略）</summary>
    Task<IReadOnlyList<DataScopePolicyDto>> GetUserScopesAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>读回角色当前权限分配（仅 IsActive=1 权限）</summary>
    Task<IReadOnlyList<PermissionSummaryDto>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default);

    /// <summary>读回角色当前业务范围分配（仅 IsEnabled=1 策略）</summary>
    Task<IReadOnlyList<DataScopePolicyDto>> GetRoleScopesAsync(int roleId, CancellationToken cancellationToken = default);

    // ==================== 测试数据清理与批量删除（R1 / R4） ====================

    /// <summary>清理测试数据（按命名规则软删 verify 残留：用户/角色软删，权限停用）</summary>
    Task<TestDataCleanupResult> CleanupTestDataAsync(int operatorId, CancellationToken cancellationToken = default);

    /// <summary>批量删除用户（软删；自删保护 + 最后 auth.manage 保护）</summary>
    Task<BatchDeleteResult> DeleteUsersBatchAsync(IReadOnlyList<int> userIds, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>批量删除角色（软删；系统角色保护）</summary>
    Task<BatchDeleteResult> DeleteRolesBatchAsync(IReadOnlyList<int> roleIds, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>批量删除权限（停用 IsActive=false，保留 RolePermission 关联，不解绑）</summary>
    Task<BatchDeleteResult> DeletePermissionsBatchAsync(IReadOnlyList<int> permissionIds, int operatorId, CancellationToken cancellationToken = default);

    /// <summary>批量删除业务范围策略（停用 IsEnabled=0）</summary>
    Task<BatchDeleteResult> DeleteScopesBatchAsync(IReadOnlyList<int> policyIds, int operatorId, CancellationToken cancellationToken = default);
}