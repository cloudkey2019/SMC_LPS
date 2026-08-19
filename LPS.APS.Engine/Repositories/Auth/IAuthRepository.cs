using LPS.APS.Core.Entities.Auth;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// Auth 库通用仓储接口（基于 EF Core）
/// </summary>
public interface IAuthRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<T> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// User 仓储接口
/// </summary>
public interface IUserRepository : IAuthRepository<User>
{
    Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetUsersByRoleAsync(int roleId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Role 仓储接口
/// </summary>
public interface IRoleRepository : IAuthRepository<Role>
{
    Task<Role?> GetByRoleCodeAsync(string roleCode, CancellationToken cancellationToken = default);
    Task<IEnumerable<Role>> GetRolesByUserAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Permission 仓储接口
/// </summary>
public interface IPermissionRepository : IAuthRepository<Permission>
{
    Task<Permission?> GetByPermissionCodeAsync(string permissionCode, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> GetPermissionsByRoleAsync(int roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Permission>> GetPermissionsByUserAsync(int userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// AuditLog 仓储接口
/// </summary>
public interface IAuditLogRepository : IAuthRepository<AuditLog>
{
    Task<IEnumerable<AuditLog>> GetLogsByUserAsync(int userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetLogsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
}
