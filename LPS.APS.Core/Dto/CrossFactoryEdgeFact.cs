namespace LPS.APS.Core.Dto;

/// <summary>
/// 跨厂边事实（定义PI路径中的跨厂转移边）
///
/// 来源：MES_APS_BOM_Workset_CrossFactoryEdge
/// 用于Transit定位：Transit事实的SourceFactory→TargetFactory匹配到对应边，
/// 从而确定Transit应从哪个Stage扣除（FromStage对应份额）
/// </summary>
public sealed class CrossFactoryEdgeFact
{
    /// <summary>
    /// 源Stage代码
    /// </summary>
    public string FromStageCode { get; init; } = string.Empty;

    /// <summary>
    /// 源工厂代码
    /// </summary>
    public string FromFactoryCode { get; init; } = string.Empty;

    /// <summary>
    /// 目标Stage代码
    /// </summary>
    public string ToStageCode { get; init; } = string.Empty;

    /// <summary>
    /// 目标工厂代码
    /// </summary>
    public string ToFactoryCode { get; init; } = string.Empty;

    /// <summary>
    /// 边序号（在该PI路径中的顺序）
    /// </summary>
    public int EdgeSequence { get; init; }
}
