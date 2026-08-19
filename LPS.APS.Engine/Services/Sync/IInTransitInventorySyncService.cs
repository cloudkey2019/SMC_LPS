using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 在途库存同步服务接口（2号位职责）
///
/// 职责：
///   - 从 ERP_InterplantInTransit_View 同步厂间在途库存
///   - 调用 sp_SyncInTransitInventory 存储过程
///   - 用于 Pegging 供给池和 ATP 计算
///
/// 调度频率：建议每30分钟执行（实时性要求高于普通库存）
/// </summary>
public interface IInTransitInventorySyncService
{
    /// <summary>
    /// 同步在途库存快照
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>同步结果</returns>
    Task<InTransitInventorySyncResultDto> SyncAsync(CancellationToken cancellationToken = default);
}
