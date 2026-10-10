using LPS.APS.Core.Authorization;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 业务范围解析服务（F-G4，3号位 Auth 域职责）
/// 解析当前用户的有效业务范围：用户直接范围 ∪ 角色范围，Global 优先。
/// </summary>
public interface IDataScopeService
{
    /// <summary>
    /// 解析指定用户的有效业务范围
    /// </summary>
    /// <param name="userId">用户 Id</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>有效业务范围上下文；无任何授权时返回 <see cref="DataScopeContext.Empty"/>（拒绝全部）</returns>
    Task<DataScopeContext> ResolveScopeAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 断言指定用户对 (scopeType, value) 拥有业务范围授权（F-G4，fail-closed）。
    /// Global 放行；该维度未授权或不含目标值一律抛 <see cref="ScopeViolationException"/>。
    /// </summary>
    /// <param name="userId">用户 Id</param>
    /// <param name="scopeType">业务范围维度（<see cref="DataScopeTypes"/> 常量值）</param>
    /// <param name="value">目标值（DomainKey / FactoryCode 等）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task EnsureInScopeAsync(int userId, string scopeType, string value, CancellationToken cancellationToken = default);
}
