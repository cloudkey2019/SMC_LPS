using Dapper;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// CTP 域键解析器实现（APS V1 CTP 评估接口契约登记 v1.0 §3.2）。
/// 直读 APS_Production（Material → DomainDefinition），不新建 Engine 仓储（2号位 边界，3号位 直读先例见 RoutingOperationGovernanceService）。
/// 红线 #4：物料映射查询返回列表，多域歧义 / 无匹配返回 null（调用方转 400），不盲目 .First()。
/// </summary>
/// <remarks>开发者：3号位（CTP 薄层契约）。</remarks>
public sealed class DomainResolver : IDomainResolver
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<DomainResolver> _logger;

    public DomainResolver(DatabaseConnectionManager connectionManager, ILogger<DomainResolver> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(string materialCode, string factoryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(materialCode) || string.IsNullOrWhiteSpace(factoryCode))
        {
            return null;
        }

        // 解析链：Material.ProductFamilyId → DomainDefinition
        //   - ScopeType='FAMILY'（全工厂归域，FactoryId 须为空）
        //   - ScopeType='FACTORY_FAMILY'（工厂+产品族域，FactoryId 匹配 Factory.Code）
        // 过滤 IsActive=1；排序 SortOrder/DomainKey 保证唯一匹配时确定性。
        const string sql = """
            SELECT d.DomainKey
            FROM DomainDefinition d
            INNER JOIN Material m ON m.ProductFamilyId = d.ProductFamilyId
            WHERE m.MaterialCode = @MaterialCode
              AND d.IsActive = 1
              AND (
                   (d.ScopeType = 'FAMILY' AND d.FactoryId IS NULL)
                   OR (d.ScopeType = 'FACTORY_FAMILY'
                       AND EXISTS (SELECT 1 FROM Factory f
                                   WHERE f.Id = d.FactoryId AND f.Code = @FactoryCode AND f.IsActive = 1))
                  )
            ORDER BY d.SortOrder, d.DomainKey
            """;

        var rows = await _connectionManager.QueryAsync<string>(
            sql,
            new { MaterialCode = materialCode, FactoryCode = factoryCode },
            db: DatabaseId.APS);

        var domainKeys = rows.ToList();

        // 红线 #4：唯一匹配才返回；0 或 >1（歧义）均返回 null → 调用方 400「无法自动确定 DomainKey」
        if (domainKeys.Count != 1)
        {
            _logger.LogWarning(
                "CTP 域键解析未收敛：MaterialCode={MaterialCode}, FactoryCode={FactoryCode}, 命中数={Count}",
                materialCode, factoryCode, domainKeys.Count);
            return null;
        }

        return domainKeys[0];
    }
}
