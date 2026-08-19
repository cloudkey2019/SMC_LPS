using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// 需求侧内存账本（对称SupplyLedgerEntry，2号位PeggingOrchestrator内存专用）
///
/// 职责：
///   - 维护需求的剩余未分配数量（RemainingQty）
///   - 记录需求的业务属性（DemandType、DemandKey、RootOrderId等）
///   - 支持按BOM层级逐层扣减需求
///   - 与SupplyLedgerEntry配合，共同实现供需原子匹配
///
/// 使用场景：
///   - LoadDemandPoolAsync 装载需求池时构建
///   - BOM遍历时按MaterialId+FactoryId查询
///   - AllocateSupplyToDemand 原子扣减时同步更新RemainingQty
/// </summary>
public sealed class DemandBalance
{
    /// <summary>
    /// 需求总量（初始需求，不可变）
    /// </summary>
    public decimal RequiredQty { get; init; }

    /// <summary>
    /// 需求剩余未分配数量（遍历时可变，初始值=需求总量）
    /// </summary>
    public decimal RemainingQty { get; set; }

    /// <summary>
    /// 需求物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 需求物料编码
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 需求工厂ID
    /// </summary>
    public int FactoryId { get; init; }

    /// <summary>
    /// 需求工厂代码
    /// </summary>
    public string FactoryCode { get; init; } = string.Empty;

    /// <summary>
    /// 需求类型（ORDER / BACKLOG / FORECAST / COMPONENT）
    /// </summary>
    public DemandType DemandType { get; init; }

    /// <summary>
    /// 需求业务键（OrderCanonicalId / TaskDraftId / ForecastKey等）
    /// </summary>
    public string DemandKey { get; init; } = string.Empty;

    /// <summary>
    /// 根订单ID（可选，便于追溯）
    /// </summary>
    public long? RootOrderId { get; init; }

    /// <summary>
    /// 当前订单ID（可选，用于分层需求）
    /// </summary>
    public long? CurrentOrderId { get; init; }

    /// <summary>
    /// BOM层级（0=成品需求，1=半成品需求...）
    /// </summary>
    public int BomLevel { get; init; }

    /// <summary>
    /// 需求交期（DueTime）
    /// </summary>
    public DateTime DueTime { get; init; }

    /// <summary>
    /// 需求优先级
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// 产品族ID
    /// </summary>
    public int ProductFamilyId { get; init; }

    /// <summary>
    /// 是否在冻结区（已下达MES不可移动）
    /// </summary>
    public bool IsInFrozenZone { get; init; }

    /// <summary>
    /// Workset ID（可选，用于关联BOM解析上下文）
    /// </summary>
    public long? WorksetId { get; init; }
}
