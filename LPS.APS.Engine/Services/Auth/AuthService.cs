using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace LPS.APS.Engine.Services.Auth;

/// <summary>
/// 认证服务实现
/// 职责：用户登录验证、JWT 签发与刷新、账户锁定管理
/// 
/// 访问数据库：APS_Auth（User/UserRole/Role 表）
/// 密码哈希：BCrypt（前期可使用 SHA256 过渡）
/// Token：JWT AccessToken + 随机 RefreshToken
/// </summary>
public class AuthService : IAuthService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(30);

    public AuthService(
        DatabaseConnectionManager connectionManager,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string userCode, string password)
    {
        _logger.LogInformation("登录尝试: UserCode={UserCode}", userCode);

        // 1. 查询用户
        var user = await _connectionManager.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM [User] WHERE UserCode = @UserCode",
            new { UserCode = userCode },
            db: DatabaseId.Auth);

        if (user == null)
        {
            _logger.LogWarning("登录失败: 用户不存在 UserCode={UserCode}", userCode);
            return LoginResult("用户名或密码错误");
        }

        // 2. 账户状态检查
        if (user.Status != "Active")
        {
            _logger.LogWarning("登录失败: 账户已禁用 UserCode={UserCode}", userCode);
            return LoginResult("账户已禁用，请联系管理员");
        }

        // 3. 锁定检查
        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.Now)
        {
            var remaining = (user.LockoutEnd.Value - DateTime.Now).TotalMinutes;
            _logger.LogWarning("登录失败: 账户锁定中 UserCode={UserCode}, 剩余{Minutes:F0}分钟", userCode, remaining);
            return LoginResult($"账户已锁定，请{remaining:F0}分钟后重试");
        }

        // 4. 密码验证
        if (!VerifyPassword(password, user.PasswordHash))
        {
            await HandleFailedLoginAsync(user);
            _logger.LogWarning("登录失败: 密码错误 UserCode={UserCode}, 失败次数={Attempts}",
                userCode, user.FailedLoginAttempts + 1);
            return LoginResult("用户名或密码错误");
        }

        // 5. 查询角色
        var roles = await _connectionManager.QueryAsync<string>(
            @"SELECT r.RoleCode 
              FROM UserRole ur 
              INNER JOIN Role r ON ur.RoleId = r.Id 
              WHERE ur.UserId = @UserId AND r.IsActive = 1",
            new { UserId = user.Id },
            db: DatabaseId.Auth);

        var roleList = roles.ToList();

        // 6. 生成 Token
        var accessToken = GenerateAccessToken(user, roleList);
        var refreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.Now.AddMinutes(GetAccessTokenExpiration());

        // 7. 更新用户登录信息
        await _connectionManager.ExecuteAsync(
            @"UPDATE [User] SET 
                LastLoginTime = GETDATE(),
                FailedLoginAttempts = 0,
                LockoutEnd = NULL,
                RefreshToken = @RefreshToken,
                RefreshTokenExpiry = @RefreshTokenExpiry,
                UpdatedAt = GETDATE()
              WHERE Id = @Id",
            new
            {
                Id = user.Id,
                RefreshToken = refreshToken,
                RefreshTokenExpiry = DateTime.Now.AddDays(GetRefreshTokenExpiration())
            },
            db: DatabaseId.Auth);

        _logger.LogInformation("登录成功: UserCode={UserCode}, Roles={Roles}", userCode, string.Join(",", roleList));

        return new LoginResult
        {
            IsSuccess = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            UserCode = user.UserCode,
            UserName = user.UserName,
            Roles = roleList
        };
    }

    /// <inheritdoc />
    public async Task<LoginResult> RefreshTokenAsync(string accessToken, string refreshToken)
    {
        // 1. 从过期的 AccessToken 中解析用户信息
        var principal = GetPrincipalFromExpiredToken(accessToken);
        if (principal == null)
            return LoginResult("无效的 AccessToken");

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
            return LoginResult("无效的 Token 声明");

        // 2. 查询用户并验证 RefreshToken
        var user = await _connectionManager.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM [User] WHERE Id = @Id",
            new { Id = userId },
            db: DatabaseId.Auth);

        if (user == null || user.RefreshToken != refreshToken)
            return LoginResult("RefreshToken 无效");

        if (user.RefreshTokenExpiry < DateTime.Now)
            return LoginResult("RefreshToken 已过期，请重新登录");

        // 3. 查询角色
        var roles = await _connectionManager.QueryAsync<string>(
            @"SELECT r.RoleCode 
              FROM UserRole ur 
              INNER JOIN Role r ON ur.RoleId = r.Id 
              WHERE ur.UserId = @UserId AND r.IsActive = 1",
            new { UserId = user.Id },
            db: DatabaseId.Auth);

        var roleList = roles.ToList();

        // 4. 生成新 Token 对
        var newAccessToken = GenerateAccessToken(user, roleList);
        var newRefreshToken = GenerateRefreshToken();
        var expiresAt = DateTime.Now.AddMinutes(GetAccessTokenExpiration());

        // 5. 更新 RefreshToken
        await _connectionManager.ExecuteAsync(
            @"UPDATE [User] SET 
                RefreshToken = @RefreshToken,
                RefreshTokenExpiry = @RefreshTokenExpiry,
                UpdatedAt = GETDATE()
              WHERE Id = @Id",
            new
            {
                Id = user.Id,
                RefreshToken = newRefreshToken,
                RefreshTokenExpiry = DateTime.Now.AddDays(GetRefreshTokenExpiration())
            },
            db: DatabaseId.Auth);

        _logger.LogInformation("Token刷新成功: UserId={UserId}", userId);

        return new LoginResult
        {
            IsSuccess = true,
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            UserCode = user.UserCode,
            UserName = user.UserName,
            Roles = roleList
        };
    }

    /// <inheritdoc />
    public async Task LogoutAsync(int userId)
    {
        await _connectionManager.ExecuteAsync(
            @"UPDATE [User] SET 
                RefreshToken = NULL,
                RefreshTokenExpiry = NULL,
                UpdatedAt = GETDATE()
              WHERE Id = @Id",
            new { Id = userId },
            db: DatabaseId.Auth);

        _logger.LogInformation("用户登出: UserId={UserId}", userId);
    }

    #region Private Methods

    private string GenerateAccessToken(User user, List<string> roles)
    {
        var secretKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("JWT SecretKey 未配置");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserCode),
            new("userName", user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // 添加角色声明
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddMinutes(GetAccessTokenExpiration()),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    private ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var secretKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("JWT SecretKey 未配置");

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidIssuer = _configuration["Jwt:Issuer"],
            ValidAudience = _configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateLifetime = false // 允许过期的 Token
        };

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, tokenValidationParameters, out var securityToken);

            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }

    private static bool VerifyPassword(string password, string passwordHash)
    {
        // TODO: 后续升级为 BCrypt.Net-Next
        // 当前使用 SHA256 + Base64 过渡方案
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        var hash = Convert.ToBase64String(hashBytes);
        return hash == passwordHash;
    }

    private async Task HandleFailedLoginAsync(User user)
    {
        var newAttempts = user.FailedLoginAttempts + 1;
        DateTime? lockoutEnd = newAttempts >= MaxFailedAttempts
            ? DateTime.Now.Add(LockoutDuration)
            : null;

        await _connectionManager.ExecuteAsync(
            @"UPDATE [User] SET 
                FailedLoginAttempts = @Attempts,
                LockoutEnd = @LockoutEnd,
                UpdatedAt = GETDATE()
              WHERE Id = @Id",
            new { Id = user.Id, Attempts = newAttempts, LockoutEnd = lockoutEnd },
            db: DatabaseId.Auth);

        if (lockoutEnd.HasValue)
        {
            _logger.LogWarning("账户已锁定: UserId={UserId}, 锁定至={LockoutEnd}",
                user.Id, lockoutEnd.Value);
        }
    }

    private int GetAccessTokenExpiration()
        => int.TryParse(_configuration["Jwt:AccessTokenExpirationMinutes"], out var min) ? min : 120;

    private int GetRefreshTokenExpiration()
        => int.TryParse(_configuration["Jwt:RefreshTokenExpirationDays"], out var days) ? days : 7;

    private static LoginResult LoginResult(string error)
        => new() { IsSuccess = false, ErrorMessage = error };

    #endregion
}
