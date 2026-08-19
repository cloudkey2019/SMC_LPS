using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// FrozenZoneSnapshot 仓储接口
/// 负责冻结区快照的持久化操作
/// </summary>
public interface IFrozenZoneSnapshotRepository
{
    /// <summary>
    /// 批量插入冻结区快照
    /// </summary>
    Task<int> BulkInsertAsync(IEnumerable<FrozenZoneSnapshot> snapshots, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据计划版本ID查询快照
    /// </summary>
    Task<IEnumerable<FrozenZoneSnapshot>> GetByPlanVersionIdAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 TaskId 查询快照
    /// </summary>
    Task<FrozenZoneSnapshot?> GetByTaskIdAsync(int planVersionId, long taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据冻结窗口查询快照（步骤2.3 冻结区判断）
    /// </summary>
    Task<IEnumerable<FrozenZoneSnapshot>> GetByFrozenWindowAsync(
        int planVersionId,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 MES 工单号查询快照
    /// </summary>
    Task<FrozenZoneSnapshot?> GetByMESWorkOrderNoAsync(string mesWorkOrderNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询已下发的快照列表
    /// </summary>
    Task<IEnumerable<FrozenZoneSnapshot>> GetDispatchedAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新下发状态
    /// </summary>
    Task<int> UpdateDispatchStatusAsync(
        int planVersionId,
        long taskId,
        bool isDispatched,
        DateTime? dispatchedAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定计划版本的快照
    /// </summary>
    Task<int> DeleteByPlanVersionIdAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计冻结区内的任务数量
    /// </summary>
    Task<int> CountInFrozenWindowAsync(int planVersionId, DateTime windowStart, DateTime windowEnd, CancellationToken cancellationToken = default);
}
