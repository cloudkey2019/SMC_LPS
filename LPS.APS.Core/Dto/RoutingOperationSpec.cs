namespace LPS.APS.Core.Dto;

/// <summary>
/// 工艺路线工序规格（拆批输入项 - 2号位装载自 RoutingOperation）
/// </summary>
public class RoutingOperationSpec
{
    public int MaterialId { get; init; }
    public string OperationCode { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
    public int OperationSeq { get; init; }
    public decimal StandardDuration { get; init; }
    public decimal SetupTime { get; init; }
}
