using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// PI Position 快照仓储接口（2号位职责）
///
/// 职责边界（2026-09-02 双专项冻结）：
/// - 2号位负责：保存 ProductionInstructionPositionSnapshot（带 ScheduleRunId/PlanVersionId）+ 数量闭环校验
/// - 5号位负责：只算 Position、不保存运行快照、不做 Snapshot 生命周期管理
/// 表：ProductionInstructionPositionSnapshot（键 ScheduleRunId + PlanVersionId + ProductionInstructionNo）
/// </summary>
public interface IProductionInstructionPositionSnapshotRepository
{
    /// <summary>
    /// 保存某次 ScheduleRun/PlanVersion 的 PI Position 快照。
    /// 幂等：先 DELETE 该 (ScheduleRunId, PlanVersionId) 的旧行，再批量 INSERT。
    /// </summary>
    /// <param name="scheduleRunId">排程运行ID（= PlanVersion.SourceScheduleRunId）</param>
    /// <param name="planVersionId">计划版本ID</param>
    /// <param name="snapshots">快照行列表（空列表则跳过）</param>
    /// <param name="ct">取消令牌</param>
    Task SaveBatchAsync(
        int scheduleRunId,
        int planVersionId,
        IReadOnlyList<ProductionInstructionPositionSnapshot> snapshots,
        CancellationToken ct = default);
}
