using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// DemandPriorityValidator 单元测试（P0-07 补充）
/// 覆盖：Validator 合法字段映射——白名单与 DemandField 枚举一致性（新增枚举必须显式登记）、
///       含 DueDate/IssueDate 的合法配置通过校验、禁止全局 PriorityScore 红线。
/// 设计意图：防止未来枚举新增值未同步 Matcher switch 时，配置越过发布校验、运行期抛 NotSupportedException。
/// </summary>
public class DemandPriorityValidatorTests
{
    private readonly DemandPriorityValidator _validator = new();

    [Fact]
    public void P007_所有枚举成员必须在Validator白名单内()
    {
        // Arrange —— 枚举是强类型，此测试保证每个枚举值都被 Matcher 支持（经白名单登记）
        var allFields = Enum.GetValues<DemandField>();

        // Act —— 构造一个引用了全部枚举字段的配置，逐字段验证
        foreach (var field in allFields)
        {
            var block = new DemandPriorityBlock
            {
                Segments =
                [
                    new PrioritySegment
                    {
                        SegmentOrder = 1, SegmentName = $"字段校验-{field}", IsEnabled = true,
                        MatchConditions =
                        [
                            new SegmentMatchCondition { Field = field, Operator = ConditionOperator.Equals, Value = null }
                        ],
                        SortFields =
                        [
                            new SegmentSortField { Field = field, Direction = SortDirection.Asc }
                        ]
                    }
                ]
            };

            var result = _validator.Validate(block);

            // Assert —— 白名单内字段不报"不在合法字段白名单内"错误
            result.Errors.Should().NotContain(e => e.Contains("不在合法字段白名单内"),
                $"枚举值 {field} 必须显式登记在 DemandPriorityValidator.SupportedDemandFields");
        }
    }

    [Fact]
    public void P007_含DueDateIssueDate的冻结示例配置_校验通过()
    {
        // Arrange —— 0 号位冻结示例：Delayed SALES_ORDER → DueDate ASC → CustomerTier DESC → IssueDate ASC
        var block = new DemandPriorityBlock
        {
            Segments =
            [
                new PrioritySegment
                {
                    SegmentOrder = 1, SegmentName = "延迟销售订单", IsEnabled = true,
                    MatchConditions =
                    [
                        new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "DELAYED" },
                        new SegmentMatchCondition { Field = DemandField.OrderType, Operator = ConditionOperator.Equals, Value = "SALES_ORDER" }
                    ],
                    SortFields =
                    [
                        new SegmentSortField { Field = DemandField.DueDate, Direction = SortDirection.Asc },
                        new SegmentSortField { Field = DemandField.CustomerTier, Direction = SortDirection.Desc },
                        new SegmentSortField { Field = DemandField.IssueDate, Direction = SortDirection.Asc }
                    ]
                }
            ]
        };

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void P007_DueDate匹配条件_白名单校验通过()
    {
        // Arrange —— 日期字段既可用于匹配也可用于排序
        var block = new DemandPriorityBlock
        {
            Segments =
            [
                new PrioritySegment
                {
                    SegmentOrder = 1, SegmentName = "一周内到期", IsEnabled = true,
                    MatchConditions =
                    [
                        new SegmentMatchCondition
                        {
                            Field = DemandField.DueDate,
                            Operator = ConditionOperator.LessOrEqual,
                            Value = new DateTime(2026, 8, 27)
                        }
                    ],
                    SortFields =
                    [
                        new SegmentSortField { Field = DemandField.DueDate, Direction = SortDirection.Asc }
                    ]
                }
            ]
        };

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void 红线_合法配置_校验通过()
    {
        // Arrange —— 合法配置（无全局 PriorityScore、无越界字段）
        var block = new DemandPriorityBlock
        {
            Segments =
            [
                new PrioritySegment
                {
                    SegmentOrder = 1, SegmentName = "正常配置", IsEnabled = true,
                    MatchConditions =
                    [
                        new SegmentMatchCondition { Field = DemandField.DelayStatus, Operator = ConditionOperator.Equals, Value = "DELAYED" }
                    ],
                    SortFields =
                    [
                        new SegmentSortField { Field = DemandField.OrderType, Direction = SortDirection.Asc }
                    ]
                }
            ]
        };

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
