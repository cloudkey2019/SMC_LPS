namespace LPS.APS.Core.Authorization;

/// <summary>
/// 业务范围上下文（F-G4）
/// 表达当前用户的有效业务范围：Factory / ProductFamily / Department / Domain / ResourceOrgGroup(兼容)。
/// 生效模型（设计方案 §9.2）：
///   - Global：全放行；
///   - 同一维度并集（OR）；
///   - 不同维度交集（AND）；
///   - 无任何范围：拒绝全部（安全默认，管理员经 Global 种子放行）。
/// </summary>
public sealed class DataScopeContext
{
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _scopes;

    private DataScopeContext(bool isGlobal, IReadOnlyDictionary<string, IReadOnlySet<string>> scopes)
    {
        IsGlobal = isGlobal;
        _scopes = scopes;
    }

    /// <summary>全局范围（全放行）</summary>
    public static DataScopeContext Global { get; } =
        new(true, new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>空范围（拒绝全部）</summary>
    public static DataScopeContext Empty { get; } =
        new(false, new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>是否全局放行</summary>
    public bool IsGlobal { get; }

    /// <summary>是否无任何范围（拒绝全部）</summary>
    public bool IsEmpty => !IsGlobal && _scopes.Count == 0;

    /// <summary>已授权的维度集合</summary>
    public IEnumerable<string> ScopeTypes => _scopes.Keys;

    /// <summary>
    /// 从 (ScopeType, ScopeValue) 策略集合构建；含 Global 维度时视为全局。
    /// 同一维度多条策略自动并集。
    /// </summary>
    public static DataScopeContext FromPolicies(IEnumerable<(string ScopeType, string ScopeValue)> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        var scopes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (type, value) in policies)
        {
            if (string.IsNullOrWhiteSpace(type))
                continue;

            if (string.Equals(type, DataScopeTypes.Global, StringComparison.OrdinalIgnoreCase))
                return Global;

            if (!scopes.TryGetValue(type, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                scopes[type] = set;
            }

            set.Add(value);
        }

        if (scopes.Count == 0)
            return Empty;

        var frozen = scopes.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlySet<string>)kv.Value,
            StringComparer.OrdinalIgnoreCase);

        return new DataScopeContext(false, frozen);
    }

    /// <summary>获取某维度允许的值集合；该维度未授权时返回 null</summary>
    public IReadOnlySet<string>? GetValues(string scopeType)
        => _scopes.TryGetValue(scopeType, out var set) ? set : null;

    /// <summary>判断给定值是否落在某维度授权范围内（Global 恒为 true；维度未授权恒为 false）</summary>
    public bool Allows(string scopeType, string value)
    {
        if (IsGlobal)
            return true;
        if (_scopes.TryGetValue(scopeType, out var set))
            return set.Contains(value);
        return false;
    }
}
