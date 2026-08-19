namespace LPS.APS.Web.Dto.Auth;

/// <summary>刷新Token请求</summary>
public class RefreshTokenRequestDto
{
    /// <summary>过期的 AccessToken</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>有效的 RefreshToken</summary>
    public string RefreshToken { get; set; } = string.Empty;
}
