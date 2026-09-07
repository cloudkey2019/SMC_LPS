using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using StrategyProfileVersion = LPS.APS.Core.Entities.APS.StrategyProfileVersion;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 3-4 联调 G3/G5 测试（2026-08-29）：
/// - G3：当前生效 PUBLISHED 版本便捷查询（C2）——生效窗口过滤、0/1/多 三态判定（多候选报错收敛不随机取，红线 #4）、
///       RuleSet/ParameterSet 返回前投影主题 JSON（与详情 A3/A10 一致）；
/// - G5：策略包版本 Diff（D3）——治理字段 + 引用字段对比（策略包版本表无 Remarks 列，故不含 Remarks）。
/// </summary>
public class PublishedVersionAndStrategyProfileDiffTests
{
    private readonly Mock<IRuleSetVersionRepository> _ruleSetRepo = new();
    private readonly Mock<IParameterSetVersionRepository> _parameterSetRepo = new();
    private readonly Mock<IStrategyProfileRepository> _strategyProfileRepo = new();
    private readonly Mock<IStrategyProfileVersionRepository> _strategyProfileVersionRepo = new();
    private readonly Mock<IGovernanceAuditLogRepository> _auditRepo = new();
    private readonly GovernanceVersionService _service;

    public PublishedVersionAndStrategyProfileDiffTests()
    {
        _service = new GovernanceVersionService(
            _ruleSetRepo.Object,
            _parameterSetRepo.Object,
            _strategyProfileRepo.Object,
            _strategyProfileVersionRepo.Object,
            _auditRepo.Object);
    }

    // ==================== G3：规则集当前生效 PUBLISHED 版本 ====================

