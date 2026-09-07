namespace LPS.APS.Core.Dto;

/// <summary>
/// 供给事实（5号位职责输出 — Timed Supply标准化结果）
///
/// 职责边界：
/// - 5号位负责计算Supply的可用时间、承诺度、置信度
/// - 2号位负责消费此结果，参与Pegging主流程
///
/// Supply类型示例：
/// - INVENTORY: 库存
/// - VMI_ONSITE: 寄售库存
/// - PO_IN_TRANSIT: 采购在途
/// - PRODUCTION_INSTRUCTION: 生产指示
/// - PLANNING_ONLY_PLACEHOLDER: 计划占位符
/// </summary>
public sealed class SupplyFact
{
    /// <summary>
    /// 供给类型
    /// </summary>
    public string SupplyType { get; init; } = default!;

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 工厂ID
    /// </summary>
    public int FactoryId { get; init; }

    /// <summary>
    /// 可用数量
    /// </summary>
    public decimal AvailableQuantity { get; init; }

    /// <summary>
    /// 可用时间
    /// </summary>
    public DateTime AvailableTime { get; init; }

    /// <summary>
    /// 承诺度（CONFIRMED/TENTATIVE/PLANNING_ONLY）
    /// </summary>
    public string? Commitment { get; init; }

    /// <summary>
    /// 置信度（HIGH/MEDIUM/LOW）
    /// </summary>
    public string? Confidence { get; init; }

    /// <summary>
    /// 来源键（原始单据号/批次号）
    /// </summary>
    public string? SourceKey { get; init; }

    /// <summary>
    /// 生产指示号（SupplyType=PRODUCTION_INSTRUCTION时使用）
    /// </summary>
    public string? ProductionInstructionNo { get; init; }

    /// <summary>
    /// 工艺段代码（SupplyType=PRODUCTION_INSTRUCTION时表示从哪个Stage开始）
    /// </summary>
    public string? StageCode { get; init; }
}
