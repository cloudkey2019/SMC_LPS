namespace LPS.APS.Core.Exceptions;

/// <summary>
/// 资源不存在异常（4号位 Setup 契约 §11.1：404）。
/// 触发：规则集版本 / 换型规则 Id 不存在。
/// 由 Web 层 <c>SetupRuleController</c> 捕获映射为 HTTP 404。
/// </summary>
public sealed class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message)
    {
    }

    public ResourceNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
