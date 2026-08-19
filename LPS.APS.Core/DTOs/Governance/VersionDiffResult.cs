namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 版本差异对比结果（阶段 A-8：3号位 Core 层 DTO）
/// 记录两个版本之间的字段级差异
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class VersionDiffResult
{
    /// <summary>源版本 ID</summary>
    public long SourceVersionId { get; set; }

    /// <summary>目标版本 ID</summary>
    public long TargetVersionId { get; set; }

    /// <summary>源版本编码</summary>
    public string SourceVersionCode { get; set; } = string.Empty;

    /// <summary>目标版本编码</summary>
    public string TargetVersionCode { get; set; } = string.Empty;

    /// <summary>实体类型（RuleSetVersion / ParameterSetVersion）</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>字段差异列表</summary>
    public List<FieldDiff> FieldDiffs { get; set; } = new();

    /// <summary>对比时间</summary>
    public DateTime ComparedAt { get; set; }
}

/// <summary>
/// 字段差异明细
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class FieldDiff
{
    /// <summary>字段名称</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>字段显示名称（中文）</summary>
    public string FieldDisplayName { get; set; } = string.Empty;

    /// <summary>源版本字段值</summary>
    public string? SourceValue { get; set; }

    /// <summary>目标版本字段值</summary>
    public string? TargetValue { get; set; }

    /// <summary>是否有变化</summary>
    public bool IsChanged { get; set; }
}
