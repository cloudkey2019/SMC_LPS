using FluentAssertions;
using LPS.APS.Application.Models;
using LPS.APS.Application.Services;
using LPS.APS.Core.Entities.APS;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// SetupTransitionRuleConflictValidator 单元测试（阶段 E-4 扩展）
/// 依据：《APS V1 Setup换型规则 v1.2》§十 唯一键：
///   EXACT（明确产品转换）唯一键 = 生产部门 + 大工艺 + 当前工序 + 设备 + 前产品 + 后产品；
///   DEFAULT（默认）唯一键 = 生产部门 + 大工艺 + 当前工序 + 设备。
/// 仅校验 IsActive=true 的规则；空规则集/无冲突合法（无规则 => 运行时 Setup 兜底 0 分钟）。
/// </summary>
public class SetupTransitionRuleConflictValidatorTests
{
    private readonly SetupTransitionRuleConflictValidator _validator = new();

    private static SetupTransitionRule Exact(int dept, string stage, string operation, int resource, int fromMaterialId, int toMaterialId) => new()
    {
        RuleSetVersionId = 1, ProductionDepartmentId = dept, StageCode = stage, OperationCode = operation,
        ResourceId = resource, FromMaterialId = fromMaterialId, ToMaterialId = toMaterialId,
        RuleType = SetupTransitionRuleType.Exact, SetupMinutes = 30, IsActive = true
    };

    private static SetupTransitionRule Default(int dept, string stage, string operation, int resource) => new()
    {
        RuleSetVersionId = 1, ProductionDepartmentId = dept, StageCode = stage, OperationCode = operation,
        ResourceId = resource, RuleType = SetupTransitionRuleType.Default, SetupMinutes = 15, IsActive = true
    };

    [Fact]
    public void E4_空规则集_校验通过()
    {
        var result = _validator.Validate(Array.Empty<SetupTransitionRule>());

        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_EXACT同键重复_拒绝()
    {
        var rules = new[]
        {
            Exact(1, "SMT", "OP10", 100, 1, 2),
            Exact(1, "SMT", "OP10", 100, 1, 2),
        };

        var result = _validator.Validate(rules);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("EXACT"));
    }

    [Fact]
    public void E4_EXACT不同产品对_通过()
    {
        var rules = new[]
        {
            Exact(1, "SMT", "OP10", 100, 1, 2),
            Exact(1, "SMT", "OP10", 100, 2, 3),
        };

        var result = _validator.Validate(rules);

        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_DEFAULT同键重复_拒绝()
    {
        var rules = new[]
        {
            Default(1, "SMT", "OP10", 100),
            Default(1, "SMT", "OP10", 100),
        };

        var result = _validator.Validate(rules);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("DEFAULT"));
    }

    [Fact]
    public void E4_EXACT与DEFAULT并行_不冲突_通过()
    {
        var rules = new[]
        {
            Exact(1, "SMT", "OP10", 100, 1, 2),
            Default(1, "SMT", "OP10", 100),
        };

        var result = _validator.Validate(rules);

        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_未生效规则不参与校验_通过()
    {
        var inactive = Exact(1, "SMT", "OP10", 100, 1, 2);
        inactive.IsActive = false;

        var rules = new[]
        {
            Exact(1, "SMT", "OP10", 100, 1, 2),
            inactive,
        };

        var result = _validator.Validate(rules);

        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_子块承载_EXACT同键重复_经ExtractRules校验拒绝()
    {
        // S-9：子块承载 → ExtractRules 反序列化 → 校验器整链（重构 S-1/S-2 承载改道后校验输入源=子块）
        var rules = new[]
        {
            Exact(1, "SMT", "OP10", 100, 1, 2),
            Exact(1, "SMT", "OP10", 100, 1, 2),
        };
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [SetupTransitionRuleProjector.SetupTransitionRulesBlockName] = rules
        });

        var fromBlock = SetupTransitionRuleProjector.ExtractRules(json);

        var result = _validator.Validate(fromBlock);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("EXACT"));
    }

    [Fact]
    public void E4_子块承载_空子块_校验通过()
    {
        // S-9：子块承载空列表 → 校验通过（无规则 => 运行时 Setup 兜底 0 分钟）
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [SetupTransitionRuleProjector.SetupTransitionRulesBlockName] = new List<SetupTransitionRule>()
        });

        var result = _validator.Validate(SetupTransitionRuleProjector.ExtractRules(json));

        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }
}