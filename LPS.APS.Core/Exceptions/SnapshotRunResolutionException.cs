namespace LPS.APS.Core.Exceptions;

/// <summary>
/// Base 链断链 / 成环 / 无唯一来源时抛出（fail-closed，不静默降级）。
/// 依据：0号位《PM回复1009-1》§三 A-Q2「追溯遇到断链、多来源或环则失败并报Issue」。
/// </summary>
/// <remarks>
/// 【2026-10-10 迁址】原落在 <c>LPS.APS.Application.Services</c>（与 <c>SnapshotRunResolver</c> 同命名空间）
/// ⇒ 被 <c>AddApplicationServices</c> 的 Scrutor 扫描命中，而 <see cref="Exception"/> 实现 <c>ISerializable</c>，
/// 该异常被当服务注册后 dev 环境 <c>ValidateOnBuild</c> 解析不出 <c>(string message)</c> 构造、启动即崩。
/// 按仓库既有约定迁至 <c>LPS.APS.Core.Exceptions</c>（与 <c>SetupRuleDataRedLineException</c> 同处），
/// 该命名空间不在任何 Scrutor 扫描集内。
/// </remarks>
public sealed class SnapshotRunResolutionException : Exception
{
    public SnapshotRunResolutionException(string message) : base(message)
    {
    }
}
