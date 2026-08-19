using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 拆批策略契约（5号位业务规则引擎职责）
///
/// 【职责边界】
///   2号位（调用方）：传入订单 + 工艺路线数据，负责 INSERT [Task] 和填充 SchedulingContext
///   5号位（实现方）：根据业务规则把一个 Order 拆成 N 个 TaskSpec（批量切分、工序合并、资源指派等）
///
/// 【V1 占位】
///   <c>DefaultBatchSplitter</c> 提供"整单 1:1 → 工序 Task"的朴素实现，
///   仅用于端到端流程打通。5号位接入后应提供生产级实现（带批量/合批/多路径/优先规则）。
///
/// 【相关 DTO】（均位于 <see cref="LPS.APS.Core.Dto"/> 命名空间，单文件一枚）
///   - <see cref="BatchSplitInput"/>
///   - <see cref="OrderSpec"/>
///   - <see cref="RoutingOperationSpec"/>
///   - <see cref="OperationEligibilitySpec"/>
///   - <see cref="TaskSpec"/>
/// </summary>
public interface IBatchSplitter
{
    /// <summary>
    /// 将订单按业务规则拆成任务规格列表。
    /// </summary>
    /// <param name="input">输入上下文（订单 + 工艺路线 + 资源能力）</param>
    /// <returns>Task 规格列表（不含 Id / 落盘由 2号位完成）</returns>
    IReadOnlyList<TaskSpec> Split(BatchSplitInput input);
}
