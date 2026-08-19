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
                ([RuleSetId], [VersionCode], [Status], [DemandPriorityJson],
                 [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy],
                 [PublishedAt], [PublishedBy], [IsDefault], [Remarks])
            VALUES
                (@RuleSetId, @VersionCode, @Status, @DemandPriorityJson,
                 @CreatedAt, @CreatedBy, @UpdatedAt, @UpdatedBy,
                 @PublishedAt, @PublishedBy, @IsDefault, @Remarks);
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
                [DemandPriorityJson] = @DemandPriorityJson,
                [UpdatedAt] = @UpdatedAt,
                [UpdatedBy] = @UpdatedBy,
                [PublishedAt] = @PublishedAt,
                [PublishedBy] = @PublishedBy,
                [IsDefault] = @IsDefault,
                [Remarks] = @Remarks
            WHERE [Id] = @Id";

        await _connectionManager.ExecuteAsync(sql, version, db: DatabaseId.APS);
    }

    /// <summary>
    /// 清除同 RuleSet 内其他版本的 IsDefault 标记（A-6 不变量：同 Set 内唯一默认）
    /// </summary>
    public async Task ClearDefaultFlagAsync(long ruleSetId, long exceptVersionId, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[RuleSetVersion]
            SET [IsDefault] = 0
            WHERE [RuleSetId] = @RuleSetId
              AND [Id] != @ExceptVersionId
              AND [IsDefault] = 1";

        await _connectionManager.ExecuteAsync(
            sql, new { RuleSetId = ruleSetId, ExceptVersionId = exceptVersionId }, db: DatabaseId.APS);
    }

    /// <summary>
    /// 查询默认版本（同 RuleSet 内 IsDefault=true 的版本）
    /// </summary>
    public async Task<RuleSetVersion?> GetDefaultByRuleSetIdAsync(long ruleSetId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP 1 * FROM [dbo].[RuleSetVersion]
            WHERE [RuleSetId] = @RuleSetId
              AND [IsDefault] = 1
            ORDER BY [PublishedAt] DESC";

        return await _connectionManager.QueryFirstOrDefaultAsync<RuleSetVersion>(
            sql, new { RuleSetId = ruleSetId }, db: DatabaseId.APS);
    }
}
