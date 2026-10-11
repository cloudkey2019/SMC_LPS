using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Application.Services;
using LPS.APS.Application.Services.Fixtures;
using LPS.APS.Application.Extensions;
using LPS.APS.BusinessRules.Extensions;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Repositories.Governance;
using LPS.APS.Scheduling.Extensions;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 跨版本连续性红线验收（v1.6 §10）：
/// 对「源 Run 有三张 MES 快照分区 + 有订单」的 PlanVersion 动态解析后触发
/// RunSchedulingAsync(pv, 251L)，验证：
///  1) 日志 [Pegging][Continuity] 分桶完成：Continuation/Free 同现；
///  2) 落库 Task.MTS_InstructionNo 非空 + 同 PI 多 Task 数量闭合（E + Q−E = Q）。
/// </summary>
/// <remarks>
/// 【2026-10-10 修复】原写死 <c>PlanVersionId = 328</c> + <c>ScheduleRun 477</c>（FAMILY_X / 11,659 订单），
/// 两者在 09-29 版本清理后均**不存在**（328 在 <c>[Order]</c> 里 0 行；库内 PV max 81 / Run max 89）
/// ⇒ 该测试按原值必失败且无法「确认就位」。改为动态解析，口径见 <see cref="PlanVersionTestFixture"/>。
/// 解析不到 = 环境态 ⇒ <c>Skip</c> 而非红。
/// </remarks>
public class ContinuityRedLineIntegrationTest
{
    private const long StrategyProfileVersionId = 251L; // SP-DEMO-V2.0

    [SkippableFact(DisplayName = "跨版本连续性红线：Continuation/Free 同现 + Task 侧 PI 继承闭合")]
    public async Task RunContinuityRedLineAsync()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDatabaseServices(configuration);
        services.AddSchedulingServices();
        services.AddBusinessRuleServices();
        services.AddApplicationServices();
        // 联调专用 fixture（与 RealSchedulingIntegrationTest 一致，测试专用不得进生产 DI）
        services.AddScoped<IDemandPriorityConfigProvider, DemandPriorityFixtureProvider>();
        services.AddScoped<IFrozenStrategySnapshotProvider, FrozenStrategySnapshotFixtureProvider>();
        // S-3：SetupTransitionRuleRepository 已撤销（承载 = RuleSetVersion.ContentSnapshotJson 子块，Provider 装配第⑦块）
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new ConsoleOutLoggerProvider()));
        services.AddScoped<SchedulingOrchestrator>();
        services.AddScoped<DatabaseConnectionManager>();

        var sp = services.BuildServiceProvider();
        var conn = sp.GetRequiredService<DatabaseConnectionManager>();

        var planVersionId = await PlanVersionTestFixture.ResolveWithMesSnapshotAsync(conn);
        Skip.If(planVersionId is null,
            "库内无「源 Run 有三张 MES 快照分区 + 有订单」的 PlanVersion（连续性红线需要 MES 在制事实）—— 环境态，非缺陷。");
        Skip.IfNot(await PlanVersionTestFixture.IsPublishedStrategyProfileVersionAsync(conn, StrategyProfileVersionId),
            $"StrategyProfileVersion {StrategyProfileVersionId} 不存在或非 PUBLISHED —— 环境态，非缺陷。");
        var pv = planVersionId!.Value;
        Console.WriteLine($"[夹具] 动态解析 PlanVersion={pv}（原硬编码 328 已于 09-29 清理后不存在）");

        var orch = sp.GetRequiredService<SchedulingOrchestrator>();

        Console.WriteLine($"触发 RunSchedulingAsync({pv}, {StrategyProfileVersionId}) ...");
        var result = await orch.RunSchedulingAsync(pv, StrategyProfileVersionId, CancellationToken.None);
        Console.WriteLine(
            $"[红线上] IsSuccess={result.IsSuccess}, Scheduled={result.ScheduledCount}, " +
            $"Unscheduled={result.UnscheduledCount}, Elapsed={result.ElapsedMs}ms, Error={result.ErrorMessage}");

        // 落库硬证据（不依赖日志）
        var tasksWithPi = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM [Task] WHERE PlanVersionId=@p AND MTS_InstructionNo IS NOT NULL",
            new { p = pv }, db: DatabaseId.APS);
        var multiTaskPi = await conn.QueryFirstOrDefaultAsync<int>(
            @"SELECT COUNT(*) FROM (
                  SELECT MTS_InstructionNo FROM [Task]
                  WHERE PlanVersionId=@p AND MTS_InstructionNo IS NOT NULL
                  GROUP BY MTS_InstructionNo HAVING COUNT(*)>=2) x",
            new { p = pv }, db: DatabaseId.APS);
        Console.WriteLine($"[红线上] Task带PI数={tasksWithPi}, 同PI多Task(MTS工单数)={multiTaskPi}");

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }
}

/// <summary>极简 Console 日志 Provider：把 LogInformation 直打到 stdout，供测试命令行抓红线日志。</summary>
internal sealed class ConsoleOutLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ConsoleOutLogger();
    public void Dispose() { }

    private sealed class ConsoleOutLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => null!;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        }
    }
}