using Microsoft.Extensions.DependencyInjection;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Repositories.Governance;
using LPS.APS.Engine.Repositories.Auth;

namespace LPS.APS.Engine.Extensions;

/// <summary>
/// 治理服务注册扩展（阶段 A-3：3号位 Engine 治理仓储 DI）
/// 按前置准备清单决策：3号位治理仓储采用自写扩展方法注册，不修改 2号位 DatabaseServiceExtensions。
/// A-7 扩展：治理审计日志仓储（AuditLogRepository）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public static class GovernanceServiceExtensions
{
    /// <summary>
    /// 注册治理仓储（RuleSetVersion / ParameterSetVersion / StrategyProfile / StrategyProfileVersion / AuditLog / ScheduleRun / PlanVersion）
    /// P0-08 扩展：ScheduleRunRepository / PlanVersionRepository（运行生命周期治理，3号位）
    /// </summary>
    /// <remarks>开发者：3号位</remarks>
    public static IServiceCollection AddGovernanceRepositories(this IServiceCollection services)
    {
        services.AddScoped<IRuleSetVersionRepository, RuleSetVersionRepository>();
        services.AddScoped<IParameterSetVersionRepository, ParameterSetVersionRepository>();
        services.AddScoped<IStrategyProfileRepository, StrategyProfileRepository>();
        services.AddScoped<IStrategyProfileVersionRepository, StrategyProfileVersionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IScheduleRunRepository, ScheduleRunRepository>();
        services.AddScoped<IPlanVersionRepository, PlanVersionRepository>();
        // G1：三张主表列表查询（3-4联调）
        services.AddScoped<IRuleSetRepository, RuleSetRepository>();
        services.AddScoped<IParameterSetRepository, ParameterSetRepository>();
        // G7：域依赖关系查询（3-4联调）
        services.AddScoped<IDomainDependencyRepository, DomainDependencyRepository>();
        // E-1：域定义治理写侧（3号位 CRUD + 启用/停用 + 审计）
        services.AddScoped<IDomainDefinitionRepository, DomainDefinitionRepository>();
        // Setup换型：产品转换换型规则（SetupTransitionRule）不再走独立物理表仓储——S-3 撤销（承载 = RuleSetVersion.ContentSnapshotJson 子块）
        // Setup换型：主数据只读查询仓储（Setup 契约 §11.1 Code 回带用；3号位 依用户授权例外自写，Engine 层此前无独立主数据读仓储）
        services.AddScoped<IMasterDataLookupRepository, MasterDataLookupRepository>();
        // Setup换型：规则缺失（uncovered-stats）只读聚合查询仓储（#10 端点；3号位 依 G4 只读查询归属自写，Setup 治理域只读事实）
        services.AddScoped<ISetupUncoveredStatRepository, SetupUncoveredStatRepository>();

        return services;
    }
}
