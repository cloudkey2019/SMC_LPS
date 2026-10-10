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
        // 注册1号位核心接口（IFiniteCapacityScheduler）——唯一真实生产入口，内部走 SolveAsync → Phase1-5
        services.AddSingleton<IFiniteCapacityScheduler, FiniteCapacitySolver>();

        // 死代码清理批次（20260923）：原 TimeSlotFinder / SetupOptimizer 实例注册已移除——
        // TimeSlotFinder 随死代码文件一并删除；SetupOptimizer 实例无消费方
        // （Phase1/2/4/5 只调其静态方法，已 grep 确认全仓无 GetService<SetupOptimizer>() / new SetupOptimizer()）。
        // 如后续确需实例化，请在此重新注册。

        return services;
    }
}
