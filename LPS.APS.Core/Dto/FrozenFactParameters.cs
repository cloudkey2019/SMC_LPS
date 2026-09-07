namespace LPS.APS.Core.Dto;

/// <summary>
/// 冻结事实参数（本次运行的参数快照）
/// 用于5号位事实计算时的统一参数基准
/// 来源：3号位Snapshot → 2号位一次装载 → 2号位投影 → 5号位消费
/// </summary>
public sealed class FrozenFactParameters
{
    /// <summary>
    /// 策略配置版本ID（用于追溯）
    /// </summary>
    public long StrategyProfileVersionId { get; init; }

    /// <summary>
    /// 默认采购提前期（天）
    /// 用于F15：ERP ETA为空时，基于PO Release/Issue Date计算默认ETA
    /// </summary>
    public int DefaultPurchaseLt { get; init; }

    /// <summary>
    /// 逾期容差（天）
    /// 用于F16：Default ETA落在运行参考时间之前时应用的容差
    /// </summary>
    public int OverdueMargin { get; init; }

    /// <summary>
    /// 到货可用偏移（仓库编码 → 小时数）
    /// 用于F17：Arrived-not-inbound的AvailableTime需加入Warehouse/Inspection/Inbound Offset
    /// </summary>
    public Dictionary<string, int> ArrivalToUsableOffsets { get; init; } = new();
}
