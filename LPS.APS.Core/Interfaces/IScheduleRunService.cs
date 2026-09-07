namespace LPS.APS.Core.Interfaces;

/// <summary>
/// ScheduleRun 生命周期管理（2号位职责）
///
/// ScheduleRun 是夜批的时间锚点，所有依赖 DataCutoffTime 的服务均通过此接口获取。
/// 消费方：NightlyBatchOrchestrator（创建）、MESSnapshotSyncService（读取）、
///         SchedulingOrchestrator（通过 PlanVersion.SourceScheduleRunId 关联）
/// </summary>
public interface IScheduleRunService
{
    /// <summary>
    /// 创建当日 ScheduleRun，锁定 DataCutoffTime。
    /// 幂等：当日已存在 RUNNING 状态记录时直接复用，不重复创建。
    /// </summary>
    /// <returns>ScheduleRunId</returns>
    Task<int> CreateScheduleRunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当日 RUNNING 状态的 ScheduleRun。
    /// 供 MES 快照 SP 读取 ScheduleRunId + DataCutoffTime。
    /// </summary>
    /// <returns>找不到时返回 null</returns>
    Task<ScheduleRunDto?> GetCurrentRunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定 ScheduleRun 的预期 DomainKey 集合（ExpectedDomainKeysJson 解析结果，保持冻结顺序）。
    /// 供 NightlyBatchOrchestrator 按冻结集合逐 Domain 创建 PlanVersion + 装载订单。
    /// </summary>
    Task<IReadOnlyList<string>> GetExpectedDomainKeysAsync(int scheduleRunId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将 ScheduleRun 标记为完成（COMPLETED）。
    /// </summary>
    Task CompleteAsync(int scheduleRunId, int durationSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将 ScheduleRun 标记为部分成功（PARTIAL_SUCCESS）——逐 Domain 串行求解中，部分 Domain 成功、部分失败/被阻断。
    /// </summary>
    Task PartialSuccessAsync(int scheduleRunId, int durationSeconds, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将 ScheduleRun 标记为失败（FAILED），记录错误信息。
    /// </summary>
    Task FailAsync(int scheduleRunId, int durationSeconds, string errorMessage, CancellationToken cancellationToken = default);
}

/// <summary>ScheduleRun 基础信息（供跨层传递）</summary>
public sealed record ScheduleRunDto(int Id, DateTime DataCutoffTime, long? StrategyProfileVersionId);
