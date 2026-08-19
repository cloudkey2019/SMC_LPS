namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 资源表（v5.0重构：从"手工维护主数据"改为"外部主数据镜像"）
/// 对应 APS_Production.Resource
/// 数据来源：ext_APS_Resource_View（ODS 契约视图）
/// 同步方式：每天定时全量刷新（设备主数据变化频率低）
/// </summary>
public class Resource
{
    public int Id { get; set; }

    /// <summary>
    /// APS 统一业务键
    /// </summary>
    public string ResourceCode { get; set; } = string.Empty;

    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// v5.0新增：源系统物理主键（MES 设备ID 或 EAM 资产ID）
    /// </summary>
    public string? ExternalResourceId { get; set; }

    /// <summary>
    /// v5.0新增：来源系统 MES / EAM
    /// </summary>
    public string SourceSystem { get; set; } = "MES";

    public int FactoryId { get; set; }

    /// <summary>
    /// v5.0.16新增：排程责任部门归属（汇总/资源能力归属维度）
    /// </summary>
    public int ProductionDepartmentId { get; set; }

    /// <summary>
    /// v5.0.16新增：源系统部门码（审计用）
    /// </summary>
    public string? SourceProductionDeptCode { get; set; }

    /// <summary>
    /// 资源类型：MACHINE / LINE / MANUAL_STATION
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    public string Status { get; set; } = "AVAILABLE";

    /// <summary>
    /// 产能系数（v5.0重命名：原Capacity → CapacityFactor，1.0=标准）
    /// </summary>
    public decimal CapacityFactor { get; set; } = 1.0m;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
