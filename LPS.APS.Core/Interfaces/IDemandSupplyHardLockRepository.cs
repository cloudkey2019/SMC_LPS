using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// DemandSupplyHardLock 仓储接口（V1.2新增，§8）
/// 负责 STRICT_BINDING 和 DEMAND_PROTECTION 锁的持久化操作
/// </summary>
public interface IDemandSupplyHardLockRepository
{
    /// <summary>
    /// 查询供给上的活跃 Lock（根据 SupplyKey）
    /// </summary>
    Task<IEnumerable<DemandSupplyHardLock>> GetActiveLocksOnSupplyAsync(
        string supplyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询需求上的活跃 Lock（根据 DemandKey）
    /// </summary>
    Task<IEnumerable<DemandSupplyHardLock>> GetActiveLocksOnDemandAsync(
        string demandKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量插入 Lock 记录
    /// </summary>
    Task<int> BulkInsertAsync(
        IEnumerable<DemandSupplyHardLock> locks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放 Lock（将状态更新为 RELEASED）
    /// </summary>
    Task<int> ReleaseLocksAsync(
        IEnumerable<long> lockIds,
        string releasedBy,
        string releaseReason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记 Lock 为 BROKEN（当发现供需不一致时）
    /// </summary>
    Task<int> MarkLocksAsBrokenAsync(
        IEnumerable<long> lockIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据来源 PlanVersion 查询 Lock
    /// </summary>
    Task<IEnumerable<DemandSupplyHardLock>> GetLocksBySourcePlanVersionAsync(
        int planVersionId,
        CancellationToken cancellationToken = default);
}
