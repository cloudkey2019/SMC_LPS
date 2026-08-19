using LPS.APS.Core.Interfaces;
using LPS.APS.Scheduling.Solvers;
using Microsoft.Extensions.DependencyInjection;

namespace LPS.APS.Scheduling.Extensions;

/// <summary>
/// Scheduling 层 DI 注册扩展
/// </summary>
public static class SchedulingServiceExtensions
{
    /// <summary>
    /// 注册排程算法服务（1号位）
    /// </summary>
    public static IServiceCollection AddSchedulingServices(this IServiceCollection services)
    {
        // 注册1号位核心接口（IFiniteCapacityScheduler）
        services.AddSingleton<IFiniteCapacityScheduler, FiniteCapacitySolver>();

        // 注册内部算法组件
        services.AddSingleton<TimeSlotFinder>();
        services.AddSingleton<SetupOptimizer>();

        return services;
    }
}
