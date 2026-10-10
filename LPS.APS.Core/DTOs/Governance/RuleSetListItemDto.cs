namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 规则集列表项 DTO（G1/A1：4号位规则集列表页）
/// 主表字段 + 最新版本摘要（R2 扩展，催办单 2026-09-22）。
/// 真源说明：
///  - CurrentVersionCode / DraftVersionCode 取自 RuleSetVersion.VersionCode（字符串，线上无数值 Version 列）；
///  - Status / LastChangeReason / PublishedAt / RetiredAt 取最新版本；
///  - DomainKey 无真源（线上无规则集↔域关联），已提报 0号位 裁决，未纳入本 DTO。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class RuleSetListItemDto
{
    public long Id { get; set; }
    public string RuleSetCode { get; set; } = string.Empty;
    public string RuleSetName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>最新 PUBLISHED 版本编码（如 V1 / RS-DEMO-V2；无则为 null）</summary>
    public string? CurrentVersionCode { get; set; }
    /// <summary>最新 DRAFT 版本编码（无则为 null）</summary>
    public string? DraftVersionCode { get; set; }
    /// <summary>最新版本状态（六态：DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED；无版本则 null）</summary>
    public string? Status { get; set; }
    /// <summary>最新版本变更原因（RuleSetVersion.Remarks）</summary>
    public string? LastChangeReason { get; set; }
    /// <summary>最新 PUBLISHED 发布时间</summary>
    public DateTime? PublishedAt { get; set; }
    /// <summary>最新版本生效窗口关闭时间（RetiredAt 语义；无则为 null）</summary>
    public DateTime? RetiredAt { get; set; }
}
