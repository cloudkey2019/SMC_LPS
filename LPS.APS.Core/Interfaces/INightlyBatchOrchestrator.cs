namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 夜间批次编排器接口（2号位职责）
/// 编排凌晨数据管道：全量订单同步 → 创建 ScheduleRun → 创建计划版本 → 订单装载 → BOM 接货
/// 调用时机：每日凌晨 00:30 由 Hangfire 触发
/// </summary>
public interface INightlyBatchOrchestrator
{
    /// <summary>
    /// 执行夜间批次管道
    /// 步骤：
    ///   1. 全量订单同步（ERP → Staging → Order_Canonical）
    ///   2. 创建 ScheduleRun（锁定 DataCutoffTime）
    ///   3. 创建计划版本（PlanVersion，写入 SourceScheduleRunId）
    ///   4. 订单装载到分区表（Order_Canonical → Order）
    ///   5. BOM 展开结果接货（ODS → APS_BOM_RAW）
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
