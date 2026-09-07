using FluentAssertions;
using LPS.APS.Core.Authorization;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>DataScopeContext 单元测试（F-G4，3号位）</summary>
public class DataScopeContextTests
{
    [Fact]
    public void Empty_无任何范围()
    {
        DataScopeContext.Empty.IsEmpty.Should().BeTrue();
        DataScopeContext.Empty.IsGlobal.Should().BeFalse();
        DataScopeContext.Empty.Allows(DataScopeTypes.Factory, "BJ").Should().BeFalse();
    }

    [Fact]
    public void Global_全放行()
    {
        DataScopeContext.Global.IsGlobal.Should().BeTrue();
        DataScopeContext.Global.Allows(DataScopeTypes.Factory, "BJ").Should().BeTrue();
        DataScopeContext.Global.Allows(DataScopeTypes.Department, "MC").Should().BeTrue();
    }

    [Fact]
    public void FromPolicies_含Global维度_视为全局()
    {
        var ctx = DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Global, "*") });
        ctx.IsGlobal.Should().BeTrue();
    }

    [Fact]
    public void FromPolicies_同一维度并集()
    {
        var ctx = DataScopeContext.FromPolicies(new[]
        {
            (DataScopeTypes.Factory, "BJ"),
            (DataScopeTypes.Factory, "TJ"),
        });

        ctx.IsEmpty.Should().BeFalse();
        ctx.GetValues(DataScopeTypes.Factory)!.Should().Contain("BJ").And.Contain("TJ");
        ctx.Allows(DataScopeTypes.Factory, "BJ").Should().BeTrue();
        ctx.Allows(DataScopeTypes.Factory, "SH").Should().BeFalse();
    }

    [Fact]
    public void Allows_维度未授权_返回false()
    {
        var ctx = DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") });
        ctx.Allows(DataScopeTypes.Department, "MC").Should().BeFalse();
        ctx.GetValues(DataScopeTypes.Department).Should().BeNull();
    }

    [Fact]
    public void FromPolicies_空输入_返回空范围()
    {
        var ctx = DataScopeContext.FromPolicies(Array.Empty<(string, string)>());
        ctx.IsEmpty.Should().BeTrue();
    }
}
