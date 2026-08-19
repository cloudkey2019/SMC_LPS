namespace LPS.APS.Core.Dto;

/// <summary>
/// 拆批输入：2号位从 APS 本地库加载后的所有必要数据。
/// 由 2号位在 SchedulingOrchestrator.阶段2 组装后传递给 5号位（IBatchSplitter）。
/// </summary>
public class BatchSplitInput
{
    public int PlanVersionId { get; init; }
    public IReadOnlyList<OrderSpec> Orders { get; init; } = Array.Empty<OrderSpec>();
    public IReadOnlyList<RoutingOperationSpec> Operations { get; init; } = Array.Empty<RoutingOperationSpec>();
    public IReadOnlyList<OperationEligibilitySpec> Eligibilities { get; init; } = Array.Empty<OperationEligibilitySpec>();
}
