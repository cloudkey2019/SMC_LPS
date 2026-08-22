using System.Data;
using Dapper;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 规则集版本仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.RuleSetVersion
/// </summary>
public class RuleSetVersionRepository : IRuleSetVersionRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<RuleSetVersionRepository> _logger;

    public RuleSetVersionRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<RuleSetVersionRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<RuleSetVersion?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[RuleSetVersion]
            WHERE [Id] = @Id";

        return await _connectionManager.QueryFirstOrDefaultAsync<RuleSetVersion>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<IReadOnlyList<RuleSetVersion>> GetByRuleSetIdAsync(long ruleSetId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[RuleSetVersion]
            WHERE [RuleSetId] = @RuleSetId
            ORDER BY [VersionCode]";

        var results = await _connectionManager.QueryAsync<RuleSetVersion>(
            sql, new { RuleSetId = ruleSetId }, db: DatabaseId.APS);

        return results.ToList();
    }

    public async Task<RuleSetVersion> AddAsync(RuleSetVersion version, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [dbo].[RuleSetVersion]
                ([RuleSetId], [VersionCode], [Status], [ContentSnapshotJson],
                 [EffectiveFrom], [EffectiveTo],
                 [PublishedAt], [PublishedBy], [ApprovedAt], [ApprovedBy],
                 [CreatedAt], [CreatedBy])
            VALUES
                (@RuleSetId, @VersionCode, @Status, @ContentSnapshotJson,
                 @EffectiveFrom, @EffectiveTo,
                 @PublishedAt, @PublishedBy, @ApprovedAt, @ApprovedBy,
                 @CreatedAt, @CreatedBy);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

        var id = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            sql, version, db: DatabaseId.APS);

        version.Id = id;
        return version;
    }

    public async Task UpdateAsync(RuleSetVersion version, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[RuleSetVersion]
            SET [VersionCode] = @VersionCode,
                [Status] = @Status,
                [ContentSnapshotJson] = @ContentSnapshotJson,
                [EffectiveFrom] = @EffectiveFrom,
                [EffectiveTo] = @EffectiveTo,
                [PublishedAt] = @PublishedAt,
                [PublishedBy] = @PublishedBy,
                [ApprovedAt] = @ApprovedAt,
                [ApprovedBy] = @ApprovedBy
            WHERE [Id] = @Id";

        await _connectionManager.ExecuteAsync(sql, version, db: DatabaseId.APS);
    }
}
