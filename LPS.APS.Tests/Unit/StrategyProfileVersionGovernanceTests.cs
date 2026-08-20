using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using StrategyProfileVersion = LPS.APS.Core.Entities.APS.StrategyProfileVersion;
using StrategyProfile = LPS.APS.Core.Entities.APS.StrategyProfile;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// R03 验收：StrategyProfileVersion 治理完整闭环（P0-06）
/// 覆盖：发布前校验（引用合法/有效期/默认歧义）、发布（IsDefault 清旧置新）、
///       当前有效默认解析（RunType 匹配 + 生效窗口 + 歧义报错）、Run 引用追溯。
/// 跨号位冻结语义：新 Run 无显式 StrategyProfileVersionId 时必须得到唯一无歧义 PUBLISHED 默认；歧义报配置错误不随机取。
/// </summary>
public class StrategyProfileVersionGovernanceTests
{
    private readonly Mock<IRuleSetVersionRepository> _ruleSetRepo = new();
    private readonly Mock<IParameterSetVersionRepository> _parameterSetRepo = new();
    private readonly Mock<IStrategyProfileRepository> _strategyProfileRepo = new();
    private readonly Mock<IStrategyProfileVersionRepository> _strategyProfileVersionRepo = new();
    private readonly Mock<IGovernanceAuditLogRepository> _auditRepo = new();
    private readonly GovernanceVersionService _service;

    public StrategyProfileVersionGovernanceTests()
    {
        _service = new GovernanceVersionService(
            _ruleSetRepo.Object,
            _parameterSetRepo.Object,
            _strategyProfileRepo.Object,
            _strategyProfileVersionRepo.Object,
            _auditRepo.Object);
    }

