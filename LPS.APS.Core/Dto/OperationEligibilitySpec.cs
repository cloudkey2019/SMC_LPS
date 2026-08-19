namespace LPS.APS.Core.Dto;

/// <summary>
/// 工序-资源能力绑定规格（拆批输入项 - 2号位装载自 OperationResourceEligibility）
/// </summary>
public class OperationEligibilitySpec
{
    public int MaterialId { get; init; }
    public string OperationCode { get; init; } = string.Empty;
    public int ResourceId { get; init; }
    public int Priority { get; init; }
}
