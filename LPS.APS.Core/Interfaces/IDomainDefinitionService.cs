namespace LPS.APS.Core.Interfaces;

/// <summary>
/// DomainDefinition 只读服务（2号位职责）
/// 对应表：APS_Production.dbo.DomainDefinition（冻结 DDL v5.1.4 §2.4aa）
/// Owner=3号位治理；2号位只读 —— 归域执行 + 订单装载筛选的唯一事实来源。
/// </summary>
public interface IDomainDefinitionService
{
    /// <summary>
    /// 读取所有有效（IsActive=1）Domain，按 SortOrder、DomainKey 稳定排序。
    /// 空集合表示治理尚未配置 Domain（FULL_SCHEDULE 下视为配置错误，由调用方抛异常，不静默降级）。
    /// </summary>
    Task<IReadOnlyList<DomainDefinitionInfo>> GetActiveDomainsAsync(CancellationToken cancellationToken = default);
}

/// <summary>DomainDefinition 有效行（2号位归域/装载所需字段）</summary>
public sealed record DomainDefinitionInfo(
    string DomainKey,
    string ScopeType,
    int ProductFamilyId,
    int? FactoryId);
