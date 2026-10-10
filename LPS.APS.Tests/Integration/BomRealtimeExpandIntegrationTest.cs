using System;
using System.Linq;
using System.Threading.Tasks;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Services.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 白天候选 BOM 实时展开服务 集成测试（2号位，2026-09-28）。
///
/// 【被测】IBomRealtimeExpandService.EnsureExpandedAsync（新增代码）：
///   ① 补写 MES_API_BOM_Request_Detail（白天独立批次号 RT:{yyyyMMdd}，先按 FK 建批次头）
///   ② 调 5号位 sp_ExpandBOMRealtime_vNext(@RequestDetailId)
///   ③ 读 MES_APS_BOM_Workset_Realtime + 三重校验
///
/// 【注意：本测试会往 ODS 写数据】—— 会建 RT:{日期} 批次头 + Detail + 实时展开结果。
///   这是该服务的正常行为（白天候选跑前准备 BOM），幂等：同订单重复调只补增量。
///
/// 【依赖 5号位 侧】sp_ExpandBOMRealtime_vNext 存在；
///   MES_BOM_Edge_RefreshLog 最新记录须为 COMPLETED（否则 SP 前置校验 RAISERROR，
///   服务会捕获并转为 Issue，不硬失败）。
/// </summary>
public class BomRealtimeExpandIntegrationTest
{
    private readonly IBomRealtimeExpandService _expandService;

    // 用 Excel 案例订单（workset例.xlsx: OrderCanonicalId=685137 /
    // OrderNo=P16473290 / FG-AF40-F06D-D / TJ / PRODUCTION_INSTRUCTION / BOMNO=202208021）
    private const long TestOrderCanonicalId = 685137;

    public BomRealtimeExpandIntegrationTest()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddDatabaseServices(configuration);
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        _expandService = serviceProvider.GetRequiredService<IBomRealtimeExpandService>();
    }

    [Fact(DisplayName = "白天BOM实时展开：补Detail → 调5号位SP → 读结果 + 三重校验")]
    public async Task EnsureExpandedAsync_SingleOrder_ProducesRows()
    {
        var result = await _expandService.EnsureExpandedAsync(new[] { TestOrderCanonicalId });

        Console.WriteLine("=== 白天 BOM 实时展开结果 ===");
        Console.WriteLine($"RequestedOrders  = {result.RequestedOrders}");
        Console.WriteLine($"DetailInserted   = {result.DetailInserted}");
        Console.WriteLine($"ExpandedOrders   = {result.ExpandedOrders}");
        Console.WriteLine($"ExpandedRowCount = {result.ExpandedRowCount}");
        Console.WriteLine($"Issues ({result.Issues.Count}):");
        foreach (var issue in result.Issues.Take(10))
            Console.WriteLine($"  - {issue}");

        Assert.Equal(1, result.RequestedOrders);
        Assert.Equal(1, result.ExpandedOrders);                 // SP 调用成功（未抛异常）
        Assert.True(result.ExpandedRowCount > 0,                // 三重校验①：结果行数 > 0
            "实时展开结果为空（可能该订单无可用 BOM / 或 sp 未展开）");
    }
}
