using Dapper;
using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 域定义仓储实现（Dapper + APS_Production，3号位治理写侧）
/// 对应表：APS_Production.dbo.DomainDefinition（冻结 DDL v5.1.4 §2.4aa）
/// 2号位 只读侧通过 IDomainDefinitionService 消费，本仓储负责治理写路径（CRUD + 启用/停用）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class DomainDefinitionRepository : IDomainDefinitionRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public DomainDefinitionRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public async Task<DomainDefinition?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM [dbo].[DomainDefinition] WHERE [Id] = @Id";
        return await _connectionManager.QueryFirstOrDefaultAsync<DomainDefinition>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<DomainDefinition?> GetByKeyAsync(string domainKey, CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM [dbo].[DomainDefinition] WHERE [DomainKey] = @DomainKey";
        return await _connectionManager.QueryFirstOrDefaultAsync<DomainDefinition>(
            sql, new { DomainKey = domainKey }, db: DatabaseId.APS);
    }

    public async Task<IReadOnlyList<DomainDefinition>> GetAllAsync(bool? isActive = null, CancellationToken ct = default)
    {
        const string sqlAll = "SELECT * FROM [dbo].[DomainDefinition] ORDER BY [SortOrder], [DomainKey]";
        const string sqlFiltered = "SELECT * FROM [dbo].[DomainDefinition] WHERE [IsActive] = @IsActive ORDER BY [SortOrder], [DomainKey]";

        var sql = isActive.HasValue ? sqlFiltered : sqlAll;
        var results = await _connectionManager.QueryAsync<DomainDefinition>(
            sql, isActive.HasValue ? new { IsActive = isActive.Value } : null, db: DatabaseId.APS);
        return results.ToList();
    }

    public async Task<IReadOnlyList<DomainDefinition>> GetActiveAsync(CancellationToken ct = default)
    {
        return await GetAllAsync(isActive: true, ct);
    }

    public async Task<DomainDefinition> CreateAsync(DomainDefinition entity, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [dbo].[DomainDefinition]
                ([DomainKey], [DomainName], [ScopeType], [ProductFamilyId], [FactoryId],
                 [IsActive], [SortOrder], [CreatedBy], [CreatedAt], [UpdatedBy], [UpdatedAt])
            VALUES
                (@DomainKey, @DomainName, @ScopeType, @ProductFamilyId, @FactoryId,
                 @IsActive, @SortOrder, @CreatedBy, @CreatedAt, @UpdatedBy, @UpdatedAt);
            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        var id = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql, entity, db: DatabaseId.APS);

        entity.Id = id;
        return entity;
    }

    public async Task UpdateAsync(DomainDefinition entity, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[DomainDefinition]
            SET [DomainName] = @DomainName,
                [ScopeType] = @ScopeType,
                [ProductFamilyId] = @ProductFamilyId,
                [FactoryId] = @FactoryId,
                [SortOrder] = @SortOrder,
                [UpdatedBy] = @UpdatedBy,
                [UpdatedAt] = @UpdatedAt
            WHERE [Id] = @Id";

        await _connectionManager.ExecuteAsync(sql, entity, db: DatabaseId.APS);
    }

    public async Task SetActiveAsync(int id, bool isActive, string? updatedBy, DateTime updatedAt, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE [dbo].[DomainDefinition]
            SET [IsActive] = @IsActive,
                [UpdatedBy] = @UpdatedBy,
                [UpdatedAt] = @UpdatedAt
            WHERE [Id] = @Id";

        await _connectionManager.ExecuteAsync(
            sql, new { Id = id, IsActive = isActive, UpdatedBy = updatedBy, UpdatedAt = updatedAt }, db: DatabaseId.APS);
    }

    public async Task<bool> ExistsByKeyAsync(string domainKey, int? excludeId = null, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT COUNT(1) FROM [dbo].[DomainDefinition]
            WHERE [DomainKey] = @DomainKey AND (@ExcludeId IS NULL OR [Id] <> @ExcludeId)";

        var count = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql, new { DomainKey = domainKey, ExcludeId = excludeId }, db: DatabaseId.APS);
        return count > 0;
    }

    public async Task<bool> ProductFamilyExistsAsync(int productFamilyId, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(1) FROM [dbo].[ProductFamily] WHERE [Id] = @Id";
        var count = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql, new { Id = productFamilyId }, db: DatabaseId.APS);
        return count > 0;
    }

    public async Task<bool> FactoryExistsAsync(int factoryId, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(1) FROM [dbo].[Factory] WHERE [Id] = @Id";
        var count = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql, new { Id = factoryId }, db: DatabaseId.APS);
        return count > 0;
    }
}
