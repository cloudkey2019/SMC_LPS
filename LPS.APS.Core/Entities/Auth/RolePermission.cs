namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 角色权限关联表
/// 对应 APS_Auth.RolePermission
/// </summary>
public class RolePermission
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public DateTime AssignedAt { get; set; }
    public int? AssignedBy { get; set; }
}
