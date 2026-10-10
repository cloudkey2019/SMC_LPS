using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 认证服务接口
/// 职责：用户登录验证、Token 签发与刷新、密码管理
/// 
/// 接口定义在 Core 层，实现在 Engine 层（访问 Auth 数据库）
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="userCode">用户工号</param>
    /// <param name="password">明文密码</param>
    /// <param name="clientIp">客户端 IP（由控制器从 HttpContext 传入，供审计，可空）</param>
    /// <param name="userAgent">客户端 UA（由控制器从 HttpContext 传入，供审计，可空）</param>
    /// <returns>登录结果（含 Token）</returns>
    Task<LoginResult> LoginAsync(string userCode, string password, string? clientIp = null, string? userAgent = null);

    /// <summary>
    /// 刷新 AccessToken
    /// </summary>
    /// <param name="accessToken">过期的 AccessToken</param>
    /// <param name="refreshToken">有效的 RefreshToken</param>
    /// <returns>新的 Token 对</returns>
    Task<LoginResult> RefreshTokenAsync(string accessToken, string refreshToken);

    /// <summary>
    /// 登出（吊销 RefreshToken）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="userCode">用户工号（由控制器从 JWT 声明传入，供审计 entityId，可空）</param>
    /// <param name="clientIp">客户端 IP（由控制器从 HttpContext 传入，供审计，可空）</param>
    /// <param name="userAgent">客户端 UA（由控制器从 HttpContext 传入，供审计，可空）</param>
    Task LogoutAsync(int userId, string? userCode = null, string? clientIp = null, string? userAgent = null);
}
