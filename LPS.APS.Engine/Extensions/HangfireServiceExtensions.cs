using Microsoft.Extensions.DependencyInjection;
using LPS.APS.Engine.Services.Sync;

namespace LPS.APS.Engine.Extensions;

/// <summary>
/// Hangfire Job定义扩展（Engine层只定义Job，不配置Hangfire服务）
/// Hangfire服务配置在Web层完成
/// </summary>
public static class HangfireJobExtensions
{
    /// <summary>
    /// 注册所有定时任务Job服务
    /// </summary>
    public static IServiceCollection AddScheduledJobs(this IServiceCollection services)
    {
        // ERP 订单同步服务已在 AddDatabaseServices 中注册
        // Hangfire RecurringJob 在 Web 层 Program.cs 中配置调度计划
        
        return services;
    }
}
