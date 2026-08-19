namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 域层协调 Job 接口（Hangfire 激活用）
/// 轮询当前层所有域 Job 完成后，enqueue 下一层域 Job。
/// </summary>
public interface IDomainLayerCoordinatorJob
{
    /// <summary>
    /// 轮询当前层全部域 Job 状态，完成后推进下一层或收尾。
    /// CancellationToken 不通过 Hangfire 传递，内部使用 CancellationToken.None。
    /// </summary>
    Task CoordinateAsync(
        int planVersionId,
        int scheduleRunId,
        long strategyProfileVersionId,
        string serializedCurrentLayerJobIds,
        string serializedNextLayerDomains,
        int nextLayerIndex,
        int totalLayers);
}
