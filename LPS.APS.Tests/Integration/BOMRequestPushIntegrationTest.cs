using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Services.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// BOM 展开请求推送（2号位）。生产入口 = Hangfire `bom-request-push`（每日 00:00）→ `BOMRequestService.PushBOMRequestToODSAsync`。
///
/// 【注意：本测试会往 ODS 写数据】动的是 ODS `MES_API_BOM_Request`（批头，Status='PENDING'）
/// 与 `MES_API_BOM_Request_Detail`（活跃订单明细，SqlBulkCopy）。**不 truncate 任何表**；
/// 真正的全量展开由 ODS 侧 `sp_ExpandBOMBatch_vNext` 完成（该 SP 开头四条全表 truncate），本测试不调它。
///
/// 为何要有这一个测试：全量批次的唯一正规生产入口就是 `BOMRequestService`。手工造 `REQ_` 批头
/// （或手工造别的批头）容易与生产逻辑漂移——2026-09-28/29 的事故正是「手工出现在同一张表里的批头」
/// 被 `sp_ExpandBOMBatch_vNext` 的 `@BatchNo IS NULL → TOP 1 ORDER BY CreatedAt DESC` 兜底选中，
/// 先 truncate 四张表、再只写 43 行，327 万行全量批即此消失。见台账 §BOM→Stage。
/// </summary>
public class BOMRequestPushIntegrationTest
{
    [Fact(DisplayName = "BOM请求推送：建 REQ_ 批头 + 活跃订单明细（会写 ODS，不展开）")]
    public async Task PushFullBomRequestAsync()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDatabaseServices(configuration);
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new ConsoleOutLoggerProvider()));

        var sp = services.BuildServiceProvider();
        var pusher = sp.GetRequiredService<IBOMRequestService>();

        Console.WriteLine("推送全量 BOM 展开请求（从 Order_Canonical 划活跃订单）...");
        var result = await pusher.PushBOMRequestToODSAsync(CancellationToken.None);
        Console.WriteLine(
            $"[推送结果] BatchNo={result.BatchNo}, RootCount={result.RootCount}, TotalOrderCount={result.TotalOrderCount}");

        Assert.False(string.IsNullOrWhiteSpace(result.BatchNo), "未生成批次号");
        Assert.True(result.TotalOrderCount > 0, "活跃订单为 0，未推送任何明细");

        Console.WriteLine($"[下一步] EXEC sp_ExpandBOMBatch_vNext @BatchNo='{result.BatchNo}'（全量展开，含四条全表 truncate）");
        Console.WriteLine($"[再下一步] BOMIntakeIntegrationTest 接货 → APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW");
    }
}
