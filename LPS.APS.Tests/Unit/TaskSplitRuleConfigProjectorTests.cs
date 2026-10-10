using FluentAssertions;
using LPS.APS.Application.Models;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// TaskSplitRuleConfigProjector 单元测试（Batch Policy 投影/编解码，0号位 2026-10-07 裁决本轮落码）。
/// 无状态纯函数，直接钉死：IsActive/生效区间筛除、去审计字段投影、ContentSnapshotJson 子块编解码 + fail-open。
/// </summary>
public class TaskSplitRuleConfigProjectorTests
{
    private static TaskSplitRuleConfig Rule(
        int id = 1, int materialId = 100, int? deptId = null, bool isActive = true,
        DateTime? effectiveFrom = null, DateTime? effectiveTo = null,
        decimal? min = null, decimal? max = null, bool allowSplit = true)
        => new()
        {
            Id = id,
            MaterialId = materialId,
            ProductionDepartmentId = deptId,
            IsActive = isActive,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            MinExecutionBatchQty = min,
            MaxExecutionBatchQty = max,
            AllowSplit = allowSplit,
        };

    [Fact]
    public void Project_筛除IsActivefalse()
    {
        var rules = new[]
        {
            Rule(id: 1, materialId: 100, deptId: 900, isActive: true),
            Rule(id: 2, materialId: 200, deptId: 900, isActive: false),
        };

        var result = TaskSplitRuleConfigProjector.Project(rules, asOf: DateTime.Now);

        result.Should().ContainSingle().Which.MaterialId.Should().Be(100);
    }

    [Fact]
    public void Project_筛除生效区间外()
    {
        var now = DateTime.Now;
        var rules = new[]
        {
            Rule(id: 1, materialId: 100, deptId: 900, effectiveFrom: now.AddDays(-1), effectiveTo: now.AddDays(1)),
            Rule(id: 2, materialId: 200, deptId: 900, effectiveFrom: now.AddDays(1)),
            Rule(id: 3, materialId: 300, deptId: 900, effectiveTo: now.AddDays(-1)),
        };

        var result = TaskSplitRuleConfigProjector.Project(rules, asOf: now);

        result.Should().ContainSingle().Which.MaterialId.Should().Be(100);
    }

    [Fact]
    public void Project_半开区间_EffectiveTo等于asOf_已过期排除()
    {
        // P0-04（0号位 审核）：投影与治理写路径统一半开 [EffectiveFrom, EffectiveTo)——截止点恰好等于 asOf 视为已过期。
        var now = new DateTime(2026, 10, 9, 11, 0, 0);
        var rules = new[]
        {
            Rule(id: 1, materialId: 100, deptId: 900, effectiveFrom: now.AddHours(-1), effectiveTo: now),
            Rule(id: 2, materialId: 200, deptId: 900, effectiveFrom: now, effectiveTo: now.AddHours(1)),
        };

        var result = TaskSplitRuleConfigProjector.Project(rules, asOf: now);

        result.Should().ContainSingle().Which.MaterialId.Should().Be(200);
    }

    [Fact]
    public void Project_半开区间_相切窗口同一瞬间至多一条有效()
    {
        // P0-04：相切窗口 [10:00,11:00) + [11:00,12:00) 在 11:00 恰好只命中第二条（杜绝投影重复键）。
        var now = new DateTime(2026, 10, 9, 11, 0, 0);
        var rules = new[]
        {
            Rule(id: 1, materialId: 100, deptId: 900, effectiveFrom: now.AddHours(-1), effectiveTo: now),
            Rule(id: 2, materialId: 200, deptId: 900, effectiveFrom: now, effectiveTo: now.AddHours(1)),
        };

        var result = TaskSplitRuleConfigProjector.Project(rules, asOf: now);

        result.Should().ContainSingle().Which.MaterialId.Should().Be(200);
    }

