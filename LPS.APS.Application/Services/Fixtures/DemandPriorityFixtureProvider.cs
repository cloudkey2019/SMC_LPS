using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;

namespace LPS.APS.Application.Services.Fixtures;

/// <summary>
/// Demand优先级策略Fixture提供器（临时模拟3号位输出）
///
/// INTEGRATION TODO: 替换为真实3号位FrozenStrategySnapshot.DemandPriority
/// - 3号位负责策略冻结，输出完整的FrozenStrategySnapshot
/// - 当前用Fixture模拟DemandPriorityConfig，仅用于2号位排序执行器开发和测试
/// - 不得将此Fixture逻辑作为生产Fallback
/// </summary>
public sealed class DemandPriorityFixtureProvider : IDemandPriorityConfigProvider
{
    /// <summary>
    /// 获取Demand优先级配置（Fixture模拟）
    ///
    /// INTEGRATION TODO: 从3号位FrozenStrategySnapshot中提取DemandPriority
    /// - 接口路径待3号位提供
    /// - 替换前确保契约一致（DemandPriorityConfig结构）
    /// </summary>
    public Task<DemandPriorityConfig> GetPriorityConfigAsync(
        long strategyProfileVersionId,
        CancellationToken cancellationToken = default)
    {
        // INTEGRATION TODO: 实际实现应调用3号位API获取FrozenStrategySnapshot
        // var snapshot = await _strategy3Client.GetFrozenSnapshotAsync(strategyProfileVersionId, cancellationToken);
        // return snapshot.DemandPriority;

        // Fixture: 简单三段式排序策略
        // Segment 1: 延迟的销售订单（按到期日升序）
        // Segment 2: 普通销售订单（按到期日升序）
        // Segment 3: 生产指示（按下单日升序）
        var fixtureConfig = new DemandPriorityConfig
        {
            Segments = new[]
            {
                new PrioritySegmentConfig
                {
                    CalculationLayer = 1,
                    SegmentOrder = 1,
                    IsEnabled = true,
                    MatchConditions = new[]
                    {
                        new MatchCondition
                        {
                            FieldName = "DelayStatus",
                            Operator = "EQ",
                            Value = "DELAYED"
                        },
                        new MatchCondition
                        {
                            FieldName = "OrderType",
                            Operator = "EQ",
                            Value = "SALES_ORDER"
                        }
                    },
                    SortFields = new[]
                    {
                        new SortField
                        {
                            FieldName = "DueDate",
                            Direction = "ASC"
                        },
                        new SortField
                        {
                            FieldName = "IssueDate",
                            Direction = "ASC"
                        }
                    },
                    StableTieBreakFields = new[] { "DemandKey" }
                },
                new PrioritySegmentConfig
                {
                    CalculationLayer = 1,
                    SegmentOrder = 2,
                    IsEnabled = true,
                    MatchConditions = new[]
                    {
                        new MatchCondition
                        {
                            FieldName = "OrderType",
                            Operator = "EQ",
                            Value = "SALES_ORDER"
                        }
                    },
                    SortFields = new[]
                    {
                        new SortField
                        {
                            FieldName = "DueDate",
                            Direction = "ASC"
                        },
                        new SortField
                        {
                            FieldName = "IssueDate",
                            Direction = "ASC"
                        }
                    },
                    StableTieBreakFields = new[] { "DemandKey" }
                },
                new PrioritySegmentConfig
                {
                    CalculationLayer = 1,
                    SegmentOrder = 3,
                    IsEnabled = true,
                    MatchConditions = new[]
                    {
                        new MatchCondition
                        {
                            FieldName = "OrderType",
                            Operator = "EQ",
                            Value = "PRODUCTION_INSTRUCTION"
                        }
                    },
                    SortFields = new[]
                    {
                        new SortField
                        {
                            FieldName = "IssueDate",
                            Direction = "ASC"
                        }
                    },
                    StableTieBreakFields = new[] { "DemandKey" }
                }
            }
        };

        return Task.FromResult(fixtureConfig);
    }
}
