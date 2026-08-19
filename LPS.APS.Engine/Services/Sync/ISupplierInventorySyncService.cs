using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 供应商库存同步服务接口（2号位职责）
///
/// 职责：
///   - 从采购系统同步供应商库存 + 在途采购单
///   - 调用 sp_SyncSupplierInventory 存储过程
///   - 用于 ATP 计算和采购建议
///
/// 调度频率：每小时执行（文档要求）
/// </summary>
public interface ISupplierInventorySyncService
{
    /// <summary>
    /// 同步供应商库存快照（包含供应商仓库库存 + 在途PO）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>同步结果</returns>
    Task<SupplierInventorySyncResultDto> SyncAsync(CancellationToken cancellationToken = default);
}