    /// <summary>已发布合法引用（RuleSet/ParameterSet 均 PUBLISHED），供合法场景复用</summary>
    private void SetupValidReferences(long ruleSetVersionId, long parameterSetVersionId)
    {
        _ruleSetRepo
            .Setup(r => r.GetByIdAsync(ruleSetVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuleSetVersion { Id = ruleSetVersionId, VersionCode = "R1", Status = GovernanceVersionStatus.Published });
        _parameterSetRepo
            .Setup(r => r.GetByIdAsync(parameterSetVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParameterSetVersion { Id = parameterSetVersionId, VersionCode = "P1", Status = GovernanceVersionStatus.Published });
    }

    [Fact]
    public async Task Validate_引用规则集不存在_返回REF_NOT_FOUND()
    {
        // Arrange
        var version = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1",
            RuleSetVersionId = 999, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Draft,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _ruleSetRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((RuleSetVersion?)null);

        // Act
        var result = await _service.ValidateStrategyProfileVersionForPublishAsync(1, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "REF_NOT_FOUND");
    }

    [Fact]
    public async Task Validate_引用规则集未发布_返回REF_NOT_PUBLISHED()
    {
        // Arrange
        var version = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1",
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Draft,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _ruleSetRepo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuleSetVersion { Id = 100, VersionCode = "R1", Status = GovernanceVersionStatus.Draft });

        // Act
        var result = await _service.ValidateStrategyProfileVersionForPublishAsync(1, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "REF_NOT_PUBLISHED");
    }

    [Fact]
    public async Task Validate_IsDefault与同Profile已有默认冲突_返回DEFAULT_CONFLICT()
    {
        // Arrange
        var version = new StrategyProfileVersion
        {
            Id = 2, StrategyProfileId = 10, VersionCode = "V2", IsDefault = true,
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Draft,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _strategyProfileVersionRepo.Setup(r => r.GetDefaultByStrategyProfileIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrategyProfileVersion { Id = 1, VersionCode = "V1", Status = GovernanceVersionStatus.Published });
        SetupValidReferences(100, 200);

        // Act
        var result = await _service.ValidateStrategyProfileVersionForPublishAsync(2, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "DEFAULT_CONFLICT");
    }

    [Fact]
    public async Task Validate_合法版本_校验通过()
    {
        // Arrange
        var version = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1",
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Draft,
            EffectiveFrom = new DateTime(2026, 1, 1),
            EffectiveTo = new DateTime(2026, 12, 31),
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        SetupValidReferences(100, 200);

        // Act
        var result = await _service.ValidateStrategyProfileVersionForPublishAsync(1, CancellationToken.None);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Publish_DraftVersion_StatusBecomesPublished()
    {
        // Arrange
        var version = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1",
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Draft,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        SetupValidReferences(100, 200);

        // Act
        await _service.PublishStrategyProfileVersionAsync(1, "tester", CancellationToken.None);

        // Assert
        version.Status.Should().Be(GovernanceVersionStatus.Published);
        version.PublishedAt.Should().NotBeNull();
        _strategyProfileVersionRepo.Verify(r => r.UpdateAsync(version, It.IsAny<CancellationToken>()), Times.Once);
        _auditRepo.Verify(r => r.AddAsync(It.IsAny<LPS.APS.Core.Entities.Auth.GovernanceAuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_IsDefault版本_先清同Profile其他默认()
    {
        // Arrange —— P0-06：IsDefault=1 发布时先 ClearDefaultFlag 避免 UQ_StrategyProfileVersion_DefaultPublished 冲突
        var version = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1", IsDefault = true,
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Approved,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _strategyProfileVersionRepo.Setup(r => r.GetDefaultByStrategyProfileIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StrategyProfileVersion?)null);
        SetupValidReferences(100, 200);

        // Act
        await _service.PublishStrategyProfileVersionAsync(1, "tester", CancellationToken.None);

        // Assert —— 先清旧默认再更新
        _strategyProfileVersionRepo.Verify(r => r.ClearDefaultFlagAsync(10, 1, It.IsAny<CancellationToken>()), Times.Once);
        version.Status.Should().Be(GovernanceVersionStatus.Published);
    }

    [Fact]
    public async Task Publish_AlreadyPublishedVersion_ThrowsInvalidOperation()
    {
        // Arrange —— 历史不可覆盖（R01 语义延伸至策略包）
        var version = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1",
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Published,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        SetupValidReferences(100, 200);

        // Act
        var act = () => _service.PublishStrategyProfileVersionAsync(1, "tester", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _strategyProfileVersionRepo.Verify(r => r.UpdateAsync(It.IsAny<StrategyProfileVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_单候选_返回该版本()
    {
        // Arrange
        var candidate = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1",
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Published, IsDefault = true,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetDefaultByRunTypeAsync(StrategyProfileRunType.FullSchedule, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyProfileVersion> { candidate });

        // Act
        var result = await _service.ResolveDefaultStrategyProfileVersionAsync(StrategyProfileRunType.FullSchedule, ct: CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
    }

    [Fact]
    public async Task Resolve_无候选_返回Null()
    {
        // Arrange
        _strategyProfileVersionRepo.Setup(r => r.GetDefaultByRunTypeAsync(StrategyProfileRunType.Simulation, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyProfileVersion>());

        // Act
        var result = await _service.ResolveDefaultStrategyProfileVersionAsync(StrategyProfileRunType.Simulation, ct: CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_多候选_抛歧义异常()
    {
        // Arrange —— 跨号位冻结语义：歧义报配置错误，不随机取一个
        var c1 = new StrategyProfileVersion { Id = 1, StrategyProfileId = 10, VersionCode = "V1" };
        var c2 = new StrategyProfileVersion { Id = 2, StrategyProfileId = 11, VersionCode = "V1" };
        _strategyProfileVersionRepo.Setup(r => r.GetDefaultByRunTypeAsync(StrategyProfileRunType.FullSchedule, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyProfileVersion> { c1, c2 });

        // Act
        var act = () => _service.ResolveDefaultStrategyProfileVersionAsync(StrategyProfileRunType.FullSchedule, ct: CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*存在歧义*");
    }

    [Fact]
    public async Task Resolve_生效窗口过滤_窗口外不入选()
    {
        // Arrange —— asOf=2026-06-01，c1 在窗口内、c2 已过期 → 恰 1 个
        var c1 = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1",
            EffectiveFrom = new DateTime(2026, 1, 1), EffectiveTo = new DateTime(2026, 12, 31),
        };
        var c2 = new StrategyProfileVersion
        {
            Id = 2, StrategyProfileId = 11, VersionCode = "V1",
            EffectiveFrom = new DateTime(2025, 1, 1), EffectiveTo = new DateTime(2025, 6, 30),
        };
        _strategyProfileVersionRepo.Setup(r => r.GetDefaultByRunTypeAsync(StrategyProfileRunType.FullSchedule, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyProfileVersion> { c1, c2 });

        // Act
        var result = await _service.ResolveDefaultStrategyProfileVersionAsync(
            StrategyProfileRunType.FullSchedule, new DateTime(2026, 6, 1), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
    }

    [Fact]
    public async Task GetTrace_组装完整追溯链路()
    {
        // Arrange
        var version = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 10, VersionCode = "V1", IsDefault = true,
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            Status = GovernanceVersionStatus.Published,
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);
        _strategyProfileRepo.Setup(r => r.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrategyProfile { Id = 10, StrategyProfileCode = "SP-FULL", RunType = StrategyProfileRunType.FullSchedule });
        _ruleSetRepo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RuleSetVersion { Id = 100, VersionCode = "R1" });
        _parameterSetRepo.Setup(r => r.GetByIdAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParameterSetVersion { Id = 200, VersionCode = "P1" });

        // Act
        var trace = await _service.GetRunStrategyProfileTraceAsync(1, CancellationToken.None);

        // Assert
        trace.StrategyProfileVersionId.Should().Be(1);
        trace.StrategyProfileCode.Should().Be("SP-FULL");
        trace.RunType.Should().Be(StrategyProfileRunType.FullSchedule);
        trace.RuleSetVersionCode.Should().Be("R1");
        trace.ParameterSetVersionCode.Should().Be("P1");
        trace.IsDefault.Should().BeTrue();
    }
}
