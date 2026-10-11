using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 续排起点「名→码 反查」测试（2号位 装载层；口径由 5号位 2026-10-09 三轮回执 §三/§六.3 钉死）。
///
/// 背景：5号位 出口 <c>StartOperationCode</c> 在 DAG 前沿/中间态分支装的是 MES 侧 <c>OperationName</c>，
/// 而 1号位 入口按 APS <c>RoutingOperation.OperationCode</c> 匹配 ⇒ 原值不改则「非空也必然 S26」。
///
/// 契约：
///   · 出口值已是本 PI 的合法码 ⇒ 原样放行（未来 MACH 侧清洗后直接给码，不需要反查）；
///   · 出口值为名、在该 PI 路由集内唯一对应一个码 ⇒ 命中；
///   · 同名多码（同文本多 PathId 的真歧义）⇒ 用**工单真实 RouteCode 子串包含**消解；
///   · 消解后仍不唯一 / 无候选 / 无路由事实 ⇒ 返回 <c>null</c>（**不猜码、不伪造、不回填 DEFAULT/1**，5号位 §六.3）。
/// </summary>
public class PiOperationCodeResolverTests
{
    private static RoutingOperationFact Op(string code, string name, string route, int pathId = 1, string stage = "CN_ASSY")
        => new()
        {
            OperationCode = code,
            OperationName = name,
            StageCode     = stage,
            RouteCode     = route,
            PathId        = pathId
        };

    private static PeggingOrchestrator.PiOperationCodeResolver Resolver(
        string pi, params RoutingOperationFact[] ops)
        => new(new Dictionary<string, IReadOnlyList<RoutingOperationFact>>(StringComparer.Ordinal)
        {
            [pi] = ops
        });

    [Fact]
    public void 唯一名_反查命中对应码()
    {
        var r = Resolver("PI1", Op("M103", "端盖ASSY", "配品,营业线ASSY,端盖ASSY,组装,水检,洁净捆包"));
        r.Resolve("PI1", "端盖ASSY", null).Should().Be("M103");
    }

    [Fact]
    public void 出口值本身已是合法码_原样放行()
    {
        var r = Resolver("PI1", Op("M103", "端盖ASSY", "配品,营业线ASSY,端盖ASSY"));
        // 出口值 = 码（非名）⇒ 不做名匹配，直接放行
        r.Resolve("PI1", "M103", null).Should().Be("M103");
        r.IsKnownCode("PI1", "M103").Should().BeTrue();
        r.IsKnownCode("PI1", "端盖ASSY").Should().BeFalse();
    }

    [Fact]
    public void 无候选名_返回null_不猜码()
    {
        // 加工侧形态：MES 工序名是纯数字私有编码，与 APS 字母表结构性不相交
        var r = Resolver("PI1", Op("J109", "CPK车削", "NC,清洗,外观检查"));
        r.Resolve("PI1", "16", null).Should().BeNull();
    }

    [Fact]
    public void 无该PI路由事实_返回null()
    {
        var r = Resolver("PI1", Op("M103", "端盖ASSY", "配品,营业线ASSY,端盖ASSY"));
        r.Resolve("OTHER", "端盖ASSY", null).Should().BeNull();
        r.Resolve("", "端盖ASSY", null).Should().BeNull();
        r.Resolve("PI1", "", null).Should().BeNull();
        r.Resolve("PI1", null, null).Should().BeNull();
    }

    [Fact]
    public void 同名多码_工单RouteCode子串包含消解()
    {
        // 同文本多 PathId 的真歧义（5号位 §三 口径：短码 ∈ 长串；双向包含以兼容 ASSY 侧两侧都是长名字串）
        var r = Resolver("PI1",
            Op("M103", "端盖ASSY", "配品,营业线ASSY,端盖ASSY,组装,水检,洁净捆包", pathId: 1),
            Op("M401", "端盖ASSY", "配品,(垫片ASSY,磁石ASSY),配特殊部品,端盖ASSY,组装1,活塞挤压,捆包,水检,洁净捆包", pathId: 2));

        r.Resolve("PI1", "端盖ASSY", "配品,营业线ASSY,端盖ASSY,组装,水检,洁净捆包").Should().Be("M103");
        r.Resolve("PI1", "端盖ASSY", "配品,(垫片ASSY,磁石ASSY),配特殊部品,端盖ASSY,组装1,活塞挤压,捆包,水检,洁净捆包").Should().Be("M401");
    }

    [Fact]
    public void 同名多码_无RouteCode或不匹配_返回null()
    {
        var r = Resolver("PI1",
            Op("M103", "端盖ASSY", "配品,营业线ASSY,端盖ASSY"),
            Op("M401", "端盖ASSY", "配品,配特殊部品,端盖ASSY,组装1"));

        r.Resolve("PI1", "端盖ASSY", null).Should().BeNull();                 // PI 级上下文无工单路由 ⇒ 不猜
        r.Resolve("PI1", "端盖ASSY", "完全无关的路线串").Should().BeNull();      // 消解不中 ⇒ 不猜
        r.Resolve("PI1", "端盖ASSY", "").Should().BeNull();
    }

    [Fact]
    public void 同名多码但同一个码_视为唯一_命中()
    {
        // 同文本多 PathId 但同码 = 假歧义（既有口径），应命中而非分歧
        var r = Resolver("PI1",
            Op("M103", "端盖ASSY", "路线A", pathId: 1),
            Op("M103", "端盖ASSY", "路线B", pathId: 2));
        r.Resolve("PI1", "端盖ASSY", null).Should().Be("M103");
    }
}
