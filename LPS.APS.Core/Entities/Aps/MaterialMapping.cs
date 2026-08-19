namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 物料映射表（SCD Type 2拉链表）
/// 对应 APS_Production.MaterialMapping
/// v4.0重构：ERP_MasterID/MES_ID 统一为 SourceID，ERP_Warehouse 统一为 Warehouse
/// </summary>
public class MaterialMapping
{
    public long Id { get; set; }
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// 源系统物理主键（ERP的MasterID / MES的MES_ID，双源统一）
    /// </summary>
    public int? SourceID { get; set; }

    /// <summary>
    /// 仓库编码（ERP和MES统一，原MES的Location实为仓库代码）
    /// </summary>
    public string? Warehouse { get; set; }

    /// <summary>
    /// 来源系统：ERP / MES
    /// </summary>
    public string Source { get; set; } = string.Empty;

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsCurrent { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
