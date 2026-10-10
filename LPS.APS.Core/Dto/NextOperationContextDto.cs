namespace LPS.APS.Core.Dto;

/// <summary>
/// PI执行起点上下文（5号位计算，2号位消费）
///
/// 回答：该PI数量份额下一步应从哪个Stage/Operation继续进入有限产能排程
///
/// 【职责边界 - 2026-09-06冻结】
/// - 5号位负责计算执行起点（事实解释）
/// - 2号位负责组装LogicalProductionDemand
/// - 1号位负责从执行起点开始的时间资源求解
///
/// 不是Supply、不是Position身份、不是Allocation对象
/// 不形成Operation级Pegging
/// 不要求独立建表，优先作为批量DTO/运行上下文消费
/// </summary>
public sealed class NextOperationContextDto
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
    /// 关联的PositionType（STAGE_WAITING/INTERPLANT_TRANSIT/UNLOCATED等）
    /// </summary>
    public string PositionType { get; init; } = string.Empty;

    /// <summary>
    /// 该执行起点的数量（同一PI可有多个切片，Σ SliceQty = 需要继续生产的PI Position数量）
    /// </summary>
    public decimal SliceQty { get; init; }

    /// <summary>
    /// 从哪个大工艺Stage继续
    /// </summary>
    public string StartStageCode { get; init; } = string.Empty;

    /// <summary>
    /// 从哪个小工序继续（需要精确到小工序时填写，否则为null）
    /// </summary>
    public string? StartOperationCode { get; init; }

    /// <summary>
    /// Routing稳定引用（RoutingKey/RoutingVersion或等价标识）
    /// </summary>
    public string? RoutingKey { get; init; }

    /// <summary>
    /// 是否为UNLOCATED（按保守规则返回最早可信Stage/Operation）
    /// </summary>
    public bool IsUnlocated { get; init; }

    /// <summary>
    /// 数据截止时间
    /// </summary>
    public DateTime DataCutoffTime { get; init; }

    /// <summary>
    /// 关联的PI Position ID（用于追溯）
    /// </summary>
    public long? SourcePositionId { get; init; }

    /// <summary>
    /// 问题信息（存在异常时）
    /// </summary>
    public string? IssueCode { get; init; }

    /// <summary>
    /// 问题描述
    /// </summary>
    public string? IssueMessage { get; init; }
}
