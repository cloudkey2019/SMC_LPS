using Microsoft.EntityFrameworkCore;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// AuditLog 仓储实现（基于 EF Core，DDL v1.3 统一审计表）。
/// 收敛治理/Candidate/运行/RBAC 审计到统一 AuditLog；追加只读（append-only）。
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly AuthDbContext _context;

    public AuditLogRepository(AuthDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<AuditLog> AddAsync(AuditLog entity, CancellationToken cancellationToken = default)
    {
        await _context.AuditLogs.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task EnsureWritableAsync(CancellationToken cancellationToken = default)
    {
        // P1-03（0号位 再审核 2026-10-10）：真实写探针——事务内 INSERT 一条 PROBE 审计记录并回滚。
        // 原「开事务+回滚」只能证明可连接/可开事务，无法发现只读库、表缺列、触发器阻断 INSERT 等真实写障碍
        // （0号位 源码引用：AuditLogRepository.cs:26-38）。探测失败 → 抛异常，调用方治理 DML 前置整操作失败、业务零变更
        // （fail-before-write）；回滚保证探测零留痕（AuditLog append-only 不受污染）。
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.AuditLogs.AddAsync(new AuditLog
        {
            ActionCode = "PROBE",
            OccurredAt = DateTime.UtcNow,
            Remark = "EnsureWritableAsync 写探针（事务内回滚，零留痕）。",
        }, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        await tx.RollbackAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async Task<IEnumerable<AuditLog>> GetLogsByUserAsync(int userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(log => log.UserId == userId)
            .OrderByDescending(log => log.OccurredAt)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetLogsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(log => log.OccurredAt >= startDate && log.OccurredAt <= endDate)
            .OrderByDescending(log => log.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .Where(log => log.EntityType == entityType && log.EntityId == entityId)
            .OrderByDescending(log => log.OccurredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLog>> QueryAsync(
        string? entityType = null,
        string? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = _context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(log => log.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(log => log.EntityId == entityId);
        }

        if (from.HasValue)
        {
            query = query.Where(log => log.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(log => log.OccurredAt <= to.Value);
        }

        query = query.OrderByDescending(log => log.OccurredAt);

        if (take.HasValue)
        {
            query = query.Take(take.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLog>> QueryPagedAsync(
        int? userId = null,
        string? action = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AuditLog> query = _context.AuditLogs.AsNoTracking();

        if (userId.HasValue)
        {
            query = query.Where(log => log.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(log => log.ActionCode == action);
        }

        if (from.HasValue)
        {
            query = query.Where(log => log.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(log => log.OccurredAt <= to.Value);
        }

        var safePage = Math.Max(page, 1);
        var safeSize = Math.Clamp(pageSize, 1, 200);

        return await query
            .OrderByDescending(log => log.OccurredAt)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .ToListAsync(cancellationToken);
    }
}