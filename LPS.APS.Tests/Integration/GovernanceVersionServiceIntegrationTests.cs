using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Repositories.Auth;
using LPS.APS.Engine.Repositories.Governance;
using LPS.APS.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using System.Text.Json;
using System.Threading;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using StrategyProfileVersion = LPS.APS.Core.Entities.APS.StrategyProfileVersion;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// GovernanceVersionService 治理版本发布闭环集成测试（P0-05/P0-06/P0-07）
/// 测试范围（真实 APS_Production 库 + 真实仓储 + 真实服务编排）：
/// 1. P0-05 正式 Publish 强制发布前校验（坏配置/引用未发布一律拒绝，无绕过路径）
/// 2. P0-06 StrategyProfileVersion 治理闭环（校验→发布→默认解析→Run 引用追溯）
/// 3. P0-07 DemandPriorityValidator 与真实库存配置的集成校验
/// 依赖 APS_Auth 库（审计日志），库缺失时动态 Skip。
/// </summary>
/// <remarks>开发者：3号位</remarks>
[Collection("B5Cache")]
public class GovernanceVersionServiceIntegrationTests : IDisposable
{
    private readonly DatabaseConnectionManager _cm;
    private readonly GovernanceVersionService _service;
    private readonly FrozenStrategySnapshotProvider _snapshotProvider;
    private readonly RuleSetVersionRepository _ruleSetVersionRepo;
    private readonly ParameterSetVersionRepository _parameterSetVersionRepo;
    private readonly StrategyProfileVersionRepository _strategyProfileVersionRepo;
    private readonly IStrategyProfileRepository _strategyProfileRepo;

    private long _testStrategyProfileId;
    private long _testRuleSetId;
    private long _testParameterSetId;
    private long _testStrategyProfileVersionId;
    private long _testRuleSetVersionId;
    private long _testParameterSetVersionId;
    private readonly string _uniqueSuffix;
    private readonly DateTime _now;

    public GovernanceVersionServiceIntegrationTests()
    {
        _now = DateTime.UtcNow;
        _uniqueSuffix = $"{_now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".Substring(0, 30);

        _cm = TestEnvironment.GetConnectionManager();
        var loggerFactory = LoggerFactory.Create(builder => { });

        _ruleSetVersionRepo = new RuleSetVersionRepository(_cm, loggerFactory.CreateLogger<RuleSetVersionRepository>());
        _parameterSetVersionRepo = new ParameterSetVersionRepository(_cm, loggerFactory.CreateLogger<ParameterSetVersionRepository>());
        _strategyProfileVersionRepo = new StrategyProfileVersionRepository(_cm, loggerFactory.CreateLogger<StrategyProfileVersionRepository>());
        _strategyProfileRepo = new StrategyProfileRepository(_cm, loggerFactory.CreateLogger<StrategyProfileRepository>());

        var auditRepo = CreateAuditRepository(loggerFactory);

        _service = new GovernanceVersionService(
            _ruleSetVersionRepo,
            _parameterSetVersionRepo,
            _strategyProfileRepo,
            _strategyProfileVersionRepo,
            auditRepo);

        // P1-01 方案 A 端到端链：Snapshot 装配使用真实仓储（三版本级联），B-5 缓存进程级静态需隔离
        _snapshotProvider = new FrozenStrategySnapshotProvider(
            _strategyProfileVersionRepo,
            _ruleSetVersionRepo,
            _parameterSetVersionRepo);
        FrozenStrategySnapshotProvider.ClearCache();
    }

