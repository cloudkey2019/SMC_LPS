using Microsoft.Extensions.DependencyInjection;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Repositories.Governance;
using LPS.APS.Engine.Repositories.Auth;

namespace LPS.APS.Engine.Extensions;

/// <summary>
/// 治理服务注册扩展（阶段 A-3：3号位 Engine 治理仓储 DI）
/// 按前置准备清单决策：3号位治理仓储采用自写扩展方法注册，不修改 2号位 DatabaseServiceExtensions。
/// A-7 扩展：治理审计日志仓储（GovernanceAuditLogRepository）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public static class GovernanceServiceExtensions
{
    /// <summary>
    /// 注册治理仓储（RuleSetVersion / ParameterSetVersion / StrategyProfile / StrategyProfileVersion / GovernanceAuditLog / ScheduleRun / PlanVersion）
    /// P0-08 扩展：ScheduleRunRepository / PlanVersionRepository（运行生命周期治理，3号位）
    /// </summary>
    /// <remarks>开发者：3号位</remarks>
    public static IServiceCollection AddGovernanceRepositories(this IServiceCollection services)
    {
        services.AddScoped<IRuleSetVersionRepository, RuleSetVersionRepository>();
        services.AddScoped<IParameterSetVersionRepository, ParameterSetVersionRepository>();
        services.AddScoped<IStrategyProfileRepository, StrategyProfileRepository>();
        services.AddScoped<IStrategyProfileVersionRepository, StrategyProfileVersionRepository>();
        services.AddScoped<IGovernanceAuditLogRepository, GovernanceAuditLogRepository>();
        services.AddScoped<IScheduleRunRepository, ScheduleRunRepository>();
        services.AddScoped<IPlanVersionRepository, PlanVersionRepository>();

        return services;
    }
}
