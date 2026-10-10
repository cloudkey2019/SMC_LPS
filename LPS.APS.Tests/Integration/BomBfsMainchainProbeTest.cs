using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Application.Services.Fixtures;
using LPS.APS.Application.Extensions;
using LPS.APS.BusinessRules.Extensions;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Repositories.Governance;
using LPS.APS.Scheduling.Extensions;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Models.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 主链 BFS 只读探针（用户 2026-09-18 裁决弃旧 DFS）：RunBfsOnlyAsync 对真实 PlanVersion 328 跑整域全量订单，
/// 验证新 BFS 展开耗时 + I1/I2 红线校验（ValidatePeggingResult）。不落库、不 Solver，可反复跑。
/// 1 单探针已证：BFS 展开 12ms（21 节点，重复展开 0）+ 红线全过；本测试验证整域（含病态共享子件订单）不拖垮。
/// </summary>
public class BomBfsMainchainProbeTest
{
    private const int PlanVersionId = 328;             // FAMILY_X（DomainKey 从库读，不硬编码）
    private const long StrategyProfileVersionId = 251L; // SP-DEMO-V2.0

    [Fact(DisplayName = "BFS主链整域只读：全量订单展开耗时+I1/I2红线")]
    public async Task BfsMainchain_AllOrders()
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
        // 联调专用 fixture（与 BomDualRunIntegrationTest 一致，测试专用不得进生产 DI）
        services.AddScoped<IDemandPriorityConfigProvider, DemandPriorityFixtureProvider>();
        services.AddScoped<IFrozenStrategySnapshotProvider, FrozenStrategySnapshotFixtureProvider>();
        // S-3：SetupTransitionRuleRepository 已撤销（承载 = RuleSetVersion.ContentSnapshotJson 子块，Provider 装配第⑦块）
        // LogLevel.Error：只关心最终报告；LoadSupplyPoolAsync 会对 3292 张 PI 逐条刷「Position 差异」Warning
        // （5号位 CalculatePiInventoryPositions 已知问题），Information/Warning 会拖死 I/O。
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Error).AddProvider(new ConsoleOutLoggerProvider()));
        services.AddScoped<DatabaseConnectionManager>();

        var sp = services.BuildServiceProvider();
        var orchestrator = sp.GetRequiredService<IPeggingOrchestrator>();
        var conn = sp.GetRequiredService<DatabaseConnectionManager>();

        var domainKey = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT DomainKey FROM PlanVersion WHERE Id=@p", new { p = PlanVersionId }, db: DatabaseId.APS);
        var orderIds = (await conn.QueryAsync<long>(
            "SELECT Id FROM [Order] WHERE PlanVersionId=@p ORDER BY Id",
            new { p = PlanVersionId }, db: DatabaseId.APS)).ToList();

        Console.WriteLine($"整域探针 Domain={domainKey} 订单数={orderIds.Count}");

        var now = DateTime.Now;
        var request = new PeggingExecutionRequest
        {
            PlanVersionId      = PlanVersionId,
            DomainKey          = domainKey ?? string.Empty,
            OrderIds           = orderIds,
            SnapshotAt         = now,
            FrozenWindowStart  = now,
            FrozenWindowEnd    = now.AddHours(2),
            AllowCrossFactory  = false,
            DefaultStrategy    = PeggingStrategyType.FIFO,
            MaxBomDepth        = 10,
            TimeoutSeconds     = 900,
            ExecutionMode      = "FULL_RUN",
            IsCandidate        = false,
            SchedulingContext  = new SchedulingContext
            {
                StrategyProfileVersionId = StrategyProfileVersionId,
                DataCutoffTime           = now,
                PlanHorizonStart         = now,
                PlanHorizonEnd           = now.AddDays(90)
            }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        Console.WriteLine($"触发 BFS 整域只读 RunBfsOnlyAsync(订单={orderIds.Count}) ...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = await orchestrator.RunBfsOnlyAsync(request, cts.Token);
        sw.Stop();

        Console.WriteLine("════ BFS 整域只读报告 ════");
        Console.WriteLine($"OrderCount={report.OrderCount} BFS循环耗时(含供给装载)={report.ElapsedMs}ms 总={sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"展开总次数={report.TraversalVisits} 唯一节点={report.UniqueNodes} 重复展开={report.DuplicateExpansions}");
        Console.WriteLine($"LPD={report.LpdCount} Allocation={report.AllocationCount} 血缘边={report.LineageCount}");
        Console.WriteLine($"DemandQuantity={report.DemandQuantity} Shortage={report.ShortageQuantity}");
        Console.WriteLine($"红线(I1/I2)通过={report.RedLinePass} 错误={report.RedLineErrors.Count}");
        foreach (var e in report.RedLineErrors.Take(20))
            Console.WriteLine($"  红线: {e}");

        Assert.True(report.RedLinePass, $"红线校验失败 {report.RedLineErrors.Count} 处");
    }
}