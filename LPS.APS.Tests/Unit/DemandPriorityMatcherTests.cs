using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// DemandPriorityMatcher 单元测试（阶段 C 验收）
/// R04: 第一命中即止
/// R05: 多 Sort 稳定排序
/// R06: 不同 DemandType 交错
/// </summary>
public class DemandPriorityMatcherTests
{
    private readonly DemandPriorityMatcher _matcher = new();

    [Fact]
    public void R04_第一命中即止_命中第一个Segment后不再匹配后续()
    {
        var priorityBlock = new DemandPriorityBlock
        {
            Segments = new List<PrioritySegment>
            {
                new PrioritySegment
                {
                    SegmentOrder = 1,
                    SegmentName = "紧急延迟订单",
                    IsEnabled = true,
                    MatchConditions = new List<SegmentMatchCondition>
                    {
                        new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "DELAYED" }
                    },
                    SortFields = new List<SegmentSortField>
                    {
                        new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                    }
                },
                new PrioritySegment
                {
                    SegmentOrder = 2,
                    SegmentName = "VIP客户",
                    IsEnabled = true,
                    MatchConditions = new List<SegmentMatchCondition>
                    {
                        new SegmentMatchCondition { Field = DemandField.CustomerTier, Operator = ConditionOperator.Equals, Value = "VIP" }
                    },
                    SortFields = new List<SegmentSortField>
                    {
                        new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                    }
                }
            }
        };

        var demand = new DemandRecord
        {
            DemandId = "D001", OrderId = "O001", CreatedAt = DateTime.UtcNow,
            DelayStatus = "DELAYED", CustomerTier = "VIP", RemainingTimeHours = 10
        };

        var matchedSegment = _matcher.FindFirstMatchSegment(demand, priorityBlock.Segments);

