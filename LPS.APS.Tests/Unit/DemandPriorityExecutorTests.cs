using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// DemandPriorityExecutor 单元测试（S5 / PM 0923 —— EXPEDITE 前置竞争层 + 空集回归）。
///
/// 覆盖口径：
///   - expedite 命中的需求作为独立竞争层整体前置（非恒先、非新增 Task 优先级）；
///   - 层内仍按 3号位冻结 DemandPriorityConfig 排序（非输入顺序）；
///   - 空集 / 未命中 = 零行为变化（普通 FULL 路径不受影响）；
///   - OrderCanonicalId 为空的需求绝不误入 expedite 桶；
///   - DemandSequence 始终 1..N 连续（两条路径都打序号）。
/// </summary>
public class DemandPriorityExecutorTests
{
    private readonly DemandPriorityExecutor _executor = new(NullLogger<DemandPriorityExecutor>.Instance);

    // ── helpers ──

    private static DemandPriorityConfig DueDateAscConfig() => new()
    {
        Segments = new List<PrioritySegmentConfig>
        {
            new()
            {
                SegmentOrder = 1,
                IsEnabled = true,
                MatchConditions = new List<MatchCondition>(),
                SortFields = new List<SortField> { new() { FieldName = "DUEDATE", Direction = "ASC" } },
                StableTieBreakFields = new List<string>()
            }
        }
    };

    private static UpstreamDemand Demand(string key, DateTime? dueDate, long? orderCanonicalId = null) => new()
    {
        DemandKey = key,
        DueDate = dueDate,
        OrderCanonicalId = orderCanonicalId
    };

    // ── S5 · EXPEDITE 前置竞争层 ──

    [Fact]
    public void Expedite_命中需求整桶前置_层内仍按冻结策略排序_非恒先()
    {
        var config = DueDateAscConfig();
        var demands = new List<UpstreamDemand>
        {
            Demand("E1", new DateTime(2026, 9, 10), orderCanonicalId: 1),
            Demand("N1", new DateTime(2026, 9, 1)),   // 交期最早，但非 expedite
            Demand("E2", new DateTime(2026, 9, 5), orderCanonicalId: 2),
        };
        var expedite = new HashSet<long> { 1, 2 };

        var result = _executor.ExecutePrioritySort(demands, config, expedite);

        // expedite 桶整体前置：交期更早的 N1 让位；
        // 层内仍按 DueDate 升序（E2 9/5 先于 E1 9/10，非输入顺序）。
        result.Select(d => d.DemandKey).Should().Equal("E2", "E1", "N1");
        result.Select(d => d.DemandSequence).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Expedite_空集_零行为变化()
    {
        var config = DueDateAscConfig();
        var demands = new List<UpstreamDemand>
        {
            Demand("E1", new DateTime(2026, 9, 10), orderCanonicalId: 1),
            Demand("N1", new DateTime(2026, 9, 1)),
            Demand("E2", new DateTime(2026, 9, 5), orderCanonicalId: 2),
        };

        var result = _executor.ExecutePrioritySort(demands, config, expediteOrderCanonicalIds: null);

        result.Select(d => d.DemandKey).Should().Equal("N1", "E2", "E1");
        result.Select(d => d.DemandSequence).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Expedite_未命中任何需求_零行为变化()
    {
        var config = DueDateAscConfig();
        var demands = new List<UpstreamDemand>
        {
            Demand("E1", new DateTime(2026, 9, 10), orderCanonicalId: 1),
            Demand("N1", new DateTime(2026, 9, 1)),
        };
        var expedite = new HashSet<long> { 999 }; // 无匹配

        var result = _executor.ExecutePrioritySort(demands, config, expedite);

        result.Select(d => d.DemandKey).Should().Equal("N1", "E1");
        result.Select(d => d.DemandSequence).Should().Equal(1, 2);
    }

    [Fact]
    public void Expedite_OrderCanonicalId为空的需求_不误入expedite桶()
    {
        var config = DueDateAscConfig();
        var demands = new List<UpstreamDemand>
        {
            Demand("NULL", new DateTime(2026, 9, 1)),   // OrderCanonicalId = null
            Demand("E1", new DateTime(2026, 9, 10), orderCanonicalId: 1),
        };
        var expedite = new HashSet<long> { 1 };

        var result = _executor.ExecutePrioritySort(demands, config, expedite);

        // E1（expedite）前置，即使 NULL 交期更早；NULL 未误入 expedite 桶。
        result.Select(d => d.DemandKey).Should().Equal("E1", "NULL");
    }

    // ── 普通 FULL 路径（无 expedite）—— DemandSequence 回归 ──

    [Fact]
    public void 普通路径_DemandSequence_N连续赋值()
    {
        var config = DueDateAscConfig();
        var demands = new List<UpstreamDemand>
        {
            Demand("A", new DateTime(2026, 9, 3)),
            Demand("B", new DateTime(2026, 9, 1)),
            Demand("C", new DateTime(2026, 9, 2)),
        };

        var result = _executor.ExecutePrioritySort(demands, config);

        result.Select(d => d.DemandKey).Should().Equal("B", "C", "A");
        result.Select(d => d.DemandSequence).Should().Equal(1, 2, 3);
    }
}