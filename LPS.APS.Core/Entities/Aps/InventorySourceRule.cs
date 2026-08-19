using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 库存来源优先级与排除规则表（L3：例外规则）
/// 对应 APS_Production.InventorySourceRule（DDL v2.8）
/// 
/// 业务用途：定义默认来源优先及来源+仓库/库位级例外排除/优先。
/// 合并原 InventorySourcePriority 的功能，增加 EXCLUDE 动作支持。
/// 
/// 默认规则（DDL 种子数据）：
///   RAW-% → PREFER ERP
///   FG-%  → PREFER ERP
///   WIP-% → PREFER ERP
///   ASSY-% → PREFER MES
/// </summary>
[Table("InventorySourceRule")]
public class InventorySourceRule
{
    public int Id { get; set; }

    /// <summary>物料编码模式，支持通配符（如 RAW-%）</summary>
    public string MaterialCodePattern { get; set; } = string.Empty;

    /// <summary>产品族ID（可空：为空表示全产品族生效）</summary>
    public int? ProductFamilyId { get; set; }

    /// <summary>来源系统：ERP / MES</summary>
    public string SourceSystem { get; set; } = string.Empty;

    /// <summary>仓库/库位代码（可空：为空表示不限定仓位）</summary>
    public string? StorageCode { get; set; }

    /// <summary>规则动作：PREFER / EXCLUDE（CHK 约束）</summary>
    public string RuleAction { get; set; } = "PREFER";

    /// <summary>优先级（数值越小越优先）</summary>
    public int Priority { get; set; } = 100;

    public string? Reason { get; set; }

    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
