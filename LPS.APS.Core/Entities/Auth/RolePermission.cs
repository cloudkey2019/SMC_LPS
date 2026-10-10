namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 角色权限关联表
/// 对应 APS_Auth.RolePermission
/// </summary>
public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }
    public DateTime AssignedAt { get; set; }
}
