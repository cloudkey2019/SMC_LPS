namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 资源组织维度表（v5.0新增，替代原 ResourceGroup，降级为纯组织维度）
/// 对应 APS_Production.ResourceOrgGroup
/// 
/// 仅用于统计切片、前端筛选、组织归类
/// 不再用于 Routing 能力分组或工序资源可替代性判断（改由 OperationResourceEligibility 承担）
/// </summary>
public class ResourceOrgGroup
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int FactoryId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
