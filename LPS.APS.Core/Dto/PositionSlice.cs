using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// 位置切片（PI Position的一个物理位置片段）
///
/// 位置类型（PositionType，v5.1.5）:
/// - FIRST_STAGE_PENDING: 首工序待开始
/// - STAGE_WAITING: Stage等待（在某Stage等待加工，或已投料未实际加工）
/// - XC: 生产指示级XC位置（线边仓）
/// - INTERPLANT_TRANSIT: 跨厂途中位置（已发出未到达）
/// - UNLOCATED: 位置不明（需要保守排程）
///
/// 冻结约束：
/// - 所有Position必须互斥，同一物理数量不能同时出现在多个位置
/// - 同一PI的所有PositionSlice.Quantity之和 = TotalRemainingQty
/// </summary>
public sealed class PositionSlice
{
    /// <summary>
    /// 位置类型
    /// FIRST_STAGE_PENDING / STAGE_WAITING / XC / INTERPLANT_TRANSIT / UNLOCATED
    /// </summary>
    public PositionType PositionType { get; init; } = default!;

    /// <summary>
    /// 工艺段代码（PositionType=STAGE时必填）
    /// </summary>
    public string? StageCode { get; init; }

    /// <summary>
    /// 位置键（工厂/车间/线体等，视业务而定）
    /// </summary>
    public string? LocationKey { get; init; }

    /// <summary>
    /// 该位置的数量
    /// </summary>
    public decimal Quantity { get; init; }

    /// <summary>
    /// 可用时间（INTERPLANT_TRANSIT / XC 等有预计到达时间）
    /// </summary>
    public DateTime? AvailableTime { get; init; }

    /// <summary>
    /// 是否强证据（true表示有MES/ERP明确事实支撑，false表示推断）
    /// </summary>
    public bool IsStrongEvidence { get; init; }

    /// <summary>
    /// 来源键（原始单据号/批次号，用于追溯）
    /// </summary>
    public string? SourceKey { get; init; }

    /// <summary>
    /// 是否位置不明（IsUnlocated=true时需要保守排程）
    ///
    /// 保守起点规则：
    /// - 按该PI的StagePath从前往后扫描
    /// - 找到第一个"无法证明一定完成"的Stage作为StartStageCode
    /// - 如果完全没有可靠位置证据，回退到StagePath的第一个Stage
    ///
    /// 原则：宁可多占未来产能，不允许因为猜得太靠后而漏排必须经过的工艺
    /// </summary>
    public bool IsUnlocated { get; init; }
}