    [Fact]
    public void Project_投影业务键与参数_去审计兼容字段_4技术字段恒null()
    {
        // v5.1.10 收口①②：实体上即便有历史 4 字段值，投影快照亦恒 null（不投 Solver 技术预算/策略列，主链不得消费）。
        var rule = Rule(id: 7, materialId: 200, deptId: 300, min: 10m, max: 50m, allowSplit: false);
        rule.PreferredBatchQty = 25m;
        rule.AllowMerge = true;
        rule.MaxOptimizationSplitCount = 3;
        rule.MaxBatchCandidates = 5;
        rule.BottleneckSplitStrategy = "PREFER_SPLIT";
        rule.NonBottleneckStrategy = "PREFER_LARGE_BATCH";

        var result = TaskSplitRuleConfigProjector.Project(new[] { rule }).Single();

        result.MaterialId.Should().Be(200);
        result.ProductionDepartmentId.Should().Be(300);
        result.MinExecutionBatchQty.Should().Be(10m);
        result.MaxExecutionBatchQty.Should().Be(50m);
        result.PreferredBatchQty.Should().Be(25m);
        result.AllowSplit.Should().BeFalse();
        result.AllowMerge.Should().BeTrue();
        result.MaxOptimizationSplitCount.Should().BeNull();
        result.MaxBatchCandidates.Should().BeNull();
        result.BottleneckSplitStrategy.Should().BeNull();
        result.NonBottleneckStrategy.Should().BeNull();
    }

    [Fact]
    public void Project_筛除NULL部门历史记录_不默认为所有部门生效()
    {
        // v5.1.10 收口④：NULL 部门历史记录不默认为所有部门的生效规则——即便 IsActive + 生效窗内亦不投快照。
        var now = DateTime.UtcNow;
        var rules = new[]
        {
            Rule(id: 1, materialId: 100, deptId: 900, effectiveFrom: now.AddDays(-1), effectiveTo: now.AddDays(1)),
            Rule(id: 2, materialId: 200, deptId: null, effectiveFrom: now.AddDays(-1), effectiveTo: now.AddDays(1)),
        };

        var result = TaskSplitRuleConfigProjector.Project(rules, asOf: now);

        result.Should().ContainSingle().Which.MaterialId.Should().Be(100);
    }

    [Fact]
    public void SetRules_保留其它子块_原子替换BatchPolicy()
    {
        var original = "{\"Lock\":{\"x\":1},\"BatchPolicy\":[{\"MaterialId\":1}]}";
        var snapshots = new List<BatchPolicyRuleSnapshot> { new() { MaterialId = 200, AllowSplit = false } };

        var merged = TaskSplitRuleConfigProjector.SetRules(original, snapshots);

        TaskSplitRuleConfigProjector.ExtractRules(merged).Should().ContainSingle().Which.MaterialId.Should().Be(200);
        merged.Should().Contain("\"Lock\"");
    }

    [Fact]
    public void ExtractRules_子块缺失为空或损坏_返回空列表_failOpen装载()
    {
        // ⑧ 为非必填块，装载 fail-open（05契约 §6.10.5，与⑦ Setup 同轨）：缺子块/为空/损坏 → 空列表。
        // 冻结 §十五 fail-closed 属匹配终端 ④（1号位 消费侧 Strategy Snapshot 校验），装载端不承载信号位。
        TaskSplitRuleConfigProjector.ExtractRules(null).Should().BeEmpty();
        TaskSplitRuleConfigProjector.ExtractRules(string.Empty).Should().BeEmpty();
        TaskSplitRuleConfigProjector.ExtractRules("{\"Lock\":{}}").Should().BeEmpty();
        TaskSplitRuleConfigProjector.ExtractRules("{\"BatchPolicy\":null}").Should().BeEmpty();
        TaskSplitRuleConfigProjector.ExtractRules("{\"BatchPolicy\":[]}").Should().BeEmpty();
        TaskSplitRuleConfigProjector.ExtractRules("{{{bad json").Should().BeEmpty();
    }

    [Fact]
    public void ExtractRules_有效子块_返回规则列表()
    {
        TaskSplitRuleConfigProjector.ExtractRules("{\"BatchPolicy\":[{\"MaterialId\":100,\"AllowSplit\":true}]}")
            .Should().ContainSingle().Which.MaterialId.Should().Be(100);
    }
}