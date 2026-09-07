namespace LPS.APS.Core.Dto;

/// <summary>
/// 生产指示位置计算输入（2号位提供给5号位的完整事实包）
///
/// 按照2↔5接口冻结文档，2号位负责装载本次ScheduleRun/DataCutoffTime的所有相关事实，
/// 5号位只接收这个事实包进行纯计算，不直接访问数据库。
/// </summary>
public sealed class ProductionInstructionPositionInput
{
    /// <summary>
    /// 生产指示号
    /// </summary>
    public string ProductionInstructionNo { get; init; } = string.Empty;

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 工厂ID
    /// </summary>
    public int FactoryId { get; init; }

    /// <summary>
    /// ERP剩余数量（该PI尚未最终进入目标M库的全部剩余数量）
    /// 这是总量红线，所有Position必须闭合到这个数量
    /// </summary>
    public decimal ErpRemainingQty { get; init; }

    /// <summary>
    /// Stage进度事实列表（2号位从快照中提取）
    /// </summary>
    public IReadOnlyList<StageProgressFact> StageProgress { get; init; } = Array.Empty<StageProgressFact>();

    /// <summary>
    /// 工序进度事实列表（如需要更细粒度）
    /// </summary>
    public IReadOnlyList<OperationProgressFact> OperationProgress { get; init; } = Array.Empty<OperationProgressFact>();

    /// <summary>
    /// PI级库存事实
    /// </summary>
    public IReadOnlyList<PiInventoryFact> PiInventories { get; init; } = Array.Empty<PiInventoryFact>();

    /// <summary>
    /// XC（线边仓）事实
    /// </summary>
    public IReadOnlyList<XcFact> XcFacts { get; init; } = Array.Empty<XcFact>();

    /// <summary>
    /// 厂间在途事实
    /// </summary>
    public IReadOnlyList<InterplantTransitFact> TransitFacts { get; init; } = Array.Empty<InterplantTransitFact>();

    /// <summary>
    /// Stage路径事实（定义该PI的加工路径）
    /// </summary>
    public IReadOnlyList<StagePathFact> StagePath { get; init; } = Array.Empty<StagePathFact>();

    /// <summary>
    /// 跨厂边事实（定义PI路径中的跨厂转移边）
    /// </summary>
    public IReadOnlyList<CrossFactoryEdgeFact> CrossFactoryEdges { get; init; } = Array.Empty<CrossFactoryEdgeFact>();

    /// <summary>
    /// 强事实（Received等有明确单据支撑的事实）
    /// </summary>
    public IReadOnlyList<ReceivedFact> StrongFacts { get; init; } = Array.Empty<ReceivedFact>();

    /// <summary>
    /// 本次计算使用的冻结参数快照ID（可选）
    /// </summary>
    public long? FrozenParameterSnapshotId { get; init; }
}
