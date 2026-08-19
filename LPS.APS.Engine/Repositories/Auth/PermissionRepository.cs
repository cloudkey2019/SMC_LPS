using Microsoft.EntityFrameworkCore;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Engine.Data;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// Permission 仓储实现（基于 EF Core）
/// </summary>
public class PermissionRepository : IPermissionRepository
{
    private readonly AuthDbContext _context;

    public PermissionRepository(AuthDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Permission?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Permissions.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Permissions
            .Where(p => p.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<Permission> AddAsync(Permission entity, CancellationToken cancellationToken = default)
    {
        await _context.Permissions.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(Permission entity, CancellationToken cancellationToken = default)
    {
        _context.Permissions.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var permission = await GetByIdAsync(id, cancellationToken);
        if (permission != null)
        {
            permission.IsActive = false;
            await UpdateAsync(permission, cancellationToken);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Permission?> GetByPermissionCodeAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        return await _context.Permissions
            .FirstOrDefaultAsync(p => p.PermissionCode == permissionCode && p.IsActive, cancellationToken);
    }

    public async Task<IEnumerable<Permission>> GetPermissionsByRoleAsync(int roleId, CancellationToken cancellationToken = default)
    {
        return await _context.Permissions
            .Where(p => p.IsActive && _context.RolePermissions.Any(rp => rp.RoleId == roleId && rp.PermissionId == p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Permission>> GetPermissionsByUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await (
            from p in _context.Permissions
            join rp in _context.RolePermissions on p.Id equals rp.PermissionId
            join ur in _context.UserRoles on rp.RoleId equals ur.RoleId
            where ur.UserId == userId && p.IsActive
            select p
        ).Distinct().ToListAsync(cancellationToken);
    }
}
