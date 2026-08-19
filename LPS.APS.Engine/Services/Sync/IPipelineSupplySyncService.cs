using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 管道供给同步服务（2号位职责 — 每日 00:55）
///
/// 管道供给四类来源（SupplyFact_Pipeline.SupplyType）：
///   INTERPLANT_IN_TRANSIT  — 厂间在途（ZP→BP 跨厂调拨，已出口未入库）
///   PURCHASE_IN_TRANSIT    — 采购在途（供应商已发货，尚未到厂）
///   VMI_ONSITE             — VMI 寄售在厂（实物已到，所有权未转移）
///   ARRIVED_NOT_RECEIVED   — 已到厂未入库（收货单据尚未关闭）
///
/// 链路：
///   ODS.ERP_*_View（5号位实现）
///     → APS.ext_ERP_*_View（SYNONYM，2号位已建）
///       → APS.ext_PipelineSupply_Source_View（UNION ALL 4来源）
///         → sp_SyncPipelineSupply → SupplyFact_Pipeline
///
/// V1 行为：TRUNCATE SupplyFact_Pipeline + 写 SUCCESS 日志（不读取任何 ODS 来源）
/// V1.1 行为：从 ext_PipelineSupply_Source_View 全量装载真实数据
/// </summary>
public interface IPipelineSupplySyncService
{
    /// <summary>
    /// 同步管道供给（调用 sp_SyncPipelineSupply）
    /// </summary>
    Task<PipelineSupplySyncResultDto> SyncAsync(CancellationToken cancellationToken = default);
}
