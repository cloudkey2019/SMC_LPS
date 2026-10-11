using Microsoft.EntityFrameworkCore;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// AuditLog 仓储实现（基于 EF Core，DDL v1.3 统一审计表）。
/// 收敛治理/Candidate/运行/RBAC 审计到统一 AuditLog；追加只读（append-only）。
/// R-01（0号位 复审 v1.1，方案B）：AddAsync 写入失败落对账日志（AuditReconcile）后原样重抛——
/// 覆盖全部审计写入服务的「业务已写、审计缺」残余窗口（跨库无 2PC，预检仅缩小窗口）。
/// </summary>
public class AuditLogRepository : IAuditLogRepository
{
    private readonly AuthDbContext _context;
    private readonly ILogger<AuditLogRepository> _logger;

    public AuditLogRepository(AuthDbContext context, ILogger<AuditLogRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<AuditLog> AddAsync(AuditLog entity, CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.AuditLogs.AddAsync(entity, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return entity;
        }
        catch (Exception ex)
        {
            // R-01（0号位 复审 v1.1，方案B）：审计写失败——业务状态通常已落库（跨库无 2PC），
            // 落完整对账日志（含全部审计字段 JSON，供运维/审计页人工补录）后原样重抛（P1-05 fail-closed 不静默）。
            AuditReconcile.Log(_logger, ex, entity);
            _context.ChangeTracker.Clear(); // 清除失败后残留的跟踪实体，避免污染同 Scoped 上下文后续操作
            throw;
        }
    }

    public async Task EnsureWritableAsync(CancellationToken cancellationToken = default)
    {
        // P1-03（0号位 再审核 2026-10-10）：真实写探针——事务内 INSERT 一条 PROBE 审计记录并回滚。
        // 原「开事务+回滚」只能证明可连接/可开事务，无法发现只读库、表缺列、触发器阻断 INSERT 等真实写障碍
        // （0号位 源码引用：AuditLogRepository.cs:26-38）。探测失败 → 抛异常，调用方治理 DML 前置整操作失败、业务零变更
        // （fail-before-write）；回滚保证探测零留痕（AuditLog append-only 不受污染）。
        // Web 端 AuthDbContext 开启 EnableRetryOnFailure（DatabaseServiceExtensions.cs），
        // EF Core 的 RetryingExecutionStrategy 禁止用户自启事务（SCOPE-04 实测 Web 端预检恒 400）——
        // 须经 CreateExecutionStrategy() 包裹（官方唯一合法形态），无 retry 的测试环境等同直接执行，两态兼容。
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
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
        });
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