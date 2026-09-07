using System.Text;
using Dapper;
using ParameterSet = LPS.APS.Core.Entities.APS.ParameterSet;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 参数集主表仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.ParameterSet
/// 3-4联调接口 G1（A8）：参数集列表查询。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class ParameterSetRepository : IParameterSetRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<ParameterSetRepository> _logger;

    public ParameterSetRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<ParameterSetRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ParameterSet>> GetListAsync(
        bool? activeOnly = null,
        string? keyword = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        var sql = new StringBuilder(@"
            SELECT * FROM [dbo].[ParameterSet]
            WHERE 1 = 1");

        var parameters = new DynamicParameters();
        if (activeOnly.HasValue)
        {
            sql.Append(" AND [IsActive] = @IsActive");
            parameters.Add("IsActive", activeOnly.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql.Append(" AND ([ParameterSetCode] LIKE @Keyword OR [ParameterSetName] LIKE @Keyword)");
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

        var results = await _connectionManager.QueryAsync<ParameterSet>(
            sql.ToString(), parameters, db: DatabaseId.APS);

        return results.ToList();
    }
}
