namespace LPS.APS.Web.Dto.Auth;

/// <summary>登录请求</summary>
public class LoginRequestDto
{
    /// <summary>用户工号</summary>
    public string UserCode { get; set; } = string.Empty;

    /// <summary>密码</summary>
    public string Password { get; set; } = string.Empty;
}
