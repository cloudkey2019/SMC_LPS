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
                ([ParameterSetId], [VersionCode], [Status], [LockJson], [SupplyJson], [ProcurementJson],
                 [CreatedAt], [CreatedBy], [UpdatedAt], [UpdatedBy],
                 [PublishedAt], [PublishedBy], [IsDefault], [Remarks])
            VALUES
                (@ParameterSetId, @VersionCode, @Status, @LockJson, @SupplyJson, @ProcurementJson,
                 @CreatedAt, @CreatedBy, @UpdatedAt, @UpdatedBy,
                 @PublishedAt, @PublishedBy, @IsDefault, @Remarks);
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
                [LockJson] = @LockJson,
                [SupplyJson] = @SupplyJson,
                [ProcurementJson] = @ProcurementJson,
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
    /// 清除同 ParameterSet 内其他版本的 IsDefault 标记（A-6 不变量：同 Set 内唯一默认）
    /// </summary>
    public async Task ClearDefaultFlagAsync(long parameterSetId, long exceptVersionId, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[ParameterSetVersion]
            SET [IsDefault] = 0
            WHERE [ParameterSetId] = @ParameterSetId
              AND [Id] != @ExceptVersionId
              AND [IsDefault] = 1";

        await _connectionManager.ExecuteAsync(
            sql, new { ParameterSetId = parameterSetId, ExceptVersionId = exceptVersionId }, db: DatabaseId.APS);
    }

    /// <summary>
    /// 查询默认版本（同 ParameterSet 内 IsDefault=true 的版本）
    /// </summary>
    public async Task<ParameterSetVersion?> GetDefaultByParameterSetIdAsync(long parameterSetId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP 1 * FROM [dbo].[ParameterSetVersion]
            WHERE [ParameterSetId] = @ParameterSetId
              AND [IsDefault] = 1
            ORDER BY [PublishedAt] DESC";

        return await _connectionManager.QueryFirstOrDefaultAsync<ParameterSetVersion>(
            sql, new { ParameterSetId = parameterSetId }, db: DatabaseId.APS);
    }
}
