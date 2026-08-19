using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 治理版本发布服务（阶段 A-4/A-5：3号位 Application 编排）
/// 六态状态机（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED）发布流程。
/// 红线（R01/R02 验收）：
/// - 已 PUBLISHED 版本不可再次发布（历史不可覆盖）；
/// - 已 PUBLISHED 版本不可原地修改，须创建新版本；
/// - 发布前校验（状态合法性、引用有效、参数越界、Guardrail 为正、On-time 0~100 等）。
/// 默认版本语义（C2-3）：未显式指定 StrategyProfileVersionId 时按 RunType 取唯一无歧义默认 PUBLISHED 版本（阶段 A-6）。
/// A-8 扩展：版本差异对比与溯源。
/// A-5 扩展：发布前完整校验。
/// </summary>
public interface IGovernanceVersionService
{
    /// <summary>发布规则集版本（DRAFT/SUBMITTED/APPROVED → PUBLISHED；已 PUBLISHED 拒绝）</summary>
    Task PublishRuleSetVersionAsync(long ruleSetVersionId, string? publishedBy, CancellationToken ct = default);

    /// <summary>发布参数集版本（DRAFT/SUBMITTED/APPROVED → PUBLISHED；已 PUBLISHED 拒绝）</summary>
    Task PublishParameterSetVersionAsync(long parameterSetVersionId, string? publishedBy, CancellationToken ct = default);

    /// <summary>对比两个规则集版本的差异（阶段 A-8：版本溯源）</summary>
    Task<VersionDiffResult> CompareRuleSetVersionsAsync(long sourceVersionId, long targetVersionId, CancellationToken ct = default);

    /// <summary>对比两个参数集版本的差异（阶段 A-8：版本溯源）</summary>
    Task<VersionDiffResult> CompareParameterSetVersionsAsync(long sourceVersionId, long targetVersionId, CancellationToken ct = default);

    /// <summary>校验规则集版本是否可发布（阶段 A-5：发布前完整校验）</summary>
    Task<PublishValidationResult> ValidateRuleSetVersionForPublishAsync(long ruleSetVersionId, CancellationToken ct = default);

    /// <summary>校验参数集版本是否可发布（阶段 A-5：发布前完整校验）</summary>
    Task<PublishValidationResult> ValidateParameterSetVersionForPublishAsync(long parameterSetVersionId, CancellationToken ct = default);
}
