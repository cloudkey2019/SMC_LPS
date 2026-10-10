using FluentAssertions;
using LPS.APS.Core.DTOs.Governance;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// OperationPlanningMode（OPM）三态治理校验单元测试（T1 · S2/S3 OPM 治理 API）。
/// 对齐冻结 DDL CHK_RoutingOperation_PlanningMode：FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY。
/// OPM 属 APS 工艺规划属性（0号位 2026-09-23 Q1 裁决），治理侧直维护唯一主路径。
/// </summary>
public class RoutingOperationPlanningModeTests
{
    [Theory]
    [InlineData("FINITE_RESOURCE")]
    [InlineData("UNCONSTRAINED")]
    [InlineData("WAIT_ONLY")]
    public void 合法三态_校验通过(string mode)
    {
        OperationPlanningModeValues.IsValid(mode).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("FINITE")]
    [InlineData("unconstrained")]
    [InlineData("MANUAL")]
    [InlineData("EXPEDITE")]
    public void 非法三态_校验拒绝(string? mode)
    {
        OperationPlanningModeValues.IsValid(mode).Should().BeFalse();
    }

    [Fact]
    public void 默认值_为FINITE_RESOURCE()
    {
        var dto = new RoutingOperationDto();
        dto.OperationPlanningMode.Should().Be(OperationPlanningModeValues.FiniteResource);
    }

    [Fact]
    public void 三态常量_与DDL逐字一致()
    {
        OperationPlanningModeValues.All.Keys.Should().BeEquivalentTo(new[]
        {
            "FINITE_RESOURCE",
            "UNCONSTRAINED",
            "WAIT_ONLY",
        });
    }
}