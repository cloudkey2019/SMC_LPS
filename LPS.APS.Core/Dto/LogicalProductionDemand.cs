namespace LPS.APS.Core.Dto;

/// <summary>
/// 逻辑生产需求（V1.2）
/// Pegging阶段形成，交给1号位Solver决定FinalTask
/// 不持久化到数据库，仅运行时内存DTO
/// </summary>
public sealed class LogicalProductionDemand
{
    /// <summary>
    /// 逻辑需求唯一键
    /// </summary>
    public string LogicalDemandKey { get; init; } = string.Empty;

    /// <summary>
    /// 计划版本ID
    /// </summary>
    public long PlanVersionId { get; init; }

    /// <summary>
    /// Domain键
    /// </summary>
    public string DomainKey { get; init; } = string.Empty;

    /// <summary>
    /// 与Pegging Allocation建立追溯
    /// </summary>
    public long AllocationSequence { get; init; }

    /// <summary>
    /// 需求键
    /// </summary>
    public string DemandKey { get; init; } = string.Empty;

    /// <summary>
    /// 订单ID（可选）
    /// </summary>
    public long? OrderId { get; init; }

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 工厂ID
    /// </summary>
    public int FactoryId { get; init; }

    /// <summary>
    /// 从哪里开始继续生产（工序代码）
    /// </summary>
    public string StartStageCode { get; init; } = string.Empty;

    /// <summary>
    /// 净产出数量
    /// </summary>
    public decimal NetOutputQty { get; init; }

    /// <summary>
    /// 计划加工数量
    /// </summary>
    public decimal PlannedProcessQty { get; init; }

    /// <summary>
    /// 下游要求的可用时间
    /// </summary>
    public DateTime RequiredAvailableTime { get; init; }

    /// <summary>
    /// 已按冻结规则排好的业务顺序
    /// 不是全局PriorityScore，而是计算层→Priority Segment→段内排序的结果
    /// </summary>
    public int DemandSequence { get; init; }

    /// <summary>
    /// 生产指示号（PI类需求使用）
    /// </summary>
    public string? ProductionInstructionNo { get; init; }

    /// <summary>
    /// 是否未定位（PI Position为UNLOCATED）
    /// </summary>
    public bool IsUnlocated { get; init; }
}
