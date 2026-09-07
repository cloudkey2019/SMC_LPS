using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// 位置计算问题记录（5号位在计算PI Position时遇到的异常）
///
/// 用于记录数据质量问题、计算异常、业务规则冲突等
/// 不影响Pegging主流程执行，仅用于后续审计和数据修复
/// </summary>
public sealed class PositionIssue
{
    /// <summary>
    /// 问题类型代码（如：STAGE_MISMATCH, QUANTITY_GAP, XC_OVERLAP等）
    /// </summary>
    public string IssueType { get; init; } = string.Empty;

    /// <summary>
    /// 问题等级
    /// </summary>
    public PositionIssueLevel Level { get; init; }

    /// <summary>
    /// 问题描述
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 涉及的PI号
    /// </summary>
    public string ProductionInstructionNo { get; init; } = string.Empty;

    /// <summary>
    /// 涉及的Stage代码（如果适用）
    /// </summary>
    public string? StageCode { get; init; }

    /// <summary>
    /// 问题数量（如果适用）
    /// </summary>
    public decimal? AffectedQuantity { get; init; }

    /// <summary>
    /// 详细上下文信息（JSON或结构化文本）
    /// </summary>
    public string? ContextData { get; init; }
    ///// <summary>
    ///// 问题严重级别
    ///// WARNING: 警告（数据可疑但可继续）
    ///// ERROR: 错误（数据缺失或冲突，降级处理）
    ///// </summary>
    //public string Severity { get; init; } = default!;

    ///// <summary>
    ///// 问题代码（用于分类统计）
    ///// </summary>
    //public string IssueCode { get; init; } = default!;

    ///// <summary>
    ///// 问题描述
    ///// </summary>
    //public string Message { get; init; } = default!;

    ///// <summary>
    ///// 关联的原始单据/批次（用于追溯）
    ///// </summary>
    //public string? SourceReference { get; init; }
}
