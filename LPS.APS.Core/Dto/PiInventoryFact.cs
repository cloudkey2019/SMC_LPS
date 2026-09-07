namespace LPS.APS.Core.Dto;

/// <summary>
/// PI级库存事实
/// </summary>
public sealed class PiInventoryFact
{
    /// <summary>
    /// 仓库代码
    /// </summary>
    public string WarehouseCode { get; init; } = string.Empty;

    /// <summary>
    /// 数量
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// 可用时间
    /// </summary>
    public DateTime? AvailableTime { get; init; }

    /// <summary>
    /// 来源单据（如果有）
    /// </summary>
    public string? SourceDocument { get; init; }

    /// <summary>
    /// 关联Stage代码（2号位通过MaterialStageDeptContext等映射表确定仓库→Stage关系后填充）
    /// null表示无法映射到明确Stage
    /// </summary>
    public string? RelatedStageCode { get; init; }

    /// <summary>
    /// 位置分类（2号位根据仓库性质判断）
    /// STAGE_INVENTORY: 明确属于某个Stage的库存
    /// INTER_STAGE_WAITING: Stage间等待库存（已离开上一Stage，未进入下一Stage）
    /// UNKNOWN: 无法判断位置类型
    /// </summary>
    public string? LocationCategory { get; init; }
}
