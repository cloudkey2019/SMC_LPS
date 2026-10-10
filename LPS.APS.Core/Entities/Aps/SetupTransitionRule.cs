namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 产品转换换型规则（SetupTransitionRule）
/// 承载：RuleSetVersion.ContentSnapshotJson.SetupTransitionRules 子块（JSON 数组，重构 S-7 契约登记，零 DDL；
/// 原独立物理表 APS_Production.SetupTransitionRule 条目已撤销，待 2号位 D-1 DROP 闭环）。
/// 依据：《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》§九/§十。
/// 红线：
///   - 规则随 RuleSetVersionId 冻结，已 PUBLISHED 版本禁止原地修改（沿用 RuleSetVersion 治理口径）；
///   - 唯一性由「发布前冲突预校验（3号位）」保证（红线 #3：不得依赖 DB 约束保证完整性）；
///   - RuleType 取值见 <see cref="SetupTransitionRuleType"/>（EXACT / DEFAULT）。
/// 开发者：3号位（Setup规则治理）。
/// </summary>
public class SetupTransitionRule
{
    /// <summary>主键</summary>
    public long Id { get; set; }

    /// <summary>所属规则集版本（复用现有规则版本治理）</summary>
    public long RuleSetVersionId { get; set; }

    /// <summary>生产部门（业务范围）</summary>
    public int ProductionDepartmentId { get; set; }

    /// <summary>大工艺阶段码（业务范围）</summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>当前小工序（当前要排 Task 的小工序）</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>当前设备（候选/实际 Resource）</summary>
    public int ResourceId { get; set; }

    /// <summary>前产品（EXACT 明确；DEFAULT 为 NULL）</summary>
    public int? FromMaterialId { get; set; }

    /// <summary>后产品（EXACT 明确；DEFAULT 为 NULL）</summary>
    public int? ToMaterialId { get; set; }

    /// <summary>规则类型（EXACT / DEFAULT）</summary>
    public string RuleType { get; set; } = SetupTransitionRuleType.Exact;

    /// <summary>换型分钟（真实资源占用分钟，非负）</summary>
    public decimal SetupMinutes { get; set; }

    /// <summary>是否有效（当前版本内容）</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>创建人</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新时间</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>更新人</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// 换型规则类型常量（EXACT=明确产品转换 / DEFAULT=当前工序+设备默认）。
/// 沿用 <see cref="Enum.GovernanceVersionStatus"/> 的字符串常量口径。
/// </summary>
public static class SetupTransitionRuleType
{
    /// <summary>明确产品转换规则（FromMaterialId/ToMaterialId 均明确）</summary>
    public const string Exact = "EXACT";

    /// <summary>默认规则（产品为空，工序+设备明确）</summary>
    public const string Default = "DEFAULT";
}