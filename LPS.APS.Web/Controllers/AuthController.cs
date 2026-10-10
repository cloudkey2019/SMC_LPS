using System.Security.Claims;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;
using LPS.APS.Web.Dto.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 认证控制器
/// 提供登录、刷新Token、登出接口
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, IDataScopeService dataScopeService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _dataScopeService = dataScopeService;
        _logger = logger;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")] // M3：登录 IP 维度限流
    public async Task<ApiResponse<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserCode) || string.IsNullOrWhiteSpace(request.Password))
            return ApiResponse<LoginResponseDto>.Fail(400, "用户名和密码不能为空");

        // 登录审计上下文：IP / UA 由控制器从 HttpContext 读取后传入，AuthService 不直接依赖 HTTP
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _authService.LoginAsync(request.UserCode, request.Password, clientIp, userAgent);

        if (!result.IsSuccess)
            return ApiResponse<LoginResponseDto>.Fail(401, result.ErrorMessage ?? "登录失败");

        return ApiResponse<LoginResponseDto>.Success(new LoginResponseDto
        {
            AccessToken = result.AccessToken!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value,
            UserId = result.UserId,
            UserCode = result.UserCode,
            UserName = result.UserName,
            Roles = result.Roles
        }, "登录成功");
    }

    /// <summary>
    /// 刷新 AccessToken
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ApiResponse<LoginResponseDto>> RefreshToken([FromBody] RefreshTokenRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken) || string.IsNullOrWhiteSpace(request.RefreshToken))
            return ApiResponse<LoginResponseDto>.Fail(400, "Token 不能为空");

        var result = await _authService.RefreshTokenAsync(request.AccessToken, request.RefreshToken);

        if (!result.IsSuccess)
            return ApiResponse<LoginResponseDto>.Fail(401, result.ErrorMessage ?? "刷新失败");

        return ApiResponse<LoginResponseDto>.Success(new LoginResponseDto
        {
            AccessToken = result.AccessToken!,
            RefreshToken = result.RefreshToken!,
            ExpiresAt = result.ExpiresAt!.Value,
            UserId = result.UserId,
            UserCode = result.UserCode,
            UserName = result.UserName,
            Roles = result.Roles
        }, "刷新成功");
    }

    /// <summary>
    /// 登出
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ApiResponse> Logout()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
            return ApiResponse.Fail(401, "无法解析当前登录用户身份");

        // 登出审计上下文：userCode 取自 JWT 声明，IP/UA 取自 HttpContext，均转发给 AuthService
        var userCode = User.FindFirst(ClaimTypes.Name)?.Value;
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        await _authService.LogoutAsync(userId, userCode, clientIp, userAgent);

        return ApiResponse.Ok("登出成功");
    }

    /// <summary>
    /// 获取当前用户信息（验证Token有效性）
    /// 含业务范围（IsGlobal + 各维度集合），直接复用 IDiaScopeService（不建立第二套 Scope DTO 真值）。
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ApiResponse<UserInfoDto>> GetCurrentUser(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userCode = User.FindFirst(ClaimTypes.Name)?.Value;
        var userName = User.FindFirst("userName")?.Value;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        var permissions = User.FindAll(PermissionCodes.PermissionClaimType).Select(c => c.Value).ToList();

        var currentUserId = int.TryParse(userId, out var id) ? id : 0;
        var scope = await _dataScopeService.ResolveScopeAsync(currentUserId, ct);

        return ApiResponse<UserInfoDto>.Success(new UserInfoDto
        {
            UserId = currentUserId,
            UserCode = userCode ?? "",
            UserName = userName ?? "",
            Roles = roles,
            Permissions = permissions,
            IsGlobal = scope.IsGlobal,
            Factories = ScopeValues(scope, DataScopeTypes.Factory),
            ProductFamilies = ScopeValues(scope, DataScopeTypes.ProductFamily),
            Departments = ScopeValues(scope, DataScopeTypes.Department),
            Domains = ScopeValues(scope, DataScopeTypes.Domain),
            ResourceOrgGroups = ScopeValues(scope, DataScopeTypes.ResourceOrgGroup)
        });
    }

    private static List<string> ScopeValues(DataScopeContext scope, string scopeType)
        => scope.GetValues(scopeType)?.OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
}
