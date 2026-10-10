using Dapper;
using LPS.APS.Core.DTOs.Setup;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// 主数据只读查询仓储实现（Dapper + APS_Production）。
/// Setup 维护 API Code 回带专用；3号位 依用户授权例外自写（Engine 层此前无独立主数据读仓储）。
/// 只读查询，无任何写操作；列名对齐冻结 DDL v5.1.2（ProductionDepartment.DeptCode / Resource.ResourceCode / Material.MaterialCode）。
/// </summary>
public sealed class MasterDataLookupRepository : IMasterDataLookupRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<MasterDataLookupRepository> _logger;

    public MasterDataLookupRepository(DatabaseConnectionManager connectionManager, ILogger<MasterDataLookupRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetDepartmentCodeAsync(int departmentId, CancellationToken ct = default)
        => await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT [DeptCode] FROM [dbo].[ProductionDepartment] WHERE [Id] = @Id",
            new { Id = departmentId }, db: DatabaseId.APS);

    /// <inheritdoc />
    public async Task<string?> GetResourceCodeAsync(int resourceId, CancellationToken ct = default)
        => await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT [ResourceCode] FROM [dbo].[Resource] WHERE [Id] = @Id",
            new { Id = resourceId }, db: DatabaseId.APS);

    /// <inheritdoc />
    public async Task<string?> GetMaterialCodeAsync(int materialId, CancellationToken ct = default)
        => await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT [MaterialCode] FROM [dbo].[Material] WHERE [Id] = @Id",
            new { Id = materialId }, db: DatabaseId.APS);

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetEligibleResourceIdsAsync(string operationCode, int materialId, CancellationToken ct = default)
        => (await _connectionManager.QueryAsync<int>(
            "SELECT DISTINCT [ResourceId] FROM [dbo].[OperationResourceEligibility] WHERE [OperationCode] = @OperationCode AND [MaterialId] = @MaterialId AND [IsActive] = 1",
            new { OperationCode = operationCode, MaterialId = materialId }, db: DatabaseId.APS))
            .ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceLookupInfo>> GetResourceInfosAsync(IReadOnlyCollection<int> resourceIds, CancellationToken ct = default)
    {
        if (resourceIds.Count == 0)
        {
            return Array.Empty<ResourceLookupInfo>();
        }

        return (await _connectionManager.QueryAsync<ResourceLookupInfo>(
            "SELECT [Id], [ResourceCode], [ResourceName], [ProductionDepartmentId] FROM [dbo].[Resource] WHERE [Id] IN @Ids",
            new { Ids = resourceIds }, db: DatabaseId.APS))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DepartmentLookupInfo>> GetDepartmentInfosAsync(IReadOnlyCollection<int> departmentIds, CancellationToken ct = default)
    {
        if (departmentIds.Count == 0)
        {
            return Array.Empty<DepartmentLookupInfo>();
        }

        return (await _connectionManager.QueryAsync<DepartmentLookupInfo>(
            "SELECT [Id], [DeptCode], [StageCode] FROM [dbo].[ProductionDepartment] WHERE [Id] IN @Ids",
            new { Ids = departmentIds }, db: DatabaseId.APS))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuleSetVersion>> GetAllRuleSetVersionsAsync(CancellationToken ct = default)
        => (await _connectionManager.QueryAsync<RuleSetVersion>(
            "SELECT [Id], [RuleSetId], [VersionCode], [Status], [EffectiveFrom], [EffectiveTo], [PublishedAt], [PublishedBy], [CreatedAt] FROM [dbo].[RuleSetVersion] ORDER BY [VersionCode]",
            db: DatabaseId.APS))
            .ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<DepartmentLookupItem>> LookupDepartmentsAsync(string? search, int limit = 200, CancellationToken ct = default)
        => (await _connectionManager.QueryAsync<DepartmentLookupItem>(
            @"SELECT TOP (@Limit) [Id] AS [MasterId], [DeptCode] AS [Code], [StageCode] AS [StageCode], [SourceDeptCode] AS [SourceDeptCode], [Holon] AS [Holon]
              FROM [dbo].[ProductionDepartment]
              WHERE (@Search IS NULL OR [DeptCode] LIKE '%' + @Search + '%')
              ORDER BY [DeptCode]",
            new { Search = search, Limit = limit }, db: DatabaseId.APS))
            .ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResourceLookupItem>> LookupResourcesAsync(string? search, int limit = 200, CancellationToken ct = default)
        => (await _connectionManager.QueryAsync<ResourceLookupItem>(
            @"SELECT TOP (@Limit) [Id] AS [MasterId], [ResourceCode] AS [Code], [ResourceName] AS [Name], [ProductionDepartmentId] AS [ProductionDepartmentId]
              FROM [dbo].[Resource]
              WHERE (@Search IS NULL OR [ResourceCode] LIKE '%' + @Search + '%' OR [ResourceName] LIKE '%' + @Search + '%')
              ORDER BY [ResourceCode]",
            new { Search = search, Limit = limit }, db: DatabaseId.APS))
            .ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<MaterialLookupItem>> LookupMaterialsAsync(string? search, bool activeOnly = false, int limit = 200, CancellationToken ct = default)
        => (await _connectionManager.QueryAsync<MaterialLookupItem>(
            @"SELECT TOP (@Limit) [Id] AS [MasterId], [MaterialCode] AS [Code], [MaterialName] AS [Name], [Spec] AS [Spec], [UOM] AS [Uom], [IsActive] AS [IsActive]
              FROM [dbo].[Material]
              WHERE (@ActiveOnly = 0 OR [IsActive] = 1)
                AND (@Search IS NULL OR [MaterialCode] LIKE '%' + @Search + '%' OR [MaterialName] LIKE '%' + @Search + '%' OR [Spec] LIKE '%' + @Search + '%')
              ORDER BY [MaterialCode]",
            new { Search = search, ActiveOnly = activeOnly, Limit = limit }, db: DatabaseId.APS))
            .ToList();
}
