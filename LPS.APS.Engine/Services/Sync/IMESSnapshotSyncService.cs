using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// MES 生产进度快照同步服务（2号位职责）
///
/// 调用链（ScheduleRun 由 IScheduleRunService 在 00:30 NightlyBatch 中创建）：
///   00:40 SyncWorkOrderAsync         — sp_SyncMESWorkOrderSnapshot
///   00:45 SyncOperationProgressAsync — sp_SyncMESOperationProgressSnapshot
///   00:50 SyncStageProgressAsync     — sp_SyncMESStageProgressSnapshot
///
/// 三个快照 SP 共享同一 ScheduleRunId + DataCutoffTime（通过 IScheduleRunService.GetCurrentRunAsync 获取）
/// </summary>
public interface IMESSnapshotSyncService
{
    /// <summary>00:40 同步工单级快照 → MESWorkOrderSnapshot</summary>
    Task<MESSnapshotSyncResultDto> SyncWorkOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>00:45 同步工序进度级快照 → OperationProgressSnapshot</summary>
    Task<MESSnapshotSyncResultDto> SyncOperationProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>00:50 同步大工艺进度级快照 → StageProgressSnapshot</summary>
    Task<MESSnapshotSyncResultDto> SyncStageProgressAsync(CancellationToken cancellationToken = default);
}
