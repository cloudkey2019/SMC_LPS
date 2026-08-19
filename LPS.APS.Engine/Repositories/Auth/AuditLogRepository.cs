using Microsoft.EntityFrameworkCore;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Engine.Data;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// AuditLog 仓储实现（基于 EF Core）
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly AuthDbContext _context;

    public AuditLogRepository(AuthDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<AuditLog?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .OrderByDescending(log => log.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditLog> AddAsync(AuditLog entity, CancellationToken cancellationToken = default)
    {
        await _context.AuditLogs.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task UpdateAsync(AuditLog entity, CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var log = await GetByIdAsync(id, cancellationToken);
        if (log != null)
        {
            _context.AuditLogs.Remove(log);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetLogsByUserAsync(int userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(log => log.UserId == userId)
            .OrderByDescending(log => log.CreatedAt)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetLogsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(log => log.CreatedAt >= startDate && log.CreatedAt <= endDate)
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
