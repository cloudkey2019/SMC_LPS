using Dapper;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// DomainDefinition 只读服务实现（2号位职责）
/// 对应表：APS_Production.dbo.DomainDefinition（冻结 DDL v5.1.4 §2.4aa）
/// 边界：只读 IsActive=1 的 Domain 配置；治理（增删改）归 3号位，本服务不写。
/// </summary>
public class DomainDefinitionService : IDomainDefinitionService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<DomainDefinitionService> _logger;

    public DomainDefinitionService(
        DatabaseConnectionManager connectionManager,
        ILogger<DomainDefinitionService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DomainDefinitionInfo>> GetActiveDomainsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT DomainKey, ScopeType, ProductFamilyId, FactoryId
            FROM DomainDefinition
            WHERE IsActive = 1
            ORDER BY SortOrder, DomainKey";

        var rows = await _connectionManager.QueryAsync<DomainDefinitionRow>(sql, db: DatabaseId.APS);
        return rows.Select(r => new DomainDefinitionInfo(r.DomainKey, r.ScopeType, r.ProductFamilyId, r.FactoryId)).ToList();
    }

    private sealed class DomainDefinitionRow
    {
        public string DomainKey { get; set; } = string.Empty;
        public string ScopeType { get; set; } = string.Empty;
        public int ProductFamilyId { get; set; }
        public int? FactoryId { get; set; }
    }
}
