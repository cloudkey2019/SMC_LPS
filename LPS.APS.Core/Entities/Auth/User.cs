namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 用户表
/// 对应 APS_Auth.[User]（DDL v1.3 冻结对齐版：LoginName/DisplayName/IsEnabled/IsDeleted）
/// </summary>
public class User
{
    public int Id { get; set; }
    public string LoginName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIP { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}