namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 权限表
/// 对应 APS_Auth.Permission
/// </summary>
public class Permission
{
    public int Id { get; set; }
    public string PermissionCode { get; set; } = string.Empty;
    public string PermissionName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
