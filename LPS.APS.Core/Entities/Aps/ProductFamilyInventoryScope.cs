using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 产品族库存仓库范围规则表（L3：准入规则）
/// 对应 APS_Production.ProductFamilyInventoryScope（DDL v2.8）
/// 
/// 业务用途：定义某产品族允许哪些仓库/库位进入供给池。
/// 核心理念：先决定"哪些仓能进场"，再谈优先级。
/// 与 InventorySourceRule 的区别：本表是"准入"粒度，那个表是"例外优先级"粒度。
/// </summary>
[Table("ProductFamilyInventoryScope")]
public class ProductFamilyInventoryScope
{
    public int Id { get; set; }

    public int ProductFamilyId { get; set; }

    /// <summary>工厂ID（可空：支持跨工厂的全局规则）</summary>
    public int? FactoryId { get; set; }

    /// <summary>来源系统：ERP / MES</summary>
    public string SourceSystem { get; set; } = string.Empty;

    /// <summary>仓库/库位代码</summary>
    public string StorageCode { get; set; } = string.Empty;

    /// <summary>1=纳入供给池，0=排除</summary>
    public bool IncludeFlag { get; set; } = true;

    /// <summary>优先级（数值越小越优先）</summary>
    public int Priority { get; set; } = 100;

    public string? Reason { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
