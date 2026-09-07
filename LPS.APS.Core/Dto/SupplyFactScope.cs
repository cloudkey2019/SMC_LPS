namespace LPS.APS.Core.Dto;

/// <summary>
/// Supply事实查询作用域（用于LoadTimedSupplyFactsAsync的查询边界）
/// </summary>
public sealed class SupplyFactScope
{
    /// <summary>
    /// 数据截止时间（本次运行的统一时间切片）
    /// </summary>
    public DateTime DataCutoffTime { get; init; }

    /// <summary>
    /// 物料ID过滤列表（可选，null表示不过滤）
    /// </summary>
    public List<int>? MaterialIds { get; init; }

    /// <summary>
    /// 工厂ID过滤列表（可选，null表示不过滤）
    /// </summary>
    public List<int>? FactoryIds { get; init; }
}
