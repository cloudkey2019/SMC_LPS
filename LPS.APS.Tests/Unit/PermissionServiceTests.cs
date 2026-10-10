using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>PermissionService 单元测试（F-G3/M1，3号位）</summary>
public class PermissionServiceTests
{
    private static PermissionService CreateService(Mock<IPermissionCodeRepository> repo)
        => new(repo.Object, new LoggerFactory().CreateLogger<PermissionService>());

    [Fact]
    public async Task HasPermissionAsync_具备权限码_返回true()
    {
        // Arrange
        var repo = new Mock<IPermissionCodeRepository>();
        repo.Setup(r => r.HasPermissionAsync(1, "aps.candidate.confirm", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var svc = CreateService(repo);

        // Act + Assert（M1：服务按需查码，直接委托仓储单码判定）
        (await svc.HasPermissionAsync(1, "aps.candidate.confirm")).Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_不具备权限码_返回false()
    {
        // Arrange
        var repo = new Mock<IPermissionCodeRepository>();
        repo.Setup(r => r.HasPermissionAsync(1, "aps.plan.view", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var svc = CreateService(repo);

        // Act + Assert
        (await svc.HasPermissionAsync(1, "aps.plan.view")).Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_无效用户或空权限码_不触仓储_返回false()
    {
        // Arrange
        var repo = new Mock<IPermissionCodeRepository>();
        var svc = CreateService(repo);

        // Act + Assert（参数非法时 fail-closed，不触发仓储查询）
        (await svc.HasPermissionAsync(0, "aps.plan.view")).Should().BeFalse();
        (await svc.HasPermissionAsync(1, " ")).Should().BeFalse();
        repo.Verify(r => r.HasPermissionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}