using Microsoft.EntityFrameworkCore;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Engine.Data;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// Role 仓储实现（基于 EF Core）
/// </summary>
public class RoleRepository : IRoleRepository
{
    private readonly AuthDbContext _context;

    public RoleRepository(AuthDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Role?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Roles.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IEnumerable<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Roles
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken);
    }

    public async Task<Role> AddAsync(Role entity, CancellationToken cancellationToken = default)
    {
        await _context.Roles.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(Role entity, CancellationToken cancellationToken = default)
    {
        _context.Roles.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var role = await GetByIdAsync(id, cancellationToken);
        if (role != null)
        {
            role.IsActive = false;
            await UpdateAsync(role, cancellationToken);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Role?> GetByRoleCodeAsync(string roleCode, CancellationToken cancellationToken = default)
    {
        return await _context.Roles
            .FirstOrDefaultAsync(r => r.RoleCode == roleCode && r.IsActive, cancellationToken);
    }

    public async Task<IEnumerable<Role>> GetRolesByUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.Roles
            .Where(r => r.IsActive && _context.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == r.Id))
            .ToListAsync(cancellationToken);
    }
}
