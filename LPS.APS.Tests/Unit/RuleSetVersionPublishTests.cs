using FluentAssertions;
using LPS.APS.Application.Services;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// R01 验收：发布 RuleSetVersion 历史不可覆盖
/// 场景：已 PUBLISHED 版本不可再次发布；发布合法前驱状态变为 PUBLISHED；不存在版本拒绝。
/// </summary>
public class RuleSetVersionPublishTests
{
    private readonly Mock<IRuleSetVersionRepository> _repo = new();
    private readonly GovernanceVersionService _service;

    public RuleSetVersionPublishTests()
    {
        _service = new GovernanceVersionService(
            _repo.Object,
            Mock.Of<IParameterSetVersionRepository>(),
            Mock.Of<IStrategyProfileRepository>(),
            Mock.Of<IStrategyProfileVersionRepository>(),
            Mock.Of<IGovernanceAuditLogRepository>());
    }

    [Fact]
    public async Task Publish_DraftVersion_StatusBecomesPublished()
    {
        // Arrange
        var version = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Draft,
            // P0-05：正式 Publish 强制发布前校验，测试须配合法内容
            DemandPriorityJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.DemandPriorityBlock
            {
                Segments =
                [
                    new LPS.APS.Core.Dto.PrioritySegment
                    {
                        SegmentOrder = 1,
                        SegmentName = "紧急订单",
                        IsEnabled = true,
                        MatchConditions =
                        [
                            new LPS.APS.Core.Dto.SegmentMatchCondition
                            {
                                Field = LPS.APS.Core.Dto.DemandField.OrderType,
                                Operator = LPS.APS.Core.Dto.ConditionOperator.Equals,
                                Value = "SO"
                            }
                        ],
                        SortFields =
                        [
                            new LPS.APS.Core.Dto.SegmentSortField
                            {
                                Field = LPS.APS.Core.Dto.DemandField.RemainingTimeHours,
                                Direction = LPS.APS.Core.Dto.SortDirection.Asc
                            }
                        ]
                    }
                ]
            })
        };
        _repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        await _service.PublishRuleSetVersionAsync(1, "tester");

        // Assert
        version.Status.Should().Be(GovernanceVersionStatus.Published);
        version.PublishedAt.Should().NotBeNull();
        version.PublishedBy.Should().Be("tester");
        _repo.Verify(r => r.UpdateAsync(version, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_AlreadyPublishedVersion_ThrowsInvalidOperation()
    {
        // Arrange —— 历史不可覆盖（R01 核心）；
        // 版本带合法 DemandPriorityJson，穿透 P0-05 校验后由状态机拒绝（确保拦截点确为"已发布"）
        var version = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
            DemandPriorityJson = System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.DemandPriorityBlock
            {
                Segments =
                [
                    new LPS.APS.Core.Dto.PrioritySegment
                    {
                        SegmentOrder = 1,
                        SegmentName = "紧急订单",
                        IsEnabled = true,
                        MatchConditions =
                        [
                            new LPS.APS.Core.Dto.SegmentMatchCondition
                            {
                                Field = LPS.APS.Core.Dto.DemandField.OrderType,
                                Operator = LPS.APS.Core.Dto.ConditionOperator.Equals,
                                Value = "SO"
                            }
                        ],
                        SortFields =
                        [
                            new LPS.APS.Core.Dto.SegmentSortField
                            {
                                Field = LPS.APS.Core.Dto.DemandField.RemainingTimeHours,
                                Direction = LPS.APS.Core.Dto.SortDirection.Asc
                            }
                        ]
                    }
                ]
            })
        };
        _repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var act = () => _service.PublishRuleSetVersionAsync(1, "tester");

        // Assert —— 已发布版本不可再次发布
        await act.Should().ThrowAsync<InvalidOperationException>();
        version.Status.Should().Be(GovernanceVersionStatus.Published);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_NonexistentVersion_ThrowsInvalidOperation()
    {
        // Arrange
        _repo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((RuleSetVersion?)null);

        // Act
        var act = () => _service.PublishRuleSetVersionAsync(999, "tester");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
