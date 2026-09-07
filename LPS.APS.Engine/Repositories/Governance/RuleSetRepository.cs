using System.Text;
using Dapper;
using RuleSet = LPS.APS.Core.Entities.APS.RuleSet;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 规则集主表仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.RuleSet
/// 3-4联调接口 G1（A1）：规则集列表查询。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class RuleSetRepository : IRuleSetRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<RuleSetRepository> _logger;

    public RuleSetRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<RuleSetRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RuleSet>> GetListAsync(
        bool? activeOnly = null,
        string? keyword = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        var sql = new StringBuilder(@"
            SELECT * FROM [dbo].[RuleSet]
            WHERE 1 = 1");

        var parameters = new DynamicParameters();
        if (activeOnly.HasValue)
        {
            sql.Append(" AND [IsActive] = @IsActive");
            parameters.Add("IsActive", activeOnly.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql.Append(" AND ([RuleSetCode] LIKE @Keyword OR [RuleSetName] LIKE @Keyword)");
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

        var results = await _connectionManager.QueryAsync<RuleSet>(
            sql.ToString(), parameters, db: DatabaseId.APS);

        return results.ToList();
    }
}
