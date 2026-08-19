using Microsoft.Extensions.Options;

namespace LPS.APS.Shared.Configuration;

/// <summary>
/// 应用程序配置验证器
/// </summary>
public class ApplicationOptionsValidator : IValidateOptions<ApplicationOptions>
{
    public ValidateOptionsResult Validate(string? name, ApplicationOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Name))
        {
            failures.Add("应用程序名称不能为空");
        }

        if (string.IsNullOrWhiteSpace(options.Version))
        {
            failures.Add("应用程序版本不能为空");
        }

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            failures.Add("环境名称不能为空");
        }

        // 验证CORS配置
        if (options.Cors?.AllowedOrigins?.Length == 0)
        {
            failures.Add("CORS允许的源不能为空");
        }

        if (options.Cors?.AllowedMethods?.Length == 0)
        {
            failures.Add("CORS允许的HTTP方法不能为空");
        }

        return failures.Any()
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// API配置验证器
/// </summary>
public class ApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Version))
        {
            failures.Add("API版本不能为空");
        }

        if (string.IsNullOrWhiteSpace(options.Title))
        {
            failures.Add("API标题不能为空");
        }

        // 验证限流配置
        if (options.RateLimit.Enabled)
        {
            if (options.RateLimit.PermitLimit <= 0)
            {
                failures.Add("限流允许的请求数必须大于0");
            }

            if (options.RateLimit.Window <= TimeSpan.Zero)
            {
                failures.Add("限流时间窗口必须大于0");
            }
        }

        return failures.Any()
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// 业务配置验证器
/// </summary>
public class BusinessOptionsValidator : IValidateOptions<BusinessOptions>
{
    public ValidateOptionsResult Validate(string? name, BusinessOptions options)
    {
        var failures = new List<string>();

        // 验证调度配置
        if (options.Scheduling.MaxJobsPerSchedule <= 0)
        {
            failures.Add("每次调度的最大作业数必须大于0");
        }

        if (options.Scheduling.DefaultTimeWindow <= TimeSpan.Zero)
        {
            failures.Add("默认时间窗口必须大于0");
        }

        if (options.Scheduling.OptimizationTimeout <= TimeSpan.Zero)
        {
            failures.Add("优化超时时间必须大于0");
        }

        if (options.Scheduling.Parallelism <= 0)
        {
            failures.Add("并行度必须大于0");
        }

        // 验证验证配置
        if (options.Validation.MaxJobDuration <= TimeSpan.Zero)
        {
            failures.Add("最大作业持续时间必须大于0");
        }

        if (options.Validation.MinOperationDuration <= TimeSpan.Zero)
        {
            failures.Add("最小操作持续时间必须大于0");
        }

        if (options.Validation.MinOperationDuration >= options.Validation.MaxJobDuration)
        {
            failures.Add("最小操作持续时间必须小于最大作业持续时间");
        }

        return failures.Any()
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Redis配置验证器
/// </summary>
public class RedisOptionsValidator : IValidateOptions<RedisOptions>
{
    public ValidateOptionsResult Validate(string? name, RedisOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("Redis连接字符串不能为空");
        }

        if (string.IsNullOrWhiteSpace(options.InstanceName))
        {
            failures.Add("Redis实例名称不能为空");
        }

        if (options.Database < 0 || options.Database > 15)
        {
            failures.Add("Redis数据库编号必须在0-15之间");
        }

        if (options.ConnectTimeout <= 0)
        {
            failures.Add("Redis连接超时时间必须大于0");
        }

        if (options.SyncTimeout <= 0)
        {
            failures.Add("Redis同步超时时间必须大于0");
        }

        if (options.AsyncTimeout <= 0)
        {
            failures.Add("Redis异步超时时间必须大于0");
        }

        return failures.Any()
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
