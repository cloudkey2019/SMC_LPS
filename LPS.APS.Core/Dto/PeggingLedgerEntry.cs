using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// Pegging 内存账本条目（纯内存中间态，不落库）
/// 5号位 BOM 遍历过程中记录供需扣减过程，算完后打包进 PeggingResultVoucher
/// </summary>
public class PeggingLedgerEntry
{
    /// <summary>
    /// 需求侧订单ID
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// 需求侧物料ID
    /// </summary>
    public int DemandMaterialId { get; set; }

    /// <summary>
    /// 需求数量
    /// </summary>
    public decimal DemandQuantity { get; set; }

    /// <summary>
    /// 供应侧物料ID
    /// </summary>
    public int SupplyMaterialId { get; set; }

    /// <summary>
    /// 已分配数量
    /// </summary>
    public decimal AllocatedQuantity { get; set; }

    /// <summary>
    /// 供应来源类型
    /// </summary>
    public SupplySourceType SourceType { get; set; }

    /// <summary>
    /// 供应来源ID
    /// </summary>
    public long? SourceId { get; set; }

    /// <summary>
    /// BOM 层级（0=成品，1=半成品...）
    /// </summary>
    public int BomLevel { get; set; }

    /// <summary>
    /// 工厂代码
    /// </summary>
    public string FactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 产品族ID
    /// </summary>
    public int ProductFamilyId { get; set; }

    /// <summary>
    /// 是否在冻结区
    /// </summary>
    public bool IsInFrozenZone { get; set; }

    /// <summary>
    /// Pegging 策略
    /// </summary>
    public PeggingStrategyType Strategy { get; set; }

    /// <summary>
    /// 供应可用时间
    /// </summary>
    public DateTime AvailableAt { get; set; }
}
