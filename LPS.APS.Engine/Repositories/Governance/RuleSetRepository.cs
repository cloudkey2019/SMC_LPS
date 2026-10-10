using System.Text;
using Dapper;
using LPS.APS.Core.DTOs.Governance;
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

    public async Task<IReadOnlyList<RuleSetListItemDto>> GetListWithVersionAsync(
        bool? activeOnly = null,
        string? keyword = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        // R2（催办单 2026-09-22）：列表主表 LEFT JOIN 最新版本摘要。
        // 版本摘要语义：
        //  - CurrentVersionCode = 最新 PUBLISHED 版本码
        //  - DraftVersionCode   = 最新 DRAFT 版本码
        //  - Status / LastChangeReason / PublishedAt / RetiredAt(EffectiveTo) = 最新版本
        // DomainKey 无真源（线上无规则集↔域关联），未纳入（已提报 0号位 裁决）。
        var sql = new StringBuilder(@"
            SELECT r.[Id], r.[RuleSetCode], r.[RuleSetName], r.[Description], r.[IsActive],
                   r.[CreatedAt], r.[CreatedBy], r.[UpdatedAt], r.[UpdatedBy],
                   v.CurrentVersionCode, v.DraftVersionCode, v.Status, v.LastChangeReason,
                   v.PublishedAt, v.RetiredAt
            FROM [dbo].[RuleSet] r
            OUTER APPLY (
                SELECT TOP 1
                       (SELECT TOP 1 [VersionCode] FROM [dbo].[RuleSetVersion]
                         WHERE [RuleSetId] = r.[Id] AND [Status] = 'PUBLISHED'
                         ORDER BY [Id] DESC) AS CurrentVersionCode,
                       (SELECT TOP 1 [VersionCode] FROM [dbo].[RuleSetVersion]
                         WHERE [RuleSetId] = r.[Id] AND [Status] = 'DRAFT'
                         ORDER BY [Id] DESC) AS DraftVersionCode,
                       [Status], [Remarks] AS LastChangeReason, [PublishedAt],
                       [EffectiveTo] AS RetiredAt
                FROM [dbo].[RuleSetVersion]
                WHERE [RuleSetId] = r.[Id]
                ORDER BY [Id] DESC
            ) v
            WHERE 1 = 1");

        var parameters = new DynamicParameters();
        if (activeOnly.HasValue)
        {
            sql.Append(" AND r.[IsActive] = @IsActive");
            parameters.Add("IsActive", activeOnly.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql.Append(" AND (r.[RuleSetCode] LIKE @Keyword OR r.[RuleSetName] LIKE @Keyword)");
            parameters.Add("Keyword", $"%{keyword.Trim()}%");
        }

        sql.Append(" ORDER BY r.[Id] DESC");

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

        var results = await _connectionManager.QueryAsync<RuleSetListItemDto>(
            sql.ToString(), parameters, db: DatabaseId.APS);

        return results.ToList();
    }
}
