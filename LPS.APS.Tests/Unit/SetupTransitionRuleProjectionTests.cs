using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LPS.APS.Application.Models;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// Setup 换型规则投影/裁剪纯函数测试（v1.2 §19.3 ①/② + §22 S01~S06 中 2号位 可覆盖的纯逻辑）。
/// 覆盖：实体→快照字段映射（去审计字段）、IsActive 过滤、EXACT/DEFAULT 分流（From/To null 语义）、
/// 按 Domain（ProductionDepartmentId + StageCode）裁剪。命中/兜底（EXACT 优先、无规则 0 分钟、
/// 禁止 SetupTime fallback）属 1号位 SetupOptimizer.ResolveSetup，不在此层。
/// </summary>
public class SetupTransitionRuleProjectionTests
{
    private static SetupTransitionRule Rule(
        int dept = 1, string stage = "S1", string op = "OP20", int resource = 10,
        int? from = null, int? to = null, string ruleType = SetupTransitionRuleType.Default,
        decimal minutes = 30m, bool isActive = true)
        => new SetupTransitionRule
        {
            Id = 0,
            RuleSetVersionId = 100,
            ProductionDepartmentId = dept,
            StageCode = stage,
            OperationCode = op,
            ResourceId = resource,
            FromMaterialId = from,
            ToMaterialId = to,
            RuleType = ruleType,
            SetupMinutes = minutes,
            IsActive = isActive
        };

    private static MaterialStageDepartmentContextDto Context(int dept, string stage)
        => new MaterialStageDepartmentContextDto { ProductionDepartmentId = dept, StageCode = stage };

    [Fact]
    public void Project_映射业务键_去审计字段_保留RuleType与分钟()
    {
        var rules = new List<SetupTransitionRule>
        {
            Rule(dept: 1, stage: "S1", op: "OP20", resource: 10, from: 101, to: 102, ruleType: SetupTransitionRuleType.Exact, minutes: 45m)
        };

        var snapshots = SetupTransitionRuleProjector.Project(rules);

        snapshots.Should().ContainSingle();
        var s = snapshots[0];
        s.ProductionDepartmentId.Should().Be(1);
        s.StageCode.Should().Be("S1");
        s.OperationCode.Should().Be("OP20");
        s.ResourceId.Should().Be(10);
        s.FromMaterialId.Should().Be(101);
        s.ToMaterialId.Should().Be(102);
        s.RuleType.Should().Be(SetupTransitionRuleType.Exact);
        s.SetupMinutes.Should().Be(45m);
    }

    [Fact]
    public void Project_筛选IsActive_排除无效规则()
    {
        var rules = new List<SetupTransitionRule>
        {
            Rule(ruleType: SetupTransitionRuleType.Exact, minutes: 10m, isActive: true),
            Rule(ruleType: SetupTransitionRuleType.Exact, minutes: 20m, isActive: false)
        };

        var snapshots = SetupTransitionRuleProjector.Project(rules);

        snapshots.Should().ContainSingle();
        snapshots[0].SetupMinutes.Should().Be(10m);
    }

    [Fact]
    public void Project_DEFAULT规则FromTo为null_EXACT有值()
    {
        var rules = new List<SetupTransitionRule>
        {
            Rule(ruleType: SetupTransitionRuleType.Default, from: null, to: null),
            Rule(ruleType: SetupTransitionRuleType.Exact, from: 1, to: 2)
        };

        var snapshots = SetupTransitionRuleProjector.Project(rules);

        snapshots.Should().HaveCount(2);
        snapshots.Single(s => s.RuleType == SetupTransitionRuleType.Default).FromMaterialId.Should().BeNull();
        snapshots.Single(s => s.RuleType == SetupTransitionRuleType.Default).ToMaterialId.Should().BeNull();
        snapshots.Single(s => s.RuleType == SetupTransitionRuleType.Exact).FromMaterialId.Should().Be(1);
        snapshots.Single(s => s.RuleType == SetupTransitionRuleType.Exact).ToMaterialId.Should().Be(2);
    }

    [Fact]
    public void ProjectFromSnapshot_子块JSON_映射快照_筛IsActive()
    {
        // S-9：子块承载 → 消费快照整链（S-8 便捷入口），IsActive=false 被过滤
        var rules = new List<SetupTransitionRule>
        {
            Rule(dept: 1, stage: "S1", op: "OP20", resource: 10, from: 101, to: 102, ruleType: SetupTransitionRuleType.Exact, minutes: 45m),
            Rule(dept: 1, stage: "S1", op: "OP20", resource: 10, from: 101, to: 102, ruleType: SetupTransitionRuleType.Exact, minutes: 20m, isActive: false)
        };
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [SetupTransitionRuleProjector.SetupTransitionRulesBlockName] = rules
        });

        var snapshots = SetupTransitionRuleProjector.ProjectFromSnapshot(json);

        snapshots.Should().ContainSingle();
        snapshots[0].RuleType.Should().Be(SetupTransitionRuleType.Exact);
        snapshots[0].SetupMinutes.Should().Be(45m);
        snapshots[0].FromMaterialId.Should().Be(101);
    }

    [Fact]
    public void ProjectFromSnapshot_子块缺失或为空_返回空列表()
    {
        // S-9：fail-open——缺失子块 / null / 空字符串 → 空列表（无规则 DEFAULT 兜底）
        var noBlock = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["DemandPriority"] = new { }
        });

        SetupTransitionRuleProjector.ProjectFromSnapshot(noBlock).Should().BeEmpty();
        SetupTransitionRuleProjector.ProjectFromSnapshot(null).Should().BeEmpty();
        SetupTransitionRuleProjector.ProjectFromSnapshot(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void ProjectFromSnapshot_损坏JSON_返回空列表_failOpen()
    {
        // S-9：fail-open——损坏子块不抛异常，空列表（区别于六块 fail-closed）
        SetupTransitionRuleProjector.ProjectFromSnapshot("{ not-valid-json").Should().BeEmpty();
        SetupTransitionRuleProjector.ProjectFromSnapshot("{\"SetupTransitionRules\": [broken}").Should().BeEmpty();
    }

    [Fact]
    public void CropToDomain_仅保留本Domain部门阶段_跨域规则被裁掉()
    {
        var rules = new List<SetupTransitionRuleSnapshot>
        {
            new() { ProductionDepartmentId = 1, StageCode = "S1", RuleType = SetupTransitionRuleType.Default, SetupMinutes = 30m },
            new() { ProductionDepartmentId = 1, StageCode = "S2", RuleType = SetupTransitionRuleType.Default, SetupMinutes = 40m },
            new() { ProductionDepartmentId = 2, StageCode = "S1", RuleType = SetupTransitionRuleType.Default, SetupMinutes = 50m }
        };
        var contexts = new List<MaterialStageDepartmentContextDto>
        {
            Context(1, "S1"),
            Context(1, "S2")
        };

        var cropped = SetupTransitionRuleProjector.CropToDomain(rules, contexts);

        cropped.Should().HaveCount(2);
        cropped.Should().OnlyContain(r => r.ProductionDepartmentId == 1); // 部门 2 规则被裁掉
    }

    [Fact]
    public void CropToDomain_上下文为空_返回空()
    {
        var rules = new List<SetupTransitionRuleSnapshot>
        {
            new() { ProductionDepartmentId = 1, StageCode = "S1", RuleType = SetupTransitionRuleType.Default }
        };

        SetupTransitionRuleProjector.CropToDomain(rules, new List<MaterialStageDepartmentContextDto>())
            .Should().BeEmpty();
    }
}
