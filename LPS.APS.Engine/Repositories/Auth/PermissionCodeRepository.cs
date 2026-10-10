using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.EntityFrameworkCore;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// 用户权限码只读查询（Auth 域，F-G3）
/// UserRole → RolePermission → Permission 关联，返回有效 PermissionCode 去重集合。
/// </summary>
public class PermissionCodeRepository : IPermissionCodeRepository
{
    private readonly AuthDbContext _context;

    public PermissionCodeRepository(AuthDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetPermissionCodesByUserIdAsync(int userId, CancellationToken ct = default)
    {
        var codes = await (
            from p in _context.Permissions
            join rp in _context.RolePermissions on p.Id equals rp.PermissionId
            join ur in _context.UserRoles on rp.RoleId equals ur.RoleId
            join r in _context.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId && p.IsActive && r.IsActive
            select p.PermissionCode
        ).Distinct().ToListAsync(ct);

        return codes;
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(int userId, string permissionCode, CancellationToken ct = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
            return false;

        // M1：单码 EXISTS（常量大小、走索引），保留每次请求命中 DB 权威、撤销即时生效。
        return await (
            from p in _context.Permissions
            join rp in _context.RolePermissions on p.Id equals rp.PermissionId
            join ur in _context.UserRoles on rp.RoleId equals ur.RoleId
            join r in _context.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId && p.IsActive && r.IsActive && p.PermissionCode == permissionCode
            select p.Id
        ).AnyAsync(ct);
    }
}
