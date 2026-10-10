using LPS.APS.Core.DTOs.Ctp;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// CTP 评估服务（APS V1 CTP 评估接口契约登记 v1.0）。
/// 3号位 职责：域键 resolve（缺省时自动解析）+ F-G4 业务范围二次校验；
/// 核心试算能力归 1号位/2号位（接入前由宿主返回 501）。
/// </summary>
/// <remarks>开发者：3号位（CTP 薄层契约）。</remarks>
public interface ICtpService
{
    /// <summary>
    /// 发起 CTP 评估前处理：确定 DomainKey（显式或缺省自动 resolve）并完成 F-G4 业务范围校验。
    /// </summary>
    /// <param name="request">CTP 评估请求</param>
    /// <param name="actorUserId">当前登录用户 Id</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>校验通过的 DomainKey（resolve 后的最终域键）</returns>
    /// <exception cref="InvalidOperationException">domainKey 缺失且自动 resolve 无唯一匹配（调用方转 400）</exception>
    /// <exception cref="LPS.APS.Core.Authorization.ScopeViolationException">F-G4 越界（调用方转 403）</exception>
    Task<string> EvaluateAsync(CtpEvaluateRequest request, int actorUserId, CancellationToken cancellationToken = default);
}
