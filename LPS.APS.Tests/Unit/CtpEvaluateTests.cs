using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Ctp;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Configuration;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// CTP 评估薄层单元测试（APS V1 CTP 评估接口契约登记 v1.0，3号位）。
/// 覆盖：CtpService 显式域键透传 + F-G4 校验、缺省自动 resolve 成功/无唯一匹配 400 语义、
///       DomainResolver 直读的命中数判定（0 / 1 / 多域歧义 → null，红线 #4）。
/// 说明：CtpController 的 403/400/501 映射与模型绑定 422 属 Web 层，由 4号位 verify-acceptance 覆盖。
/// </summary>
public class CtpEvaluateTests
{
    private static Mock<DatabaseConnectionManager> CreateConnectionMock()
    {
        var options = new DatabaseOptions
        {
            APS = new DatabaseConnectionOptions { ConnectionString = "Server=localhost;Database=APS_Production;" },
            ODS = new DatabaseConnectionOptions { ConnectionString = "Server=localhost;Database=MES_Integration;" },
            Auth = new DatabaseConnectionOptions { ConnectionString = "Server=localhost;Database=APS_Auth;" },
        };
        return new Mock<DatabaseConnectionManager>(Options.Create(options));
    }

    private static CtpEvaluateRequest Request(string? domainKey = null) => new()
    {
        OrderCanonicalId = "ORD-001",
        MaterialCode = "M-100",
        Quantity = 10,
        FactoryCode = "F1",
        RequestedDueDate = "2026-10-15",
        CustomerCode = null,
        Purpose = "CTP",
        DomainKey = domainKey,
    };

    // ---------- CtpService ----------

    [Fact]
    public async Task EvaluateAsync_ExplicitDomainKey_SkipsResolve_AndChecksScope()
    {
        var resolver = new Mock<IDomainResolver>();
        var scope = new Mock<IDataScopeService>();
        var service = new CtpService(resolver.Object, scope.Object, Mock.Of<ILogger<CtpService>>());

        var result = await service.EvaluateAsync(Request("FAMILY_INJECTION"), 42);

        result.Should().Be("FAMILY_INJECTION");
        resolver.Verify(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        scope.Verify(s => s.EnsureInScopeAsync(42, DataScopeTypes.Domain, "FAMILY_INJECTION", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_MissingDomainKey_Resolves_AndChecksScope()
    {
        var resolver = new Mock<IDomainResolver>();
        resolver.Setup(r => r.ResolveAsync("M-100", "F1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("BJ_FAMILY_INJECTION");
        var scope = new Mock<IDataScopeService>();
        var service = new CtpService(resolver.Object, scope.Object, Mock.Of<ILogger<CtpService>>());

        var result = await service.EvaluateAsync(Request(null), 7);

        result.Should().Be("BJ_FAMILY_INJECTION");
        resolver.Verify(r => r.ResolveAsync("M-100", "F1", It.IsAny<CancellationToken>()), Times.Once);
        scope.Verify(s => s.EnsureInScopeAsync(7, DataScopeTypes.Domain, "BJ_FAMILY_INJECTION", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_ResolveReturnsNull_ThrowsInvalidOperation_NotResolved()
    {
        var resolver = new Mock<IDomainResolver>();
        resolver.Setup(r => r.ResolveAsync("M-100", "F1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var scope = new Mock<IDataScopeService>();
        var service = new CtpService(resolver.Object, scope.Object, Mock.Of<ILogger<CtpService>>());

        var act = async () => await service.EvaluateAsync(Request(null), 7);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*无法自动确定 DomainKey*");
        scope.Verify(s => s.EnsureInScopeAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_ScopeViolation_Propagates()
    {
        var resolver = new Mock<IDomainResolver>();
        var scope = new Mock<IDataScopeService>();
        scope.Setup(s => s.EnsureInScopeAsync(1, DataScopeTypes.Domain, "FAMILY_INJECTION", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ScopeViolationException(DataScopeTypes.Domain, "FAMILY_INJECTION"));
        var service = new CtpService(resolver.Object, scope.Object, Mock.Of<ILogger<CtpService>>());

        var act = async () => await service.EvaluateAsync(Request("FAMILY_INJECTION"), 1);

        await act.Should().ThrowAsync<ScopeViolationException>();
    }

    [Fact]
    public void CtpService_Constructor_NullGuards()
    {
        var scope = new Mock<IDataScopeService>().Object;
        var logger = Mock.Of<ILogger<CtpService>>();

        FluentActions.Invoking(() => new CtpService(null!, scope, logger))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new CtpService(new Mock<IDomainResolver>().Object, null!, logger))
            .Should().Throw<ArgumentNullException>();
    }

    // ---------- DomainResolver ----------

    [Fact]
    public async Task ResolveAsync_EmptyMaterialOrFactory_ReturnsNull()
    {
        var connection = CreateConnectionMock();
        var resolver = new DomainResolver(connection.Object, Mock.Of<ILogger<DomainResolver>>());

        (await resolver.ResolveAsync("", "F1")).Should().BeNull();
        (await resolver.ResolveAsync("M-100", " ")).Should().BeNull();
        (await resolver.ResolveAsync("M-100", null!)).Should().BeNull();
        connection.Verify(c => c.QueryAsync<string>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_SingleMatch_ReturnsDomainKey()
    {
        var connection = CreateConnectionMock();
        connection.Setup(c => c.QueryAsync<string>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string> { "FAMILY_INJECTION" });
        var resolver = new DomainResolver(connection.Object, Mock.Of<ILogger<DomainResolver>>());

        var result = await resolver.ResolveAsync("M-100", "F1");

        result.Should().Be("FAMILY_INJECTION");
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsNull()
    {
        var connection = CreateConnectionMock();
        connection.Setup(c => c.QueryAsync<string>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string>());
        var resolver = new DomainResolver(connection.Object, Mock.Of<ILogger<DomainResolver>>());

        var result = await resolver.ResolveAsync("M-100", "F1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_MultipleMatches_ReturnsNull_NoBlindFirst()
    {
        // 红线 #4：物料映射多域歧义 → null（不盲目 .First()），调用方转 400
        var connection = CreateConnectionMock();
        connection.Setup(c => c.QueryAsync<string>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<string> { "FAMILY_A", "FAMILY_B" });
        var resolver = new DomainResolver(connection.Object, Mock.Of<ILogger<DomainResolver>>());

        var result = await resolver.ResolveAsync("M-100", "F1");

        result.Should().BeNull();
    }
}
