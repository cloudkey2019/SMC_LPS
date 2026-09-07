using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 域定义仓储接口（3号位治理写侧，对应 APS_Production.dbo.DomainDefinition）
/// 提供 Domain 的 CRUD + 启用/停用 + 唯一性/引用存在性检查。
/// 与 2号位 只读侧 IDomainDefinitionService 严格区分：本接口负责治理写路径。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IDomainDefinitionRepository
{
    /// <summary>按主键查询域定义</summary>
    Task<DomainDefinition?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>按 DomainKey 查询域定义（唯一）</summary>
    Task<DomainDefinition?> GetByKeyAsync(string domainKey, CancellationToken ct = default);

    /// <summary>
    /// 查询全部域定义。
    /// <paramref name="isActive"/> 为 null 时返回全部，否则按有效状态过滤。
    /// </summary>
    Task<IReadOnlyList<DomainDefinition>> GetAllAsync(bool? isActive = null, CancellationToken ct = default);

    /// <summary>查询当前有效（IsActive=1）域集合，按 SortOrder、DomainKey 稳定排序</summary>
    Task<IReadOnlyList<DomainDefinition>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>新建域定义，回填主键 Id 后返回</summary>
    Task<DomainDefinition> CreateAsync(DomainDefinition entity, CancellationToken ct = default);

    /// <summary>更新域定义（DomainKey / IsActive 不在此更新）</summary>
    Task UpdateAsync(DomainDefinition entity, CancellationToken ct = default);

    /// <summary>启用/停用域定义（仅更新 IsActive + 审计人 + 时间）</summary>
    Task SetActiveAsync(int id, bool isActive, string? updatedBy, DateTime updatedAt, CancellationToken ct = default);

    /// <summary>DomainKey 唯一性检查（excludeId 用于更新时排除自身）</summary>
    Task<bool> ExistsByKeyAsync(string domainKey, int? excludeId = null, CancellationToken ct = default);

    /// <summary>产品族引用存在性检查（ProductFamily.Id）</summary>
    Task<bool> ProductFamilyExistsAsync(int productFamilyId, CancellationToken ct = default);

    /// <summary>工厂引用存在性检查（Factory.Id）</summary>
    Task<bool> FactoryExistsAsync(int factoryId, CancellationToken ct = default);
}
