using LPS.APS.Core.Authorization;
using LPS.APS.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LPS.APS.Web.Extensions;

/// <summary>认证授权扩展（F-G2/F-G3，3号位）</summary>
public static class AuthorizationExtensions
{
    /// <summary>注册功能权限点授权处理器</summary>
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }

    /// <summary>注册 V1 全部功能权限点策略（策略名 = PermissionCode）</summary>
    public static AuthorizationOptions AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var code in PermissionCodes.All)
        {
            options.AddPolicy(code, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(code)));
        }

        return options;
    }
}
