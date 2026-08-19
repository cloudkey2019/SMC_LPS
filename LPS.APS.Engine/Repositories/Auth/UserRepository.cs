using Microsoft.EntityFrameworkCore;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Engine.Data;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// User 仓储实现（基于 EF Core）
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly AuthDbContext _context;

    public UserRepository(AuthDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Users.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Where(u => u.Status != "Deleted")
            .ToListAsync(cancellationToken);
    }

    public async Task<User> AddAsync(User entity, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(User entity, CancellationToken cancellationToken = default)
    {
        _context.Users.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await GetByIdAsync(id, cancellationToken);
        if (user != null)
        {
            user.Status = "Deleted";
            await UpdateAsync(user, cancellationToken);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<User?> GetByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.UserName == userName && u.Status != "Deleted", cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.Status != "Deleted", cancellationToken);
    }

    public async Task<IEnumerable<User>> GetUsersByRoleAsync(int roleId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Where(u => u.Status != "Deleted" && _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId))
            .ToListAsync(cancellationToken);
    }
}