        matchedSegment.Should().NotBeNull();
        matchedSegment!.SegmentOrder.Should().Be(1);
    }

    [Fact]
    public void R05_多Sort稳定排序_依次应用排序字段()
    {
        var priorityBlock = new DemandPriorityBlock
        {
            Segments = new List<PrioritySegment>
            {
                new PrioritySegment
                {
                    SegmentOrder = 1, SegmentName = "标准订单", IsEnabled = true,
                    MatchConditions = new List<SegmentMatchCondition>
                    {
                        new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "ONTRACK" }
                    },
                    SortFields = new List<SegmentSortField>
                    {
                        new SegmentSortField { Field = DemandField.CustomerTier, Direction = SortDirection.Desc },
                        new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                    },
                    StableTieBreakFields = new List<string> { "OrderId" }
                }
            }
        };

        var demands = new List<DemandRecord>
        {
            new DemandRecord { DemandId = "D001", OrderId = "O003", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "A", RemainingTimeHours = 20 },
            new DemandRecord { DemandId = "D002", OrderId = "O001", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "VIP", RemainingTimeHours = 30 },
            new DemandRecord { DemandId = "D003", OrderId = "O002", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "VIP", RemainingTimeHours = 10 },
            new DemandRecord { DemandId = "D004", OrderId = "O004", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "A", RemainingTimeHours = 10 }
        };

        var sorted = _matcher.SortDemands(demands, priorityBlock);

        sorted.Should().HaveCount(4);
        sorted[0].OrderId.Should().Be("O002");
        sorted[1].OrderId.Should().Be("O001");
        sorted[2].OrderId.Should().Be("O004");
        sorted[3].OrderId.Should().Be("O003");
    }

    [Fact]
    public void R06_不同DemandType交错_按Segment分组后组内排序()
    {
        var priorityBlock = new DemandPriorityBlock
        {
            Segments = new List<PrioritySegment>
            {
                new PrioritySegment
                {
                    SegmentOrder = 1, SegmentName = "紧急", IsEnabled = true,
                    MatchConditions = new List<SegmentMatchCondition>
                    {
                        new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "DELAYED" }
                    },
                    SortFields = new List<SegmentSortField>
                    {
                        new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                    }
                },
                new PrioritySegment
                {
                    SegmentOrder = 2, SegmentName = "正常", IsEnabled = true,
                    MatchConditions = new List<SegmentMatchCondition>
                    {
                        new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "ONTRACK" }
                    },
                    SortFields = new List<SegmentSortField>
                    {
                        new SegmentSortField { Field = DemandField.CustomerTier, Direction = SortDirection.Desc }
                    }
                }
            }
        };

        var demands = new List<DemandRecord>
        {
            new DemandRecord { DemandId = "D001", OrderId = "O001", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "A", OrderType = "SO" },
            new DemandRecord { DemandId = "D002", OrderId = "O002", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", RemainingTimeHours = 5, OrderType = "WO" },
            new DemandRecord { DemandId = "D003", OrderId = "O003", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "VIP", OrderType = "Transfer" },
            new DemandRecord { DemandId = "D004", OrderId = "O004", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", RemainingTimeHours = 2, OrderType = "SO" }
        };

        var sorted = _matcher.SortDemands(demands, priorityBlock);

        sorted.Should().HaveCount(4);
        sorted[0].OrderId.Should().Be("O004");
        sorted[1].OrderId.Should().Be("O002");
        sorted[2].OrderId.Should().Be("O003");
        sorted[3].OrderId.Should().Be("O001");
    }

    [Fact]
    public void IsSegmentMatch_所有条件满足才命中()
    {
        var segment = new PrioritySegment
        {
            SegmentOrder = 1, SegmentName = "测试", IsEnabled = true,
            MatchConditions = new List<SegmentMatchCondition>
            {
                new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "DELAYED" },
                new SegmentMatchCondition { Field = DemandField.CustomerTier, Operator = ConditionOperator.Equals, Value = "VIP" }
            }
        };

        var demandMatch = new DemandRecord { DemandId = "D001", OrderId = "O001", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", CustomerTier = "VIP" };
        var demandNoMatch = new DemandRecord { DemandId = "D002", OrderId = "O002", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", CustomerTier = "A" };

        _matcher.IsSegmentMatch(demandMatch, segment).Should().BeTrue();
        _matcher.IsSegmentMatch(demandNoMatch, segment).Should().BeFalse();
    }

    [Fact]
    public void 调试_集成测试失败场景()
    {
        var priorityBlock = new DemandPriorityBlock
        {
            Segments = new List<PrioritySegment>
            {
                new PrioritySegment
                {
                    SegmentOrder = 1,
                    SegmentName = "紧急延迟订单",
                    IsEnabled = true,
                    MatchConditions = new List<SegmentMatchCondition>
                    {
                        new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "DELAYED" }
                    },
                    SortFields = new List<SegmentSortField>
                    {
                        new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                    },
                    StableTieBreakFields = new List<string> { "OrderId" }
                },
                new PrioritySegment
                {
                    SegmentOrder = 2,
                    SegmentName = "VIP客户",
                    IsEnabled = true,
                    MatchConditions = new List<SegmentMatchCondition>
                    {
                        new SegmentMatchCondition { Field = DemandField.CustomerTier, Operator = ConditionOperator.Equals, Value = "VIP" }
                    },
                    SortFields = new List<SegmentSortField>
                    {
                        new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                    }
                }
            }
        };

        var demands = new List<DemandRecord>
        {
            new DemandRecord { DemandId = "D001", OrderId = "O001", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", CustomerTier = "A", RemainingTimeHours = 5 },
            new DemandRecord { DemandId = "D002", OrderId = "O002", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "VIP", RemainingTimeHours = 20 },
            new DemandRecord { DemandId = "D003", OrderId = "O003", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", CustomerTier = "VIP", RemainingTimeHours = 2 }
        };

        var sorted = _matcher.SortDemands(demands, priorityBlock);

        sorted.Should().HaveCount(3);
        sorted[0].OrderId.Should().Be("O003", "DELAYED中Hours最小(2)应排第一");
        sorted[1].OrderId.Should().Be("O001", "DELAYED中Hours第二小(5)应排第二");
        sorted[2].OrderId.Should().Be("O002", "ONTRACK应排最后");
    }
}
