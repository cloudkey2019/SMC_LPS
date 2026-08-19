namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 订单装载服务接口（2号位职责）
/// 从 Order_Canonical 装载到 Order 分区表（sp_SyncOrdersToPartitionTable）
/// 调用时机：每天 00:05（夜间批次）或手动触发
/// </summary>
public interface IOrderLoadingService
{
    /// <summary>
    /// 将 Order_Canonical 中活跃订单装载到 Order 分区表
    /// </summary>
    /// <param name="planVersionId">排程计划版本ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>装载的订单数量</returns>
    Task<int> LoadOrdersToPartitionTableAsync(int planVersionId, CancellationToken cancellationToken = default);
}