    /// <summary>构造审计仓储（Auth 库 EF Core；库不可达时构造成功、首次写入时失败→测试 Skip 条件先行探测）</summary>
    private static GovernanceAuditLogRepository CreateAuditRepository(ILoggerFactory loggerFactory)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build();
        var authConn = configuration.GetSection("Database:Auth:ConnectionString").Value ?? string.Empty;

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer(authConn)
            .Options;
        return new GovernanceAuditLogRepository(
            new AuthDbContext(options),
            loggerFactory.CreateLogger<GovernanceAuditLogRepository>());
    }

    [SkippableFact]
    public async Task 发布闭环_规则集参数集策略包全链路_校验发布解析追溯()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasContentSnapshotJsonColumn() || !TestEnvironment.HasGovernanceAuditLogTable(),
            "测试环境缺 APS_Auth 库、ContentSnapshotJson 列或 GovernanceAuditLog 表（方案 A/审计 DDL 未迁移），需 2号位部署 v5.1.2 后转绿");

        await SetupBaseVersionsAsync();

        // 校验：规则集/参数集 DRAFT 阶段可校验（基于 ContentSnapshotJson 子块）
        var ruleSetValidation = await _service.ValidateRuleSetVersionForPublishAsync(_testRuleSetVersionId);
        ruleSetValidation.IsValid.Should().BeTrue();
        var paramSetValidation = await _service.ValidateParameterSetVersionForPublishAsync(_testParameterSetVersionId);
        paramSetValidation.IsValid.Should().BeTrue();

        // 发布：规则集/参数集 DRAFT → PUBLISHED（策略包引用版本须先 PUBLISHED 才能通过 REF_NOT_PUBLISHED，P0-06）
        await _service.PublishRuleSetVersionAsync(_testRuleSetVersionId, "IntegrationTest");
        await _service.PublishParameterSetVersionAsync(_testParameterSetVersionId, "IntegrationTest");

        // 策略包：引用已 PUBLISHED → 校验通过 → 发布
        var spvValidation = await _service.ValidateStrategyProfileVersionForPublishAsync(_testStrategyProfileVersionId);
        spvValidation.IsValid.Should().BeTrue();
        await _service.PublishStrategyProfileVersionAsync(_testStrategyProfileVersionId, "IntegrationTest");

        var publishedRuleSet = await _ruleSetVersionRepo.GetByIdAsync(_testRuleSetVersionId);
        publishedRuleSet!.Status.Should().Be("PUBLISHED");
        var publishedSpv = await _strategyProfileVersionRepo.GetByIdAsync(_testStrategyProfileVersionId);
        publishedSpv!.Status.Should().Be("PUBLISHED");
        publishedSpv.IsDefault.Should().BeTrue();

        // 默认解析：RunType 命中唯一 PUBLISHED 默认版本
        // 注：测试用独立 RunType（LOCAL_RESCHEDULE），避免与生产种子 SP-DEMO-V2.0（FULL_SCHEDULE IsDefault=1）歧义
        var resolved = await _service.ResolveDefaultStrategyProfileVersionAsync("LOCAL_RESCHEDULE");
        resolved.Should().NotBeNull();
        resolved!.Id.Should().Be(_testStrategyProfileVersionId);

        // Run 引用追溯：版本维完整链（父包 + 规则集 + 参数集）
        var trace = await _service.GetRunStrategyProfileTraceAsync(_testStrategyProfileVersionId);
        trace.StrategyProfileVersionId.Should().Be(_testStrategyProfileVersionId);
        trace.StrategyProfileCode.Should().NotBeNullOrWhiteSpace();
        trace.RunType.Should().Be("LOCAL_RESCHEDULE");
        trace.RuleSetVersionId.Should().Be(_testRuleSetVersionId);
        trace.RuleSetVersionCode.Should().NotBeNullOrWhiteSpace();
        trace.ParameterSetVersionId.Should().Be(_testParameterSetVersionId);
        trace.ParameterSetVersionCode.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task 发布前校验_P005_坏配置版本被拒无绕过()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasContentSnapshotJsonColumn(),
            "测试环境缺 APS_Auth 库或 ContentSnapshotJson 列（方案 A 未迁移），需 2号位部署 v5.1.2 后转绿");

        await SetupBaseVersionsAsync();

        // 篡改为损坏的 ContentSnapshotJson（非法 JSON，P0-01：内容唯一载体为快照，校验/发布须基于快照读到内容失败）
        await _ruleSetVersionRepo.UpdateAsync(new RuleSetVersion
        {
            Id = _testRuleSetVersionId,
            RuleSetId = _testRuleSetId,
            VersionCode = $"TEST-R-{_uniqueSuffix}",
            Status = "DRAFT",
            ContentSnapshotJson = "{ not valid json",
        });

        // P0-05 无绕过路径：Validate 返回 Error，Publish 直接抛异常
        var validation = await _service.ValidateRuleSetVersionForPublishAsync(_testRuleSetVersionId);
        validation.IsValid.Should().BeFalse();

        var act = async () => await _service.PublishRuleSetVersionAsync(_testRuleSetVersionId, "IntegrationTest");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*发布前校验失败*");

        var after = await _ruleSetVersionRepo.GetByIdAsync(_testRuleSetVersionId);
        after!.Status.Should().Be("DRAFT"); // 拒绝后仍为 DRAFT，未被篡改为发布
    }

    [SkippableFact]
    public async Task 发布前校验_P006_引用未发布版本被拒()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasContentSnapshotJsonColumn(),
            "测试环境缺 APS_Auth 库或 ContentSnapshotJson 列（方案 A 未迁移），需 2号位部署 v5.1.2 后转绿");

        // 规则集/参数集保持 DRAFT（不发布）→ 策略包引用未发布版本应被拒
        await SetupBaseVersionsAsync();

        var validation = await _service.ValidateStrategyProfileVersionForPublishAsync(_testStrategyProfileVersionId);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Code == "REF_NOT_PUBLISHED");
    }

    [SkippableFact]
    public async Task 校验器集成_P007_真实库存合法配置通过()
    {
        // 不依赖 Auth 库（纯校验），但依赖真实库存配置写入 + 方案 A ContentSnapshotJson 列
        Skip.If(!TestEnvironment.HasContentSnapshotJsonColumn(), "测试库缺 ContentSnapshotJson 列（方案 A 未迁移），需 2号位 v5.1.2 迁移后转绿");

        await SetupBaseVersionsAsync();

        var ruleSetVersion = await _ruleSetVersionRepo.GetByIdAsync(_testRuleSetVersionId);
        var priorityBlock = LoadDemandPriorityBlock(ruleSetVersion!.ContentSnapshotJson!);

        var validator = new DemandPriorityValidator();
        var result = validator.Validate(priorityBlock!);

        result.IsValid.Should().BeTrue();
    }

    // ==================== P1-01 方案 A 端到端链（真实持久化全链路） ====================

    [SkippableFact]
    public async Task 方案A_端到端链_CreateReloadUpdateValidatePublishReloadSnapshot_历史重放()
    {
        // P1-01 方案 A 真实持久化端到端链（ContentSnapshotJson 唯一内容真相）：
        // Create → Reload → Update → Validate → Publish → Reload → Snapshot 六块值 → 历史重放
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasContentSnapshotJsonColumn() || !TestEnvironment.HasGovernanceAuditLogTable(),
            "测试库缺 APS_Auth 库、ContentSnapshotJson 列或 GovernanceAuditLog 表（方案 A/审计 DDL 未迁移），需 2号位部署 v5.1.2 后转绿");

        await SetupBaseVersionsAsync();

        // ① Reload（服务层 Create 已归一化主题 JSON → ContentSnapshotJson 落库；GET 投影回主题 JSON 前端兼容）
        var reloadedRuleSet = await _service.GetRuleSetVersionAsync(_testRuleSetVersionId);
        reloadedRuleSet.Should().NotBeNull();
        reloadedRuleSet!.ContentSnapshotJson.Should().NotBeNullOrWhiteSpace();
        reloadedRuleSet.DemandPriorityJson.Should().NotBeNullOrWhiteSpace();

        var reloadedParamSet = await _service.GetParameterSetVersionAsync(_testParameterSetVersionId);
        reloadedParamSet.Should().NotBeNull();
        reloadedParamSet!.LockJson.Should().NotBeNullOrWhiteSpace();
        reloadedParamSet.SolverStrategyJson.Should().NotBeNullOrWhiteSpace();
        reloadedParamSet.CandidateGuardrailJson.Should().NotBeNullOrWhiteSpace();

        // ② Update（DRAFT 阶段修订内容 → 服务层归一化重建快照，主题 JSON 非空触发重建）
        await _service.UpdateRuleSetVersionAsync(_testRuleSetVersionId, new RuleSetVersion
        {
            Id = _testRuleSetVersionId,
            RuleSetId = _testRuleSetId,
            VersionCode = $"TEST-R-{_now:yyyyMMddHHmmss}-U1",
            DemandPriorityJson = JsonSerializer.Serialize(new DemandPriorityBlock
            {
                Segments = new List<PrioritySegment>
                {
                    new PrioritySegment
                    {
                        SegmentOrder = 1,
                        SegmentName = "方案A-紧急订单-U1",
                        IsEnabled = true,
                        MatchConditions = new List<SegmentMatchCondition>
                        {
                            new SegmentMatchCondition { Field = DemandField.OrderType, Operator = ConditionOperator.Equals, Value = "SO" }
                        },
                        SortFields = new List<SegmentSortField>
                        {
                            new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                        }
                    }
                }
            }),
        });

        var updated = await _service.GetRuleSetVersionAsync(_testRuleSetVersionId);
        // 投影 JSON 为默认编码（非 ASCII 转义为 \uXXXX），按块反序列化后断言解码值，避免转义串比对误判
        var updatedBlock = JsonSerializer.Deserialize<DemandPriorityBlock>(updated!.DemandPriorityJson!);
        updatedBlock!.Segments.Should().ContainSingle(s => s.SegmentName == "方案A-紧急订单-U1");

        // ③ Validate（规则集/参数集 DRAFT 阶段可校验；策略包引用版本须先 PUBLISHED 才能通过 REF_NOT_PUBLISHED）
        (await _service.ValidateRuleSetVersionForPublishAsync(_testRuleSetVersionId)).IsValid.Should().BeTrue();
        (await _service.ValidateParameterSetVersionForPublishAsync(_testParameterSetVersionId)).IsValid.Should().BeTrue();

        // ④ Publish 规则集 + 参数集（DRAFT → PUBLISHED）
        await _service.PublishRuleSetVersionAsync(_testRuleSetVersionId, "IntegrationTest");
        await _service.PublishParameterSetVersionAsync(_testParameterSetVersionId, "IntegrationTest");

        // ⑤ Validate + Publish 策略包（引用已 PUBLISHED → 校验通过；P0-06 正式发布强制校验）
        (await _service.ValidateStrategyProfileVersionForPublishAsync(_testStrategyProfileVersionId)).IsValid.Should().BeTrue();
        await _service.PublishStrategyProfileVersionAsync(_testStrategyProfileVersionId, "IntegrationTest");

        // ⑥ Reload 验证 PUBLISHED
        var pubRuleSet = await _ruleSetVersionRepo.GetByIdAsync(_testRuleSetVersionId);
        pubRuleSet!.Status.Should().Be("PUBLISHED");
        var pubSpv = await _strategyProfileVersionRepo.GetByIdAsync(_testStrategyProfileVersionId);
        pubSpv!.Status.Should().Be("PUBLISHED");

        // ⑦ Snapshot 六块值（真实 DB → Provider 装配，逐块断言哨兵值）
        var snapshot = await _snapshotProvider.GetFrozenStrategySnapshotAsync(_testStrategyProfileVersionId, CancellationToken.None);
        snapshot.Should().NotBeNull();
        snapshot.RuleSetVersionId.Should().Be(_testRuleSetVersionId);
        snapshot.ParameterSetVersionId.Should().Be(_testParameterSetVersionId);
        snapshot.DemandPriority.Segments.Should().ContainSingle(s => s.SegmentName == "方案A-紧急订单-U1");
        snapshot.Lock.Trigger.RemainingTimeThresholdHours.Should().Be(48);
        snapshot.Supply.Inventory.WarehousePriority.Should().Equal(new List<string> { "WH-A-01", "WH-A-02" });
        snapshot.Procurement.PlanningYields.Should().ContainSingle(p => p.MaterialId == "MAT-A-001" && p.YieldPercent == 0.95m);
        snapshot.SolverStrategy.Mode.Should().Be(SolverStrategyMode.Backward);
        snapshot.SolverStrategy.OnTimeTarget.TargetPercent.Should().Be(85);
        snapshot.CandidateGuardrail.NormalMs.Should().Be(70_000);

        // ⑧ 历史重放（同 VersionId 二次调用 → B-5 缓存命中：内容值相等、FrozenAt 刷新为本次时点）
        var replay = await _snapshotProvider.GetFrozenStrategySnapshotAsync(_testStrategyProfileVersionId, CancellationToken.None);
        replay.Should().NotBeNull();
        replay.DemandPriority.Segments.Should().ContainSingle(s => s.SegmentName == "方案A-紧急订单-U1");
        replay.FrozenAt.Should().BeAfter(snapshot.FrozenAt);
    }

    /// <summary>从 ContentSnapshotJson 提取 DemandPriority 子块（方案 A 读回路径，契约 §6.10.5）</summary>
    private static DemandPriorityBlock? LoadDemandPriorityBlock(string contentSnapshotJson)
    {
        using var doc = JsonDocument.Parse(contentSnapshotJson);
        if (!doc.RootElement.TryGetProperty("DemandPriority", out var blockElement))
        {
            return null;
        }
        return blockElement.Deserialize<DemandPriorityBlock>();
    }

    // ==================== 数据准备 ====================

    /// <summary>创建 RuleSet/ParameterSet/StrategyProfile 父记录 + 三版本（均 DRAFT），互相引用合法 JSON</summary>
    private async Task SetupBaseVersionsAsync()
    {
        // 父表：RuleSet
        _testRuleSetId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[RuleSet] ([RuleSetCode], [RuleSetName], [Description], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-RS-{_uniqueSuffix}", Name = "集成测试规则集", Description = "Integration Test", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);

        // 父表：ParameterSet
        _testParameterSetId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[ParameterSet] ([ParameterSetCode], [ParameterSetName], [Description], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-PS-{_uniqueSuffix}", Name = "集成测试参数集", Description = "Integration Test", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);

        // 父表：StrategyProfile（RunType=LOCAL_RESCHEDULE 供默认解析命中；独立 RunType 不与生产种子 FULL_SCHEDULE IsDefault=1 撞歧义）
        _testStrategyProfileId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[StrategyProfile] ([StrategyProfileCode], [StrategyProfileName], [Description], [RunType], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, @RunType, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-SP-{_uniqueSuffix}", Name = "集成测试策略包", Description = "Integration Test", RunType = "LOCAL_RESCHEDULE", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);

        // 版本表：RuleSetVersion —— 走服务层 CRUD（方案 A P0-01：入参 DemandPriorityJson 为内存字段，
        // 服务层 EnsureNormalized 归一化写入 ContentSnapshotJson 落库；Create 强制 DRAFT、治理字段置空）
        var ruleSetVersion = await _service.CreateRuleSetVersionAsync(new RuleSetVersion
        {
            RuleSetId = _testRuleSetId,
            VersionCode = $"TEST-R-{_now:yyyyMMddHHmmss}",
            DemandPriorityJson = JsonSerializer.Serialize(new DemandPriorityBlock
            {
                Segments = new List<PrioritySegment>
                {
                    new PrioritySegment
                    {
                        SegmentOrder = 1,
                        SegmentName = "方案A-紧急订单",
                        IsEnabled = true,
                        MatchConditions = new List<SegmentMatchCondition>
                        {
                            new SegmentMatchCondition { Field = DemandField.OrderType, Operator = ConditionOperator.Equals, Value = "SO" }
                        },
                        SortFields = new List<SegmentSortField>
                        {
                            new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }
                        }
                    }
                }
            }),
        }, "IntegrationTest");
        _testRuleSetVersionId = ruleSetVersion.Id;

        // 版本表：ParameterSetVersion —— 服务层 CRUD，五主题 JSON 归一化到 ContentSnapshotJson
        // （含 SolverStrategy/CandidateGuardrail，P0-02 六块统一真实来源）
        var parameterSetVersion = await _service.CreateParameterSetVersionAsync(new ParameterSetVersion
        {
            ParameterSetId = _testParameterSetId,
            VersionCode = $"TEST-P-{_now:yyyyMMddHHmmss}",
            LockJson = JsonSerializer.Serialize(new LockBlock
            {
                Trigger = new ProtectionTriggerParams { UseRemainingTimeThreshold = true, RemainingTimeThresholdHours = 48 },
                Sticky = new StickyProtectionParams { RequireReleaseRecord = true },
            }),
            SupplyJson = JsonSerializer.Serialize(new SupplyBlock
            {
                Inventory = new InventoryAvailabilityRule
                {
                    IsEnabled = true,
                    WarehousePriority = new List<string> { "WH-A-01", "WH-A-02" },
                },
                PiSort = new PiSortParams { SortBy = PiSortBy.IssueDateAsc },
            }),
            ProcurementJson = JsonSerializer.Serialize(new ProcurementBlock
            {
                DefaultPurchaseLt = new List<PurchaseLtRule> { new() { WarehouseCode = "WH-A-01", DefaultLtDays = 7 } },
                OverdueMargin = new OverdueMarginParams { MarginPercent = 0.1m, MinimumExtraDays = 1 },
                ArrivalToUsableOffsets = new List<WarehouseOffsetRule> { new() { WarehouseCode = "WH-A-01", OffsetHours = 24 } },
                PlanningYields = new List<PlanningYieldRule> { new() { MaterialId = "MAT-A-001", YieldPercent = 0.95m } },
            }),
            SolverStrategyJson = JsonSerializer.Serialize(new SolverStrategyBlock
            {
                Mode = SolverStrategyMode.Backward,
                OnTimeTarget = new OnTimeTargetParams { TargetPercent = 85 },
                Setup = new SetupParams { DefaultSetupMinutes = 45, SetupLookAheadSize = 4 },
            }),
            CandidateGuardrailJson = JsonSerializer.Serialize(new CandidateGuardrailBlock
            {
                NormalMs = 70_000,
                SoftMs = 110_000,
                LocalHardMs = 200_000,
                MaxRepairAttempts = 7,
                MaxPropagationRounds = 12,
            }),
        }, "IntegrationTest");
        _testParameterSetVersionId = parameterSetVersion.Id;

        // 版本表：StrategyProfileVersion —— 服务层 CRUD（引用上面两版本，IsDefault=1；Create 保留 IsDefault、强制 DRAFT）
        var spv = await _service.CreateStrategyProfileVersionAsync(new StrategyProfileVersion
        {
            StrategyProfileId = _testStrategyProfileId,
            VersionCode = $"TEST-S-{_now:yyyyMMddHHmmss}",
            RuleSetVersionId = _testRuleSetVersionId,
            ParameterSetVersionId = _testParameterSetVersionId,
            IsDefault = true,
        }, "IntegrationTest");
        _testStrategyProfileVersionId = spv.Id;
    }

    public void Dispose()
    {
        CleanupTestDataAsync().GetAwaiter().GetResult();
    }

    private async Task CleanupTestDataAsync()
    {
        if (_testStrategyProfileVersionId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM StrategyProfileVersion WHERE Id = @Id", new { Id = _testStrategyProfileVersionId }, db: DatabaseId.APS);
        }
        if (_testRuleSetVersionId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM RuleSetVersion WHERE Id = @Id", new { Id = _testRuleSetVersionId }, db: DatabaseId.APS);
        }
        if (_testParameterSetVersionId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM ParameterSetVersion WHERE Id = @Id", new { Id = _testParameterSetVersionId }, db: DatabaseId.APS);
        }
        if (_testStrategyProfileId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM StrategyProfile WHERE Id = @Id", new { Id = _testStrategyProfileId }, db: DatabaseId.APS);
        }
        if (_testRuleSetId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM RuleSet WHERE Id = @Id", new { Id = _testRuleSetId }, db: DatabaseId.APS);
        }
        if (_testParameterSetId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM ParameterSet WHERE Id = @Id", new { Id = _testParameterSetId }, db: DatabaseId.APS);
        }
    }
}
