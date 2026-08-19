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
    /// 从ODS拉取BOM展开结果到APS本地库，并生成 OrderBomRequestLink
    /// </summary>
    /// <param name="batchNo">批次号（由 BOMRequestService 推送时生成）</param>
    /// <param name="planVersionId">计划版本ID（由 NightlyBatchOrchestrator 显式传入，禁止内部猜测）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>拉取的行数</returns>
    Task<int> PullBOMResultFromODSAsync(string batchNo, int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找最近一个READY状态的批次号（用于NightlyBatchOrchestrator自动接货）
    /// </summary>
    /// <returns>READY批次号，无则返回null</returns>
    Task<string?> FindReadyBatchAsync(CancellationToken cancellationToken = default);
}
