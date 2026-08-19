namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 订单验证与提升服务接口
/// 调用 sp_ValidateAndPromoteOrders 将 ERP_Order_Staging 中 PENDING 记录
/// 验证后提升到 Order_Canonical
/// </summary>
public interface IOrderPromotionService
{
    /// <summary>
    /// 执行验证与提升
    /// 返回：(提升成功数, 校验失败数)
    /// </summary>
    Task<(int PromotedCount, int FailedCount)> ValidateAndPromoteAsync(CancellationToken cancellationToken = default);
}
