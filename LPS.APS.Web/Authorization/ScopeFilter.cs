using LPS.APS.Core.Authorization;

namespace LPS.APS.Web.Authorization;

/// <summary>
/// Scope 校验/过滤辅助（5号位接口接入线，消费 3号位 IDataScopeService 的样板）
///
/// 语义（对齐 3号位 30-3 回执 + F-G4 裁决）：
///   - Global：全放行，不追加过滤；
///   - Empty（无任何策略）：拒绝全部（安全默认）；
///   - 同一维度并集（IN 集合过滤），不同维度交集（多列 AND 过滤）；
///   - 本端点可执行的维度均未获授权时 fail-closed 拒绝全部（Business Scope ≠ Domain Scope 别名，
///     ProductFamily/Department 等无法映射到本端点过滤列的维度授权不扩大可见范围）。
/// </summary>
public static class ScopeFilter
{
    /// <summary>
    /// 是否应拒绝全部（fail-closed）。
    /// scope 为空，或本端点可执行的维度均未获授权时返回 true。
    /// </summary>
    /// <param name="scope">已解析的业务范围</param>
    /// <param name="enforceableTypes">本端点可执行的过滤维度（DataScopeTypes 常量）</param>
    public static bool IsDenyAll(DataScopeContext scope, params string[] enforceableTypes)
    {
        if (scope.IsGlobal)
            return false;
        if (scope.IsEmpty)
            return true;
        return enforceableTypes.All(t => scope.GetValues(t) is null);
    }

    /// <summary>
    /// 取某维度的过滤集合。
    /// Global 或该维度未授权时返回 null（表示不追加该维度过滤）。
    /// </summary>
    public static IReadOnlySet<string>? Values(DataScopeContext scope, string type)
        => scope.IsGlobal ? null : scope.GetValues(type);
}
