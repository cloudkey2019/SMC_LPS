using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LPS.APS.BusinessRules.Extensions;

/// <summary>
/// BusinessRules 层 DI 注册扩展（5号位）
/// </summary>
public static class BusinessRuleServiceExtensions
{
    /// <summary>
    /// 注册业务规则服务（Scrutor 自动扫描）
    /// V1正式路径：Calculators / Services / Repositories / Loaders / Converters
    /// </summary>
    public static IServiceCollection AddBusinessRuleServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.Scan(scan => scan
            .FromAssemblies(assembly)
                .AddClasses(classes => classes.Where(t =>
                    t.Namespace != null &&
                    (t.Namespace.StartsWith("LPS.APS.BusinessRules.Calculators") ||
                     t.Namespace.StartsWith("LPS.APS.BusinessRules.Services") ||
                     t.Namespace.StartsWith("LPS.APS.BusinessRules.Repositories") ||
                     t.Namespace.StartsWith("LPS.APS.BusinessRules.Loaders") ||
                     t.Namespace.StartsWith("LPS.APS.BusinessRules.Converters"))))
                .AsSelfWithInterfaces()
                .WithScopedLifetime()
        );

        return services;
    }
}
