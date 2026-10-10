using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Services.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 独立 BOM 接货入口（2号位）：
/// 与 NightlyBatchOrchestrator 解耦，单独触发「找最近 READY 批次 → 接货」，供白天补接货 / 联调。
/// 接货落库到 APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW（StagePath 事实源，跨版本连续性 E>0 的关键上游）。
/// </summary>
/// <remarks>
/// 【2026-09-29 修复】原为无闸门 `[Fact]`，两个问题：
/// <list type="number">
/// <item>
/// <b>它是破坏性写</b>：BOM 接货对 <c>APS_BOM_RAW</c> / <c>APS_BOM_STAGE_PATH_RAW</c> 等表**整表 TRUNCATE**
/// 后重灌当批（见 `aps_bom` 接货事故的教训）⇒ 每次 `dotnet test` 都在**无提示地清表重灌**。
/// 现与 `RealDomainFullRunTest` 统一：须显式设 <c>APS_REAL_DOMAIN_RUN=1</c> 才运行。
/// </item>
/// <item>
/// <b>红灯是环境态、不是缺陷</b>：原断言「必须找到 READY 批次」，但上游 ERP 没产新批次时
/// 「无 READY 批次」是**正常状态** ⇒ 该测试会在没有新 BOM 的日子恒红。现改为 <c>Skip</c>。
/// </item>
/// </list>
/// </remarks>
public class BOMIntakeIntegrationTest
{
    // 【2026-10-08 修复】原为写死的 `PlanVersionId = 328`（CNT_FAMILYX）。该版本在 09-29 清理后
    // 已不存在（现为 1~4），写死即过期。改为按「当次 ScheduleRun 的 DAILY_BASELINE 版本」动态解析，
    // 与 NightlyBatchOrchestrator Step 5 取 planVersionIds 的口径一致，避免再次腐化。
    private static async Task<List<int>> ResolvePlanVersionIdsAsync(DatabaseConnectionManager conn)
        => (await conn.QueryAsync<int>(
            @"SELECT Id FROM PlanVersion
              WHERE VersionCategory = 'DAILY_BASELINE'
                AND SourceScheduleRunId = (SELECT MAX(Id) FROM ScheduleRun)",
            db: DatabaseId.APS)).ToList();

    [SkippableFact(DisplayName = "独立接货：拉取最近 READY BOM 批次并落库（APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW）")]
    public async Task IntakeLatestReadyBatchAsync()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("APS_REAL_DOMAIN_RUN") == "1",
            "BOM 接货会整表 TRUNCATE 后重灌；须显式设 APS_REAL_DOMAIN_RUN=1 才运行。");

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDatabaseServices(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new ConsoleOutLoggerProvider()));

        var sp = services.BuildServiceProvider();
        var puller = sp.GetRequiredService<IBOMResultPullService>();
        var conn = sp.GetRequiredService<DatabaseConnectionManager>();

        var planVersionIds = await ResolvePlanVersionIdsAsync(conn);
        Console.WriteLine($"[PlanVersion] 动态解析到 {planVersionIds.Count} 个: {string.Join(", ", planVersionIds)}");
        Skip.If(planVersionIds.Count == 0, "PlanVersion 表无当次 DAILY_BASELINE 版本，无法生成 OrderBomRequestLink —— 环境态。");

        Console.WriteLine($"触发独立接货 IntakeLatestReadyBatchAsync([{string.Join(",", planVersionIds)}]) ...");
        var result = await puller.IntakeLatestReadyBatchAsync(planVersionIds, CancellationToken.None);
        Console.WriteLine(
            $"[接货] IntakePerformed={result.IntakePerformed}, BatchNo={result.BatchNo ?? "<无READY批次>"}, PulledCount={result.PulledCount}");

        // 「无 READY 批次」= 上游 ERP 尚未产批，属**正常环境态**，不是缺陷 ⇒ Skip 而非 Fail
        // （原 `Assert.True` 会把正常态报成红灯，见类 remarks）。
        Skip.IfNot(result.IntakePerformed,
            $"上游无 READY 状态的 BOM 批次（BatchNo={result.BatchNo ?? "<无>"}），接货未执行 —— 环境态，非缺陷。");

        // 落库硬证据：接货后两表的当批行数 + StagePath 覆盖的物料数
        var bomRowCount = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM APS_BOM_RAW WHERE BatchNo=@b",
            new { b = result.BatchNo }, db: DatabaseId.APS);
        var stageDetailRowCount = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM APS_BOM_STAGE_PATH_RAW WHERE BatchNo=@b",
            new { b = result.BatchNo }, db: DatabaseId.APS);
        var stageMaterialCount = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(DISTINCT ChildMaterialCode) FROM APS_BOM_STAGE_PATH_RAW WHERE BatchNo=@b",
            new { b = result.BatchNo }, db: DatabaseId.APS);

        Console.WriteLine(
            $"[接货后] APS_BOM_RAW={bomRowCount}行, APS_BOM_STAGE_PATH_RAW={stageDetailRowCount}行, " +
            $"StagePath覆盖物料={stageMaterialCount}个");
    }
}