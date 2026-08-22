using System.Data;
using Dapper;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 参数集版本仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.ParameterSetVersion
/// </summary>
public class ParameterSetVersionRepository : IParameterSetVersionRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<ParameterSetVersionRepository> _logger;

    public ParameterSetVersionRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<ParameterSetVersionRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<ParameterSetVersion?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[ParameterSetVersion]
            WHERE [Id] = @Id";

        return await _connectionManager.QueryFirstOrDefaultAsync<ParameterSetVersion>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<IReadOnlyList<ParameterSetVersion>> GetByParameterSetIdAsync(long parameterSetId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[ParameterSetVersion]
            WHERE [ParameterSetId] = @ParameterSetId
            ORDER BY [VersionCode]";

        var results = await _connectionManager.QueryAsync<ParameterSetVersion>(
            sql, new { ParameterSetId = parameterSetId }, db: DatabaseId.APS);

        return results.ToList();
    }

    public async Task<ParameterSetVersion> AddAsync(ParameterSetVersion version, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [dbo].[ParameterSetVersion]
                ([ParameterSetId], [VersionCode], [Status], [ContentSnapshotJson],
                 [EffectiveFrom], [EffectiveTo],
                 [PublishedAt], [PublishedBy], [ApprovedAt], [ApprovedBy],
                 [CreatedAt], [CreatedBy])
            VALUES
                (@ParameterSetId, @VersionCode, @Status, @ContentSnapshotJson,
                 @EffectiveFrom, @EffectiveTo,
                 @PublishedAt, @PublishedBy, @ApprovedAt, @ApprovedBy,
                 @CreatedAt, @CreatedBy);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);";

        var id = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            sql, version, db: DatabaseId.APS);

        version.Id = id;
        return version;
    }

    public async Task UpdateAsync(ParameterSetVersion version, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[ParameterSetVersion]
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
