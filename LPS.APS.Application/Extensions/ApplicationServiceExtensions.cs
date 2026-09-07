using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace LPS.APS.Application.Extensions;

/// <summary>
/// Application 层 DI 注册扩展（3号位）
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// 注册应用服务（Scrutor 自动扫描）
    /// 新增服务只需在 Services 命名空间下创建 IXxxService + XxxService，无需手动注册
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.Scan(scan => scan
            .FromAssemblies(assembly)
                .AddClasses(classes => classes.Where(t =>
                    t.Namespace != null &&
                    t.Namespace.StartsWith("LPS.APS.Application.Services") &&
                    !t.Namespace.StartsWith("LPS.APS.Application.Services.Fixtures") &&
                    t.DeclaringType == null))
                .AsImplementedInterfaces()
                .WithScopedLifetime()
        );

        return services;
    }
}
