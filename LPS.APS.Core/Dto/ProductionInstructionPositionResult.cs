namespace LPS.APS.Core.Dto;

/// <summary>
/// 生产指示位置结果（5号位职责输出 — PI Position计算结果）
///
/// 职责边界：
/// - 5号位负责计算PI在工厂内的物理位置（Stage/XC/Transit/Waiting/Unlocated）
/// - 2号位负责消费此结果，参与Pegging主流程
///
/// 冻结约束：
/// - Σ PositionSlice.Quantity = TotalRemainingQty = ERP RemainingQty
/// - 所有Position必须互斥，同一物理数量不能同时算Stage、XC和Transit
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
