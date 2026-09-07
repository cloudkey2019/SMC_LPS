using LPS.APS.Core.DTOs.Governance;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using StrategyProfileVersion = LPS.APS.Core.Entities.APS.StrategyProfileVersion;

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
    /// <summary>发布规则集版本（DRAFT/SUBMITTED/APPROVED → PUBLISHED；已 PUBLISHED 拒绝）。G10：changeReason 记录于审计日志（版本表无 Remarks 列）。</summary>
    Task PublishRuleSetVersionAsync(long ruleSetVersionId, string? publishedBy, CancellationToken ct = default, string? changeReason = null);

    /// <summary>发布参数集版本（DRAFT/SUBMITTED/APPROVED → PUBLISHED；已 PUBLISHED 拒绝）。G10：changeReason 记录于审计日志（版本表无 Remarks 列）。</summary>
    Task PublishParameterSetVersionAsync(long parameterSetVersionId, string? publishedBy, CancellationToken ct = default, string? changeReason = null);

    /// <summary>对比两个规则集版本的差异（阶段 A-8：版本溯源）</summary>
    Task<VersionDiffResult> CompareRuleSetVersionsAsync(long sourceVersionId, long targetVersionId, CancellationToken ct = default);

    /// <summary>对比两个参数集版本的差异（阶段 A-8：版本溯源）</summary>
    Task<VersionDiffResult> CompareParameterSetVersionsAsync(long sourceVersionId, long targetVersionId, CancellationToken ct = default);

    /// <summary>对比两个策略包版本的差异（G5/D3：3-4联调；V1 可后置，4号位策略包对比页）</summary>
    Task<VersionDiffResult> CompareStrategyProfileVersionsAsync(long sourceVersionId, long targetVersionId, CancellationToken ct = default);

    /// <summary>校验规则集版本是否可发布（阶段 A-5：发布前完整校验）</summary>
    Task<PublishValidationResult> ValidateRuleSetVersionForPublishAsync(long ruleSetVersionId, CancellationToken ct = default);

    /// <summary>校验参数集版本是否可发布（阶段 A-5：发布前完整校验）</summary>
    Task<PublishValidationResult> ValidateParameterSetVersionForPublishAsync(long parameterSetVersionId, CancellationToken ct = default);

    /// <summary>
    /// 校验策略包版本是否可发布（P0-06：3号位治理完整闭环）
    /// 校验项：状态可发布 / 版本编码非空 / 引用合法（RuleSetVersion、ParameterSetVersion 存在且 PUBLISHED）/
    /// 生效窗口（EffectiveFrom &lt; EffectiveTo）/ 默认歧义（IsDefault=1 且同 Profile 已存在另一 PUBLISHED 默认 → 报错）。
    /// </summary>
    Task<PublishValidationResult> ValidateStrategyProfileVersionForPublishAsync(long strategyProfileVersionId, CancellationToken ct = default);

    /// <summary>
    /// 发布策略包版本（P0-06：DRAFT/SUBMITTED/APPROVED → PUBLISHED；已 PUBLISHED 拒绝）
    /// 发布前强制校验（与 P0-05 一致，无绕过路径）。
    /// IsDefault=1 时：先清同 Profile 其他默认（ClearDefaultFlagAsync）再置位，避免 UQ_StrategyProfileVersion_DefaultPublished 冲突。
    /// G10：changeReason 记录于审计日志（版本表无 Remarks 列）。
    /// </summary>
    Task PublishStrategyProfileVersionAsync(long strategyProfileVersionId, string? publishedBy, CancellationToken ct = default, string? changeReason = null);

    // ==================== G2：版本停用（3-4联调；Retired↔DISABLED 语义映射 §8.4） ====================

    /// <summary>停用规则集版本（SUBMITTED/APPROVED/PUBLISHED → DISABLED；DISABLED/ARCHIVED 拒绝；DRAFT 不可停用）。reason 记录于审计日志。</summary>
    Task DisableRuleSetVersionAsync(long ruleSetVersionId, string? operatedBy, string? reason = null, CancellationToken ct = default);

    /// <summary>停用参数集版本（SUBMITTED/APPROVED/PUBLISHED → DISABLED；DISABLED/ARCHIVED 拒绝；DRAFT 不可停用）。reason 记录于审计日志。</summary>
    Task DisableParameterSetVersionAsync(long parameterSetVersionId, string? operatedBy, string? reason = null, CancellationToken ct = default);

    /// <summary>停用策略包版本（SUBMITTED/APPROVED/PUBLISHED → DISABLED；DISABLED/ARCHIVED 拒绝；DRAFT 不可停用）。reason 记录于审计日志。</summary>
    Task DisableStrategyProfileVersionAsync(long strategyProfileVersionId, string? operatedBy, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// 解析当前有效默认 PUBLISHED 策略包版本（P0-06：跨号位冻结语义，C2-3）
    /// 语义：按 RunType 匹配 IsActive=1 StrategyProfile（RunType 在父表）的 IsDefault=1+PUBLISHED 版本，
    ///       再过滤 EffectiveFrom/EffectiveTo 生效窗口（asOf 为空取当前时刻）。
    /// 结果：0 个 → null；恰 1 个 → 返回；&gt;1 个 → 抛 InvalidOperationException（歧义，报配置错误，不随机取一个）。
    /// </summary>
    Task<StrategyProfileVersion?> ResolveDefaultStrategyProfileVersionAsync(string? runType, DateTime? asOf = null, CancellationToken ct = default);

    // ==================== G3：当前 Published 版本便捷查询（3-4联调；C2，可替代：版本列表 + 前端过滤） ====================

    /// <summary>规则集当前生效 PUBLISHED 版本直达（G3/C2：生效窗口过滤；多候选报错收敛不随机取——红线 #4）</summary>
    Task<RuleSetVersion?> GetPublishedRuleSetVersionAsync(long ruleSetId, CancellationToken ct = default);

    /// <summary>参数集当前生效 PUBLISHED 版本直达（G3/C2）</summary>
    Task<ParameterSetVersion?> GetPublishedParameterSetVersionAsync(long parameterSetId, CancellationToken ct = default);

    /// <summary>策略包当前生效 PUBLISHED 版本直达（G3/C2）</summary>
    Task<StrategyProfileVersion?> GetPublishedStrategyProfileVersionAsync(long strategyProfileId, CancellationToken ct = default);

    /// <summary>Run 引用追溯（P0-06：StrategyProfileVersion → 父 Profile + 引用的 RuleSet/ParameterSet 版本）</summary>
    Task<RunStrategyProfileTrace> GetRunStrategyProfileTraceAsync(long strategyProfileVersionId, CancellationToken ct = default);

    // ==================== 第四轮复审 P0-01/P0-02 收口：CRUD 状态机不可绕过（2026-08-24） ====================

    /// <summary>
    /// 创建规则集版本（P0-02：强制初始状态 DRAFT——入参 Status 一律被忽略覆盖为 DRAFT，
    /// 状态只能经 Submit/Approve/Publish 流转，Create 不可直提 PUBLISHED）。
    /// P0-01：DRAFT 阶段编辑的 DemandPriorityJson（内存/API 字段）由服务层归一化写入 ContentSnapshotJson，
    /// 真实持久化载体唯一为 ContentSnapshotJson；治理字段（Published/Approved）一律置空，由后续流转写入。
    /// </summary>
    Task<RuleSetVersion> CreateRuleSetVersionAsync(RuleSetVersion version, string? createdBy, CancellationToken ct = default);

    /// <summary>
    /// 更新规则集版本（P0-02：状态机约束——已 PUBLISHED 不可原地修改抛异常；DISABLED/ARCHIVED 不可改；
    /// 入参 Status/治理字段一律被忽略，保持现有记录值，禁止越权改状态）。
    /// P0-01：DRAFT 编辑内容统一归一化到 ContentSnapshotJson 后持久化。
    /// </summary>
    Task UpdateRuleSetVersionAsync(long ruleSetVersionId, RuleSetVersion version, CancellationToken ct = default);

    /// <summary>获取规则集版本详情（P0-01：从 ContentSnapshotJson 投影回 DemandPriorityJson 内存字段，前端 API 兼容）</summary>
    Task<RuleSetVersion?> GetRuleSetVersionAsync(long ruleSetVersionId, CancellationToken ct = default);

    /// <summary>创建参数集版本（P0-02：强制 DRAFT；P0-01：五主题 JSON 归一化到 ContentSnapshotJson 持久化）</summary>
    Task<ParameterSetVersion> CreateParameterSetVersionAsync(ParameterSetVersion version, string? createdBy, CancellationToken ct = default);

    /// <summary>更新参数集版本（P0-02：PUBLISHED/DISABLED/ARCHIVED 拒绝；Status/治理字段冻结；P0-01：内容归一化）</summary>
    Task UpdateParameterSetVersionAsync(long parameterSetVersionId, ParameterSetVersion version, CancellationToken ct = default);

    /// <summary>获取参数集版本详情（P0-01：ContentSnapshotJson 五子块投影回五主题 JSON 内存字段）</summary>
    Task<ParameterSetVersion?> GetParameterSetVersionAsync(long parameterSetVersionId, CancellationToken ct = default);

    /// <summary>创建策略包版本（P0-02：强制 DRAFT；治理字段置空）</summary>
    Task<StrategyProfileVersion> CreateStrategyProfileVersionAsync(StrategyProfileVersion version, string? createdBy, CancellationToken ct = default);

    /// <summary>更新策略包版本（P0-02：PUBLISHED/DISABLED/ARCHIVED 拒绝；Status/治理字段冻结）</summary>
    Task UpdateStrategyProfileVersionAsync(long strategyProfileVersionId, StrategyProfileVersion version, CancellationToken ct = default);
}
