using Hangfire.Dashboard;

namespace LPS.APS.Web.Extensions;

/// <summary>
/// Hangfire Dashboard 鉴权过滤器（生产环境使用）
/// 开发环境下 Dashboard 无鉴权限制，可直接访问
/// 生产环境下需要验证用户身份（后续接入 APS_Auth 权限体系）
/// </summary>
public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // TODO: 接入 APS_Auth 权限体系后，检查用户是否有 Hangfire 管理权限
        // 当前：仅允许已认证用户访问
        return httpContext.User?.Identity?.IsAuthenticated ?? false;
    }
}
