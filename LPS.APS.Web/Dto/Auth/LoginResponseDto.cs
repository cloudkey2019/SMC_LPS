namespace LPS.APS.Web.Dto.Auth;

/// <summary>登录响应</summary>
public class LoginResponseDto
{
    /// <summary>AccessToken（JWT）</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>RefreshToken</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>AccessToken 过期时间</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>用户ID</summary>
    public int UserId { get; set; }

    /// <summary>用户工号</summary>
    public string UserCode { get; set; } = string.Empty;

    /// <summary>用户姓名</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>角色列表</summary>
    public List<string> Roles { get; set; } = new();
}
