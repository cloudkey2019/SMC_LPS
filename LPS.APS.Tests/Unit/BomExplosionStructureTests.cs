using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LPS.APS.Application.Models;
using LPS.APS.Application.Services;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// BOM 去重逐层结构（ExplodeOrderStructure）纯函数测试 —— 阶段2 S2.1 验收。
/// 共享子件合并为一行带父边列表、每物料取最深出现层（LLC 语义）、采购件即叶、真实环收敛。
/// 这是「Allocation 读取 Explosion 结果」的骨架，须在接入净额前独立钉死。
/// </summary>
public class BomExplosionStructureTests
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

    private static BomOrderDemand Demand(string rootCode) =>
        new(RootOrderId: 101, RootMaterialId: 1, RootMaterialCode: rootCode, FactoryId: 7, RootQty: 10m);

    private BomLevelNode NodeOf(string code, IReadOnlyList<BomLevelNode> nodes) => nodes.Single(n => n.MaterialCode == code);

    [Fact]
    public void 共享子件菱形_去重为一行_父边保全集_最深层正确()
    {
        // A→B→D(×2)、A→C→D(×3)：D 两路径去重为一行，ParentEdges=[B×2, C×3]，Level=2。
        var s = Structure(
            null,
            ("A", "B", 2, 1m),
            ("A", "C", 3, 1m),
            ("B", "D", 4, 2m),
            ("C", "D", 4, 3m));

        var nodes = Service.ExplodeOrderStructure(s, Demand("A"));

        nodes.Should().OnlyContain(n => n.FactoryId == 7);
        NodeOf("D", nodes).Should().BeEquivalentTo(new
        {
            Level = 2,
            IsPurchased = false
        });
        NodeOf("D", nodes).ParentEdges.Should().BeEquivalentTo(new[]
        {
            new BomParentEdge(2, "B", 2m),
            new BomParentEdge(3, "C", 3m)
        });
        nodes.Count(n => n.MaterialCode == "D").Should().Be(1); // 去重：D 只有一行
    }

    [Fact]
    public void 多层级联_层级与父边逐层正确()
    {
        var s = Structure(
            null,
            ("A", "B", 2, 2m),
            ("B", "C", 3, 3m));

        var nodes = Service.ExplodeOrderStructure(s, Demand("A"));

        NodeOf("A", nodes).Level.Should().Be(0);
        NodeOf("A", nodes).ParentEdges.Should().BeEmpty(); // root 无父边
        NodeOf("B", nodes).Level.Should().Be(1);
        NodeOf("B", nodes).ParentEdges.Should().ContainSingle(e => e.ParentMaterialCode == "A" && e.QtyPerUnit == 2m);
        NodeOf("C", nodes).Level.Should().Be(2);
        NodeOf("C", nodes).ParentEdges.Should().ContainSingle(e => e.ParentMaterialCode == "B" && e.QtyPerUnit == 3m);
    }

    [Fact]
    public void 同一物料多深出现_取最深出现层_LLC语义()
    {
        // D 经 A→D（level1）与 A→B→C→D（level3）两路：最深 = 3，父边 = [A×1, C×1]。
        var s = Structure(
            null,
            ("A", "D", 4, 1m),
            ("A", "B", 2, 1m),
            ("B", "C", 3, 1m),
            ("C", "D", 4, 1m));

        var nodes = Service.ExplodeOrderStructure(s, Demand("A"));

        NodeOf("D", nodes).Level.Should().Be(3); // 取最深（3 而非 1）
        NodeOf("D", nodes).ParentEdges.Should().BeEquivalentTo(new[]
        {
            new BomParentEdge(1, "A", 1m),
            new BomParentEdge(3, "C", 1m)
        });
    }

    [Fact]
    public void 采购件即叶_不下钻()
    {
        // C 采购件但结构里给 C 配了子件 E：去重结构须把 C 当叶、不展开 E。
        var s = Structure(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["C"] = true },
            ("A", "B", 2, 1m),
            ("B", "C", 3, 1m),
            ("C", "E", 5, 1m));

        var nodes = Service.ExplodeOrderStructure(s, Demand("A"));

        NodeOf("C", nodes).IsPurchased.Should().BeTrue();
        nodes.Should().NotContain(n => n.MaterialCode == "E"); // 采购件叶，E 不出现
    }

    [Fact]
    public void 共享子件更深路径_最深层下推到其子件()
    {
        // 记忆化重写的关键回归：D 先经 A→D（level1）被枚举（含其子件 E），后又经
        // A→B→C→D（level3）重回。最深层（LLC）须把 D 抬到 3，并把 E 一并下推到 4——
        // 不能因「D 已枚举」而把 E 冻结在 level2。
        var s = Structure(
            null,
            ("A", "D", 4, 1m),
            ("D", "E", 5, 1m),
            ("A", "B", 2, 1m),
            ("B", "C", 3, 1m),
            ("C", "D", 4, 1m));

        var nodes = Service.ExplodeOrderStructure(s, Demand("A"));

        NodeOf("D", nodes).Level.Should().Be(3);
        NodeOf("E", nodes).Level.Should().Be(4); // 经最深 D 下推，而非浅层 2
    }

    [Fact]
    public void 真实环_收敛_不记环边不抬层()
    {
        // A→B→A：round 回到 root 被截断，A 仍是 level 0 无父边、B level 1 父边=[A]。
        var s = Structure(
            null,
            ("A", "B", 2, 1m),
            ("B", "A", 1, 1m));

        var nodes = Service.ExplodeOrderStructure(s, Demand("A"));

        nodes.Should().HaveCount(2);
        NodeOf("A", nodes).Level.Should().Be(0);
        NodeOf("A", nodes).ParentEdges.Should().BeEmpty();
        NodeOf("B", nodes).Level.Should().Be(1);
        NodeOf("B", nodes).ParentEdges.Should().ContainSingle(e => e.ParentMaterialCode == "A");
    }

    [Fact]
    public void 输出按层升序_同级按物料码稳定()
    {
        var s = Structure(
            null,
            ("A", "B", 2, 1m),
            ("A", "C", 3, 1m),
            ("B", "D", 4, 1m),
            ("C", "D", 4, 1m));

        var nodes = Service.ExplodeOrderStructure(s, Demand("A"));

        nodes.Select(n => (n.Level, n.MaterialCode)).Should().BeEquivalentTo(
            new[] { (0, "A"), (1, "B"), (1, "C"), (2, "D") },
            o => o.WithStrictOrdering());
    }
}