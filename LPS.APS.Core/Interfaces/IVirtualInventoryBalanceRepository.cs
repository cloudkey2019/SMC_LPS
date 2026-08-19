using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// VirtualInventoryBalance 仓储接口
/// 负责虚拟库存余额的持久化操作
/// </summary>
public interface IVirtualInventoryBalanceRepository
{
    /// <summary>
    /// 批量插入虚拟库存余额
    /// </summary>
    Task<int> BulkInsertAsync(IEnumerable<VirtualInventoryBalance> balances, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据计划版本ID查询余额
    /// </summary>
    Task<IEnumerable<VirtualInventoryBalance>> GetByPlanVersionIdAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据拓扑序查询余额（单向传播）
    /// </summary>
    Task<IEnumerable<VirtualInventoryBalance>> GetByTopologicalOrderAsync(
        int planVersionId,
        int topologicalOrder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据产品族查询余额（跨域依赖）
    /// </summary>
    Task<IEnumerable<VirtualInventoryBalance>> GetByProductFamilyAsync(
        int planVersionId,
        int sourceProductFamilyId,
        int targetProductFamilyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据上游任务查询余额（血缘追溯）
    /// </summary>
    Task<IEnumerable<VirtualInventoryBalance>> GetByUpstreamTaskIdAsync(
        int planVersionId,
        long upstreamTaskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新已分配数量和剩余数量
    /// </summary>
    Task<int> UpdateAllocatedQuantityAsync(
        int planVersionId,
        long balanceId,
        decimal allocatedQuantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记为已传播
    /// </summary>
    Task<int> MarkAsPropagatedAsync(
        int planVersionId,
        IEnumerable<long> balanceIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定计划版本的余额记录
    /// </summary>
    Task<int> DeleteByPlanVersionIdAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计剩余可用量总和
    /// </summary>
    Task<decimal> SumRemainingQuantityAsync(
        int planVersionId,
        int materialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询未传播的虚拟库存（按拓扑序排序）
    /// </summary>
    Task<IEnumerable<VirtualInventoryBalance>> GetUnpropagatedOrderedAsync(
        int planVersionId,
        CancellationToken cancellationToken = default);
}
