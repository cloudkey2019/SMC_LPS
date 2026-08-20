using System.Data;
using Dapper;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 策略包版本仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.StrategyProfileVersion
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class StrategyProfileVersionRepository : IStrategyProfileVersionRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<StrategyProfileVersionRepository> _logger;

    public StrategyProfileVersionRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<StrategyProfileVersionRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<StrategyProfileVersion?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[StrategyProfileVersion]
            WHERE [Id] = @Id";

        return await _connectionManager.QueryFirstOrDefaultAsync<StrategyProfileVersion>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<IReadOnlyList<StrategyProfileVersion>> GetByStrategyProfileIdAsync(long strategyProfileId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[StrategyProfileVersion]
            WHERE [StrategyProfileId] = @StrategyProfileId
            ORDER BY [VersionCode]";

        var results = await _connectionManager.QueryAsync<StrategyProfileVersion>(
            sql, new { StrategyProfileId = strategyProfileId }, db: DatabaseId.APS);

        return results.ToList();
    }

    public async Task<StrategyProfileVersion?> GetDefaultByStrategyProfileIdAsync(long strategyProfileId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[StrategyProfileVersion]
            WHERE [StrategyProfileId] = @StrategyProfileId
              AND [IsDefault] = 1
              AND [Status] = 'PUBLISHED'";

        return await _connectionManager.QueryFirstOrDefaultAsync<StrategyProfileVersion>(
            sql, new { StrategyProfileId = strategyProfileId }, db: DatabaseId.APS);
    }

    public async Task<StrategyProfileVersion> AddAsync(StrategyProfileVersion version, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [dbo].[StrategyProfileVersion]
            ([StrategyProfileId], [VersionCode], [RuleSetVersionId], [ParameterSetVersionId],
             [Status], [EffectiveFrom], [EffectiveTo], [IsDefault],
             [PublishedAt], [PublishedBy], [ApprovedAt], [ApprovedBy],
             [CreatedAt], [CreatedBy])
            VALUES
            (@StrategyProfileId, @VersionCode, @RuleSetVersionId, @ParameterSetVersionId,
             @Status, @EffectiveFrom, @EffectiveTo, @IsDefault,
             @PublishedAt, @PublishedBy, @ApprovedAt, @ApprovedBy,
             @CreatedAt, @CreatedBy);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

        var id = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            sql, version, db: DatabaseId.APS);
        version.Id = id;
        return version;
    }

    public async Task UpdateAsync(StrategyProfileVersion version, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[StrategyProfileVersion]
            SET [VersionCode] = @VersionCode,
                [RuleSetVersionId] = @RuleSetVersionId,
                [ParameterSetVersionId] = @ParameterSetVersionId,
                [Status] = @Status,
                [EffectiveFrom] = @EffectiveFrom,
                [EffectiveTo] = @EffectiveTo,
                [IsDefault] = @IsDefault,
                [PublishedAt] = @PublishedAt,
                [PublishedBy] = @PublishedBy,
                [ApprovedAt] = @ApprovedAt,
                [ApprovedBy] = @ApprovedBy
            WHERE [Id] = @Id";

        await _connectionManager.ExecuteAsync(sql, version, db: DatabaseId.APS);
    }

    public async Task<IReadOnlyList<StrategyProfileVersion>> GetDefaultByRunTypeAsync(string runType, CancellationToken ct = default)
    {
        // P0-06：RunType 在父表 StrategyProfile；返回所有候选默认版本全集（不 TOP 1 随机取），
        // 由 Application 层结合 EffectiveFrom/EffectiveTo 过滤后判定 0/1/多（多 → 歧义报错）
        const string sql = @"
            SELECT v.*
            FROM [dbo].[StrategyProfileVersion] v
            JOIN [dbo].[StrategyProfile] p ON p.Id = v.StrategyProfileId
            WHERE v.[IsDefault] = 1
              AND v.[Status] = 'PUBLISHED'
              AND p.[RunType] = @RunType
              AND p.[IsActive] = 1";

        var results = await _connectionManager.QueryAsync<StrategyProfileVersion>(
            sql, new { RunType = runType }, db: DatabaseId.APS);

        return results.ToList();
    }

    public async Task ClearDefaultFlagAsync(long strategyProfileId, long exceptVersionId, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[StrategyProfileVersion]
            SET [IsDefault] = 0
            WHERE [StrategyProfileId] = @StrategyProfileId
              AND [Id] <> @ExceptVersionId
              AND [IsDefault] = 1";

        await _connectionManager.ExecuteAsync(
            sql,
            new { StrategyProfileId = strategyProfileId, ExceptVersionId = exceptVersionId },
            db: DatabaseId.APS);
    }
}
