using FluentAssertions;
using LPS.APS.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 跨厂 SH 级（INTER_FACTORY_ORDER）处理器时间原语测试。
/// 契约（PM 2026-09-09 v1.0）：
///   - SourceReadyTime = max(该 SH 全部源厂 Task 的 PlannedEndTime)，源厂未排 = null；
///   - SH AvailableTime = SourceReadyTime + CrossFactoryLT（Transport/Inspection/Transfer 三元组天数）。
/// </summary>
public class CrossFactoryPeggingHandlerTests
{
    private readonly CrossFactoryPeggingHandler _handler = new(NullLogger<CrossFactoryPeggingHandler>.Instance);

    // ── CalculateSourceReadyTime：max(源厂 Task 完成时间) ──

    [Fact]
    public void SourceReadyTime_TakesMaxOfCompletionTimes()
    {
        var times = new DateTime?[] { new(2026, 9, 1), new(2026, 9, 5), new(2026, 9, 3) };

        var result = _handler.CalculateSourceReadyTime(times);

        result.Should().Be(new DateTime(2026, 9, 5));
    }

    [Fact]
    public void SourceReadyTime_IgnoresNulls()
    {
        var times = new DateTime?[] { null, new(2026, 9, 2), null };

        var result = _handler.CalculateSourceReadyTime(times);

        result.Should().Be(new DateTime(2026, 9, 2));
    }

    [Fact]
    public void SourceReadyTime_EmptyOrAllNull_ReturnsNull()
    {
        _handler.CalculateSourceReadyTime(Array.Empty<DateTime?>()).Should().BeNull();
        _handler.CalculateSourceReadyTime(new DateTime?[] { null, null }).Should().BeNull();
    }

    // ── CalculateDownstreamAvailableTime：跨厂 LT 三元组传播 ──

    [Fact]
    public void DownstreamAvailableTime_AddsAllThreeLeadTimeComponents()
    {
        var sourceReady = new DateTime(2026, 9, 1);
        var leadTime = new CrossFactoryLeadTime { TransportDays = 2, InspectionDays = 1, TransferDays = 1 };

        var result = _handler.CalculateDownstreamAvailableTime(sourceReady, leadTime);

        result.Should().Be(new DateTime(2026, 9, 5));
    }

    [Fact]
    public void DownstreamAvailableTime_ZeroLeadTime_ReturnsSourceReady()
    {
        var sourceReady = new DateTime(2026, 9, 1);
        var leadTime = new CrossFactoryLeadTime { TransportDays = 0, InspectionDays = 0, TransferDays = 0 };

        var result = _handler.CalculateDownstreamAvailableTime(sourceReady, leadTime);

        result.Should().Be(sourceReady);
    }
}