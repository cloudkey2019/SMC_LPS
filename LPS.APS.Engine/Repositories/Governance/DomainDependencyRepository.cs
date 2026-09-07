using Dapper;
using DomainDependency = LPS.APS.Core.Entities.APS.DomainDependency;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 域依赖关系仓储实现（Dapper + APS_Production，只读）
/// 对应表：APS_Production.dbo.Domain_Dependency（2号位 sp_ScanDomainDependency 扫描落库）
/// 3-4联调接口 G7：失败链 / 域依赖关系查询。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class DomainDependencyRepository : IDomainDependencyRepository
{
    private const string DirectionDownstream = "downstream";
    private const string DirectionUpstream = "upstream";

    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<DomainDependencyRepository> _logger;

    public DomainDependencyRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<DomainDependencyRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DomainDependency>> GetByDomainAsync(
        string domainCode,
        string direction,
        CancellationToken ct = default)
    {
        var normalizedDirection = string.Equals(direction, DirectionUpstream, StringComparison.OrdinalIgnoreCase)
            ? DirectionUpstream
            : DirectionDownstream;

        // downstream：查询以该域为上游的依赖（本域的下游）
        // upstream  ：查询以该域为下游的依赖（本域的上游）
        var sql = normalizedDirection == DirectionUpstream
            ? @"SELECT * FROM [dbo].[Domain_Dependency]
                WHERE [DownstreamDomainCode] = @DomainCode
                ORDER BY [UpstreamDomainCode], [ChildMaterialCode]"
            : @"SELECT * FROM [dbo].[Domain_Dependency]
                WHERE [UpstreamDomainCode] = @DomainCode
                ORDER BY [DownstreamDomainCode], [ChildMaterialCode]";

        var results = await _connectionManager.QueryAsync<DomainDependency>(
            sql, new { DomainCode = domainCode }, db: DatabaseId.APS);

        return results.ToList();
    }
}
