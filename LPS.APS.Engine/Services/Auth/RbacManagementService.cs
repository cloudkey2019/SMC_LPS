using Dapper;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Security;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Auth;

/// <summary>
/// RBAC 管理服务实现（F-G5，3号位）
/// 提供 User/Role/Permission/UserRole/RolePermission/DataScopePolicy 的增删改与分配能力。
/// 仅访问 APS_Auth 库（Dapper 直连，与 AuthService 一致，不引入 EF 跟踪开销）。
/// 密码哈希：PBKDF2-SHA256（见 <see cref="PasswordHasher"/>），与 <see cref="AuthService"/> 登录校验一致。
/// 变更审计：写入 <see cref="GovernanceAuditLog"/>（复用既有审计表，best-effort，失败不阻断主流程）。
/// </summary>
public class RbacManagementService : IRbacManagementService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IGovernanceAuditLogRepository _auditRepository;
    private readonly ILogger<RbacManagementService> _logger;

    /// <summary>合法业务范围维度（DDL v1.1 CK_DataScope_Type）</summary>
    private static readonly HashSet<string> ScopeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        DataScopeTypes.Factory, DataScopeTypes.ProductFamily, DataScopeTypes.Department,
        DataScopeTypes.Domain, DataScopeTypes.ResourceOrgGroup, DataScopeTypes.Global
    };

    /// <summary>用户状态白名单（AuthService 仅认 Status='Active'）</summary>
    private static readonly HashSet<string> UserStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active", "Deleted"
    };

    public RbacManagementService(
        DatabaseConnectionManager connectionManager,
        IGovernanceAuditLogRepository auditRepository,
        ILogger<RbacManagementService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ==================== 用户 ====================

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummaryDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<UserSummaryDto>(
            @"SELECT Id, UserCode, UserName, Email, PhoneNumber, FactoryId, DepartmentId,
                     Status, LastLoginTime, CreatedAt
              FROM [User]
              WHERE Status <> 'Deleted'
              ORDER BY Id",
            db: DatabaseId.Auth);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<UserSummaryDto> CreateUserAsync(CreateUserRequest request, int operatorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserCode))
            throw new InvalidOperationException("用户编码不能为空");
        if (string.IsNullOrWhiteSpace(request.UserName))
            throw new InvalidOperationException("用户名称不能为空");
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("密码不能为空");
        if (request.Password.Length < 8 || request.Password.Length > 128)
            throw new InvalidOperationException("密码长度须为 8~128 位");
        if (string.Equals(request.Password, request.UserCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("密码不得与用户编码相同");

        await EnsureUniqueAsync(
            "SELECT COUNT(*) FROM [User] WHERE UserCode = @UserCode",
            new { request.UserCode },
            $"用户编码已存在：{request.UserCode}");

        var newId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [User]
                (UserCode, UserName, PasswordHash, Email, PhoneNumber, FactoryId, DepartmentId, Status, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES
                (@UserCode, @UserName, @PasswordHash, @Email, @PhoneNumber, @FactoryId, @DepartmentId, 'Active', GETDATE(), GETDATE())",
            new
            {
                request.UserCode,
                request.UserName,
                PasswordHash = PasswordHasher.Hash(request.Password),
                request.Email,
                request.PhoneNumber,
                request.FactoryId,
                request.DepartmentId
            },
            db: DatabaseId.Auth);

        _logger.LogInformation("创建用户成功: UserCode={UserCode}, Id={Id}", request.UserCode, newId);

        await WriteAuditAsync("Create", "User", newId, operatorId, versionCode: request.UserCode, afterStatus: "Active");

        return new UserSummaryDto
        {
            Id = newId,
            UserCode = request.UserCode,
            UserName = request.UserName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            FactoryId = request.FactoryId,
            DepartmentId = request.DepartmentId,
            Status = "Active",
            CreatedAt = DateTime.Now
        };
    }

    /// <inheritdoc />
    public async Task UpdateUserAsync(int userId, UpdateUserRequest request, int operatorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
            throw new InvalidOperationException("用户名称不能为空");
        if (!UserStatuses.Contains(request.Status))
            throw new InvalidOperationException($"用户状态非法：{request.Status}（应为 Active / Deleted）");

        await EnsureExistsAsync("SELECT COUNT(*) FROM [User] WHERE Id = @Id", userId, "用户不存在");

        if (request.Status != "Active" && await IsLastAuthManagerAsync(userId))
            throw new InvalidOperationException("不能停用最后一名持有 auth.manage 权限的用户");

        await _connectionManager.ExecuteAsync(
            @"UPDATE [User] SET
                UserName = @UserName, Email = @Email, PhoneNumber = @PhoneNumber,
                FactoryId = @FactoryId, DepartmentId = @DepartmentId,
                Status = @Status, UpdatedAt = GETDATE()
              WHERE Id = @Id",
            new
            {
                Id = userId,
                request.UserName,
                request.Email,
                request.PhoneNumber,
                request.FactoryId,
                request.DepartmentId,
                request.Status
            },
            db: DatabaseId.Auth);

        _logger.LogInformation("更新用户成功: Id={Id}", userId);

        await WriteAuditAsync("Update", "User", userId, operatorId, afterStatus: request.Status);
    }

    /// <inheritdoc />
    public async Task DeleteUserAsync(int userId, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [User] WHERE Id = @Id", userId, "用户不存在");

        if (await IsLastAuthManagerAsync(userId))
            throw new InvalidOperationException("不能删除最后一名持有 auth.manage 权限的用户");

        await _connectionManager.ExecuteAsync(
            "UPDATE [User] SET Status = 'Deleted', UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = userId },
            db: DatabaseId.Auth);

        _logger.LogInformation("软删除用户成功: Id={Id}", userId);

        await WriteAuditAsync("Delete", "User", userId, operatorId, afterStatus: "Deleted");
    }

    /// <inheritdoc />
    public async Task AssignUserRolesAsync(int userId, IReadOnlyList<int> roleIds, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [User] WHERE Id = @Id AND Status <> 'Deleted'", userId, "用户不存在");
        var ids = await EnsureIdsExistAsync(
            "SELECT COUNT(*) FROM [Role] WHERE Id IN @Ids AND IsActive = 1",
            roleIds, "存在无效或已停用的角色 Id");

        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync("DELETE FROM UserRole WHERE UserId = @UserId", new { UserId = userId }, tx);
            foreach (var roleId in ids)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO UserRole (UserId, RoleId, AssignedBy) VALUES (@UserId, @RoleId, @OperatorId)",
                    new { UserId = userId, RoleId = roleId, OperatorId = operatorId }, tx);
            }
            return true;
        }, DatabaseId.Auth);

        _logger.LogInformation("分配用户角色成功: UserId={UserId}, RoleCount={Count}", userId, ids.Count);

        await WriteAuditAsync("AssignRoles", "User", userId, operatorId, remarks: $"分配角色 {ids.Count} 项");
    }

    // ==================== 角色 ====================

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSummaryDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<RoleSummaryDto>(
            @"SELECT Id, RoleCode, RoleName, Description, IsSystemRole, IsActive, CreatedAt
              FROM [Role]
              ORDER BY Id",
            db: DatabaseId.Auth);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<RoleSummaryDto> CreateRoleAsync(CreateRoleRequest request, int operatorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RoleCode))
            throw new InvalidOperationException("角色编码不能为空");
        if (string.IsNullOrWhiteSpace(request.RoleName))
            throw new InvalidOperationException("角色名称不能为空");

        await EnsureUniqueAsync(
            "SELECT COUNT(*) FROM [Role] WHERE RoleCode = @RoleCode",
            new { request.RoleCode },
            $"角色编码已存在：{request.RoleCode}");

        var newId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [Role] (RoleCode, RoleName, Description, IsSystemRole, IsActive, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@RoleCode, @RoleName, @Description, 0, 1, GETDATE(), GETDATE())",
            new { request.RoleCode, request.RoleName, request.Description },
            db: DatabaseId.Auth);

        _logger.LogInformation("创建角色成功: RoleCode={RoleCode}, Id={Id}", request.RoleCode, newId);

        await WriteAuditAsync("Create", "Role", newId, operatorId, versionCode: request.RoleCode, afterStatus: "Active");

        return new RoleSummaryDto
        {
            Id = newId,
            RoleCode = request.RoleCode,
            RoleName = request.RoleName,
            Description = request.Description,
            IsSystemRole = false,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
    }

    /// <inheritdoc />
    public async Task UpdateRoleAsync(int roleId, UpdateRoleRequest request, int operatorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RoleName))
            throw new InvalidOperationException("角色名称不能为空");

        await EnsureExistsAsync("SELECT COUNT(*) FROM [Role] WHERE Id = @Id", roleId, "角色不存在");

        await _connectionManager.ExecuteAsync(
            @"UPDATE [Role] SET RoleName = @RoleName, Description = @Description, IsActive = @IsActive, UpdatedAt = GETDATE()
              WHERE Id = @Id",
            new { Id = roleId, request.RoleName, request.Description, request.IsActive },
            db: DatabaseId.Auth);

        _logger.LogInformation("更新角色成功: Id={Id}", roleId);

        await WriteAuditAsync("Update", "Role", roleId, operatorId, afterStatus: request.IsActive ? "Active" : "Inactive");
    }

    /// <inheritdoc />
    public async Task DeleteRoleAsync(int roleId, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [Role] WHERE Id = @Id", roleId, "角色不存在");

        await _connectionManager.ExecuteAsync(
            "UPDATE [Role] SET IsActive = 0, UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = roleId },
            db: DatabaseId.Auth);

        _logger.LogInformation("软删除角色成功: Id={Id}", roleId);

        await WriteAuditAsync("Delete", "Role", roleId, operatorId, afterStatus: "Inactive");
    }

    /// <inheritdoc />
    public async Task AssignRolePermissionsAsync(int roleId, IReadOnlyList<int> permissionIds, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [Role] WHERE Id = @Id", roleId, "角色不存在");
        var ids = await EnsureIdsExistAsync(
            "SELECT COUNT(*) FROM [Permission] WHERE Id IN @Ids AND IsActive = 1",
            permissionIds, "存在无效或已停用的权限 Id");

        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync("DELETE FROM RolePermission WHERE RoleId = @RoleId", new { RoleId = roleId }, tx);
            foreach (var permissionId in ids)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO RolePermission (RoleId, PermissionId) VALUES (@RoleId, @PermissionId)",
                    new { RoleId = roleId, PermissionId = permissionId }, tx);
            }
            return true;
        }, DatabaseId.Auth);

        _logger.LogInformation("分配角色权限成功: RoleId={RoleId}, PermissionCount={Count}", roleId, ids.Count);

        await WriteAuditAsync("AssignPermissions", "Role", roleId, operatorId, remarks: $"分配权限 {ids.Count} 项");
    }

    // ==================== 权限 ====================

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionSummaryDto>> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<PermissionSummaryDto>(
            @"SELECT Id, PermissionCode, PermissionName, Description, Module, Category, IsActive, CreatedAt
              FROM [Permission]
              ORDER BY Id",
            db: DatabaseId.Auth);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<PermissionSummaryDto> CreatePermissionAsync(CreatePermissionRequest request, int operatorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PermissionCode))
            throw new InvalidOperationException("权限编码不能为空");
        if (string.IsNullOrWhiteSpace(request.PermissionName))
            throw new InvalidOperationException("权限名称不能为空");

        await EnsureUniqueAsync(
            "SELECT COUNT(*) FROM [Permission] WHERE PermissionCode = @PermissionCode",
            new { request.PermissionCode },
            $"权限编码已存在：{request.PermissionCode}");

        var newId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [Permission]
                (PermissionCode, PermissionName, Description, Module, Category, IsActive, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@PermissionCode, @PermissionName, @Description, @Module, @Category, 1, GETDATE(), GETDATE())",
            new
            {
                request.PermissionCode,
                request.PermissionName,
                request.Description,
                request.Module,
                request.Category
            },
            db: DatabaseId.Auth);

        _logger.LogInformation("创建权限成功: PermissionCode={PermissionCode}, Id={Id}", request.PermissionCode, newId);

        await WriteAuditAsync("Create", "Permission", newId, operatorId, versionCode: request.PermissionCode, afterStatus: "Active");

        return new PermissionSummaryDto
        {
            Id = newId,
            PermissionCode = request.PermissionCode,
            PermissionName = request.PermissionName,
            Description = request.Description,
            Module = request.Module,
            Category = request.Category,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
    }

    // ==================== 业务范围策略 ====================

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataScopePolicyDto>> GetDataScopePoliciesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<DataScopePolicyDto>(
            @"SELECT Id, ScopeType, ScopeValue, Description, CreatedAt
              FROM DataScopePolicy
              ORDER BY ScopeType, Id",
            db: DatabaseId.Auth);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<DataScopePolicyDto> CreateDataScopePolicyAsync(CreateDataScopePolicyRequest request, int operatorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ScopeType))
            throw new InvalidOperationException("范围维度不能为空");
        if (string.IsNullOrWhiteSpace(request.ScopeValue))
            throw new InvalidOperationException("范围取值不能为空");
        if (!ScopeTypes.Contains(request.ScopeType))
            throw new InvalidOperationException($"范围维度非法：{request.ScopeType}");

        await EnsureUniqueAsync(
            "SELECT COUNT(*) FROM DataScopePolicy WHERE ScopeType = @ScopeType AND ScopeValue = @ScopeValue",
            new { request.ScopeType, request.ScopeValue },
            $"业务范围策略已存在：{request.ScopeType}/{request.ScopeValue}");

        var newId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES (@ScopeType, @ScopeValue, @Description, GETDATE())",
            new { request.ScopeType, request.ScopeValue, request.Description },
            db: DatabaseId.Auth);

        _logger.LogInformation("创建业务范围策略成功: {ScopeType}/{ScopeValue}, Id={Id}", request.ScopeType, request.ScopeValue, newId);

        await WriteAuditAsync("Create", "DataScopePolicy", newId, operatorId, versionCode: $"{request.ScopeType}/{request.ScopeValue}");

        return new DataScopePolicyDto
        {
            Id = newId,
            ScopeType = request.ScopeType,
            ScopeValue = request.ScopeValue,
            Description = request.Description,
            CreatedAt = DateTime.Now
        };
    }

    /// <inheritdoc />
    public async Task UpdateDataScopePolicyAsync(int policyId, string? description, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM DataScopePolicy WHERE Id = @Id", policyId, "业务范围策略不存在");

        await _connectionManager.ExecuteAsync(
            "UPDATE DataScopePolicy SET Description = @Description WHERE Id = @Id",
            new { Id = policyId, Description = description },
            db: DatabaseId.Auth);

        _logger.LogInformation("更新业务范围策略说明成功: Id={Id}", policyId);

        await WriteAuditAsync("Update", "DataScopePolicy", policyId, operatorId);
    }

    /// <inheritdoc />
    public async Task DeleteDataScopePolicyAsync(int policyId, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM DataScopePolicy WHERE Id = @Id", policyId, "业务范围策略不存在");

        // 先清引用（UserDataScope / RoleDataScope 有 ON DELETE CASCADE，但显式清理更可控）
        await _connectionManager.ExecuteAsync("DELETE FROM UserDataScope WHERE ScopePolicyId = @Id", new { Id = policyId }, db: DatabaseId.Auth);
        await _connectionManager.ExecuteAsync("DELETE FROM RoleDataScope WHERE ScopePolicyId = @Id", new { Id = policyId }, db: DatabaseId.Auth);
        await _connectionManager.ExecuteAsync("DELETE FROM DataScopePolicy WHERE Id = @Id", new { Id = policyId }, db: DatabaseId.Auth);

        _logger.LogInformation("删除业务范围策略成功: Id={Id}", policyId);

        await WriteAuditAsync("Delete", "DataScopePolicy", policyId, operatorId);
    }

    /// <inheritdoc />
    public async Task AssignUserScopesAsync(int userId, IReadOnlyList<int> policyIds, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [User] WHERE Id = @Id AND Status <> 'Deleted'", userId, "用户不存在");
        var ids = await EnsureIdsExistAsync(
            "SELECT COUNT(*) FROM DataScopePolicy WHERE Id IN @Ids",
            policyIds, "存在无效的业务范围策略 Id");

        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync("DELETE FROM UserDataScope WHERE UserId = @UserId", new { UserId = userId }, tx);
            foreach (var policyId in ids)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO UserDataScope (UserId, ScopePolicyId, AssignedBy) VALUES (@UserId, @PolicyId, @OperatorId)",
                    new { UserId = userId, PolicyId = policyId, OperatorId = operatorId }, tx);
            }
            return true;
        }, DatabaseId.Auth);

        _logger.LogInformation("分配用户业务范围成功: UserId={UserId}, ScopeCount={Count}", userId, ids.Count);

        await WriteAuditAsync("AssignScopes", "User", userId, operatorId, remarks: $"分配业务范围 {ids.Count} 项");
    }

    /// <inheritdoc />
    public async Task AssignRoleScopesAsync(int roleId, IReadOnlyList<int> policyIds, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [Role] WHERE Id = @Id", roleId, "角色不存在");
        var ids = await EnsureIdsExistAsync(
            "SELECT COUNT(*) FROM DataScopePolicy WHERE Id IN @Ids",
            policyIds, "存在无效的业务范围策略 Id");

        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync("DELETE FROM RoleDataScope WHERE RoleId = @RoleId", new { RoleId = roleId }, tx);
            foreach (var policyId in ids)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO RoleDataScope (RoleId, ScopePolicyId) VALUES (@RoleId, @PolicyId)",
                    new { RoleId = roleId, PolicyId = policyId }, tx);
            }
            return true;
        }, DatabaseId.Auth);

        _logger.LogInformation("分配角色业务范围成功: RoleId={RoleId}, ScopeCount={Count}", roleId, ids.Count);

        await WriteAuditAsync("AssignScopes", "Role", roleId, operatorId, remarks: $"分配业务范围 {ids.Count} 项");
    }

    // ==================== 私有辅助 ====================

    /// <summary>校验主键存在性，不存在抛 <see cref="KeyNotFoundException"/>。</summary>
    private async Task EnsureExistsAsync(string countSql, int id, string notFoundMessage)
    {
        var count = await _connectionManager.QueryFirstOrDefaultAsync<int?>(countSql, new { Id = id }, db: DatabaseId.Auth) ?? 0;
        if (count == 0)
            throw new KeyNotFoundException(notFoundMessage);
    }

    /// <summary>校验字段唯一性，已存在抛 <see cref="InvalidOperationException"/>。</summary>
    private async Task EnsureUniqueAsync(string countSql, object parameters, string duplicateMessage)
    {
        var count = await _connectionManager.QueryFirstOrDefaultAsync<int?>(countSql, parameters, db: DatabaseId.Auth) ?? 0;
        if (count > 0)
            throw new InvalidOperationException(duplicateMessage);
    }

    /// <summary>校验 Id 列表全部存在，返回去重后的 Id 列表；存在无效 Id 抛 <see cref="InvalidOperationException"/>。</summary>
    private async Task<IReadOnlyList<int>> EnsureIdsExistAsync(string countSql, IReadOnlyList<int> ids, string invalidMessage)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
            return Array.Empty<int>();

        var found = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            countSql, new { Ids = distinct }, db: DatabaseId.Auth) ?? 0;

        if (found != distinct.Count)
            throw new InvalidOperationException(invalidMessage);

        return distinct;
    }

    /// <summary>判定指定用户是否为最后一名持有 auth.manage 权限的活跃用户（用于防止管理员锁死）。</summary>
    private async Task<bool> IsLastAuthManagerAsync(int userId)
    {
        var holds = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            @"SELECT COUNT(*) FROM [User] u
              JOIN UserRole ur ON ur.UserId = u.Id
              JOIN Role r ON r.Id = ur.RoleId AND r.IsActive = 1
              JOIN RolePermission rp ON rp.RoleId = r.Id
              JOIN Permission p ON p.Id = rp.PermissionId AND p.IsActive = 1
              WHERE u.Id = @UserId AND u.Status = 'Active' AND p.PermissionCode = @AuthManage",
            new { UserId = userId, AuthManage = PermissionCodes.AuthManage }, db: DatabaseId.Auth) ?? 0;

        if (holds == 0)
            return false;

        var total = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            @"SELECT COUNT(DISTINCT u.Id) FROM [User] u
              JOIN UserRole ur ON ur.UserId = u.Id
              JOIN Role r ON r.Id = ur.RoleId AND r.IsActive = 1
              JOIN RolePermission rp ON rp.RoleId = r.Id
              JOIN Permission p ON p.Id = rp.PermissionId AND p.IsActive = 1
              WHERE u.Status = 'Active' AND p.PermissionCode = @AuthManage",
            new { AuthManage = PermissionCodes.AuthManage }, db: DatabaseId.Auth) ?? 0;

        return total <= 1;
    }

    /// <summary>
    /// 写审计（复用 GovernanceAuditLog，best-effort：表不可用时仅告警不阻断主流程）。
    /// </summary>
    private async Task WriteAuditAsync(
        string operationType,
        string entityType,
        long entityId,
        int operatorId,
        string? versionCode = null,
        string? afterStatus = null,
        string? remarks = null)
    {
        try
        {
            await _auditRepository.AddAsync(new GovernanceAuditLog
            {
                OperationType = operationType,
                EntityType = entityType,
                EntityId = entityId,
                VersionCode = versionCode,
                AfterStatus = afterStatus,
                OperatedBy = operatorId.ToString(),
                OperatedAt = DateTime.Now,
                Remarks = remarks
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "RBAC 审计写入失败（GovernanceAuditLog 可能未就绪）：{OperationType} {EntityType} {EntityId}",
                operationType, entityType, entityId);
        }
    }
}
