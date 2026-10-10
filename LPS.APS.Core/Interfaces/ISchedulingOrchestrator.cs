using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 排程编排器接口（2号位职责 — §2.5.1 排程发令枪）
/// 
/// 时序：每日02:00（所有数据管道完成后）
/// 
/// 编排流程：
///   阶段1: 装载排程沙盘（SchedulingContext）
///     - 从 APS 库读取当前 PlanVersion 的订单（Order分区表）
///     - 从 APS_BOM_RAW 读取 BOM + LLC
///     - 从 Material/MaterialSupplyContext 读取物料 + 供给属性
///     - 从 Resource/ResourceCalendar 读取设备 + 日历
///     - 从库存快照读取初始库存（§2.5.2 互斥隔离）
///   阶段2: Pegging + 拆批 → 生成 Task 列表
///   阶段3: 调用 FiniteCapacitySolver.Solve()（1号位纯内存算法）
///   阶段4: 排程结果落盘（Task表 UPDATE StartTime/EndTime）
///   阶段5: 标记 PlanVersion 状态为 Computed
///   阶段6: 快照封存（§2.6 SchedulingContext → .json.gz）
/// </summary>
public interface ISchedulingOrchestrator
{
    /// <summary>
    /// 执行排程推演
    /// </summary>
    /// <param name="planVersionId">计划版本ID（由夜间批次创建）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>排程结果摘要</returns>
    Task<SchedulingRunResult> RunSchedulingAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 自动发现最新待排计划版本并执行排程（Hangfire 定时触发入口）
    /// </summary>
    Task<SchedulingRunResult> RunSchedulingAutoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 ScheduleRun 执行全量排程并**一次性收口**（P1-03：夜间发令枪与 FAILED 恢复共用此 seam）。
    ///
    /// 语义：读 Run 的 ExpectedDomainKeysJson → 按 Domain_Dependency 拓扑序**逐域串行**执行 →
    ///       全部跑完后**只收口一次**（全成功 COMPLETED / 部分 PARTIAL_SUCCESS / 全失败 FAILED）。
    /// 边界：**不是**逐域收口（逐域调 Complete/Fail 会把同一 Run 收口多次）；单域候选走
    ///       <see cref="RunSchedulingAndFinalizeAsync"/>，不适用本方法。
    ///
    /// 前置：Run 存在；其 ExpectedDomainKeysJson 中每个 Domain 均有
    ///       Status='Created' 且 SourceScheduleRunId=该 Run 的 PlanVersion 壳（壳由 3号位 运行治理侧建）。
    /// 后置：Run 达终态；各 PV 达 Computed / ComputeFailed。
    /// 本方法不抛业务异常（Cancellation 除外），失败以返回结果表达。
    /// </summary>
    /// <param name="scheduleRunId">待执行的 ScheduleRun Id（调用方负责判定「该不该跑」，本方法不重复校验 RunType/Status）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<SchedulingRunResult> ExecuteRunAsync(int scheduleRunId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 白天候选专用：单域执行并收口 Run（3号位 建 Run + 候选壳后调用，§5.3.1）。
    /// 语义：RUNNING → COMPLETED（IsSuccess=true）/ FAILED（IsSuccess=false，ErrorMessage 落 Run）。
    /// 基线快照：按 scheduleRunId 反查 BasePlanVersionId，候选需求侧订单数据源钉基线（只读，防 ACTIVE 漂移）。
    /// </summary>
    /// <param name="planVersionId">候选 PlanVersion 壳 Id（结果写回目标）</param>
    /// <param name="scheduleRunId">3号位 创建的白天候选 ScheduleRun Id（终态收口目标 + BasePlanVersionId 来源）</param>
    /// <param name="strategyProfileVersionId">策略包版本 Id（以 Run 冻结值优先，本参数作回退）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<SchedulingRunResult> RunSchedulingAndFinalizeAsync(
        int planVersionId,
        int scheduleRunId,
        long strategyProfileVersionId,
        CancellationToken cancellationToken = default);
}
