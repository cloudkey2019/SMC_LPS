namespace LPS.APS.Core.Exceptions;

/// <summary>
/// Setup 换型规则数据红线异常（4号位 Setup 契约 §11.1：422）。
/// 数据红线：唯一键冲突（EXACT/DEFAULT）、SetupMinutes 越界、From==To、EXACT 缺产品、DEFAULT 携带产品 等业务规则违反。
/// 区别于 <see cref="InvalidOperationException"/>（400：状态红线/参数错误）。
/// 由 Web 层 <c>SetupRuleController</c> 捕获映射为 HTTP 422。
/// </summary>
public sealed class SetupRuleDataRedLineException : Exception
{
    public SetupRuleDataRedLineException(string message) : base(message)
    {
    }

    public SetupRuleDataRedLineException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
