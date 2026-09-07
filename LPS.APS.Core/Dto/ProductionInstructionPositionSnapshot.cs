namespace LPS.APS.Core.Dto;

/// <summary>
/// 生产指示位置快照（2号位落库实体 — 对应表 ProductionInstructionPositionSnapshot）
///
/// 定位（冻结 v5.1.4）：Position 不是独立 Supply，而是某次 ScheduleRun/PlanVersion
/// 中 ProductionInstruction 剩余数量的位置事实快照。
///
/// 冻结约束：
/// - 同一 PI 的 Σ Quantity = ERP PI RemainingQty（数量闭环，2号位校验）
/// - PositionType 存枚举名（FIRST_STAGE_PENDING / STAGE_WAITING / XC / INTERPLANT_TRANSIT / UNLOCATED）
/// - 2号位只保存 + 校验，不自行修正 5号位 计算出的位置数量
/// </summary>
public sealed class ProductionInstructionPositionSnapshot
{
    /// <summary>自增主键（INSERT 时由数据库生成，不传入）</summary>
    public long Id { get; init; }

    /// <summary>排程运行ID（= PlanVersion.SourceScheduleRunId）</summary>
    public int ScheduleRunId { get; init; }

    /// <summary>计划版本ID</summary>
    public int PlanVersionId { get; init; }

    /// <summary>生产指示单号</summary>
    public string ProductionInstructionNo { get; init; } = string.Empty;

    /// <summary>物料ID</summary>
    public int MaterialId { get; init; }

    /// <summary>物料编码</summary>
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>位置类型（枚举名：FIRST_STAGE_PENDING / STAGE_WAITING / XC / INTERPLANT_TRANSIT / UNLOCATED）</summary>
    public string PositionType { get; init; } = string.Empty;

    /// <summary>该位置的数量</summary>
    public decimal Quantity { get; init; }

    /// <summary>当前工艺段代码（STAGE_WAITING / FIRST_STAGE_PENDING 时有效）</summary>
    public string? CurrentStageCode { get; init; }

    /// <summary>下一工艺段代码（V1 暂不落）</summary>
    public string? NextStageCode { get; init; }

    /// <summary>可用时间（Transit / XC 等有预计到达时间）</summary>
    public DateTime? AvailableTime { get; init; }

    /// <summary>来源类型（V1 暂不落）</summary>
    public string? SourceType { get; init; }

    /// <summary>来源键（原始单据号/批次号，用于追溯）</summary>
    public string? SourceKey { get; init; }

    /// <summary>异常码（QUANTITY_GAP=数量不闭合 / POSITION_FAILED=计算失败；null=正常）</summary>
    public string? IssueCode { get; init; }

    /// <summary>置信度（V1 暂不落）</summary>
    public string? Confidence { get; init; }

    /// <summary>创建时间（数据库默认 GETDATE()，不传入）</summary>
    public DateTime CreatedAt { get; init; }
}
