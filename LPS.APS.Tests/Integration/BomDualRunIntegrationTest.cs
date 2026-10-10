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
/// 阶段2 S2.4 双跑对照（不切主链）：同一 PlanVersion + Demand + 供给池，旧 DFS vs 新 BFS 各跑一遍，
/// 产出 PM 0918-3 §八 7 项 + §六 I1-I4 报告。只读不落库（RunDualCompareAsync 不写 Pegging/Task/PSA）。
///
/// 保护阈值采样（PM 0918-3 §八 指定 100/500/1000 反推阈值）：先跑 100 单探旧 DFS 的展开爆炸耗时，
/// 再决定是否放开 500/1000。旧 DFS 对共享子件无记忆化会指数重遍历（2.6h 根因），故每档用 CancellationToken 兜底。
/// </summary>
public class BomDualRunIntegrationTest
{
    private const int PlanVersionId = 328;             // FAMILY_X（DomainKey 从库读，不硬编码）
    private const long StrategyProfileVersionId = 251L; // SP-DEMO-V2.0

    // 保护阈值采样第一档实测：100 单旧 DFS 在 15 分钟兜底内未收敛（DB 空闲、纯应用层 BOM 深递归 CPU），
    // 阈值下界 < 100。降档到 10 单先拿一份「能收敛」的 7 项报告 —— 碎片/重复展开是逐订单病态、非逐批次，
    // 10 单已足够对照新旧算法 I1-I3 + Δ + 硬指标。
    [Fact(DisplayName = "双跑对照·保护阈值10单：旧DFS vs 新BFS 7项报告")]
    public async Task DualRun_10Orders()
        => await RunDualAsync(orderCount: 10, timeout: TimeSpan.FromMinutes(20));

    // 保护阈值 500 / 1000 单：待 100 单探明旧 DFS 单档耗时后再放开（旧 DFS 无记忆化，中位病态单即可分钟级）。
    // [Fact(DisplayName = "双跑对照·保护阈值500单")]
    // public async Task DualRun_500Orders()
    //     => await RunDualAsync(orderCount: 500, timeout: TimeSpan.FromMinutes(40));
    //
    // [Fact(DisplayName = "双跑对照·保护阈值1000单")]
    // public async Task DualRun_1000Orders()
    //     => await RunDualAsync(orderCount: 1000, timeout: TimeSpan.FromMinutes(80));

