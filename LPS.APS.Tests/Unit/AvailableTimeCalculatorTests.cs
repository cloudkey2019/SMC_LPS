using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// AvailableTime 纯函数计算测试（2号位阶段 3）
/// 契约：AvailableTime = EtaInvariant.Resolve(Manual, ERP, ReleaseDate+DefaultLT) + ArrivalToUsableOffset。
/// 本测试用「模拟数据」验证三级优先级链与 Offset 修正，不依赖 5号位真实数据源
/// （当前 ODS 未透出 ReleaseDate、Pipeline 空转，故以构造的假事实驱动逻辑验证）。
/// </summary>
public class AvailableTimeCalculatorTests
{
    private static FrozenStrategySnapshot Snapshot(
        PurchaseLtRule[]? ltRules = null,
        WarehouseOffsetRule[]? offsets = null)
        => new()
        {
            Procurement = new ProcurementBlock
            {
                DefaultPurchaseLt = ltRules ?? [],
                ArrivalToUsableOffsets = offsets ?? []
            }
        };

    private static RawProcurementFact Fact(
        string lineNo = "10",
        string storageCode = "WH-A",
        DateTime? eta = null,
        DateTime? releaseDate = null,
        int materialId = 100)
        => new()
        {
            MaterialId = materialId,
            MaterialCode = "MAT-100",
            FactoryId = 1,
            FactoryCode = "F1",
            StorageCode = storageCode,
            RemainingQty = 50m,
            Eta = eta,
            ReleaseDate = releaseDate,
            SourceDocumentNo = "PO-001",
            SourceDocumentLineNo = lineNo
        };

    [Fact]
    public void Compute_ManualEta优先_并加Offset()
    {
        var snapshot = Snapshot(
            ltRules: new[] { new PurchaseLtRule { WarehouseCode = "WH-A", DefaultLtDays = 7 } },
            offsets: new[] { new WarehouseOffsetRule { WarehouseCode = "WH-A", OffsetHours = 2 } });
        var map = new Dictionary<(string, int, int, string), DateTime>
        {
            [("PO-001", 10, 100, "WH-A")] = new DateTime(2026, 9, 10, 8, 0, 0)
        };
        var fact = Fact(eta: new DateTime(2026, 9, 5), releaseDate: new DateTime(2026, 9, 1));

        var result = AvailableTimeCalculator.Compute(fact, map, snapshot);

        // Manual(09-10 08:00) 覆盖 ERP(09-05)，再加 2h Offset
        result.Should().Be(new DateTime(2026, 9, 10, 10, 0, 0));
    }

    [Fact]
    public void Compute_Manual为空_回落ERP_并加Offset()
    {
        var snapshot = Snapshot(offsets: new[] { new WarehouseOffsetRule { WarehouseCode = "WH-A", OffsetHours = 3 } });
        var map = new Dictionary<(string, int, int, string), DateTime>();
        var fact = Fact(eta: new DateTime(2026, 9, 5, 0, 0, 0), releaseDate: new DateTime(2026, 9, 1));

        var result = AvailableTimeCalculator.Compute(fact, map, snapshot);

        result.Should().Be(new DateTime(2026, 9, 5, 3, 0, 0));
    }

    [Fact]
    public void Compute_Manual与ERP为空_回落ReleaseDate加DefaultLT()
    {
        var snapshot = Snapshot(
            ltRules: new[] { new PurchaseLtRule { WarehouseCode = "WH-A", DefaultLtDays = 7 } });
        var map = new Dictionary<(string, int, int, string), DateTime>();
        var fact = Fact(eta: null, releaseDate: new DateTime(2026, 9, 1));

        var result = AvailableTimeCalculator.Compute(fact, map, snapshot);

        result.Should().Be(new DateTime(2026, 9, 8));
    }

    [Fact]
    public void Compute_全部为空_返回null()
    {
        var snapshot = Snapshot();
        var map = new Dictionary<(string, int, int, string), DateTime>();
        var fact = Fact(eta: null, releaseDate: null);

        var result = AvailableTimeCalculator.Compute(fact, map, snapshot);

        result.Should().BeNull();
    }

    [Fact]
    public void Compute_DefaultLT_优先Material级规则_再回落Warehouse级默认()
    {
        // Material 级 LT(14 天) 应优先于 Warehouse 级默认(7 天)
        var snapshot = Snapshot(ltRules: new[]
        {
            new PurchaseLtRule { WarehouseCode = "WH-A", MaterialId = null, DefaultLtDays = 7 },
            new PurchaseLtRule { WarehouseCode = "WH-A", MaterialId = "100", DefaultLtDays = 14 }
        });
        var map = new Dictionary<(string, int, int, string), DateTime>();
        var fact = Fact(eta: null, releaseDate: new DateTime(2026, 9, 1), materialId: 100);

        var result = AvailableTimeCalculator.Compute(fact, map, snapshot);

        result.Should().Be(new DateTime(2026, 9, 15));
    }

    [Fact]
    public void Compute_LineNo无法解析_Manual不命中_回落ERP()
    {
        var snapshot = Snapshot();
        var map = new Dictionary<(string, int, int, string), DateTime>
        {
            [("PO-001", 10, 100, "WH-A")] = new DateTime(2026, 9, 10)
        };
        // SourceDocumentLineNo 非数字 → int.TryParse 失败 → Manual 不命中
        var fact = Fact(lineNo: "N/A", eta: new DateTime(2026, 9, 5), releaseDate: null);

        var result = AvailableTimeCalculator.Compute(fact, map, snapshot);

        result.Should().Be(new DateTime(2026, 9, 5));
    }

    [Fact]
    public void BuildManualEtaMap_过滤未激活与默认值()
    {
        var overrides = new List<ProcurementManualEtaOverride>
        {
            new() { PONo = "PO-001", LineNo = 10, MaterialId = 100, ReceivingWarehouse = "WH-A",
                    ManualEta = new DateTime(2026, 9, 10), IsActive = true },
            new() { PONo = "PO-002", LineNo = 11, MaterialId = 100, ReceivingWarehouse = "WH-A",
                    ManualEta = new DateTime(2026, 9, 11), IsActive = false },   // 已取消，应被过滤
            new() { PONo = "PO-003", LineNo = 12, MaterialId = 100, ReceivingWarehouse = "WH-A",
                    ManualEta = default, IsActive = true }                       // default(DateTime) 视为无值，应被过滤
        };

        var map = AvailableTimeCalculator.BuildManualEtaMap(overrides);

        map.Should().HaveCount(1);
        map.Should().ContainKey(("PO-001", 10, 100, "WH-A"));
        map.Should().NotContainKey(("PO-002", 11, 100, "WH-A"));
        map.Should().NotContainKey(("PO-003", 12, 100, "WH-A"));
    }
}
