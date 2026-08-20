using Dapper;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// PlanVersion 治理仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.PlanVersion（冻结 DDL v5.1.2 §3.2）
/// 边界：仅更新确认/激活列（Status / ActivatedAt / ActivatedBy）；不重写 2号位结果持久化流转。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class PlanVersionRepository : IPlanVersionRepository
{
    private const string PlanVersionActiveStatus = "ACTIVE";

    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<PlanVersionRepository> _logger;

    public PlanVersionRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<PlanVersionRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<PlanVersion?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[PlanVersion]
            WHERE [Id] = @Id";

        return await _connectionManager.QueryFirstOrDefaultAsync<PlanVersion>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<PlanVersion?> GetActiveByDomainKeyAsync(string domainKey, int? exceptPlanVersionId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[PlanVersion]
            WHERE [DomainKey] = @DomainKey
              AND [Status] = @ActiveStatus
              AND (@ExceptId IS NULL OR [Id] <> @ExceptId)";

        return await _connectionManager.QueryFirstOrDefaultAsync<PlanVersion>(
            sql,
            new { DomainKey = domainKey, ActiveStatus = PlanVersionActiveStatus, ExceptId = exceptPlanVersionId },
            db: DatabaseId.APS);
    }

    public async Task UpdateAsync(PlanVersion version, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[PlanVersion]
            SET [Status]       = @Status,
                [ActivatedAt]  = @ActivatedAt,
                [ActivatedBy]  = @ActivatedBy
            WHERE [Id] = @Id";

        await _connectionManager.ExecuteAsync(
            sql,
            new
            {
                version.Id,
                version.Status,
                version.ActivatedAt,
                version.ActivatedBy,
            },
            db: DatabaseId.APS);
    }

    public async Task<PlanVersion?> GetLatestByScheduleRunIdAsync(int scheduleRunId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP 1 * FROM [dbo].[PlanVersion]
            WHERE [SourceScheduleRunId] = @ScheduleRunId
            ORDER BY [CreatedAt] DESC, [Id] DESC";

        return await _connectionManager.QueryFirstOrDefaultAsync<PlanVersion>(
            sql, new { ScheduleRunId = scheduleRunId }, db: DatabaseId.APS);
    }
}
