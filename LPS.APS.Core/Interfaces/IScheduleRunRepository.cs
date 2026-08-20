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
    /// FAILED 恢复：新建一条 RUNNING ScheduleRun，继承 source 的 RunType / StrategyProfileVersionId / ExpectedDomainKeysJson 基线。
    /// 绝不动 source（旧 FAILED 记录）；返回新建 ScheduleRun.Id。
    /// </summary>
    Task<int> InsertForRecoveryAsync(ScheduleRunGov source, string triggeredBy, CancellationToken ct = default);
}
