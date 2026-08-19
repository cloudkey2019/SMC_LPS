using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Configuration;

namespace LPS.APS.Engine.Extensions;

/// <summary>
/// 数据库服务注册扩展
/// </summary>
public static class DatabaseServiceExtensions
{
    /// <summary>
    /// 注册数据库相关服务（三库架构：APS本地库 + ODS集成防腐层 + Auth权限库）
    /// </summary>
    public static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 注册三库配置（Database.APS + Database.ODS + Database.Auth）
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));

        // 验证数据库配置
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        // 注册数据库连接管理器（管理APS、ODS、Auth三个连接）
        services.AddSingleton<DatabaseConnectionManager>();

        // 注册 Auth 库 EF Core DbContext
        var authConnectionString = configuration.GetSection("Database:Auth:ConnectionString").Value;
        services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlServer(authConnectionString, sqlOptions =>
            {
                sqlOptions.CommandTimeout(30);
                sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            });
            // 开发环境启用敏感数据日志
            options.EnableSensitiveDataLogging(configuration.GetValue<bool>("Logging:EnableSensitiveDataLogging", false));
        });

        // Scrutor 自动扫描注册：接口 IXxx → 实现 Xxx（Repositories + Services）
        // 新增仓储或服务只需遵循命名约定，无需手动注册
        services.Scan(scan => scan
            .FromAssemblyOf<DatabaseConnectionManager>()
                // 注册 Auth 仓储（APS 侧走 Dapper + Services，无仓储层）
                .AddClasses(classes => classes.InNamespaces(
                    "LPS.APS.Engine.Repositories.Auth",
                    "LPS.APS.Engine.Repositories.Pegging"))
                .AsImplementedInterfaces()
                .WithScopedLifetime()
                // 注册所有服务（IERPOrderSyncService → ERPOrderSyncService 等）
                .AddClasses(classes => classes.InNamespaces(
                    "LPS.APS.Engine.Services.Sync",
                    "LPS.APS.Engine.Services.Auth"))
                .AsImplementedInterfaces()
                .WithScopedLifetime()
        );

        return services;
    }

    /// <summary>
    /// 注册数据库健康检查
    /// </summary>
    public static IServiceCollection AddDatabaseHealthCheck(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database-aps")
            .AddCheck<OdsDatabaseHealthCheck>("database-ods")
            .AddCheck<AuthDatabaseHealthCheck>("database-auth");

        return services;
    }
}

/// <summary>
/// 数据库配置验证器（三库架构）
/// </summary>
public class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var failures = new List<string>();

        // 验证APS本地库配置
        if (!options.APS.IsValid())
        {
            if (string.IsNullOrWhiteSpace(options.APS.ConnectionString))
                failures.Add("APS本地库连接字符串不能为空");
            if (options.APS.CommandTimeout <= 0)
                failures.Add("APS本地库命令超时时间必须大于0");
            if (options.APS.ConnectionTimeout <= 0)
                failures.Add("APS本地库连接超时时间必须大于0");
        }

        // 验证ODS集成防腐层配置
        if (!options.ODS.IsValid())
        {
            if (string.IsNullOrWhiteSpace(options.ODS.ConnectionString))
                failures.Add("ODS集成防腐层连接字符串不能为空");
            if (options.ODS.CommandTimeout <= 0)
                failures.Add("ODS集成防腐层命令超时时间必须大于0");
            if (options.ODS.ConnectionTimeout <= 0)
                failures.Add("ODS集成防腐层连接超时时间必须大于0");
        }

        // 验证Auth权限库配置
        if (!options.Auth.IsValid())
        {
            if (string.IsNullOrWhiteSpace(options.Auth.ConnectionString))
                failures.Add("Auth权限库连接字符串不能为空");
            if (options.Auth.CommandTimeout <= 0)
                failures.Add("Auth权限库命令超时时间必须大于0");
            if (options.Auth.ConnectionTimeout <= 0)
                failures.Add("Auth权限库连接超时时间必须大于0");
        }

        if (options.MaxRetryCount < 0)
            failures.Add("最大重试次数不能小于0");

        if (options.RetryDelay < 0)
            failures.Add("重试延迟不能小于0");

        return failures.Any()
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// APS本地库健康检查
/// </summary>
public class DatabaseHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly DatabaseConnectionManager _connectionManager;

    public DatabaseHealthCheck(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _connectionManager.TestConnectionAsync(DatabaseId.APS);

            return isHealthy
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("APS本地库连接正常")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("APS本地库连接失败");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("APS本地库健康检查异常", ex);
        }
    }
}

/// <summary>
/// ODS集成防腐层健康检查
/// </summary>
public class OdsDatabaseHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly DatabaseConnectionManager _connectionManager;

    public OdsDatabaseHealthCheck(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _connectionManager.TestConnectionAsync(DatabaseId.ODS);

            return isHealthy
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("ODS集成防腐层连接正常")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("ODS集成防腐层连接失败");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("ODS集成防腐层健康检查异常", ex);
        }
    }
}

/// <summary>
/// Auth权限库健康检查
/// </summary>
public class AuthDatabaseHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly DatabaseConnectionManager _connectionManager;

    public AuthDatabaseHealthCheck(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _connectionManager.TestConnectionAsync(DatabaseId.Auth);

            return isHealthy
                ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Auth权限库连接正常")
                : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Auth权限库连接失败");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Auth权限库健康检查异常", ex);
        }
    }
}