    [Fact]
    public async Task GetPublishedRuleSet_恰一个生效PUBLISHED_返回并投影DemandPriorityJson()
    {
        // Arrange —— 单候选：PUBLISHED + 生效窗口覆盖当前时刻
        var versions = new List<RuleSetVersion>
        {
            new()
            {
                Id = 1, RuleSetId = 10, VersionCode = "V1",
                Status = GovernanceVersionStatus.Published,
                EffectiveFrom = DateTime.UtcNow.AddDays(-1),
                EffectiveTo = DateTime.UtcNow.AddDays(1),
                ContentSnapshotJson = SnapshotWithDemandPriority()
            }
        };
        _ruleSetRepo.Setup(r => r.GetByRuleSetIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

        // Act
        var result = await _service.GetPublishedRuleSetVersionAsync(10, CancellationToken.None);

        // Assert —— 返回版本且 DemandPriorityJson 从快照投影（前端与 A3 详情一致）
        result.Should().NotBeNull();
        result!.Id.Should().Be(1);
        result.DemandPriorityJson.Should().NotBeNullOrWhiteSpace();
        result.DemandPriorityJson!.Contains("Delayed_SO", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public async Task GetPublishedRuleSet_无PUBLISHED版本_返回null()
    {
        // Arrange —— 全部 DRAFT
        var versions = new List<RuleSetVersion>
        {
            new() { Id = 1, RuleSetId = 10, VersionCode = "V1", Status = GovernanceVersionStatus.Draft }
        };
        _ruleSetRepo.Setup(r => r.GetByRuleSetIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

        // Act
        var result = await _service.GetPublishedRuleSetVersionAsync(10, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPublishedRuleSet_多个PUBLISHED均生效_报歧义不随机取()
    {
        // Arrange —— 两个 PUBLISHED 且窗口均覆盖当前时刻（R01 允许历史版本保持 PUBLISHED，须收敛）
        var versions = new List<RuleSetVersion>
        {
            new() { Id = 1, RuleSetId = 10, VersionCode = "V1", Status = GovernanceVersionStatus.Published },
            new() { Id = 2, RuleSetId = 10, VersionCode = "V2", Status = GovernanceVersionStatus.Published }
        };
        _ruleSetRepo.Setup(r => r.GetByRuleSetIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

        // Act
        var act = () => _service.GetPublishedRuleSetVersionAsync(10, CancellationToken.None);

        // Assert —— 多候选报错收敛，绝不 First() 随机取（红线 #4）
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*多个当前生效 PUBLISHED 版本*");
    }

    [Fact]
    public async Task GetPublishedRuleSet_生效窗口过滤_已过期PUBLISHED被排除()
    {
        // Arrange —— 两个 PUBLISHED，仅 V2 生效窗口覆盖当前（V1 已过期）
        var versions = new List<RuleSetVersion>
        {
            new()
            {
                Id = 1, RuleSetId = 10, VersionCode = "V1",
                Status = GovernanceVersionStatus.Published,
                EffectiveTo = DateTime.UtcNow.AddDays(-1)     // 已过期
            },
            new()
            {
                Id = 2, RuleSetId = 10, VersionCode = "V2",
                Status = GovernanceVersionStatus.Published,
                EffectiveFrom = DateTime.UtcNow.AddDays(-1),
                EffectiveTo = DateTime.UtcNow.AddDays(1)
            }
        };
        _ruleSetRepo.Setup(r => r.GetByRuleSetIdAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

        // Act
        var result = await _service.GetPublishedRuleSetVersionAsync(10, CancellationToken.None);

        // Assert —— 只返回窗口内的 V2
        result.Should().NotBeNull();
        result!.Id.Should().Be(2);
    }

    // ==================== G3：参数集当前生效 PUBLISHED 版本 ====================

    [Fact]
    public async Task GetPublishedParameterSet_返回并投影LockJson()
    {
        // Arrange
        var versions = new List<ParameterSetVersion>
        {
            new()
            {
                Id = 5, ParameterSetId = 20, VersionCode = "P1",
                Status = GovernanceVersionStatus.Published,
                ContentSnapshotJson = SnapshotWithLock()
            }
        };
        _parameterSetRepo.Setup(r => r.GetByParameterSetIdAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

        // Act
        var result = await _service.GetPublishedParameterSetVersionAsync(20, CancellationToken.None);

        // Assert —— LockJson 从快照投影（与 A10 详情一致）
        result.Should().NotBeNull();
        result!.LockJson.Should().NotBeNullOrWhiteSpace();
        result.LockJson!.Contains("ProtectDelayed", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    // ==================== G3：策略包当前生效 PUBLISHED 版本 ====================

    [Fact]
    public async Task GetPublishedStrategyProfile_恰一个生效PUBLISHED_返回()
    {
        // Arrange
        var versions = new List<StrategyProfileVersion>
        {
            new()
            {
                Id = 7, StrategyProfileId = 30, VersionCode = "S1",
                Status = GovernanceVersionStatus.Published,
                RuleSetVersionId = 1, ParameterSetVersionId = 5
            }
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByStrategyProfileIdAsync(30, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

        // Act
        var result = await _service.GetPublishedStrategyProfileVersionAsync(30, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(7);
        result.RuleSetVersionId.Should().Be(1);
    }

    [Fact]
    public async Task GetPublishedStrategyProfile_多个PUBLISHED均生效_报歧义()
    {
        // Arrange
        var versions = new List<StrategyProfileVersion>
        {
            new() { Id = 1, StrategyProfileId = 30, VersionCode = "S1", Status = GovernanceVersionStatus.Published },
            new() { Id = 2, StrategyProfileId = 30, VersionCode = "S2", Status = GovernanceVersionStatus.Published }
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByStrategyProfileIdAsync(30, It.IsAny<CancellationToken>())).ReturnsAsync(versions);

        // Act
        var act = () => _service.GetPublishedStrategyProfileVersionAsync(30, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ==================== G5：策略包版本 Diff ====================

    [Fact]
    public async Task CompareStrategyProfile_不同引用与默认标志_返回IsChanged差异()
    {
        // Arrange
        var source = new StrategyProfileVersion
        {
            Id = 1, StrategyProfileId = 30, VersionCode = "S1",
            Status = GovernanceVersionStatus.Published,
            RuleSetVersionId = 100, ParameterSetVersionId = 200,
            IsDefault = true
        };
        var target = new StrategyProfileVersion
        {
            Id = 2, StrategyProfileId = 30, VersionCode = "S2",
            Status = GovernanceVersionStatus.Draft,
            RuleSetVersionId = 101, ParameterSetVersionId = 200,
            IsDefault = false
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        // Act
        var result = await _service.CompareStrategyProfileVersionsAsync(1, 2, CancellationToken.None);

        // Assert —— EntityType 与变化字段
        result.EntityType.Should().Be("StrategyProfileVersion");
        result.SourceVersionCode.Should().Be("S1");
        result.TargetVersionCode.Should().Be("S2");
        result.FieldDiffs.Single(f => f.FieldName == "RuleSetVersionId").IsChanged.Should().BeTrue();
        result.FieldDiffs.Single(f => f.FieldName == "ParameterSetVersionId").IsChanged.Should().BeFalse();
        result.FieldDiffs.Single(f => f.FieldName == "IsDefault").IsChanged.Should().BeTrue();
        result.FieldDiffs.Single(f => f.FieldName == "Status").IsChanged.Should().BeTrue();
    }

    [Fact]
    public async Task CompareStrategyProfile_源版本不存在_抛异常()
    {
        // Arrange
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync((StrategyProfileVersion?)null);

        // Act
        var act = () => _service.CompareStrategyProfileVersionsAsync(1, 2, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*源策略包版本不存在*");
    }

    [Fact]
    public async Task CompareStrategyProfile_同一版本_全部IsChanged为false()
    {
        // Arrange —— source==target 合法（D1/D3 语义：全 IsChanged=false）
        var version = new StrategyProfileVersion
        {
            Id = 9, StrategyProfileId = 30, VersionCode = "S9",
            Status = GovernanceVersionStatus.Published,
            RuleSetVersionId = 100, ParameterSetVersionId = 200
        };
        _strategyProfileVersionRepo.Setup(r => r.GetByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var result = await _service.CompareStrategyProfileVersionsAsync(9, 9, CancellationToken.None);

        // Assert
        result.FieldDiffs.Should().OnlyContain(f => !f.IsChanged);
    }

    // ==================== 辅助 ====================

    private static string SnapshotWithDemandPriority()
    {
        return """{"DemandPriority":{"Segments":[{"SegmentOrder":1,"SegmentName":"Delayed_SO","IsEnabled":true,"MatchConditions":[],"SortFields":[],"StableTieBreakFields":[]}]}}""";
    }

    private static string SnapshotWithLock()
    {
        return """{"Lock":{"Trigger":{"UseRemainingTimeThreshold":false,"ProtectDelayed":true},"Sticky":{}}}""";
    }
}
