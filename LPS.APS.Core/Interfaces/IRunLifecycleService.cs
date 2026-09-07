using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// ScheduleRun 运行生命周期治理（3号位，P0-08）
/// 边界：3号位生命周期治理（ExpectedDomainKeysJson 冻结规则 / Candidate 最小确认 / FAILED 新建 Run / Run 引用追溯）；
///       不重写 2号位已冻结的运行状态执行逻辑（SchedulingOrchestrator / ScheduleRunService / DomainSchedulingJob 不动）。
/// DDL 依据：冻结 DDL v5.1.2（ScheduleRun.ExpectedDomainKeysJson / PlanVersion.ActivatedAt·ActivatedBy / UQ_PlanVersion_OneActivePerDomain）。
/// </summary>
public interface IRunLifecycleService
{
    /// <summary>
    /// 校验 ScheduleRun.ExpectedDomainKeysJson 冻结规则（P0-08）。
    /// 规则：JSON 数组格式（DDL CHECK ISJSON 兜底）；
    ///       RunType=FULL_SCHEDULE 类 → Domain 数 ≥ 1；
    ///       RunType=RESCHEDULE 类（Candidate）→ Domain 数恰为 1。
    /// 失败：抛 InvalidOperationException（配置错误，不静默降级）。
    /// </summary>
    /// <param name="scheduleRunId">ScheduleRun.Id</param>
    /// <param name="ct">取消令牌</param>
    Task ValidateExpectedDomainKeysAsync(int scheduleRunId, CancellationToken ct = default);

    /// <summary>
    /// Candidate 最小人工确认（P0-08：正式 Reschedule Candidate 只做最小确认）。
    /// 落点：PlanVersion（Status=CANDIDATE → 校验 DomainKey 非空 + 同域唯一 → 写 ActivatedAt/ActivatedBy）。
    /// 仅记录：Actor / ConfirmedAt / CandidatePlanVersionId(=planVersionId) / 必要 Remark（审计日志）。
    /// 不强制 OA；不建 MultiDomain Candidate。
    /// </summary>
    /// <param name="planVersionId">Candidate 计划版本 Id</param>
    /// <param name="actor">确认人（Actor）</param>
    /// <param name="remark">必要备注（可空）</param>
    /// <param name="ct">取消令牌</param>
    Task ConfirmCandidateAsync(int planVersionId, string actor, string? remark, CancellationToken ct = default);

    /// <summary>
    /// 激活 Candidate（确认后正式采用：CANDIDATE → ACTIVE）。
    /// 前置：DomainKey 非空（V1 必填语义）；同域已有 ACTIVE 则报错（UQ_PlanVersion_OneActivePerDomain 应用层预检）。
    /// </summary>
    /// <param name="planVersionId">Candidate 计划版本 Id</param>
    /// <param name="actor">激活人</param>
    /// <param name="ct">取消令牌</param>
    Task ActivateCandidateAsync(int planVersionId, string actor, CancellationToken ct = default);

    /// <summary>
    /// FAILED 恢复（P0-08）：为 FAILED ScheduleRun **新建** 一条 RUNNING ScheduleRun 重跑。
    /// 继承旧 Run 的 StrategyProfileVersionId 与 ExpectedDomainKeysJson 基线；
    /// 绝不动旧 FAILED 记录（不回改 RUNNING）。
    /// 返回新 ScheduleRunId。
    /// </summary>
    /// <param name="failedScheduleRunId">已 FAILED 的 ScheduleRun.Id</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>新建的 ScheduleRun.Id</returns>
    Task<int> RecoverFailedRunAsync(int failedScheduleRunId, CancellationToken ct = default);

    /// <summary>
    /// Run 引用追溯（P0-08 第 12 项）：ScheduleRun → StrategyProfileVersion → RuleSet/ParameterSet
    /// + 关联 PlanVersion 状态与结果。P0-06 已提供版本维（RunStrategyProfileTrace），本方法补齐 Run 维。
    /// </summary>
    /// <param name="scheduleRunId">ScheduleRun.Id</param>
    /// <param name="ct">取消令牌</param>
    Task<RunReferenceTrace> GetRunReferenceTraceAsync(int scheduleRunId, CancellationToken ct = default);

    /// <summary>
    /// Run 域级状态汇总（G8：3号位文档 §十六 FULL 失败链）。
    /// 基于 ExpectedDomainKeysJson + 该 Run 的 PlanVersion 集合，逐域判定 成功(COMPLETED)/待确认(CANDIDATE)/
    /// 计算中(RUNNING)/失败(FAILED)/被阻断(BLOCKED)/未参与(NOT_STARTED)。
    /// 被阻断判定：Run 已终态且存在 FAILED 域时，无 PlanVersion 的预期域标记 BLOCKED（非根因）。
    /// </summary>
    /// <param name="scheduleRunId">ScheduleRun.Id</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<RunDomainStatusDto>> GetRunDomainStatusAsync(int scheduleRunId, CancellationToken ct = default);

    /// <summary>
    /// 白天候选运行创建（B-1：0号位 2026-08-29 裁决3——MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / INSERT_ORDER_WHATIF
    /// 的白天候选 ScheduleRun 创建归 3号位 运行治理侧；冻结 运行类型 × 用途 × 策略版本，交 2号位 主流程执行收口）。
    /// 校验顺序（任一失败抛 InvalidOperationException，不静默降级）：
    ///   Actor 非空 → RunType ∈ 白天候选类 → Purpose ∈ RunType 冻结合法组合（实施包 §十九）→ DomainKey 非空（单 Domain）
    ///   → Base ACTIVE（显式 Id 校验存在/ACTIVE/Domain 一致，缺省按 DomainKey 解析当前 ACTIVE，无则拒绝）
    ///   → 默认策略版本（有效窗口 0 缺失 / 多歧义拒绝，红线 #4）→ 单事务原子写 Run + Candidate 壳 → CreateCandidateRun 审计。
    /// 触发接缝：创建后按《B-1 契约草案》方案 A/B/C 调 2号位 主流程，本轮未接线（待 2号位/0号位 裁定）。
    /// </summary>
    /// <param name="spec">创建入参（RunType / Purpose / DomainKey / BasePlanVersionId / DataCutoffTime / Actor）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>新建 ScheduleRun.Id 与 Candidate 壳 Id</returns>
    Task<CandidateRunCreatedResult> CreateCandidateRunAsync(CandidateRunCreateSpec spec, CancellationToken ct = default);
}
