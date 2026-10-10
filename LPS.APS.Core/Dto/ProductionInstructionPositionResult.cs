namespace LPS.APS.Core.Dto;

/// <summary>
/// 生产指示位置结果（5号位职责输出 — PI Position计算结果 + NextOperationContext）
///
/// 职责边界：
/// - 5号位负责计算PI在工厂内的物理位置（Stage/XC/Transit/Waiting/Unlocated）
/// - 5号位负责计算执行起点上下文（NextOperationContext）
/// - 2号位负责消费此结果，组装LogicalProductionDemand，参与Pegging主流程
///
/// 冻结约束：
/// - Σ PositionSlice.Quantity = TotalRemainingQty = ERP RemainingQty
/// - 所有Position必须互斥，同一物理数量不能同时算Stage、XC和Transit
/// - Σ NextOperationContext.SliceQty = 需要继续生产的PI Position数量
/// - NextOperationContext不是Supply、不是Position、不是Allocation
/// </summary>
public sealed class ProductionInstructionPositionResult
{
    /// <summary>
    /// 生产指示单号
    /// </summary>
    public string ProductionInstructionNo { get; init; } = default!;

    /// <summary>
    /// 剩余总数量（必须等于所有PositionSlice.Quantity之和）
    /// </summary>
    public decimal TotalRemainingQty { get; init; }

    /// <summary>
    /// 位置切片列表（FIRST_STAGE_PENDING / STAGE_WAITING / XC / INTERPLANT_TRANSIT / UNLOCATED）
    /// </summary>
    public IReadOnlyList<PositionSlice> Positions { get; init; } = Array.Empty<PositionSlice>();

    /// <summary>
    /// 执行起点上下文列表（该PI数量份额下一步从哪个Stage/Operation继续）
    /// 同一PI可有多个切片（如200件从NC开始，800件从挤丝开始）
    /// Σ SliceQty = 需要继续生产的PI Position数量
    /// </summary>
    public IReadOnlyList<NextOperationContextDto> NextOperationContexts { get; init; } = Array.Empty<NextOperationContextDto>();

    /// <summary>
    /// 标准化既存MES执行上下文列表（5号位 → 2号位，跨版本连续性专用）
    ///
    /// 每个IN_PROGRESS MES工单对应一个上下文，供2号位 Continuation 分桶消费。
    /// 5号位结合 WorkOrderSnapshot + OperationProgress + StageProgress + PI Position 形成。
    ///
    /// ⚠️ DerivedRemainingQty 是运行时派生，不是MES工单级原生字段。
    /// </summary>
    public IReadOnlyList<ExistingExecutionContextDto> ExistingExecutionContexts { get; init; } = Array.Empty<ExistingExecutionContextDto>();

    /// <summary>
    /// 位置计算问题记录
    /// </summary>
    public IReadOnlyList<PositionIssue> Issues { get; init; } = Array.Empty<PositionIssue>();

    /// <summary>
    /// 是否计算成功（总量是否闭合）
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 失败原因（如果IsSuccess=false）
    /// </summary>
    public string? FailureReason { get; init; }
}