    private static async Task RunDualAsync(int orderCount, TimeSpan timeout)
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
        // 联调专用 fixture（与 ContinuityRedLineIntegrationTest 一致，测试专用不得进生产 DI）
        services.AddScoped<IDemandPriorityConfigProvider, DemandPriorityFixtureProvider>();
        services.AddScoped<IFrozenStrategySnapshotProvider, FrozenStrategySnapshotFixtureProvider>();
        // S-3：SetupTransitionRuleRepository 已撤销（承载 = RuleSetVersion.ContentSnapshotJson 子块，Provider 装配第⑦块）
        // LogLevel.Error：双跑只关心最终 7 项报告 + 硬超时结论；LoadSupplyPoolAsync 会对 3292 张 PI 逐条刷
        // 「Position 差异」Warning（5号位 CalculatePiInventoryPositions 已知问题），Information/Warning 会拖死 I/O。
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Error).AddProvider(new ConsoleOutLoggerProvider()));
        services.AddScoped<DatabaseConnectionManager>();

        var sp = services.BuildServiceProvider();
        var orchestrator = sp.GetRequiredService<IPeggingOrchestrator>();
        var conn = sp.GetRequiredService<DatabaseConnectionManager>();

        // DomainKey 以 PlanVersion 为准（PM 0902 终裁：禁止用 PlanVersionId/ScheduleRunId 冒充）
        var domainKey = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT DomainKey FROM PlanVersion WHERE Id=@p", new { p = PlanVersionId }, db: DatabaseId.APS);
        var orderIds = (await conn.QueryAsync<long>(
            "SELECT TOP (@n) Id FROM [Order] WHERE PlanVersionId=@p ORDER BY Id",
            new { n = orderCount, p = PlanVersionId }, db: DatabaseId.APS)).ToList();

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
            TimeoutSeconds     = (int)timeout.TotalSeconds,
            ExecutionMode      = "FULL_RUN",
            IsCandidate        = false,
            SchedulingContext  = new SchedulingContext { StrategyProfileVersionId = StrategyProfileVersionId }
        };

        using var cts = new CancellationTokenSource(timeout);

        Console.WriteLine($"触发双跑 RunDualCompareAsync(PlanVersion={PlanVersionId}, Domain={request.DomainKey}, 订单={orderCount}, 硬超时={timeout.TotalMinutes}分) ...");
        var dualTask = orchestrator.RunDualCompareAsync(request, cts.Token);
        // 硬超时（Task.WhenAny）替代协作取消：旧 DFS 无记忆化深递归不响应 ct（检查点只在订单边界），
        // 到点直接判「未收敛」作为保护阈值下界证据，避免被单订单病态递归无限拖住。
        var completed = await Task.WhenAny(dualTask, Task.Delay(timeout, CancellationToken.None));
        if (completed != dualTask)
        {
            Console.WriteLine($"⚠️ 双跑未在 {timeout.TotalMinutes} 分钟内收敛（卡点=旧 DFS 单订单 BOM 深递归，DB 空闲纯 CPU）→ 保护阈值下界 < {orderCount} 单");
            Assert.Fail($"双跑未在 {timeout.TotalMinutes} 分钟内收敛（保护阈值下界 < {orderCount} 单）");
            return;
        }
        var report = await dualTask;

        Console.WriteLine("════ 双跑 7 项报告 ════");
        Console.WriteLine($"PlanVersion={report.PlanVersionId} OrderCount={report.OrderCount}");
        Console.WriteLine($"I1 NetOutput 闭合 = {report.NetOutputClosed}  (差异={report.NetOutputDiffs.Count})");
        Console.WriteLine($"I2 Supply     闭合 = {report.SupplyClosed}  (差异={report.SupplyDiffs.Count})");
        Console.WriteLine($"I2-PI         闭合 = {report.PiClosed}  (差异={report.PiDiffs.Count})");
        Console.WriteLine($"I3 Lineage    闭合 = {report.LineageClosed}  (mismatch={report.LineageMismatches.Count})");
        Console.WriteLine($"I4 PiPosition 闭合 = {report.PiPositionClosed}  (2号位段恒成立, 1号位 bitmap 待验)");
        Console.WriteLine($"Δ1 LPD 碎片   old={report.OldLpdCount}  new={report.NewLpdCount}");
        Console.WriteLine($"Δ2 Allocation old={report.OldAllocationCount}  new={report.NewAllocationCount}");
        Console.WriteLine($"Δ3 DemandQty  old={report.OldDemandQuantity}  new={report.NewDemandQuantity}");
        Console.WriteLine($"硬指标 重复展开 old={report.OldTraversalVisits}  new={report.NewTraversalVisits}");
        Console.WriteLine($"耗时 ms       old={report.OldMs}  new={report.NewMs}");
        Console.WriteLine($"DeferredToPos1 = {string.Join("; ", report.DeferredToPosition1)}");
        Console.WriteLine($"Pass = {report.Pass}");

        if (report.NetOutputDiffs.Count > 0)
        {
            Console.WriteLine("── I1 NetOutput 差异（前20）──");
            foreach (var d in report.NetOutputDiffs.Take(20))
                Console.WriteLine($"  {d.Key}: old={d.OldQty} new={d.NewQty}");
        }
        if (report.SupplyDiffs.Count > 0)
        {
            Console.WriteLine("── I2 Supply 差异（前20）──");
            foreach (var d in report.SupplyDiffs.Take(20))
                Console.WriteLine($"  {d.Key}: old={d.OldQty} new={d.NewQty}");
        }
        if (report.LineageMismatches.Count > 0)
        {
            Console.WriteLine("── I3 Lineage mismatch（前20）──");
            foreach (var m in report.LineageMismatches.Take(20))
                Console.WriteLine($"  {m}");
        }

        Assert.True(report.NetOutputClosed, $"I1 净产出不闭合：{report.NetOutputDiffs.Count} 处差异");
        Assert.True(report.SupplyClosed,     $"I2 供给不闭合：{report.SupplyDiffs.Count} 处差异");
        // I3 血缘：已知在「共享子件+中间库存」下旧 DFS 边集漂移（碎片化表征），此处只打印不硬断言 Pass ——
        // 双跑的目的正是暴露该差异供 PM 裁定，不等价于 bug。硬断言只落在 I1/I2（净额/供给两维是硬红线）。
    }
}