using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;

namespace LPS.APS.Engine.Repositories.Auth;

/// <summary>
/// 治理审计日志仓储实现（阶段 A-7：3号位 Engine 层 EF Core 实现）
/// 对应 APS_Auth.dbo.GovernanceAuditLog
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class GovernanceAuditLogRepository : IGovernanceAuditLogRepository
{
    private readonly AuthDbContext _context;
    private readonly ILogger<GovernanceAuditLogRepository> _logger;

    public GovernanceAuditLogRepository(
        AuthDbContext context,
        ILogger<GovernanceAuditLogRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>记录治理版本操作审计日志</summary>
    public async Task AddAsync(GovernanceAuditLog log, CancellationToken ct = default)
    {
        _context.GovernanceAuditLogs.Add(log);
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation("治理审计日志已记录：{OperationType} {EntityType} {EntityId}",
            log.OperationType, log.EntityType, log.EntityId);
    }

    /// <summary>查询指定实体的审计日志（按操作时间倒序）</summary>
    public async Task<IReadOnlyList<GovernanceAuditLog>> GetByEntityAsync(
        string entityType,
        long entityId,
        CancellationToken ct = default)
    {
        return await _context.GovernanceAuditLogs
            .Where(log => log.EntityType == entityType && log.EntityId == entityId)
            .OrderByDescending(log => log.OperatedAt)
            .ToListAsync(ct);
    }

    /// <summary>查询指定时间范围内的审计日志</summary>
    public async Task<IReadOnlyList<GovernanceAuditLog>> GetByTimeRangeAsync(
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        return await _context.GovernanceAuditLogs
            .Where(log => log.OperatedAt >= from && log.OperatedAt <= to)
            .OrderByDescending(log => log.OperatedAt)
            .ToListAsync(ct);
    }
}
