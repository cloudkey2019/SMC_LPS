namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// ERP订单同步服务接口（2号位职责）
/// 数据路径：ODS.ext_v_APS_SalesOrder → APS.ERP_Order_Staging → APS.Order_Canonical
/// </summary>
public interface IERPOrderSyncService
{
    /// <summary>
    /// 全量同步（每日凌晨执行）
    /// 拉取所有未取消/未完成订单写入Staging，然后触发验证与提升
    /// </summary>
    Task FullSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 增量同步（每小时执行）
    /// 基于UpdatedAt水位线拉取变更订单，写入Staging，然后触发验证与提升
    /// </summary>
    Task IncrementalSyncAsync(CancellationToken cancellationToken = default);
}
