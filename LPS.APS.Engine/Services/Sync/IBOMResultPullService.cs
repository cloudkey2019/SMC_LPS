namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// BOM展开结果接货服务接口（2号位职责 — 2.3.4）
///
/// 数据路径：
///   ODS库: MES_API_BOM_Request（READY状态） + MES_APS_BOM_Workset（展开结果）
///   → 流式 SqlBulkCopy →
///   APS库: APS_BOM_RAW（本地BOM缓存）
///   → 后续: sp_CalculateLLC 计算低阶码
///   → 后续: 生成 OrderBomRequestLink（v5.0.31）
/// </summary>
public interface IBOMResultPullService
{
    /// <summary>
    /// 从ODS拉取BOM展开结果到APS本地库，并生成 OrderBomRequestLink。
    /// BOM 是物料级事实、按批次接货一次；OrderBomRequestLink 跨本批全部 Domain PlanVersion 映射
    /// （每顶层 Order 只归一个真实 Domain，故 OrderCanonicalId 在本批各 PlanVersion 中唯一命中）。
    /// </summary>
    /// <param name="batchNo">批次号（由 BOMRequestService 推送时生成）</param>
    /// <param name="planVersionIds">本批全部 Domain 的 PlanVersionID 集合（由 NightlyBatchOrchestrator 显式传入，禁止内部猜测）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>拉取的行数</returns>
    Task<int> PullBOMResultFromODSAsync(string batchNo, IReadOnlyList<int> planVersionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找最近一个READY状态的批次号（用于NightlyBatchOrchestrator自动接货）
    /// </summary>
    /// <returns>READY批次号，无则返回null</returns>
    Task<string?> FindReadyBatchAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 独立接货：查找最近 READY 批次并接货（与 NightlyBatchOrchestrator 解耦，供白天补接货 / 联调单独触发）。
    /// 等价于 FindReadyBatchAsync → PullBOMResultFromODSAsync 的串联封装；无 READY 批次时返回空结果（不抛异常）。
    /// </summary>
    /// <param name="planVersionIds">本批全部 Domain 的 PlanVersionID 集合（供 OrderBomRequestLink 映射，禁止内部猜测）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<BOMIntakeResult> IntakeLatestReadyBatchAsync(IReadOnlyList<int> planVersionIds, CancellationToken cancellationToken = default);
}
