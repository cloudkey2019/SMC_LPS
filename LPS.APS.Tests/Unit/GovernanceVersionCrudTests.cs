using FluentAssertions;
using LPS.APS.Application.Services;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 第四轮复审 P0-01/P0-02 收口测试（2026-08-24）：
/// - P0-02：CRUD 状态机不可绕过——Create 强制 DRAFT（绕过测试）、Update 已发布/失效/归档拒绝、
///          入参 Status 越权被冻结、Publish 为唯一转 PUBLISHED 路径；
/// - P0-01：ContentSnapshotJson 为唯一内容真相——Validate 基于快照子块（真实 DB 仅快照场景）、
///          Create/Update 归一化到快照、Diff 基于快照子块、GET 投影回主题 JSON 前端兼容。
/// </summary>
public class GovernanceVersionCrudTests
{
    /// <summary>操作人（P1-04 审计 UserId 断言基准）</summary>
    private const int ActorUserId = 7;

    private readonly Mock<IRuleSetVersionRepository> _ruleSetRepo = new();
    private readonly Mock<IParameterSetVersionRepository> _paramRepo = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly GovernanceVersionService _service;

    public GovernanceVersionCrudTests()
    {
        _service = new GovernanceVersionService(
            _ruleSetRepo.Object,
            _paramRepo.Object,
            Mock.Of<IStrategyProfileRepository>(),
            Mock.Of<IStrategyProfileVersionRepository>(),
            _auditRepo.Object);
    }

    // ==================== P0-02：CRUD 状态机绕过测试 ====================

    [Fact]
    public async Task CreateRuleSet_入参Status为PUBLISHED_强制落为DRAFT()
    {
        // Arrange —— 绕过路径 1：Create 请求伪造 Status=PUBLISHED，期望被强制覆盖为 DRAFT
        var request = new RuleSetVersion
        {
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
            PublishedAt = DateTime.Now,   // 伪造治理字段
            DemandPriorityJson = ValidDemandPriorityJson()
        };
        _ruleSetRepo.Setup(r => r.AddAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuleSetVersion v, CancellationToken _) => v);

        // Act
        var created = await _service.CreateRuleSetVersionAsync(request, "creator", ActorUserId);

        // Assert —— Create 不可直提 PUBLISHED，治理字段置空
        created.Status.Should().Be(GovernanceVersionStatus.Draft);
        created.PublishedAt.Should().BeNull();
        created.PublishedBy.Should().BeNull();
        created.ApprovedAt.Should().BeNull();
        created.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task UpdateRuleSet_已发布版本_拒绝原地修改()
    {
        // Arrange —— 绕过路径 2：已 PUBLISHED 版本 Update 原地修改（历史不可变，R01）
        var existing = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published,
            ContentSnapshotJson = SnapshotWithDemandPriority("已发布快照")
        };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var update = new RuleSetVersion { Id = 1, VersionCode = "V1-篡改" };

        // Act
        var act = () => _service.UpdateRuleSetVersionAsync(1, update, ActorUserId, default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _ruleSetRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRuleSet_入参Status为PUBLISHED_冻结保持现有状态()
    {
        // Arrange —— 绕过路径 3：DRAFT 版本 Update 时越权改 Status=PUBLISHED，期望被冻结保持 DRAFT
        var existing = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Draft,
            ContentSnapshotJson = SnapshotWithDemandPriority("基线")
        };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        RuleSetVersion? captured = null;
        _ruleSetRepo.Setup(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()))
            .Callback<RuleSetVersion, CancellationToken>((v, _) => captured = v)
            .Returns(Task.CompletedTask);

