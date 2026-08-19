namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 用户表
/// 对应 APS_Auth.[User]
/// </summary>
public class User
{
    public int Id { get; set; }
    public string UserCode { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public int? FactoryId { get; set; }
    public int? DepartmentId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime? LastLoginTime { get; set; }
    public string? LastLoginIP { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
