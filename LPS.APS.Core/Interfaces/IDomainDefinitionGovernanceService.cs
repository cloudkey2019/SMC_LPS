using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using ProductFamily = LPS.APS.Core.Entities.APS.ProductFamily;
using Factory = LPS.APS.Core.Entities.APS.Factory;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 域定义治理服务接口（3号位应用编排）
/// 职责：新建 / 编辑 / 启用 / 停用 + 校验（唯一性 / ScopeType / 引用合法性）+ 审计 + 当前有效集合查询。
/// 红线：DomainKey 一经创建不可变更（稳定键）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IDomainDefinitionGovernanceService
{
    /// <summary>
    /// 按主键查询域定义（用户态详情读）。
    /// N-03（0号位 复审 v1.1）：非 Global 用户目标域不在授权 Domain 范围 → 抛 <see cref="Exceptions.ScopeViolationException"/>（403，
    /// 防通过 Id 窥探他域定义）。<paramref name="actorUserId"/> 为受信主体（Controller 从登录身份取）。
    /// </summary>
    Task<DomainDefinition?> GetByIdAsync(int id, int actorUserId, CancellationToken ct = default);

    /// <summary>
    /// 查询全部域定义（含停用，用户态列表读）。
    /// N-03（0号位 复审 v1.1）：列表按操作人 Domain Scope 过滤——Global 不过滤；非 Global 仅返回授权域；
    /// 无任何 Domain 授权 → fail-closed 空列表。<paramref name="actorUserId"/> 为受信主体。
    /// </summary>
    Task<IReadOnlyList<DomainDefinition>> GetAllAsync(int actorUserId, CancellationToken ct = default);

    /// <summary>
    /// 查询当前有效（IsActive=1）域集合。
    /// N-03（0号位 复审 v1.1）：信任调用（2号位 归域执行唯一事实源，G-D09/G-D10）——不做用户 Domain Scope 过滤，
    /// 与用户态列表/详情（<see cref="GetAllAsync(int, CancellationToken)"/> / <see cref="GetByIdAsync(int, int, CancellationToken)"/>）严格分离。
    /// </summary>
    Task<IReadOnlyList<DomainDefinition>> GetActiveAsync(CancellationToken ct = default);

    /// <summary>产品族下拉列表（Domain 维护页数据源）</summary>
    Task<IReadOnlyList<ProductFamily>> GetProductFamiliesAsync(CancellationToken ct = default);

    /// <summary>工厂下拉列表（Domain 维护页数据源）</summary>
    Task<IReadOnlyList<Factory>> GetFactoriesAsync(CancellationToken ct = default);

    /// <summary>新建域定义（校验 + 业务范围校验 + 审计；新建默认启用）</summary>
    /// <param name="actorUserId">操作者用户 Id（来自 JWT，业务范围校验依据）</param>
    Task<DomainDefinition> CreateAsync(DomainDefinition input, int actorUserId, string? operatedBy, CancellationToken ct = default);

    /// <summary>编辑域定义（DomainKey 不可变更；校验 + 业务范围校验 + 审计）</summary>
    /// <param name="actorUserId">操作者用户 Id（来自 JWT，业务范围校验依据）</param>
    Task<DomainDefinition> UpdateAsync(int id, DomainDefinition input, int actorUserId, string? operatedBy, CancellationToken ct = default);

    /// <summary>启用/停用域定义（校验 + 业务范围校验 + 审计）</summary>
    /// <param name="actorUserId">操作者用户 Id（来自 JWT，业务范围校验依据）</param>
    Task<DomainDefinition> SetActiveAsync(int id, bool isActive, int actorUserId, string? operatedBy, CancellationToken ct = default);
}
