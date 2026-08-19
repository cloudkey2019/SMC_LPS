namespace LPS.APS.Core.Dto;

/// <summary>
/// 订单规格（拆批输入项 - 2号位装载自 Order 分区表）
/// </summary>
public class OrderSpec
{
    public long OrderId { get; init; }
    public string OrderNo { get; init; } = string.Empty;
    public int MaterialId { get; init; }
    public string MaterialCode { get; init; } = string.Empty;
    public int ProductFamilyId { get; init; }
    public int FactoryId { get; init; }
    public decimal Quantity { get; init; }
    public string UOM { get; init; } = string.Empty;
    public DateTime CustomerDueDate { get; init; }
    public int Priority { get; init; }
}
