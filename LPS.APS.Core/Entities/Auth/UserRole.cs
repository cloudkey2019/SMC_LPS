namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 用户角色关联表
/// 对应 APS_Auth.UserRole
/// </summary>
public class UserRole
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public DateTime AssignedAt { get; set; }
    public int? AssignedBy { get; set; }
}
