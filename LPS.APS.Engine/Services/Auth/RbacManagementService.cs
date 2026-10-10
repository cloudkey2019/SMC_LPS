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
/// 变更审计：写入 <see cref="AuditLog"/>（复用既有审计表，P1-05 fail-closed：审计失败即抛，不静默吞掉）。
/// </summary>
public class RbacManagementService : IRbacManagementService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IAuditLogRepository _auditRepository;
    private readonly ILogger<RbacManagementService> _logger;

    /// <summary>合法业务范围维度（DDL v1.3 CK_DataScope_Type）</summary>
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
        IAuditLogRepository auditRepository,
        ILogger<RbacManagementService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ==================== 用户 ====================

    /// <inheritdoc />
    public async Task<PageResult<UserSummaryDto>> GetUsersPagedAsync(int page, int pageSize, string? keyword, string? status, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();

        if (normalizedStatus is null)
            conditions.Add("IsDeleted = 0");                                   // 默认不含已删除
        else if (normalizedStatus.Equals("Active", StringComparison.OrdinalIgnoreCase))
            conditions.Add("IsDeleted = 0 AND IsEnabled = 1");
        else if (normalizedStatus.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            conditions.Add("IsDeleted = 0 AND IsEnabled = 0");
        else if (normalizedStatus.Equals("Deleted", StringComparison.OrdinalIgnoreCase))
            conditions.Add("IsDeleted = 1");
        else
            conditions.Add("IsDeleted = 0");                                   // 未知状态值宽容回退

        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add("(LoginName LIKE @Keyword OR DisplayName LIKE @Keyword OR Email LIKE @Keyword)");
            parameters.Add("@Keyword", $"%{keyword.Trim()}%");
        }

        var where = "WHERE " + string.Join(" AND ", conditions);

        return await QueryPagedAsync<UserSummaryDto>(
            @"Id, LoginName AS UserCode, DisplayName AS UserName, Email, PhoneNumber,
              CASE WHEN IsDeleted = 1 THEN 'Deleted'
                   WHEN IsEnabled = 0 THEN 'Disabled'
                   ELSE 'Active' END AS Status,
              LastLoginAt AS LastLoginTime, CreatedAt",
            $"FROM [User] {where}", "Id", page, pageSize, parameters);
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
            "SELECT COUNT(*) FROM [User] WHERE LoginName = @UserCode",
            new { request.UserCode },
            $"用户编码已存在：{request.UserCode}");

        var newId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [User]
                (LoginName, DisplayName, PasswordHash, Email, PhoneNumber, IsEnabled, IsDeleted, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES
                (@UserCode, @UserName, @PasswordHash, @Email, @PhoneNumber, 1, 0, GETDATE(), GETDATE())",
            new
            {
                request.UserCode,
                request.UserName,
                PasswordHash = PasswordHasher.Hash(request.Password),
                request.Email,
                request.PhoneNumber
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
                DisplayName = @UserName, Email = @Email, PhoneNumber = @PhoneNumber,
                IsEnabled = CASE WHEN @Status = 'Active' THEN 1 ELSE 0 END,
                IsDeleted = CASE WHEN @Status = 'Deleted' THEN 1 ELSE 0 END,
                RefreshToken = CASE WHEN @Status = 'Active' THEN RefreshToken ELSE NULL END,
                RefreshTokenExpiry = CASE WHEN @Status = 'Active' THEN RefreshTokenExpiry ELSE NULL END,
                UpdatedAt = GETDATE()
              WHERE Id = @Id",
            new
            {
                Id = userId,
                request.UserName,
                request.Email,
                request.PhoneNumber,
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
            "UPDATE [User] SET IsDeleted = 1, IsEnabled = 0, RefreshToken = NULL, RefreshTokenExpiry = NULL, UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = userId },
            db: DatabaseId.Auth);

        _logger.LogInformation("软删除用户成功: Id={Id}", userId);

        await WriteAuditAsync("Delete", "User", userId, operatorId, afterStatus: "Deleted");
    }

    /// <inheritdoc />
    public async Task AssignUserRolesAsync(int userId, IReadOnlyList<int> roleIds, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [User] WHERE Id = @Id AND IsDeleted = 0", userId, "用户不存在");
        var ids = await EnsureIdsExistAsync(
            "SELECT COUNT(*) FROM [Role] WHERE Id IN @Ids AND IsActive = 1",
            roleIds, "存在无效或已停用的角色 Id");

        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync("DELETE FROM UserRole WHERE UserId = @UserId", new { UserId = userId }, tx);
            foreach (var roleId in ids)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO UserRole (UserId, RoleId, AssignedAt, AssignedBy) VALUES (@UserId, @RoleId, GETDATE(), @OperatorId)",
                    new { UserId = userId, RoleId = roleId, OperatorId = operatorId }, tx);
            }
            return true;
        }, DatabaseId.Auth);

        _logger.LogInformation("分配用户角色成功: UserId={UserId}, RoleCount={Count}", userId, ids.Count);

        await WriteAuditAsync("AssignRoles", "User", userId, operatorId, remarks: $"分配角色 {ids.Count} 项");
    }

    // ==================== 角色 ====================

    /// <inheritdoc />
    public async Task<PageResult<RoleSummaryDto>> GetRolesPagedAsync(int page, int pageSize, string? keyword, bool? isSystem, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (isSystem.HasValue)
        {
            conditions.Add("IsSystemRole = @IsSystemRole");
            parameters.Add("@IsSystemRole", isSystem.Value);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add("(RoleCode LIKE @Keyword OR RoleName LIKE @Keyword)");
            parameters.Add("@Keyword", $"%{keyword.Trim()}%");
        }

        var where = conditions.Count == 0 ? "WHERE 1 = 1" : "WHERE " + string.Join(" AND ", conditions);

        return await QueryPagedAsync<RoleSummaryDto>(
            "Id, RoleCode, RoleName, Description, IsSystemRole, IsActive, CreatedAt",
            $"FROM [Role] {where}", "Id", page, pageSize, parameters);
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
        await EnsureRoleDeletableAsync(roleId);

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
                    "INSERT INTO RolePermission (RoleId, PermissionId, AssignedAt) VALUES (@RoleId, @PermissionId, GETDATE())",
                    new { RoleId = roleId, PermissionId = permissionId }, tx);
            }
            return true;
        }, DatabaseId.Auth);

        _logger.LogInformation("分配角色权限成功: RoleId={RoleId}, PermissionCount={Count}", roleId, ids.Count);

        await WriteAuditAsync("AssignPermissions", "Role", roleId, operatorId, remarks: $"分配权限 {ids.Count} 项");
    }

    // ==================== 权限 ====================

    /// <inheritdoc />
    public async Task<PageResult<PermissionSummaryDto>> GetPermissionsPagedAsync(int page, int pageSize, string? module, string? actionType, string? keyword, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(module))
        {
            conditions.Add("Module = @Module");
            parameters.Add("@Module", module.Trim());
        }
        if (!string.IsNullOrWhiteSpace(actionType))
        {
            conditions.Add("ActionType = @ActionType");
            parameters.Add("@ActionType", actionType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add("(PermissionCode LIKE @Keyword OR PermissionName LIKE @Keyword)");
            parameters.Add("@Keyword", $"%{keyword.Trim()}%");
        }

        var where = conditions.Count == 0 ? "WHERE 1 = 1" : "WHERE " + string.Join(" AND ", conditions);

        return await QueryPagedAsync<PermissionSummaryDto>(
            "Id, PermissionCode, PermissionName, Description, Module, ActionType, IsActive, CreatedAt",
            $"FROM [Permission] {where}", "Module, Id", page, pageSize, parameters);
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
                (PermissionCode, PermissionName, Description, Module, ActionType, IsActive, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@PermissionCode, @PermissionName, @Description, @Module, @ActionType, 1, GETDATE(), GETDATE())",
            new
            {
                request.PermissionCode,
                request.PermissionName,
                request.Description,
                request.Module,
                request.ActionType
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
            ActionType = request.ActionType,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
    }

    // ==================== 业务范围策略 ====================

    /// <inheritdoc />
    public async Task<PageResult<DataScopePolicyDto>> GetScopesPagedAsync(int page, int pageSize, string? scopeType, string? keyword, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { "IsEnabled = 1" };
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(scopeType))
        {
            conditions.Add("ScopeType = @ScopeType");
            parameters.Add("@ScopeType", scopeType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add("(ScopeValue LIKE @Keyword OR Description LIKE @Keyword)");
            parameters.Add("@Keyword", $"%{keyword.Trim()}%");
        }

        var where = "WHERE " + string.Join(" AND ", conditions);

        return await QueryPagedAsync<DataScopePolicyDto>(
            "Id, ScopeType, ScopeValue, Description, CreatedAt",
            $"FROM DataScopePolicy {where}", "Id", page, pageSize, parameters);
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
            @"INSERT INTO DataScopePolicy (ScopeType, ScopeValue, Description, IsEnabled, CreatedAt, UpdatedAt)
              OUTPUT INSERTED.Id
              VALUES (@ScopeType, @ScopeValue, @Description, 1, GETDATE(), GETDATE())",
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
            "UPDATE DataScopePolicy SET Description = @Description, UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = policyId, Description = description },
            db: DatabaseId.Auth);

        _logger.LogInformation("更新业务范围策略说明成功: Id={Id}", policyId);

        await WriteAuditAsync("Update", "DataScopePolicy", policyId, operatorId);
    }

    /// <inheritdoc />
    public async Task DeleteDataScopePolicyAsync(int policyId, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM DataScopePolicy WHERE Id = @Id", policyId, "业务范围策略不存在");

        // D-2 裁决：正式「删除」收敛为「停用」，不物理删除、不级联清理引用，
        // 保留 UserDataScope / RoleDataScope 关联与历史，停用后不再参与分配与授权。
        await _connectionManager.ExecuteAsync(
            "UPDATE DataScopePolicy SET IsEnabled = 0, UpdatedAt = GETDATE() WHERE Id = @Id",
            new { Id = policyId }, db: DatabaseId.Auth);

        _logger.LogInformation("停用业务范围策略成功: Id={Id}", policyId);

        await WriteAuditAsync("Disable", "DataScopePolicy", policyId, operatorId, afterStatus: "Inactive");
    }

    /// <inheritdoc />
    public async Task AssignUserScopesAsync(int userId, IReadOnlyList<int> policyIds, int operatorId, CancellationToken cancellationToken = default)
    {
        await EnsureExistsAsync("SELECT COUNT(*) FROM [User] WHERE Id = @Id AND IsDeleted = 0", userId, "用户不存在");
        var ids = await EnsureIdsExistAsync(
            "SELECT COUNT(*) FROM DataScopePolicy WHERE Id IN @Ids AND IsEnabled = 1",
            policyIds, "存在无效的业务范围策略 Id");

        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync("DELETE FROM UserDataScope WHERE UserId = @UserId", new { UserId = userId }, tx);
            foreach (var policyId in ids)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO UserDataScope (UserId, ScopePolicyId, AssignedAt, AssignedBy) VALUES (@UserId, @PolicyId, GETDATE(), @OperatorId)",
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
            "SELECT COUNT(*) FROM DataScopePolicy WHERE Id IN @Ids AND IsEnabled = 1",
            policyIds, "存在无效的业务范围策略 Id");

        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync("DELETE FROM RoleDataScope WHERE RoleId = @RoleId", new { RoleId = roleId }, tx);
            foreach (var policyId in ids)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO RoleDataScope (RoleId, ScopePolicyId, AssignedAt) VALUES (@RoleId, @PolicyId, GETDATE())",
                    new { RoleId = roleId, PolicyId = policyId }, tx);
            }
            return true;
        }, DatabaseId.Auth);

        _logger.LogInformation("分配角色业务范围成功: RoleId={RoleId}, ScopeCount={Count}", roleId, ids.Count);

        await WriteAuditAsync("AssignScopes", "Role", roleId, operatorId, remarks: $"分配业务范围 {ids.Count} 项");
    }

    // ==================== 读回当前分配 ====================

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSummaryDto>> GetUserRolesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<RoleSummaryDto>(
            @"SELECT r.Id, r.RoleCode, r.RoleName, r.Description, r.IsSystemRole, r.IsActive, r.CreatedAt
              FROM [Role] r
              INNER JOIN UserRole ur ON ur.RoleId = r.Id
              WHERE ur.UserId = @UserId AND r.IsActive = 1
              ORDER BY r.Id",
            new { UserId = userId }, db: DatabaseId.Auth);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataScopePolicyDto>> GetUserScopesAsync(int userId, CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<DataScopePolicyDto>(
            @"SELECT p.Id, p.ScopeType, p.ScopeValue, p.Description, p.CreatedAt
              FROM DataScopePolicy p
              INNER JOIN UserDataScope uds ON uds.ScopePolicyId = p.Id
              WHERE uds.UserId = @UserId AND p.IsEnabled = 1
              ORDER BY p.ScopeType, p.Id",
            new { UserId = userId }, db: DatabaseId.Auth);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionSummaryDto>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<PermissionSummaryDto>(
            @"SELECT p.Id, p.PermissionCode, p.PermissionName, p.Description, p.Module, p.ActionType, p.IsActive, p.CreatedAt
              FROM [Permission] p
              INNER JOIN RolePermission rp ON rp.PermissionId = p.Id
              WHERE rp.RoleId = @RoleId AND p.IsActive = 1
              ORDER BY p.Id",
            new { RoleId = roleId }, db: DatabaseId.Auth);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DataScopePolicyDto>> GetRoleScopesAsync(int roleId, CancellationToken cancellationToken = default)
    {
        var rows = await _connectionManager.QueryAsync<DataScopePolicyDto>(
            @"SELECT p.Id, p.ScopeType, p.ScopeValue, p.Description, p.CreatedAt
              FROM DataScopePolicy p
              INNER JOIN RoleDataScope rds ON rds.ScopePolicyId = p.Id
              WHERE rds.RoleId = @RoleId AND p.IsEnabled = 1
              ORDER BY p.ScopeType, p.Id",
            new { RoleId = roleId }, db: DatabaseId.Auth);

        return rows.ToList();
    }

    // ==================== 测试数据清理与批量删除（R1 / R4） ====================

    /// <inheritdoc />
    public async Task<TestDataCleanupResult> CleanupTestDataAsync(int operatorId, CancellationToken cancellationToken = default)
    {
        // R1 方案 a：仅按命名规则软删/停用 verify 残留，不带任意 id 列表（fail-closed）。
        var users = await _connectionManager.ExecuteAsync(
            @"UPDATE [User] SET IsDeleted = 1, IsEnabled = 0, UpdatedAt = GETDATE()
              WHERE IsDeleted = 0
                AND Id <> @OperatorId
                AND (LoginName LIKE 'TEST-USER-%' OR LoginName LIKE 'aps.auth.negate.%')",
            new { OperatorId = operatorId },
            db: DatabaseId.Auth);

        var roles = await _connectionManager.ExecuteAsync(
            "UPDATE [Role] SET IsActive = 0, UpdatedAt = GETDATE() WHERE IsActive = 1 AND RoleCode LIKE 'aps.verify.role.%'",
            db: DatabaseId.Auth);

        var permissions = await _connectionManager.ExecuteAsync(
            "UPDATE [Permission] SET IsActive = 0, UpdatedAt = GETDATE() WHERE IsActive = 1 AND PermissionCode LIKE 'aps.verify.%'",
            db: DatabaseId.Auth);

        _logger.LogInformation(
            "清理测试数据完成: 用户={Users}, 角色={Roles}, 权限={Permissions}",
            users, roles, permissions);

        await WriteAuditAsync("CleanupTestData", "TestData", 0, operatorId,
            remarks: $"users={users}, roles={roles}, permissions={permissions}");

        return new TestDataCleanupResult
        {
            Users = users,
            Roles = roles,
            Permissions = permissions,
            Failed = Array.Empty<TestDataCleanupFailure>()
        };
    }

    /// <inheritdoc />
    public async Task<BatchDeleteResult> DeleteUsersBatchAsync(IReadOnlyList<int> userIds, int operatorId, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<int>();
        var failed = new List<BatchDeleteFailure>();

        foreach (var userId in userIds.Distinct())
        {
            try
            {
                await DeleteUserAsync(userId, operatorId, cancellationToken);
                succeeded.Add(userId);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                failed.Add(new BatchDeleteFailure("user", userId, ex.Message));
            }
        }

        return new BatchDeleteResult { Succeeded = succeeded, Failed = failed };
    }

    /// <inheritdoc />
    public async Task<BatchDeleteResult> DeleteRolesBatchAsync(IReadOnlyList<int> roleIds, int operatorId, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<int>();
        var failed = new List<BatchDeleteFailure>();

        foreach (var roleId in roleIds.Distinct())
        {
            try
            {
                // 复用单删（含系统角色保护，单删/批量已收敛）
                await DeleteRoleAsync(roleId, operatorId, cancellationToken);
                succeeded.Add(roleId);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                failed.Add(new BatchDeleteFailure("role", roleId, ex.Message));
            }
        }

        return new BatchDeleteResult { Succeeded = succeeded, Failed = failed };
    }

    /// <inheritdoc />
    public async Task<BatchDeleteResult> DeletePermissionsBatchAsync(IReadOnlyList<int> permissionIds, int operatorId, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<int>();
        var failed = new List<BatchDeleteFailure>();

        // 内置权限码保护：V1 功能权限码（PermissionCodes.All，43 码）不可停用，防止功能授权失效/管理员自锁。
        var builtIns = new HashSet<string>(PermissionCodes.All, StringComparer.OrdinalIgnoreCase);

        // 权限批量删除 = 停用（IsActive=false，保留 RolePermission 关联，不解绑），与 R1 清理语义一致。
        foreach (var permissionId in permissionIds.Distinct())
        {
            try
            {
                var code = await _connectionManager.QueryFirstOrDefaultAsync<string?>(
                    "SELECT PermissionCode FROM [Permission] WHERE Id = @Id", new { Id = permissionId }, db: DatabaseId.Auth);
                if (code is null)
                    throw new KeyNotFoundException("权限不存在");
                if (builtIns.Contains(code))
                    throw new InvalidOperationException($"内置权限码不可停用：{code}");

                await _connectionManager.ExecuteAsync(
                    "UPDATE [Permission] SET IsActive = 0, UpdatedAt = GETDATE() WHERE Id = @Id",
                    new { Id = permissionId }, db: DatabaseId.Auth);
                await WriteAuditAsync("Disable", "Permission", permissionId, operatorId, afterStatus: "Inactive");
                succeeded.Add(permissionId);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                failed.Add(new BatchDeleteFailure("permission", permissionId, ex.Message));
            }
        }

        return new BatchDeleteResult { Succeeded = succeeded, Failed = failed };
    }

    /// <inheritdoc />
    public async Task<BatchDeleteResult> DeleteScopesBatchAsync(IReadOnlyList<int> policyIds, int operatorId, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<int>();
        var failed = new List<BatchDeleteFailure>();

        foreach (var policyId in policyIds.Distinct())
        {
            try
            {
                await DeleteDataScopePolicyAsync(policyId, operatorId, cancellationToken);
                succeeded.Add(policyId);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                failed.Add(new BatchDeleteFailure("scope", policyId, ex.Message));
            }
        }

        return new BatchDeleteResult { Succeeded = succeeded, Failed = failed };
    }

    // ==================== 私有辅助 ====================

    /// <summary>通用分页查询：COUNT 总数 + OFFSET/FETCH 取当前页（R2 契约，pageSize 上限 200）。</summary>
    private async Task<PageResult<T>> QueryPagedAsync<T>(
        string columnList,
        string fromClause,
        string orderBy,
        int page,
        int pageSize,
        DynamicParameters parameters)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;

        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var total = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            $"SELECT COUNT(*) {fromClause}", parameters, db: DatabaseId.Auth) ?? 0;

        var rows = await _connectionManager.QueryAsync<T>(
            $"SELECT {columnList} {fromClause} ORDER BY {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            parameters, db: DatabaseId.Auth);

        return new PageResult<T>
        {
            Items = rows.ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>校验主键存在性，不存在抛 <see cref="KeyNotFoundException"/>。</summary>
    private async Task EnsureExistsAsync(string countSql, int id, string notFoundMessage)
    {
        var count = await _connectionManager.QueryFirstOrDefaultAsync<int?>(countSql, new { Id = id }, db: DatabaseId.Auth) ?? 0;
        if (count == 0)
            throw new KeyNotFoundException(notFoundMessage);
    }

    /// <summary>校验角色可停用：不存在抛 <see cref="KeyNotFoundException"/>，系统角色抛 <see cref="InvalidOperationException"/>（单删与批量共用，收敛分叉）。</summary>
    private async Task EnsureRoleDeletableAsync(int roleId)
    {
        var isSystem = await _connectionManager.QueryFirstOrDefaultAsync<bool?>(
            "SELECT IsSystemRole FROM [Role] WHERE Id = @Id", new { Id = roleId }, db: DatabaseId.Auth);
        if (isSystem is null)
            throw new KeyNotFoundException("角色不存在");
        if (isSystem.Value)
            throw new InvalidOperationException($"系统角色不可停用：Id={roleId}");
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
              WHERE u.Id = @UserId AND u.IsEnabled = 1 AND u.IsDeleted = 0 AND p.PermissionCode = @AuthManage",
            new { UserId = userId, AuthManage = PermissionCodes.AuthManage }, db: DatabaseId.Auth) ?? 0;

        if (holds == 0)
            return false;

        var total = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            @"SELECT COUNT(DISTINCT u.Id) FROM [User] u
              JOIN UserRole ur ON ur.UserId = u.Id
              JOIN Role r ON r.Id = ur.RoleId AND r.IsActive = 1
              JOIN RolePermission rp ON rp.RoleId = r.Id
              JOIN Permission p ON p.Id = rp.PermissionId AND p.IsActive = 1
              WHERE u.IsEnabled = 1 AND u.IsDeleted = 0 AND p.PermissionCode = @AuthManage",
            new { AuthManage = PermissionCodes.AuthManage }, db: DatabaseId.Auth) ?? 0;

        return total <= 1;
    }

    /// <summary>
    /// 写审计（复用 AuditLog，P1-05 fail-closed：审计失败即抛，保证权限变更可追溯）。
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
            await _auditRepository.AddAsync(new AuditLog
            {
                ActionCode = operationType,
                EntityType = entityType,
                EntityId = entityId.ToString(),
                VersionCode = versionCode,
                NewValue = afterStatus,
                UserId = operatorId,
                UserCode = operatorId.ToString(),
                OccurredAt = DateTime.Now,
                Remark = remarks
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "RBAC 审计写入失败（AuditLog 可能未就绪）：{ActionCode} {EntityType} {EntityId}",
                operationType, entityType, entityId);
            throw;
        }
    }
}
