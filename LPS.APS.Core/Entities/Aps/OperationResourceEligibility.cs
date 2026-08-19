namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 工序资源能力关系表（v5.0新增，替代原 ResourceGroup 的能力分组功能）
/// 对应 APS_Production.OperationResourceEligibility
/// 数据来源：ext_APS_OperationResourceEligibility_View（ODS 契约视图）
/// 
/// 核心语义：某物料、某路径、某工序，允许使用哪些资源
/// 同样两台设备，生产不同产品或走不同路径时，可替代性可能不同
/// </summary>
public class OperationResourceEligibility
{
    public long Id { get; set; }
    public int MaterialId { get; set; }
    public int ProductionDepartmentId { get; set; }

    /// <summary>
    /// 工艺路径编码（V1固定'DEFAULT'，V2扩展多路径）
    /// </summary>
    public string RouteCode { get; set; } = "DEFAULT";

    /// <summary>
    /// 路径序号（V1固定1，V2扩展多路径）
    /// </summary>
    public int PathId { get; set; } = 1;

    /// <summary>
    /// 工序编码
    /// </summary>
    public string OperationCode { get; set; } = string.Empty;

    public int ResourceId { get; set; }

    /// <summary>
    /// 优先级（1=最优，越小越优先）
    /// </summary>
    public int Priority { get; set; } = 1;

    /// <summary>
    /// 该资源执行该工序的产能系数（1.0=标准）
    /// </summary>
    public decimal CapacityFactor { get; set; } = 1.0m;

    /// <summary>
    /// 是否首选资源
    /// </summary>
    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
