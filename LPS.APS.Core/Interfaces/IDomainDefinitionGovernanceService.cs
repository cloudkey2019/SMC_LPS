using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 域定义治理服务接口（3号位应用编排）
/// 职责：新建 / 编辑 / 启用 / 停用 + 校验（唯一性 / ScopeType / 引用合法性）+ 审计 + 当前有效集合查询。
/// 红线：DomainKey 一经创建不可变更（稳定键）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IDomainDefinitionGovernanceService
{
    /// <summary>按主键查询域定义</summary>
    Task<DomainDefinition?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>查询全部域定义（含停用）</summary>
    Task<IReadOnlyList<DomainDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>查询当前有效（IsActive=1）域集合</summary>
    Task<IReadOnlyList<DomainDefinition>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>新建域定义（校验 + 审计；新建默认启用）</summary>
    Task<DomainDefinition> CreateAsync(DomainDefinition input, string? operatedBy, CancellationToken ct = default);

    /// <summary>编辑域定义（DomainKey 不可变更；校验 + 审计）</summary>
    Task<DomainDefinition> UpdateAsync(int id, DomainDefinition input, string? operatedBy, CancellationToken ct = default);

    /// <summary>启用/停用域定义（校验 + 审计）</summary>
    Task<DomainDefinition> SetActiveAsync(int id, bool isActive, string? operatedBy, CancellationToken ct = default);
}
