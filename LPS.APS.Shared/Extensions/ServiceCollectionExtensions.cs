using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using LPS.APS.Shared.Configuration;

namespace LPS.APS.Shared.Extensions;

/// <summary>
/// 共享库服务注册扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册共享库基础服务
    /// </summary>
    public static IServiceCollection AddSharedServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 注册配置选项
        services.AddConfigurationOptions(configuration);

        // 注册内存缓存
        services.AddMemoryCache();

        return services;
    }

    /// <summary>
    /// 注册配置选项
    /// </summary>
    public static IServiceCollection AddConfigurationOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // 注册应用程序配置
        services.Configure<ApplicationOptions>(configuration.GetSection("Application"));
        services.AddSingleton<IValidateOptions<ApplicationOptions>, ApplicationOptionsValidator>();

        // 注册API配置
        services.Configure<ApiOptions>(configuration.GetSection("Api"));
        services.AddSingleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>();

        // 注册业务配置
        services.Configure<BusinessOptions>(configuration.GetSection("Business"));
        services.AddSingleton<IValidateOptions<BusinessOptions>, BusinessOptionsValidator>();

        // 注册Redis配置（可选，待Redis实际接入时启用）
        services.Configure<RedisOptions>(configuration.GetSection("Redis"));
        services.AddSingleton<IValidateOptions<RedisOptions>, RedisOptionsValidator>();

        return services;
    }
}
