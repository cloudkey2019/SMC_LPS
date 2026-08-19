using System.Security.Claims;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;
using LPS.APS.Web.Dto.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ApiResponse<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.UserCode) || string.IsNullOrWhiteSpace(request.Password))
            return ApiResponse<LoginResponseDto>.Fail(400, "用户名和密码不能为空");

        var result = await _authService.LoginAsync(request.UserCode, request.Password);

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
        if (int.TryParse(userIdClaim, out var userId))
        {
            await _authService.LogoutAsync(userId);
        }

        return ApiResponse.Ok("登出成功");
    }

    /// <summary>
    /// 获取当前用户信息（验证Token有效性）
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public ApiResponse<UserInfoDto> GetCurrentUser()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userCode = User.FindFirst(ClaimTypes.Name)?.Value;
        var userName = User.FindFirst("userName")?.Value;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return ApiResponse<UserInfoDto>.Success(new UserInfoDto
        {
            UserId = int.Parse(userId ?? "0"),
            UserCode = userCode ?? "",
            UserName = userName ?? "",
            Roles = roles
        });
    }
}
