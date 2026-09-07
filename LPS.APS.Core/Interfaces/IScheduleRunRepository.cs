using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// ScheduleRun 治理仓储接口（3号位，P0-08）
/// 对应表：APS_Production.dbo.ScheduleRun（冻结 DDL v5.1.2 §3.1）
/// 边界：只读取冻结列；ScheduleRun 仅新增"FAILED 恢复新建"一条写入路径（IRunLifecycleService.RecoverFailedRunAsync 内部使用），
///       不重写 2号位运行状态执行流转（SchedulingOrchestrator / ScheduleRunService 不动）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public interface IScheduleRunRepository
{
    /// <summary>按 Id 读取 ScheduleRun（只读冻结列；不存在返回 null）</summary>
    Task<ScheduleRunGov?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Run 列表查询（G4：0号位 2026-08-29 裁决——3号位 只读 ScheduleRun 运行事实，封装查询接口给 4号位，不降级）。
    /// 可选按 status / runType 过滤，按 Id 倒序取最新，take 缺省 100、上限 200；
    /// 只读冻结列，绝不修改 2号位 运行结果语义（状态回填仍由 2号位 运行收口负责）。
    /// </summary>
    Task<IReadOnlyList<ScheduleRunGov>> GetListAsync(
        int? take = null,
        string? status = null,
        string? runType = null,
        CancellationToken ct = default);

    /// <summary>
    /// FAILED 恢复：新建一条 RUNNING ScheduleRun，继承 source 的 RunType / StrategyProfileVersionId / ExpectedDomainKeysJson 基线。
    /// 绝不动 source（旧 FAILED 记录）；返回新建 ScheduleRun.Id。
    /// </summary>
    Task<int> InsertForRecoveryAsync(ScheduleRunGov source, string triggeredBy, CancellationToken ct = default);

    /// <summary>
    /// 白天候选运行创建（B-1：0号位 2026-08-29 裁决3——ScheduleRun 创建归 3号位 运行治理侧）。
    /// 单事务原子写入：① ScheduleRun（Status='RUNNING'，冻结 RunType / BasePlanVersionId / StrategyProfileVersionId / ExpectedDomainKeysJson）；
    ///                 ② 新建 Candidate PlanVersion 壳（Status='BUILDING'，VersionCategory='CANDIDATE'，SourceScheduleRunId=新 Run Id）。
    /// 任一步失败整体回滚，不产生孤立 RUNNING 运行。触发 2号位 主流程不在本方法内（契约接缝，见 IRunLifecycleService）。
    /// </summary>
    /// <param name="spec">创建入参（RunType / DomainKey / BasePlanVersionId / DataCutoffTime / Actor / 壳计划窗口）</param>
    /// <param name="strategyProfileVersionId">冻结的默认策略包版本 Id</param>
    /// <param name="triggeredBy">触发来源（= spec.Actor）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>新建 ScheduleRun.Id 与 Candidate 壳 Id</returns>
    Task<CandidateRunCreatedResult> CreateCandidateRunAsync(
        CandidateRunCreateSpec spec,
        long strategyProfileVersionId,
        string triggeredBy,
        CancellationToken ct = default);
}
