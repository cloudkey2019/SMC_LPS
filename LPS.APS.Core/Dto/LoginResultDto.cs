namespace LPS.APS.Core.Dto;

/// <summary>
/// 登录结果
/// </summary>
public class LoginResult
{
    /// <summary>是否成功</summary>
    public bool IsSuccess { get; set; }

    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>AccessToken（JWT）</summary>
    public string? AccessToken { get; set; }

    /// <summary>RefreshToken（长期令牌）</summary>
    public string? RefreshToken { get; set; }

    /// <summary>AccessToken 过期时间</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>用户ID</summary>
    public int UserId { get; set; }

    /// <summary>用户工号</summary>
    public string UserCode { get; set; } = string.Empty;

    /// <summary>用户姓名</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>角色列表</summary>
    public List<string> Roles { get; set; } = new();
}
