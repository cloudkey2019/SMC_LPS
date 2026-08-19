namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 域级排程 Job 接口（Hangfire 激活用）
/// 每个产品族域独立实例，由 DomainLayerCoordinatorJob 按拓扑层次 enqueue。
/// </summary>
public interface IDomainSchedulingJob
{
    /// <summary>
    /// 执行单域排程：Pegging → Solve → 落盘 → 写虚拟库存。
    /// CancellationToken 不通过 Hangfire 传递，内部使用 CancellationToken.None。
    /// </summary>
    Task ExecuteAsync(
        int planVersionId,
        int scheduleRunId,
        string domainKey,
        int productFamilyId,
        int layerIndex,
        long strategyProfileVersionId);
}
