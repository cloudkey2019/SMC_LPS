using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Extensions;
using LPS.APS.Engine.Services.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// 白天订单池同步 集成测试（2号位，2026-09-28）。
///
/// 【被测】IOrderLoadingService.LoadOrdersToActivePlanVersionsAsync（新增代码）：
///   查 Status='ACTIVE' 的 PlanVersion → 逐个调 sp_SyncOrdersToPartitionTable 装订单。
///   场景：白天 ERP 每小时同步后，把新订单压进 ACTIVE PV 的 [Order]，供用户选单做插单试排。
///
/// 【本测试自建自清】当前库无 ACTIVE PlanVersion（全 TEST/Computed），故测试临时建一个
///   FAMILY_X 域（有效 DomainDefinition）的 PlanVersion 并置 ACTIVE，验证后清理。
///
/// 【清理范围】测试 PV 的 [Order] 行 + PlanVersion 行（按测试 VersionCode 前缀精准删，不碰真实数据）。
/// </summary>
public class OrderLoadingIntegrationTest
{
    private const string TestVersionCodePrefix = "TEST-P0-ORDERPOOL-";
    private const string TestDomainKey = "FAMILY_X"; // DomainDefinition 有效域（FAMILY / ProductFamilyId=1）

    private readonly IOrderLoadingService _orderLoadingService;
    private readonly DatabaseConnectionManager _connectionManager;

    public OrderLoadingIntegrationTest()
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
        _orderLoadingService = serviceProvider.GetRequiredService<IOrderLoadingService>();
        _connectionManager = serviceProvider.GetRequiredService<DatabaseConnectionManager>();
    }

    [Fact(DisplayName = "白天订单池：LoadOrdersToActivePlanVersionsAsync 对 ACTIVE PV 装订单")]
    public async Task LoadOrdersToActivePlanVersionsAsync_LoadsOrdersIntoActivePv()
    {
        await CleanupAsync();

        // ── 1. 建测试 PV 并置 ACTIVE（FAMILY_X 有效域）──
        var versionCode = TestVersionCodePrefix + DateTime.Now.ToString("yyyyMMddHHmmss");
        var pvId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO PlanVersion
                (VersionCode, VersionCategory, PlanHorizonStart, PlanHorizonEnd, ComputeMode,
                 Status, BatchNo, DomainKey, CreatedBy, CreatedAt)
              OUTPUT INSERTED.Id
              VALUES (@VersionCode, 'TEST', GETDATE(), DATEADD(DAY, 90, GETDATE()), 'FULL',
                      'ACTIVE', 'P0TEST', @DomainKey, 'P0TEST', GETDATE())",
            new { VersionCode = versionCode, DomainKey = TestDomainKey },
            db: DatabaseId.APS);

        Assert.True(pvId > 0, "测试 PlanVersion 创建失败");

        try
        {
            // ── 2. 调被测方法 ──
            var inserted = await _orderLoadingService.LoadOrdersToActivePlanVersionsAsync();

            var loadedInPv = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @PvId",
                new { PvId = pvId }, db: DatabaseId.APS);

            Console.WriteLine("=== 白天订单池同步结果 ===");
            Console.WriteLine($"测试 PlanVersionId = {pvId}（DomainKey={TestDomainKey}）");
            Console.WriteLine($"插入合计（本方法返回）= {inserted}");
            Console.WriteLine($"该 PV 实际订单行数        = {loadedInPv}");

            Assert.True(loadedInPv > 0, $"ACTIVE PV 未装入订单（DomainKey={TestDomainKey}）");

            // ── 3. 幂等：再调一次，应为 0 新增 ──
            var secondRun = await _orderLoadingService.LoadOrdersToActivePlanVersionsAsync();
            var loadedAfterSecond = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @PvId",
                new { PvId = pvId }, db: DatabaseId.APS);

            Console.WriteLine($"二次调用返回 = {secondRun}，PV 订单数（应不变）= {loadedAfterSecond}");

            Assert.Equal(0, secondRun);                             // NOT EXISTS 保护：无新增
            Assert.Equal(loadedInPv, loadedAfterSecond);            // 行数不变
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <summary>按测试 VersionCode 前缀精准清理（先删订单、再删 PV），不碰真实数据。</summary>
    private async Task CleanupAsync()
    {
        await _connectionManager.ExecuteAsync(
            @"DELETE FROM [Order]
              WHERE PlanVersionId IN (
                  SELECT Id FROM PlanVersion WHERE VersionCode LIKE @Prefix + '%')",
            new { Prefix = TestVersionCodePrefix }, db: DatabaseId.APS);

        await _connectionManager.ExecuteAsync(
            "DELETE FROM PlanVersion WHERE VersionCode LIKE @Prefix + '%'",
            new { Prefix = TestVersionCodePrefix }, db: DatabaseId.APS);
    }
}
