using System.Text;
using Dapper;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 策略包主表仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.StrategyProfile
/// P0-06：RunType 匹配须经父表 StrategyProfile.RunType（StrategyProfileVersion 无 RunType 列）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class StrategyProfileRepository : IStrategyProfileRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<StrategyProfileRepository> _logger;

    public StrategyProfileRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<StrategyProfileRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<StrategyProfile?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[StrategyProfile]
            WHERE [Id] = @Id";

        return await _connectionManager.QueryFirstOrDefaultAsync<StrategyProfile>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<IReadOnlyList<StrategyProfile>> GetByRunTypeAsync(string runType, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT * FROM [dbo].[StrategyProfile]
            WHERE [RunType] = @RunType
              AND [IsActive] = 1";

        var results = await _connectionManager.QueryAsync<StrategyProfile>(
            sql, new { RunType = runType }, db: DatabaseId.APS);

        return results.ToList();
    }

    public async Task<IReadOnlyList<StrategyProfile>> GetListAsync(
        bool? activeOnly = null,
        string? keyword = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        var sql = new StringBuilder(@"
            SELECT * FROM [dbo].[StrategyProfile]
            WHERE 1 = 1");

        var parameters = new DynamicParameters();
        if (activeOnly.HasValue)
        {
            sql.Append(" AND [IsActive] = @IsActive");
            parameters.Add("IsActive", activeOnly.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql.Append(" AND ([StrategyProfileCode] LIKE @Keyword OR [StrategyProfileName] LIKE @Keyword)");
            parameters.Add("Keyword", $"%{keyword.Trim()}%");
        }

        sql.Append(" ORDER BY [Id] DESC");

        if (skip.HasValue)
        {
            sql.Append(" OFFSET @Skip ROWS");
            parameters.Add("Skip", skip.Value);
        }

        if (take.HasValue)
        {
            sql.Append(" FETCH NEXT @Take ROWS ONLY");
            parameters.Add("Take", take.Value);
        }

        var results = await _connectionManager.QueryAsync<StrategyProfile>(
            sql.ToString(), parameters, db: DatabaseId.APS);

        return results.ToList();
    }
}
