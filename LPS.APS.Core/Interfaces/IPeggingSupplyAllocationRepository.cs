using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// PeggingSupplyAllocation 仓储接口
/// 负责非 Task 供应分配记录的持久化操作
/// </summary>
public interface IPeggingSupplyAllocationRepository
{
    /// <summary>
    /// 批量插入供应分配记录
    /// </summary>
    Task<int> BulkInsertAsync(IEnumerable<PeggingSupplyAllocation> allocations, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据计划版本ID查询分配记录
    /// </summary>
    Task<IEnumerable<PeggingSupplyAllocation>> GetByPlanVersionIdAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据订单ID查询分配记录
    /// </summary>
    Task<IEnumerable<PeggingSupplyAllocation>> GetByOrderIdAsync(int planVersionId, long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据供应来源查询分配记录（库存扣减）
    /// </summary>
    Task<IEnumerable<PeggingSupplyAllocation>> GetBySupplySourceAsync(
        int planVersionId,
        string supplySourceType,
        long? supplySourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据批次号查询分配记录（FEFO 策略）
    /// </summary>
    Task<IEnumerable<PeggingSupplyAllocation>> GetByBatchNumberAsync(
        int planVersionId,
        string batchNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记供应为已消耗
    /// </summary>
    Task<int> MarkAsConsumedAsync(
        int planVersionId,
        IEnumerable<long> allocationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定计划版本的分配记录
    /// </summary>
    Task<int> DeleteByPlanVersionIdAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计未消耗的分配记录数量
    /// </summary>
    Task<int> CountUnconsumedAsync(int planVersionId, CancellationToken cancellationToken = default);
}
