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
        // Arrange —— 历史不可覆盖（R01 核心）
        var version = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
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
