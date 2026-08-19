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
    /// 新增规则只需在 Rules 命名空间下创建 IXxxRule + XxxRule，无需手动注册
    /// </summary>
    public static IServiceCollection AddBusinessRuleServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.Scan(scan => scan
            .FromAssemblies(assembly)
                .AddClasses(classes => classes.Where(t =>
                    t.Namespace != null &&
                    t.Namespace.StartsWith("LPS.APS.BusinessRules.Rules")))
                .AsImplementedInterfaces()
                .WithScopedLifetime()
        );

        return services;
    }
}
