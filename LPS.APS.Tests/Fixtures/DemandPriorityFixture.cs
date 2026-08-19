using LPS.APS.Core.Dto;
using System.Text.Json;

namespace LPS.APS.Tests.Fixtures;

/// <summary>
/// Demand Priority Fixture 数据（阶段 C-4）
/// 供 2号位联调使用的示例 FrozenStrategySnapshot
/// </summary>
public static class DemandPriorityFixture
{
    /// <summary>
    /// 标准三层优先级策略：紧急延迟 > VIP客户 > 标准订单
    /// </summary>
    public static FrozenStrategySnapshot GetStandardPrioritySnapshot()
    {
        return new FrozenStrategySnapshot
        {
            StrategyProfileVersionId = 1001,
            RuleSetVersionId = 2001,
            ParameterSetVersionId = 3001,
            FrozenAt = DateTime.UtcNow,
            DemandPriority = new DemandPriorityBlock
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
            },
            Lock = new LockBlock(),
            Supply = new SupplyBlock(),
            Procurement = new ProcurementBlock(),
            SolverStrategy = new SolverStrategyBlock(),
            CandidateGuardrail = new CandidateGuardrailBlock()
        };
    }

    public static string ToJson(FrozenStrategySnapshot snapshot)
    {
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
    }
}
