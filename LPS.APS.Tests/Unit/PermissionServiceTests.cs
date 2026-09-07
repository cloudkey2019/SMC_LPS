using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>PermissionService 单元测试（F-G3，3号位）</summary>
public class PermissionServiceTests
{
    private static PermissionService CreateService(Mock<IPermissionCodeRepository> repo)
        => new(repo.Object, new LoggerFactory().CreateLogger<PermissionService>());

    [Fact]
    public async Task HasPermissionAsync_用户具备权限码_返回true()
    {
        // Arrange
        var repo = new Mock<IPermissionCodeRepository>();
        repo.Setup(r => r.GetPermissionCodesByUserIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "candidate.confirm", "rule.publish" });

        var svc = CreateService(repo);

        // Act + Assert
        (await svc.HasPermissionAsync(1, "candidate.confirm")).Should().BeTrue();
        (await svc.HasPermissionAsync(1, "rule.publish")).Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_用户不具备权限码_返回false()
    {
        // Arrange
        var repo = new Mock<IPermissionCodeRepository>();
        repo.Setup(r => r.GetPermissionCodesByUserIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "plan.view" });

        var svc = CreateService(repo);

        // Act + Assert
        (await svc.HasPermissionAsync(1, "candidate.confirm")).Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_无效用户或空权限码_返回false()
    {
        // Arrange
        var repo = new Mock<IPermissionCodeRepository>();
        var svc = CreateService(repo);

        // Act + Assert（不触发仓储查询）
        (await svc.HasPermissionAsync(0, "plan.view")).Should().BeFalse();
        (await svc.HasPermissionAsync(1, " ")).Should().BeFalse();
        repo.Verify(r => r.GetPermissionCodesByUserIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
