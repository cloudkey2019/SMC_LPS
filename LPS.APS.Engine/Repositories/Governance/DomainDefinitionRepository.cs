using System.Data;
using Dapper;
using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using ProductFamily = LPS.APS.Core.Entities.APS.ProductFamily;
using Factory = LPS.APS.Core.Entities.APS.Factory;
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
        // N-05（0号位 复审 v1.1）：scope 组合唯一性「检查 + 写」原分离存在 TOCTOU 并发竞态——
        // 两个并发请求同时通过预检后都 INSERT（DomainKey 不同时均成功，破坏「同 scope 仅一个启用域」）。
        // ⇒ 在 (ScopeType, ProductFamilyId, FactoryId) 独占应用锁（sp_getapplock，事务 Owner 自动释放）事务内
        //   复核唯一性再写，零 DDL（红线 #6 合规，复刻 TaskSplitRuleConfig P1-02 先例）。
        var id = await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await AcquireScopeLockAsync(conn, tx, entity.ScopeType, entity.ProductFamilyId, entity.FactoryId);
            await EnsureScopeComboUniqueInTransactionAsync(
                conn, tx, entity.ScopeType, entity.ProductFamilyId, entity.FactoryId, excludeId: null);

            const string sql = @"
                INSERT INTO [dbo].[DomainDefinition]
                    ([DomainKey], [DomainName], [ScopeType], [ProductFamilyId], [FactoryId],
                     [IsActive], [SortOrder], [CreatedBy], [CreatedAt], [UpdatedBy], [UpdatedAt])
                VALUES
                    (@DomainKey, @DomainName, @ScopeType, @ProductFamilyId, @FactoryId,
                     @IsActive, @SortOrder, @CreatedBy, @CreatedAt, @UpdatedBy, @UpdatedAt);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            return await conn.QueryFirstOrDefaultAsync<int>(sql, entity, tx);
        }, DatabaseId.APS);

        entity.Id = id;
        return entity;
    }

    public async Task UpdateAsync(DomainDefinition entity, CancellationToken ct = default)
    {
        // N-05：同 Create——scope 组合唯一性复核（排除自身）+ UPDATE 置于应用锁事务内。
        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await AcquireScopeLockAsync(conn, tx, entity.ScopeType, entity.ProductFamilyId, entity.FactoryId);
            await EnsureScopeComboUniqueInTransactionAsync(
                conn, tx, entity.ScopeType, entity.ProductFamilyId, entity.FactoryId, excludeId: entity.Id);

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

            await conn.ExecuteAsync(sql, entity, tx);
            return true;
        }, DatabaseId.APS);
    }

    public async Task SetActiveAsync(int id, bool isActive, string? updatedBy, DateTime updatedAt,
        string scopeType, int productFamilyId, int? factoryId, CancellationToken ct = default)
    {
        // N-05：启用时同 Create/Update——scope 组合唯一性复核（排除自身）+ UPDATE 置于应用锁事务内；
        // 停用不产生唯一性风险，仅持锁串行化写。锁键取目标域定义当前 scope。
        await _connectionManager.ExecuteInTransactionAsync(async (conn, tx) =>
        {
            await AcquireScopeLockAsync(conn, tx, scopeType, productFamilyId, factoryId);
            if (isActive)
            {
                await EnsureScopeComboUniqueInTransactionAsync(
                    conn, tx, scopeType, productFamilyId, factoryId, excludeId: id);
            }

            const string sql = @"
                UPDATE [dbo].[DomainDefinition]
                SET [IsActive] = @IsActive,
                    [UpdatedBy] = @UpdatedBy,
                    [UpdatedAt] = @UpdatedAt
                WHERE [Id] = @Id";

            await conn.ExecuteAsync(
                sql, new { Id = id, IsActive = isActive, UpdatedBy = updatedBy, UpdatedAt = updatedAt }, tx);
            return true;
        }, DatabaseId.APS);
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

    public async Task<bool> ExistsActiveScopeComboAsync(
        string scopeType, int productFamilyId, int? factoryId, int? excludeId = null, CancellationToken ct = default)
    {
        // G2 scope 组合唯一性（仅启用域）：FAMILY 按 ProductFamilyId；FACTORY_FAMILY 按 FactoryId+ProductFamilyId。
        // FAMILY 的 FactoryId 恒为 NULL（治理侧已校验 FAMILY 不得指定 FactoryId），故 @FactoryId IS NULL 分支仅命中 FAMILY。
        const string sql = @"
            SELECT COUNT(1) FROM [dbo].[DomainDefinition]
            WHERE [IsActive] = 1
              AND [ScopeType] = @ScopeType
              AND [ProductFamilyId] = @ProductFamilyId
              AND (@FactoryId IS NULL OR [FactoryId] = @FactoryId)
              AND (@ExcludeId IS NULL OR [Id] <> @ExcludeId)";

        var count = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql,
            new { ScopeType = scopeType, ProductFamilyId = productFamilyId, FactoryId = factoryId, ExcludeId = excludeId },
            db: DatabaseId.APS);
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

    public async Task<IReadOnlyList<ProductFamily>> GetProductFamiliesAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM [dbo].[ProductFamily] ORDER BY [Code]";
        var results = await _connectionManager.QueryAsync<ProductFamily>(sql, null, db: DatabaseId.APS);
        return results.ToList();
    }

    public async Task<IReadOnlyList<Factory>> GetFactoriesAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT * FROM [dbo].[Factory] ORDER BY [Code]";
        var results = await _connectionManager.QueryAsync<Factory>(sql, null, db: DatabaseId.APS);
        return results.ToList();
    }

    /// <summary>
    /// N-05：获取 (ScopeType, ProductFamilyId, FactoryId) 键上的 SQL Server 独占应用锁（sp_getapplock，事务 Owner）。
    /// 使「scope 组合唯一性检查 + 写」在同一事务内串行化，杜绝并发 TOCTOU 双启用；事务提交/回滚自动释放锁，零 DDL（红线 #6 合规）。
    /// 返回值 &lt; 0 表示未获锁（超时返回 -2），抛业务异常；30 秒等待防死锁/饿死。
    /// </summary>
    private static async Task AcquireScopeLockAsync(
        IDbConnection conn, IDbTransaction tx, string scopeType, int productFamilyId, int? factoryId)
    {
        const string sql = @"
DECLARE @ret int;
EXEC @ret = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @LockTimeout;
SELECT @ret;";
        var normalizedType = (scopeType ?? string.Empty).Trim().ToUpperInvariant();
        var p = new DynamicParameters();
        p.Add("Resource", $"DomainDefinition:Scope:{normalizedType}:{productFamilyId}:{factoryId}", DbType.String, size: 255);
        p.Add("LockTimeout", 30000, DbType.Int32);

        var result = await conn.QueryFirstOrDefaultAsync<int>(sql, p, tx);
        if (result < 0)
        {
            throw new InvalidOperationException("并发创建/更新域繁忙，获取域定义业务锁超时，请稍后重试。");
        }
    }

    /// <summary>
    /// N-05：在持有 (ScopeType, ProductFamilyId, FactoryId) 独占应用锁的事务内复核 scope 组合唯一性（仅启用域）。
    /// FAMILY 按 ProductFamilyId；FACTORY_FAMILY 按 FactoryId+ProductFamilyId；excludeId 排除自身。
    /// 与 <see cref="ExistsActiveScopeComboAsync"/> 同口径，作为写入事务内的权威校验（服务层预检之外的第二道防线）。
    /// </summary>
    private static async Task EnsureScopeComboUniqueInTransactionAsync(
        IDbConnection conn, IDbTransaction tx, string scopeType, int productFamilyId, int? factoryId, int? excludeId)
    {
        var normalizedType = (scopeType ?? string.Empty).Trim().ToUpperInvariant();
        const string sql = @"
            SELECT COUNT(1) FROM [dbo].[DomainDefinition]
            WHERE [IsActive] = 1
              AND [ScopeType] = @ScopeType
              AND [ProductFamilyId] = @ProductFamilyId
              AND (@FactoryId IS NULL OR [FactoryId] = @FactoryId)
              AND (@ExcludeId IS NULL OR [Id] <> @ExcludeId)";

        var count = await conn.QueryFirstOrDefaultAsync<int>(
            sql,
            new
            {
                ScopeType = normalizedType,
                ProductFamilyId = productFamilyId,
                FactoryId = factoryId,
                ExcludeId = excludeId
            },
            tx);

        if (count > 0)
        {
            throw new InvalidOperationException(
                normalizedType == "FACTORY_FAMILY"
                    ? $"已存在启用中的 FACTORY_FAMILY 域（FactoryId={factoryId}, ProductFamilyId={productFamilyId}），同一工厂+产品族组合仅允许一个启用域"
                    : $"已存在启用中的 FAMILY 域（ProductFamilyId={productFamilyId}），同一产品族仅允许一个启用域");
        }
    }
}
