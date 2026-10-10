using LPS.APS.Core.Entities.Auth;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 统一审计日志仓储接口（DDL v1.3 收敛：治理/Candidate/运行/RBAC 审计统一写入 AuditLog 表）。
/// 审计只追加（append-only），不提供更新/删除。
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>追加一条审计记录</summary>
    Task<AuditLog> AddAsync(AuditLog entity, CancellationToken cancellationToken = default);

    /// <summary>预检：确认审计库当前可写（开事务即回滚，不落数据），供治理写操作落库状态前调用。</summary>
    Task EnsureWritableAsync(CancellationToken cancellationToken = default);

    /// <summary>按主键查询</summary>
    Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>按操作人分页查询</summary>
    Task<IEnumerable<AuditLog>> GetLogsByUserAsync(int userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>按时间范围查询</summary>
    Task<IEnumerable<AuditLog>> GetLogsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>按实体类型 + 实体 Id 查询审计（按发生时间倒序）</summary>
    Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>组合条件查询审计（实体类型/实体 Id/时间范围 可空组合，按发生时间倒序，可限条数）</summary>
    Task<IReadOnlyList<AuditLog>> QueryAsync(
        string? entityType = null,
        string? entityId = null,
        DateTime? from = null,
        DateTime? to = null,
        int? take = null,
        CancellationToken cancellationToken = default);

    /// <summary>分页组合查询审计（操作人 Id / 动作 / 时间范围 可空组合，按发生时间倒序分页）</summary>
    Task<IReadOnlyList<AuditLog>> QueryPagedAsync(
        int? userId = null,
        string? action = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}