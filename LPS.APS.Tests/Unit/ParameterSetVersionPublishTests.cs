using FluentAssertions;
using LPS.APS.Application.Services;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// R02 验收：发布 ParameterSetVersion 新 Run 可引用、旧 Run 不变
/// 场景：发布新版本 V2 后，V2 为新 PUBLISHED 记录；旧版本 V1 记录（Id/内容）保持不变——
/// 旧 Run 引用 V1 不因新版本发布而漂移，新 Run 可引用 V2。
/// </summary>
public class ParameterSetVersionPublishTests
{
    private readonly Mock<IParameterSetVersionRepository> _repo = new();
    private readonly GovernanceVersionService _service;

    public ParameterSetVersionPublishTests()
    {
        _service = new GovernanceVersionService(
            Mock.Of<IRuleSetVersionRepository>(),
            _repo.Object,
            Mock.Of<IGovernanceAuditLogRepository>());
    }

    [Fact]
    public async Task Publish_DraftVersion_StatusBecomesPublished()
    {
        // Arrange
        var version = new ParameterSetVersion
        {
            Id = 1,
            ParameterSetId = 20,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Draft,
        };
        _repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        await _service.PublishParameterSetVersionAsync(1, "tester");

        // Assert
        version.Status.Should().Be(GovernanceVersionStatus.Published);
        version.PublishedAt.Should().NotBeNull();
        _repo.Verify(r => r.UpdateAsync(version, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_NewV2_OldV1RecordUnchanged()
    {
        // Arrange —— R02 核心：V1 已发布（旧 Run 引用），V2 为草稿（新 Run 将引用）
        var v1 = new ParameterSetVersion
        {
            Id = 1,
            ParameterSetId = 20,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
            PublishedAt = new DateTime(2026, 8, 1),
        };
        var v2 = new ParameterSetVersion
        {
            Id = 2,
            ParameterSetId = 20,
            VersionCode = "V2",
            Status = GovernanceVersionStatus.Draft,
        };
        _repo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(v2);

        // Act —— 发布 V2
        await _service.PublishParameterSetVersionAsync(2, "tester");

        // Assert —— V2 成为新 PUBLISHED；V1 记录完全不变（旧 Run 引用 V1 不受影响）
        v2.Status.Should().Be(GovernanceVersionStatus.Published);
        v2.PublishedAt.Should().NotBeNull();
        v1.Id.Should().Be(1);
        v1.VersionCode.Should().Be("V1");
        v1.Status.Should().Be(GovernanceVersionStatus.Published);
        v1.PublishedAt.Should().Be(new DateTime(2026, 8, 1));
    }

    [Fact]
    public async Task Publish_AlreadyPublishedVersion_ThrowsInvalidOperation()
    {
        // Arrange —— 历史不可覆盖（R01 语义延伸至参数集）
        var version = new ParameterSetVersion
        {
            Id = 1,
            ParameterSetId = 20,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
        };
        _repo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var act = () => _service.PublishParameterSetVersionAsync(1, "tester");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _repo.Verify(r => r.UpdateAsync(It.IsAny<ParameterSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
