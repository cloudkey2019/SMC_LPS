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
    /// 物料编码
    /// </summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 工厂ID
    /// </summary>
    public int FactoryId { get; init; }

    /// <summary>
    /// 工厂编码
    /// </summary>
    public string FactoryCode { get; init; } = string.Empty;

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
    /// 工单快照事实列表（2号位从MESWorkOrderSnapshot提取，仅IN_PROGRESS工单）
    ///
    /// 用途：5号位结合工单身份（MESWorkOrderNo/WorkOrderStatus）+ 工序进度 + PI Position
    /// 输出标准化既存执行上下文（ExistingExecutionContext），供2号位 Continuation 分桶。
    ///
    /// ⚠️ 只传IN_PROGRESS工单；RELEASED/CLOSED/DELETED不传。
    /// </summary>
    public IReadOnlyList<WorkOrderSnapshotFact> WorkOrders { get; init; } = Array.Empty<WorkOrderSnapshotFact>();

    /// <summary>
    /// Routing 工序节点事实（按物料+部门过滤后的该 PI 子集；5号位 DAG 拓扑前沿算法输入）
    /// </summary>
    public IReadOnlyList<RoutingOperationFact> RoutingOperations { get; init; } = Array.Empty<RoutingOperationFact>();

    /// <summary>
    /// Routing 依赖边事实（同上物料+部门范围；激活条件：Count > 0 即走 DAG 拓扑前沿）
    /// </summary>
    public IReadOnlyList<RoutingDependencyFact> RoutingDependencies { get; init; } = Array.Empty<RoutingDependencyFact>();

    /// <summary>
    /// 本次计算使用的冻结参数快照ID（可选）
    /// </summary>
    public long? FrozenParameterSnapshotId { get; init; }
}