        var update = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 99,                       // 伪造归属
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Published, // 越权改状态
            DemandPriorityJson = ValidDemandPriorityJson()
        };

        // Act
        await _service.UpdateRuleSetVersionAsync(1, update, ActorUserId, default);

        // Assert —— 落库实体 Status/RuleSetId 均以现有记录为准
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(GovernanceVersionStatus.Draft);
        captured.RuleSetId.Should().Be(10);
        captured.ContentSnapshotJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UpdateRuleSet_DISABLED版本_拒绝修改()
    {
        // Arrange —— 绕过路径 4：失效版本不可再改
        var existing = new RuleSetVersion { Id = 1, RuleSetId = 10, VersionCode = "V1", Status = GovernanceVersionStatus.Disabled };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        // Act
        var act = () => _service.UpdateRuleSetVersionAsync(1, new RuleSetVersion { Id = 1 }, ActorUserId, default);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ==================== P0-01：ContentSnapshotJson 唯一内容真相 ====================

    [Fact]
    public async Task ValidateRuleSet_仅快照无主题JSON_基于快照子块校验通过()
    {
        // 真实 DB 场景：主题 JSON 列不存在，只有 ContentSnapshotJson——Validate 必须基于快照子块读到内容
        var version = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Draft,
            ContentSnapshotJson = SnapshotWithDemandPriority("真实快照")
        };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var result = await _service.ValidateRuleSetVersionForPublishAsync(1, default);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CreateParameterSet_五主题JSON_归一化写入ContentSnapshotJson()
    {
        // Arrange —— DRAFT 编辑的五主题 JSON（内存/API 字段）必须归一化到 ContentSnapshotJson 落库
        var request = new ParameterSetVersion
        {
            ParameterSetId = 20,
            VersionCode = "P1",
            LockJson = "{\"Trigger\":{}}",
            SupplyJson = "{\"Inventory\":{}}",
            ProcurementJson = "{\"PlanningYields\":[],\"DefaultPurchaseLt\":[],\"ArrivalToUsableOffsets\":[],\"OverdueMargin\":{}}",
            SolverStrategyJson = "{}",
            CandidateGuardrailJson = "{}"
        };
        ParameterSetVersion? captured = null;
        _paramRepo.Setup(r => r.AddAsync(It.IsAny<ParameterSetVersion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParameterSetVersion v, CancellationToken _) => { captured = v; return v; });

        // Act
        var created = await _service.CreateParameterSetVersionAsync(request, "creator", ActorUserId);

        // Assert —— 落库实体 ContentSnapshotJson 含五子块，Status 强制 DRAFT
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(GovernanceVersionStatus.Draft);
        using var doc = System.Text.Json.JsonDocument.Parse(captured.ContentSnapshotJson!);
        doc.RootElement.TryGetProperty("Lock", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Supply", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Procurement", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("SolverStrategy", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("CandidateGuardrail", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateParameterSet_补齐内容_快照重建含新子块()
    {
        // Arrange —— DB 已有仅 Lock 的快照；Update 补齐 Supply（主题 JSON 非空触发重建，避免"快照已存在不更新"断链）
        var existing = new ParameterSetVersion
        {
            Id = 2,
            ParameterSetId = 20,
            VersionCode = "P1",
            Status = GovernanceVersionStatus.Draft,
            ContentSnapshotJson = "{\"Lock\":{\"Trigger\":{}}}"
        };
        _paramRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        ParameterSetVersion? captured = null;
        _paramRepo.Setup(r => r.UpdateAsync(It.IsAny<ParameterSetVersion>(), It.IsAny<CancellationToken>()))
            .Callback<ParameterSetVersion, CancellationToken>((v, _) => captured = v)
            .Returns(Task.CompletedTask);

        var update = new ParameterSetVersion
        {
            Id = 2,
            ParameterSetId = 20,
            VersionCode = "P1",
            LockJson = "{\"Trigger\":{}}",
            SupplyJson = "{\"Inventory\":{}}"
        };

        // Act
        await _service.UpdateParameterSetVersionAsync(2, update, ActorUserId, default);

        // Assert —— 补齐的 Supply 进入重建快照；Status 冻结
        captured.Should().NotBeNull();
        using var doc = System.Text.Json.JsonDocument.Parse(captured!.ContentSnapshotJson!);
        doc.RootElement.TryGetProperty("Lock", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Supply", out _).Should().BeTrue();
        captured.Status.Should().Be(GovernanceVersionStatus.Draft);
    }

    [Fact]
    public async Task CompareRuleSet_Diff基于ContentSnapshotJson子块()
    {
        // Arrange —— Diff 必须基于快照子块比较（P1-02），而非临时主题 JSON
        var source = new RuleSetVersion { Id = 1, RuleSetId = 10, VersionCode = "V1", ContentSnapshotJson = SnapshotWithDemandPriority("基线") };
        var target = new RuleSetVersion { Id = 2, RuleSetId = 10, VersionCode = "V2", ContentSnapshotJson = SnapshotWithDemandPriority("新版") };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        _ruleSetRepo.Setup(r => r.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(target);

        // Act
        var result = await _service.CompareRuleSetVersionsAsync(1, 2, default);

        // Assert
        var dpDiff = result.FieldDiffs.Single(d => d.FieldName == "DemandPriority");
        dpDiff.IsChanged.Should().BeTrue();
        dpDiff.SourceValue.Should().NotBeNull();
    }

    [Fact]
    public async Task GetRuleSet_快照投影回主题JSON_前端兼容()
    {
        // Arrange —— GET 详情须把 ContentSnapshotJson 投影回 DemandPriorityJson（前端 API 兼容，A2 投影）
        var version = new RuleSetVersion { Id = 1, RuleSetId = 10, VersionCode = "V1", ContentSnapshotJson = SnapshotWithDemandPriority("投影") };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        // Act
        var result = await _service.GetRuleSetVersionAsync(1, default);

        // Assert
        result.Should().NotBeNull();
        result!.DemandPriorityJson.Should().NotBeNullOrWhiteSpace();
    }

    // ==================== P1-04：Create/Update 审计覆盖（补审计+预检反证） ====================

    [Fact]
    public async Task CreateRuleSet_审计写入_ActionCode为Create_UserId为操作人()
    {
        // Arrange —— DRAFT 版本创建（P0-02 状态机同 CreateRuleSet_入参Status为PUBLISHED 首测装配）
        var request = new RuleSetVersion
        {
            RuleSetId = 10,
            VersionCode = "V1",
            DemandPriorityJson = ValidDemandPriorityJson()
        };
        _ruleSetRepo.Setup(r => r.AddAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RuleSetVersion v, CancellationToken _) => { v.Id = 100; return v; });
        Core.Entities.Auth.AuditLog? captured = null;
        _auditRepo.Setup(a => a.AddAsync(It.IsAny<Core.Entities.Auth.AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<Core.Entities.Auth.AuditLog, CancellationToken>((l, _) => captured = l)
            .ReturnsAsync(new Core.Entities.Auth.AuditLog());

        // Act
        await _service.CreateRuleSetVersionAsync(request, "creator", ActorUserId);

        // Assert —— 审计记录 Create + 操作人 UserId
        captured.Should().NotBeNull();
        captured!.ActionCode.Should().Be("Create");
        captured.EntityType.Should().Be("RuleSetVersion");
        captured.EntityId.Should().Be("100");
        captured.UserId.Should().Be(ActorUserId);
        captured.UserCode.Should().Be("creator");
    }

    [Fact]
    public async Task UpdateRuleSet_审计写入_ActionCode为Update_OldValue为旧版本码()
    {
        // Arrange —— DRAFT 现有版本 + 更新（快照基线）
        var existing = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Draft,
            ContentSnapshotJson = SnapshotWithDemandPriority("基线")
        };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        Core.Entities.Auth.AuditLog? captured = null;
        _auditRepo.Setup(a => a.AddAsync(It.IsAny<Core.Entities.Auth.AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<Core.Entities.Auth.AuditLog, CancellationToken>((l, _) => captured = l)
            .ReturnsAsync(new Core.Entities.Auth.AuditLog());

        var update = new RuleSetVersion { Id = 1, VersionCode = "V2", DemandPriorityJson = ValidDemandPriorityJson() };

        // Act
        await _service.UpdateRuleSetVersionAsync(1, update, ActorUserId);

        // Assert —— 审计记录 Update + OldValue=旧版本码 + 操作人
        captured.Should().NotBeNull();
        captured!.ActionCode.Should().Be("Update");
        captured.EntityType.Should().Be("RuleSetVersion");
        captured.OldValue.Should().Be("V1");
        captured.NewValue.Should().Be("V2");
        captured.UserId.Should().Be(ActorUserId);
    }

    [Fact]
    public async Task UpdateRuleSet_审计预检失败_业务零落库()
    {
        // Arrange —— EnsureWritableAsync 注入失败（P1-03 同模式：fail-before-write）
        var existing = new RuleSetVersion
        {
            Id = 1,
            RuleSetId = 10,
            VersionCode = "V1",
            Status = GovernanceVersionStatus.Draft,
            ContentSnapshotJson = SnapshotWithDemandPriority("基线")
        };
        _ruleSetRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _auditRepo.Setup(a => a.EnsureWritableAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("APS_Auth 审计库不可写（测试注入）"));
        var update = new RuleSetVersion { Id = 1, VersionCode = "V2", DemandPriorityJson = ValidDemandPriorityJson() };

        // Act
        var act = () => _service.UpdateRuleSetVersionAsync(1, update, ActorUserId);

        // Assert —— 预检失败 → 业务零落库（UpdateAsync Never）
        await act.Should().ThrowAsync<InvalidOperationException>();
        _ruleSetRepo.Verify(r => r.UpdateAsync(It.IsAny<RuleSetVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ==================== 测试装配辅助 ====================

    /// <summary>合法 DemandPriorityBlock JSON（含一个 Segment，P0-05 校验通过基准）</summary>
    private static string ValidDemandPriorityJson() =>
        System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.DemandPriorityBlock
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
        });

    /// <summary>构造仅含 DemandPriority 子块的 ContentSnapshotJson（真实 DB 持久化载体形态）</summary>
    private static string SnapshotWithDemandPriority(string segmentName) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["DemandPriority"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(new LPS.APS.Core.Dto.DemandPriorityBlock
                {
                    Segments =
                    [
                        new LPS.APS.Core.Dto.PrioritySegment
                        {
                            SegmentOrder = 1,
                            SegmentName = segmentName,
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
                }))
        });
}
