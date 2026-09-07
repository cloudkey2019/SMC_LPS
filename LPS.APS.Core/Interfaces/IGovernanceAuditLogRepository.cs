using LPS.APS.Core.Entities.Auth;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 治理审计日志仓储接口（阶段 A-7：3号位 Core 层接口）
/// 对应 APS_Auth.dbo.GovernanceAuditLog（EF Core 实现）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IGovernanceAuditLogRepository
{
    /// <summary>记录治理版本操作审计日志</summary>
    Task AddAsync(GovernanceAuditLog log, CancellationToken ct = default);

    /// <summary>查询指定实体的审计日志（按操作时间倒序）</summary>
    Task<IReadOnlyList<GovernanceAuditLog>> GetByEntityAsync(string entityType, long entityId, CancellationToken ct = default);

    /// <summary>查询指定时间范围内的审计日志</summary>
    Task<IReadOnlyList<GovernanceAuditLog>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>组合条件查询审计日志（G6：实体类型/实体ID/时间范围 可空组合，按操作时间倒序，可限条数）</summary>
    Task<IReadOnlyList<GovernanceAuditLog>> QueryAsync(
        string? entityType = null,
        long? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        int? take = null,
        CancellationToken ct = default);
}
