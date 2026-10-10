using FluentAssertions;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// SolverStrategyModeMap 固定映射单测（P1-02 §五-3：1↔2 契约 Mode↔SchedulingDirection 正式化）。
/// 契约冻结：Forward→"FORWARD"、Backward→"BACKWARD"、Mixed→"MIXED"；未知枚举防御回退 Backward。
/// </summary>
public class SolverStrategyModeMapTests
{
    [Fact]
    public void Forward_映射_FORWARD()
    {
        SolverStrategyModeMap.ToDirection(SolverStrategyMode.Forward).Should().Be("FORWARD");
    }

    [Fact]
    public void Backward_映射_BACKWARD()
    {
        SolverStrategyModeMap.ToDirection(SolverStrategyMode.Backward).Should().Be("BACKWARD");
    }

    [Fact]
    public void Mixed_映射_MIXED()
    {
        SolverStrategyModeMap.ToDirection(SolverStrategyMode.Mixed).Should().Be("MIXED");
    }

    [Fact]
    public void Auto_映射_AUTO()
    {
        SolverStrategyModeMap.ToDirection(SolverStrategyMode.Auto).Should().Be("AUTO");
    }

    [Fact]
    public void 未知枚举_防御回退_BACKWARD()
    {
        SolverStrategyModeMap.ToDirection((SolverStrategyMode)99).Should().Be("BACKWARD");
    }
}
