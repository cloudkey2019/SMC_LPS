using FluentAssertions;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Rules;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// D-8 ETA Invariant 固化测试（阶段 D 门 R10）
/// 契约：ETA 优先级链 <c>Manual ETA &gt; ERP ETA &gt; DefaultLT</c> 冻结为业务规则，
/// 不做成可任意重排的 Rule Chain（不可配置化）——《3号位实施包》§10.2 /《Pegging业务说明》§10.1。
/// 语义：
/// 1. 人工ETA取消后回落ERP ETA；ERP ETA为空再回落DefaultLT；
/// 2. DefaultLT 推算的日期保留"估算"属性，不能伪装成真实供应商承诺（§10.3）；
/// 3. 纯函数每次按当前输入解析，不缓存旧值（"人工取消后回落"即多次调用的确定性结果）。
/// 开发者：3号位
/// </summary>
public class EtaInvariantTests
{
    // ============ R10：参数链可表达（优先级固定，不可重排） ============

    [Fact]
    public void Resolve_Manual存在_无论ERP与DefaultLT如何_一律以Manual为准()
    {
        // Arrange
        var manualEta = new DateTime(2026, 9, 10);   // 人工 ETA 即使晚于 ERP/DefaultLT
        var erpEta = new DateTime(2026, 9, 1);        // ERP 更早也不参与
        var defaultLtEta = new DateTime(2026, 9, 5);

        // Act
        var resolution = EtaInvariant.Resolve(manualEta, erpEta, defaultLtEta);

        // Assert
        resolution.EffectiveEta.Should().Be(manualEta);
        resolution.Source.Should().Be(EtaSource.Manual);
        resolution.IsEstimated.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Manual为空_回落ERP()
    {
        // Arrange
        DateTime? manualEta = null;
        var erpEta = new DateTime(2026, 9, 5);
        var defaultLtEta = new DateTime(2026, 9, 10);

        // Act
        var resolution = EtaInvariant.Resolve(manualEta, erpEta, defaultLtEta);

        // Assert
        resolution.EffectiveEta.Should().Be(erpEta);
        resolution.Source.Should().Be(EtaSource.Erp);
        resolution.IsEstimated.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Manual与ERP均为空_回落DefaultLT()
    {
        // Arrange
        DateTime? manualEta = null;
        DateTime? erpEta = null;
        var defaultLtEta = new DateTime(2026, 9, 10);

        // Act
        var resolution = EtaInvariant.Resolve(manualEta, erpEta, defaultLtEta);

        // Assert
        resolution.EffectiveEta.Should().Be(defaultLtEta);
        resolution.Source.Should().Be(EtaSource.DefaultLt);
        // §10.3：DefaultLT 推算必须保留"估算"属性，不得伪装成真实供应商承诺
        resolution.IsEstimated.Should().BeTrue();
    }

    [Fact]
    public void Resolve_全部为空_返回None_无有效ETA()
    {
        // Arrange
        DateTime? manualEta = null;
        DateTime? erpEta = null;
        DateTime? defaultLtEta = null;

        // Act
        var resolution = EtaInvariant.Resolve(manualEta, erpEta, defaultLtEta);

        // Assert
        resolution.EffectiveEta.Should().BeNull();
        resolution.Source.Should().Be(EtaSource.None);
        resolution.IsEstimated.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Manual取消后回落ERP_纯函数不缓存旧值()
    {
        // 契约语义（§10.1）：人工ETA取消后回落ERP ETA——每次调用按当前输入确定性解析，不缓存历史
        // Arrange
        var manualEta = new DateTime(2026, 9, 1);
        var erpEta = new DateTime(2026, 9, 5);

        // Act
        var withManual = EtaInvariant.Resolve(manualEta, erpEta, null);
        var afterCancelled = EtaInvariant.Resolve(null, erpEta, null);

        // Assert
        withManual.Source.Should().Be(EtaSource.Manual);
        afterCancelled.Source.Should().Be(EtaSource.Erp);
        afterCancelled.EffectiveEta.Should().Be(erpEta);
    }

    [Fact]
    public void Resolve_Manual晚于ERP_仍以Manual为准_优先级与日期先后无关()
    {
        // Arrange
        var manualEta = new DateTime(2026, 9, 10);
        var erpEta = new DateTime(2026, 9, 1);

        // Act
        var resolution = EtaInvariant.Resolve(manualEta, erpEta, null);

        // Assert：来源优先级是唯一决定因素，不按"取最早日期"折算
        resolution.Source.Should().Be(EtaSource.Manual);
        resolution.EffectiveEta.Should().Be(manualEta);
    }

    // ============ R10 负向验收：无 ETA 排序配置入口（不可配置化） ============

    [Fact]
    public void 六块配置DTO及嵌套参数类型_不暴露ETA优先级重排配置入口()
    {
        // 验收（任务清单 D-8）：无 ETA 排序配置入口——冻结链不可通过任何配置重排。
        // 若未来某块（顶层或嵌套参数类型）新增这些属性，说明引入了"可任意重排的 Rule Chain"，违反裁决，测试立即失败。
        // 注意：这是启发式冒烟检查——仅能发现其枚举到的类型；完备性由设计审查保证，本测试不声称穷尽证明。
        // Arrange
        var prohibitedFields = new[]
        {
            "EtaPriority", "EtaOrder", "EtaChain", "EtaSort", "EtaPrecedence",
            "EtaPriorityMode", "ManualEtaPriority", "ErpEtaPriority", "DefaultLtPriority",
            "EtaFallbackOrder", "EtaResolutionMode"
        };

        // 全部六块 + 快照容器（含 DemandPriorityBlock——ETA 排序最可能藏匿处）
        var blockTypes = new[]
        {
            typeof(DemandPriorityBlock), typeof(LockBlock), typeof(SupplyBlock),
            typeof(ProcurementBlock), typeof(SolverStrategyBlock), typeof(CandidateGuardrailBlock),
            typeof(FrozenStrategySnapshot)
        };

        // Act：顶层属性 + 一层嵌套参数类型属性（防 ETA 重排字段藏进 PiSortParams 等嵌套类型逃逸检测）
        var propertyNames = blockTypes
            .SelectMany(b => b.GetProperties().Select(p => p.Name))
            .Concat(blockTypes
                .SelectMany(b => b.GetProperties().Select(p => p.PropertyType))
                .Where(t => t.IsClass && t.Namespace == typeof(DemandPriorityBlock).Namespace)
                .SelectMany(t => t.GetProperties().Select(p => p.Name)))
            .Distinct()
            .ToArray();

        // Assert
        propertyNames.Should().NotContain(prohibitedFields);
    }
}
