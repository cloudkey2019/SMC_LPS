namespace LPS.APS.Shared.Configuration;

/// <summary>
/// 应用程序配置选项
/// </summary>
public class ApplicationOptions
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public CorsOptions Cors { get; set; } = new();
}

/// <summary>
/// CORS配置选项
/// </summary>
public class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public string[] AllowedMethods { get; set; } = Array.Empty<string>();
    public string[] AllowedHeaders { get; set; } = Array.Empty<string>();
    public bool AllowCredentials { get; set; } = true;
}

/// <summary>
/// API配置选项
/// </summary>
public class ApiOptions
{
    public string Version { get; set; } = "v1";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RateLimitOptions RateLimit { get; set; } = new();
}

/// <summary>
/// 限流配置选项
/// </summary>
public class RateLimitOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
    public int PermitLimit { get; set; } = 100;
    public bool SlidingWindow { get; set; } = true;
}

/// <summary>
/// 业务配置选项
/// </summary>
public class BusinessOptions
{
    public SchedulingOptions Scheduling { get; set; } = new();
    public ValidationOptions Validation { get; set; } = new();
}

/// <summary>
/// 排程配置选项
/// </summary>
public class SchedulingOptions
{
    public int MaxJobsPerSchedule { get; set; } = 1000;
    public TimeSpan DefaultTimeWindow { get; set; } = TimeSpan.FromHours(8);
    public TimeSpan OptimizationTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int Parallelism { get; set; } = 4;
}

/// <summary>
/// 验证配置选项
/// </summary>
public class ValidationOptions
{
    public bool EnableStrictValidation { get; set; } = true;
    public TimeSpan MaxJobDuration { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan MinOperationDuration { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Redis配置选项
/// </summary>
public class RedisOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string InstanceName { get; set; } = "LPS.APS";
    public int Database { get; set; } = 0;
    public int ConnectTimeout { get; set; } = 5000;
    public int SyncTimeout { get; set; } = 5000;
    public int AsyncTimeout { get; set; } = 5000;
}
