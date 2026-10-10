using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 厂间在途 PI 精确归属（AttributeInterplantTransitToPi 纯函数）测试。
/// 契约（PM 0910 §十三）：Transit（DocumentType=PRODUCTION_INSTRUCTION）按 SourceDocumentNo=PI号 归属唯一 PI，
/// Material/目标厂仅做一致性校验；失败（PI号缺失/找不到PI/Material或Factory冲突/数量超界）记 Issue、不入已定位 Position。
/// </summary>
public class TransitPiAttributionTests
{
    private static InterplantTransitFact Transit(string piNo, string material = "A", string targetFactory = "F1", decimal qty = 120m)
        => new()
        {
            TransitDocumentNo = piNo,
            MaterialCode = material,
            SourceFactoryCode = "F0",
            TargetFactoryCode = targetFactory,
            Quantity = qty
        };

    private static Dictionary<string, (string MaterialCode, string FactoryCode)> Pis(params (string no, string material, string factory)[] items)
        => items.ToDictionary(i => i.no, i => (i.material, i.factory), StringComparer.Ordinal);

    // ── 正常归属 ──

    [Fact]
    public void 单PI_按PI号归属成功()
    {
        var transits = new[] { Transit("PI-C01") };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        issues.Should().BeEmpty();
        byPi.Should().ContainKey("PI-C01");
        byPi["PI-C01"].Should().HaveCount(1);
    }

    [Fact]
    public void 多PI同物料_只归属到PI号匹配的那张PI()
    {
        // PI-C01/PI-C02 同物料 A 同工厂 F1，在途 120 只属 PI-C01
        var transits = new[] { Transit("PI-C01") };
        var pis = Pis(("PI-C01", "A", "F1"), ("PI-C02", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        issues.Should().BeEmpty();
        byPi.Keys.Should().BeEquivalentTo(new[] { "PI-C01" });
        byPi["PI-C01"].Single().Quantity.Should().Be(120m);
    }

    [Fact]
    public void 同一PI多行Transit_聚合()
    {
        var transits = new[] { Transit("PI-C01", qty: 50m), Transit("PI-C01", qty: 70m) };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        issues.Should().BeEmpty();
        byPi["PI-C01"].Sum(t => t.Quantity).Should().Be(120m);
    }

    // ── 失败校验 ──

    [Fact]
    public void PI号缺失_打TRANSIT_PI_NO_MISSING()
    {
        var transits = new[] { Transit("") };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        byPi.Should().BeEmpty();
        issues.Should().ContainSingle(i => i.StartsWith("TRANSIT_PI_NO_MISSING"));
    }

    [Fact]
    public void PI号找不到对应PI_打TRANSIT_PI_NOT_FOUND()
    {
        var transits = new[] { Transit("PI-UNKNOWN") };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        byPi.Should().BeEmpty();
        issues.Should().ContainSingle(i => i.StartsWith("TRANSIT_PI_NOT_FOUND") && i.Contains("PI-UNKNOWN"));
    }

    [Fact]
    public void 物料不一致_打TRANSIT_PI_MATERIAL_MISMATCH()
    {
        var transits = new[] { Transit("PI-C01", material: "B") };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        byPi.Should().BeEmpty();
        issues.Should().ContainSingle(i => i.StartsWith("TRANSIT_PI_MATERIAL_MISMATCH"));
    }

    [Fact]
    public void 目标厂不一致_打TRANSIT_PI_FACTORY_MISMATCH()
    {
        var transits = new[] { Transit("PI-C01", targetFactory: "F2") };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        byPi.Should().BeEmpty();
        issues.Should().ContainSingle(i => i.StartsWith("TRANSIT_PI_FACTORY_MISMATCH"));
    }

    [Fact]
    public void 数量超界_打TRANSIT_PI_INVALID_QUANTITY()
    {
        var transits = new[] { Transit("PI-C01", qty: 0m) };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        byPi.Should().BeEmpty();
        issues.Should().ContainSingle(i => i.StartsWith("TRANSIT_PI_INVALID_QUANTITY"));
    }

    // ── 混合：失败的不污染已定位 PI、已定位的仍归入 ──

    [Fact]
    public void 部分失败_已定位的仍归入_失败的记Issue()
    {
        var transits = new[]
        {
            Transit("PI-C01", qty: 50m),       // 合法
            Transit("PI-UNKNOWN", qty: 30m),   // NOT_FOUND
            Transit("PI-C01", material: "X")   // 物料冲突
        };
        var pis = Pis(("PI-C01", "A", "F1"));

        var (byPi, issues) = PeggingOrchestrator.AttributeInterplantTransitToPi(transits, pis);

        byPi["PI-C01"].Should().HaveCount(1);
        byPi["PI-C01"].Single().Quantity.Should().Be(50m);
        issues.Should().HaveCount(2);
    }
}