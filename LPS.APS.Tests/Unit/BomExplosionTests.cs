using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LPS.APS.Application.Models;
using LPS.APS.Application.Services;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// BOM 结构展开（Explosion）纯函数测试 —— 阶段1 验收（PM 0918-2 §一）：
/// 数量一致 + 血缘一致 + 路径语义一致。用共享子件菱形钉住 PM 点名的反例
/// （数量 100=100 但血缘错）；逐路径各产一行、数量不丢、GrossQty = 根需求 × 逐层单位配比。
/// </summary>
public class BomExplosionTests
{
    private static readonly IBomExplosionService Service = new BomExplosionService();

    private static BomStructure Structure(
        IReadOnlyDictionary<string, bool>? purchased,
        params (string Parent, string Child, int ChildId, decimal Qty)[] edges)
    {
        var byParent = edges
            .GroupBy(e => e.Parent, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<BomComponent>)g
                    .Select(e => new BomComponent(e.Child, e.ChildId, e.Qty))
                    .ToList(),
                StringComparer.Ordinal);

        return new BomStructure(byParent, purchased ?? new Dictionary<string, bool>(StringComparer.Ordinal));
    }

    private static BomOrderDemand Demand(string rootCode, decimal qty) =>
        new(RootOrderId: 101, RootMaterialId: 1, RootMaterialCode: rootCode, FactoryId: 7, RootQty: qty);

    [Fact]
    public void 单层BOM_根需求按配比展开_血缘随路径()
    {
        var s = Structure(
            null,
            ("A", "B", 2, 2m),
            ("A", "C", 3, 3m));

        var lines = Service.ExplodeOrder(s, Demand("A", 10m));

        lines.Should().HaveCount(3);
        lines.Should().OnlyContain(l => l.RootOrderId == 101 && l.FactoryId == 7);

        lines.Single(l => l.MaterialCode == "A").GrossQty.Should().Be(10m);
        lines.Single(l => l.MaterialCode == "A").Level.Should().Be(0);

        var b = lines.Single(l => l.MaterialCode == "B");
        b.GrossQty.Should().Be(20m);
        b.Path.Should().Be("A/B");

        var c = lines.Single(l => l.MaterialCode == "C");
        c.GrossQty.Should().Be(30m);
        c.Path.Should().Be("A/C");
    }

    [Fact]
    public void 多层级联_配比逐层相乘()
    {
        var s = Structure(
            null,
            ("A", "B", 2, 2m),
            ("B", "C", 3, 3m));

        var c = Service.ExplodeOrder(s, Demand("A", 10m)).Single(l => l.MaterialCode == "C");

        c.GrossQty.Should().Be(60m);       // 10 × 2 × 3
        c.Level.Should().Be(2);
        c.Path.Should().Be("A/B/C");
    }

    [Fact]
    public void 共享子件菱形_逐路径各产一行_情况B血缘不丢()
    {
        // A→B→D(×2)、A→C→D(×3)：D 经两条来源路径，数量不合并、血缘保留。
        var s = Structure(
            null,
            ("A", "B", 2, 1m),
            ("A", "C", 3, 1m),
            ("B", "D", 4, 2m),
            ("C", "D", 4, 3m));

        var dLines = Service.ExplodeOrder(s, Demand("A", 10m))
            .Where(l => l.MaterialCode == "D")
            .ToList();

        dLines.Should().HaveCount(2);                                        // 情况B：两路径 → 两行（阶段1 不合并）
        dLines.Should().ContainSingle(l => l.Path == "A/B/D" && l.GrossQty == 20m); // 10×1×2
        dLines.Should().ContainSingle(l => l.Path == "A/C/D" && l.GrossQty == 30m); // 10×1×3
        dLines.Sum(l => l.GrossQty).Should().Be(50m);                        // D 总量 50，分两行承载、数量不丢
    }

    [Fact]
    public void 真实环_路径级visited截断_不死循环()
    {
        // A→B→A：真实环，visited 截断，与旧 TraverseBomNode 同语义。
        var s = Structure(
            null,
            ("A", "B", 2, 1m),
            ("B", "A", 1, 1m));

        var lines = Service.ExplodeOrder(s, Demand("A", 10m));

        lines.Should().HaveCount(2);        // A(根) + B；B→A 被 visited 截断
        lines.Select(l => l.MaterialCode).Should().Contain("A").And.Contain("B");
    }

    [Fact]
    public void IsPurchased与IsLeaf_按结构透传()
    {
        var s = Structure(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["C"] = true },
            ("A", "B", 2, 1m),
            ("B", "C", 3, 1m));

        var lines = Service.ExplodeOrder(s, Demand("A", 10m));

        lines.Single(l => l.MaterialCode == "A").IsPurchased.Should().BeFalse();
        lines.Single(l => l.MaterialCode == "B").IsLeaf.Should().BeFalse();
        lines.Single(l => l.MaterialCode == "C").IsPurchased.Should().BeTrue();
        lines.Single(l => l.MaterialCode == "C").IsLeaf.Should().BeTrue();
    }
}