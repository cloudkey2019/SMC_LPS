namespace LPS.APS.Core.Authorization;

/// <summary>
/// 业务范围越界异常（F-G4，fail-closed）。
/// 当操作用户的 DataScopePolicy 未授权目标 (ScopeType, ScopeValue) 时由 IDataScopeService.EnsureInScopeAsync 抛出，
/// Web 层统一映射 HTTP 403（区别于普通参数/状态错误 400）。
/// </summary>
public sealed class ScopeViolationException : InvalidOperationException
{
    /// <summary>越界维度（对齐 <see cref="DataScopeTypes"/> 常量值）</summary>
    public string ScopeType { get; }

    /// <summary>越界目标值（DomainKey / FactoryCode 等）</summary>
    public string Value { get; }

    public ScopeViolationException(string scopeType, string value)
        : base($"业务范围越界（{scopeType}）：{value} 不在当前用户授权范围")
    {
        ScopeType = scopeType;
        Value = value;
    }
}