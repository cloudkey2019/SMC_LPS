namespace LPS.APS.Core.Dto;

/// <summary>
/// ODS/复杂事实Issue查询DTO（5号位提供给4号位）
///
/// 用于Explanation辅助、事实异常展示
/// 统一聚合来自多个5号位事实源的Issue
/// </summary>
public sealed class BusinessFactIssueDto
{
    /// <summary>
    /// Issue来源（BOM_WORKSET / MATERIAL_STAGE_CONTEXT / PI_POSITION / MANUAL_ETA等）
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// 问题类型代码
    /// </summary>
    public string IssueType { get; init; } = string.Empty;

    /// <summary>
    /// 问题严重级别（INFO / WARN / ERROR / CRITICAL）
    /// </summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>
    /// 问题描述（人类可读）
    /// </summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>
    /// 涉及的物料编码
    /// </summary>
    public string? MaterialCode { get; init; }

    /// <summary>
    /// 涉及的工厂编码
    /// </summary>
    public string? FactoryCode { get; init; }

    /// <summary>
    /// 涉及的单据号（PI号/PO号/BOM号等）
    /// </summary>
    public string? DocumentNo { get; init; }

    /// <summary>
    /// 涉及的Stage代码
    /// </summary>
    public string? StageCode { get; init; }

    /// <summary>
    /// 受影响数量
    /// </summary>
    public decimal? AffectedQuantity { get; init; }

    /// <summary>
    /// 降级动作标签
    /// </summary>
    public string? DegradeAction { get; init; }

    /// <summary>
    /// 复核状态（PENDING / CONFIRMED / IGNORED / FIXED）
    /// </summary>
    public string ReviewStatus { get; init; } = "PENDING";

    /// <summary>
    /// 复核人
    /// </summary>
    public string? ReviewedBy { get; init; }

    /// <summary>
    /// 复核时间
    /// </summary>
    public DateTime? ReviewedAt { get; init; }

    /// <summary>
    /// 发生时间
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
