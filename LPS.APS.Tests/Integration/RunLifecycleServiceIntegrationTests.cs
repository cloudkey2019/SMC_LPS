using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Repositories.Auth;
using LPS.APS.Engine.Repositories.Governance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using PlanVersion = LPS.APS.Core.Entities.APS.PlanVersion;
using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using StrategyProfileVersion = LPS.APS.Core.Entities.APS.StrategyProfileVersion;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// RunLifecycleService 运行生命周期治理集成测试（P0-08）
/// 测试范围（真实 APS_Production 库 + 真实仓储 + 真实服务编排）：
/// 1. FAILED 恢复新建 RUNNING（继承策略包版本与 Domain 基线，旧记录不动）
/// 2. Run 引用追溯（Run → 策略包版本 → 规则集/参数集版本 + 关联 PlanVersion）
/// 3. Candidate 最小确认与激活（真实落库 Status/ActivatedAt/ActivatedBy）
/// 依赖：
/// - APS_Auth 库（审计日志），缺失则 Skip
/// - ScheduleRun.ExpectedDomainKeysJson 列（冻结 DDL v5.1.2，测试库未迁移则 Skip 恢复/追溯）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class RunLifecycleServiceIntegrationTests : IDisposable
{
    private readonly DatabaseConnectionManager _cm;
    private readonly RunLifecycleService _service;
    private readonly ScheduleRunRepository _scheduleRunRepo;
    private readonly PlanVersionRepository _planVersionRepo;
    private readonly StrategyProfileVersionRepository _strategyProfileVersionRepo;
    private readonly RuleSetVersionRepository _ruleSetVersionRepo;
    private readonly ParameterSetVersionRepository _parameterSetVersionRepo;

    private long _testStrategyProfileVersionId;
    private long _testRuleSetVersionId;
    private long _testParameterSetVersionId;
    private int _testScheduleRunId;
    private int _testPlanVersionId;
    private int _testBasePlanVersionId;
    private int _testCreatedRunId;
    private int _testRecoveredRunId;
    private readonly string _uniqueSuffix;
    private readonly DateTime _now;

    public RunLifecycleServiceIntegrationTests()
    {
        _now = DateTime.UtcNow;
        _uniqueSuffix = $"{_now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".Substring(0, 30);

        _cm = TestEnvironment.GetConnectionManager();
        var loggerFactory = LoggerFactory.Create(builder => { });

        _scheduleRunRepo = new ScheduleRunRepository(_cm, loggerFactory.CreateLogger<ScheduleRunRepository>());
        _planVersionRepo = new PlanVersionRepository(_cm, loggerFactory.CreateLogger<PlanVersionRepository>());
        _strategyProfileVersionRepo = new StrategyProfileVersionRepository(_cm, loggerFactory.CreateLogger<StrategyProfileVersionRepository>());
        _ruleSetVersionRepo = new RuleSetVersionRepository(_cm, loggerFactory.CreateLogger<RuleSetVersionRepository>());
        _parameterSetVersionRepo = new ParameterSetVersionRepository(_cm, loggerFactory.CreateLogger<ParameterSetVersionRepository>());

        var auditRepo = CreateAuditRepository(loggerFactory);

        _service = new RunLifecycleService(
            _scheduleRunRepo,
            _planVersionRepo,
            _strategyProfileVersionRepo,
            _ruleSetVersionRepo,
            _parameterSetVersionRepo,
            auditRepo);
    }

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
    public async Task 恢复失败运行_新建RUNNING继承基线_旧记录不动()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "测试环境缺 APS_Auth 库，需 2号位部署后转绿");
        Skip.If(!TestEnvironment.HasScheduleRunExpectedDomainKeysColumn(), "测试库 ScheduleRun 缺 ExpectedDomainKeysJson 列（冻结 DDL v5.1.2 未迁移），需 2号位迁移后转绿");
        Skip.If(!TestEnvironment.HasGovernanceAuditLogTable(), "测试库缺 GovernanceAuditLog 表（审计 DDL 未迁移），需 2号位建表后转绿");

        // 建 FAILED 运行（预期 Domain 冻结 ["D1","D2"]，引用真实策略包版本）
        await SetupFailedRunAsync();

        // 恢复：新建 RUNNING 继承基线（新 Run Id 必须存字段，供 Dispose 清理——否则引用策略版本残留，删版本 FK 冲突）
        _testRecoveredRunId = await _service.RecoverFailedRunAsync(_testScheduleRunId, CancellationToken.None);

        // 新记录落库验证：RUNNING + 继承 RunType/StrategyProfileVersionId/ExpectedDomainKeysJson
        var newRun = await _scheduleRunRepo.GetByIdAsync(_testRecoveredRunId);
        newRun.Should().NotBeNull();
        newRun!.Status.Should().Be("RUNNING");
        newRun.RunType.Should().Be("FULL_SCHEDULE");
        newRun.StrategyProfileVersionId.Should().Be(_testStrategyProfileVersionId);
        newRun.ExpectedDomainKeysJson.Should().Be("""["D1","D2"]""");

        // 旧记录不动：仍 FAILED
        var oldRun = await _scheduleRunRepo.GetByIdAsync(_testScheduleRunId);
        oldRun!.Status.Should().Be("FAILED");
        oldRun.ErrorMessage.Should().Be("致命错误");
    }

    [SkippableFact]
    public async Task Run列表查询_G4过滤排序与take_真实库()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "测试环境缺 APS_Auth 库，需 2号位部署后转绿");
        Skip.If(!TestEnvironment.HasScheduleRunExpectedDomainKeysColumn(), "测试库 ScheduleRun 缺 ExpectedDomainKeysJson 列（冻结 DDL v5.1.2 未迁移），需 2号位迁移后转绿");

        // 建 FAILED FULL_SCHEDULE 运行（含本次测试可识别的运行）
        await SetupFailedRunAsync();

        // 1) status 过滤：FAILED 命中，COMPLETED 不命中
        var failedRuns = await _scheduleRunRepo.GetListAsync(status: "FAILED", ct: CancellationToken.None);
        failedRuns.Should().Contain(r => r.Id == _testScheduleRunId);

        var completedRuns = await _scheduleRunRepo.GetListAsync(status: "COMPLETED", ct: CancellationToken.None);
        completedRuns.Should().NotContain(r => r.Id == _testScheduleRunId);

        // 2) runType 过滤：FULL_SCHEDULE 命中
        var fullRuns = await _scheduleRunRepo.GetListAsync(runType: "FULL_SCHEDULE", ct: CancellationToken.None);
        fullRuns.Should().Contain(r => r.Id == _testScheduleRunId);

        // 3) 排序：Id 倒序（最新在前），且返回行含本次新建运行
        var newest = await _scheduleRunRepo.GetListAsync(take: 10, ct: CancellationToken.None);
        newest.Should().NotBeEmpty();
        var ids = newest.Select(r => r.Id).ToList();
        ids.Should().BeInDescendingOrder();
        ids.Should().Contain(_testScheduleRunId);

        // 4) take 上限约束：take=1 只返回 1 行
        var single = await _scheduleRunRepo.GetListAsync(take: 1, ct: CancellationToken.None);
        single.Should().HaveCount(1);
    }

    [SkippableFact]
    public async Task 引用追溯_真实库完整链路()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "测试环境缺 APS_Auth 库，需 2号位部署后转绿");
        Skip.If(!TestEnvironment.HasScheduleRunExpectedDomainKeysColumn(), "测试库 ScheduleRun 缺 ExpectedDomainKeysJson 列（冻结 DDL v5.1.2 未迁移），需 2号位迁移后转绿");

        // 建：策略包版本（引用规则集/参数集版本）+ 完成运行 + 关联 PlanVersion
        await SetupTraceChainAsync();

        var trace = await _service.GetRunReferenceTraceAsync(_testScheduleRunId, CancellationToken.None);

        trace.ScheduleRunId.Should().Be(_testScheduleRunId);
        trace.Status.Should().Be("COMPLETED");
        trace.StrategyProfileVersionId.Should().Be(_testStrategyProfileVersionId);
        trace.StrategyProfileVersionCode.Should().NotBeNullOrWhiteSpace();
        trace.RuleSetVersionCode.Should().NotBeNullOrWhiteSpace();
        trace.ParameterSetVersionCode.Should().NotBeNullOrWhiteSpace();
        trace.ExpectedDomainKeysJson.Should().Be("""["D1"]""");
        trace.PlanVersionId.Should().Be(_testPlanVersionId);
        trace.PlanVersionStatus.Should().Be("ACTIVE");
    }

    [SkippableFact]
    public async Task 候选确认与激活_真实库落库()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasGovernanceAuditLogTable(),
            "测试环境缺 APS_Auth 库或 GovernanceAuditLog 表（审计 DDL 未迁移），需 2号位建表后转绿");

        await SetupCandidateAsync();

        // 确认（二轮复审 P0-05）：仅记审计，不写 ActivatedAt/ActivatedBy、不转 ACTIVE
        await _service.ConfirmCandidateAsync(_testPlanVersionId, "tester", "集成测试确认", CancellationToken.None);
        var confirmed = await _planVersionRepo.GetByIdAsync(_testPlanVersionId);
        confirmed!.Status.Should().Be("Computed");
        confirmed.ActivatedAt.Should().BeNull();
        confirmed.ActivatedBy.Should().BeNull();

        // 激活（二轮复审 P0-03/04/06）：已确认 + 来源 MANUAL_RESCHEDULE 可激活 → 原子替换 CANDIDATE → ACTIVE
        await _service.ActivateCandidateAsync(_testPlanVersionId, "tester", CancellationToken.None);
        var activated = await _planVersionRepo.GetByIdAsync(_testPlanVersionId);
        activated!.Status.Should().Be("ACTIVE");
        activated.ActivatedAt.Should().NotBeNull();
        activated.ActivatedBy.Should().Be("tester");
    }

    [SkippableFact]
    public async Task 创建白天候选运行_RUNNING运行加BUILDING壳_真实库()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "测试环境缺 APS_Auth 库，需 2号位部署后转绿");
        Skip.If(!TestEnvironment.HasScheduleRunExpectedDomainKeysColumn(), "测试库 ScheduleRun 缺 ExpectedDomainKeysJson 列（冻结 DDL v5.1.2 未迁移），需 2号位迁移后转绿");
        Skip.If(!TestEnvironment.HasGovernanceAuditLogTable(), "测试库缺 GovernanceAuditLog 表（审计 DDL 未迁移），需 2号位建表后转绿");

        // 建前置：MANUAL_RESCHEDULE 默认 PUBLISHED 策略版本 + 唯一 Domain 的 ACTIVE Base 计划版本
        // （唯一 Domain 防 xUnit 并行下其他用例同域 ACTIVE 触发 UQ_PlanVersion_OneActivePerDomain）
        var domainKey = $"D1-{_uniqueSuffix[^8..]}";
        await SetupBaseActiveAndDefaultStrategyAsync(domainKey);

        // 创建白天候选运行（未传 BasePlanVersionId → 服务解析当前 ACTIVE 为 Base）
        var result = await _service.CreateCandidateRunAsync(new CandidateRunCreateSpec
        {
            RunType = "MANUAL_RESCHEDULE",
            Purpose = "MANUAL_ADJUSTMENT",
            DomainKey = domainKey,
            Actor = "tester",
        }, CancellationToken.None);

        // 新 Run 落库：RUNNING + BasePlanVersionId + 冻结 Domain 基线 + 继承默认策略版本
        _testCreatedRunId = result.NewScheduleRunId;
        result.NewScheduleRunId.Should().BeGreaterThan(0);
        result.NewPlanVersionId.Should().BeGreaterThan(0);
        var run = await _scheduleRunRepo.GetByIdAsync(result.NewScheduleRunId);
        run.Should().NotBeNull();
        run!.Status.Should().Be("RUNNING");
        run.RunType.Should().Be("MANUAL_RESCHEDULE");
        run.BasePlanVersionId.Should().Be(_testBasePlanVersionId);
        run.StrategyProfileVersionId.Should().Be(_testStrategyProfileVersionId);
        run.ExpectedDomainKeysJson.Should().Be($"""["{domainKey}"]""");

        // 新壳落库：CANDIDATE + Created + SourceScheduleRunId=新 Run（白天候选壳初始态，D7 词表）
        _testPlanVersionId = result.NewPlanVersionId;
        var shell = await _planVersionRepo.GetByIdAsync(result.NewPlanVersionId);
        shell.Should().NotBeNull();
        shell!.VersionCategory.Should().Be("CANDIDATE");
        shell.Status.Should().Be("Created");
        shell.DomainKey.Should().Be(domainKey);
        shell.SourceScheduleRunId.Should().Be(result.NewScheduleRunId);
    }

    /// <summary>
    /// 建 B-1 前置：MANUAL_RESCHEDULE 默认 PUBLISHED 策略版本（IsDefault=1、生效窗口覆盖当前）
    /// + DomainKey 对应 ACTIVE Base 计划版本（白天 Candidate 锚定，只读）。
    /// </summary>
    private async Task SetupBaseActiveAndDefaultStrategyAsync(string domainKey)
    {
        var ruleSetId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[RuleSet] ([RuleSetCode], [RuleSetName], [Description], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-RS-MR-{_uniqueSuffix}", Name = "集成测试规则集", Description = "Integration Test", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);
        var parameterSetId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[ParameterSet] ([ParameterSetCode], [ParameterSetName], [Description], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-PS-MR-{_uniqueSuffix}", Name = "集成测试参数集", Description = "Integration Test", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);
        var strategyProfileId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[StrategyProfile] ([StrategyProfileCode], [StrategyProfileName], [Description], [RunType], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, @RunType, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-SP-MR-{_uniqueSuffix}", Name = "集成测试策略包", Description = "Integration Test", RunType = "MANUAL_RESCHEDULE", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);

        var ruleSetVersion = await _ruleSetVersionRepo.AddAsync(new RuleSetVersion
        {
            RuleSetId = ruleSetId,
            VersionCode = $"TEST-R-MR-{_now:yyyyMMddHHmmss}",
            Status = "PUBLISHED",
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        });
        _testRuleSetVersionId = ruleSetVersion.Id;

        var parameterSetVersion = await _parameterSetVersionRepo.AddAsync(new ParameterSetVersion
        {
            ParameterSetId = parameterSetId,
            VersionCode = $"TEST-P-MR-{_now:yyyyMMddHHmmss}",
            Status = "PUBLISHED",
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        });
        _testParameterSetVersionId = parameterSetVersion.Id;

        var spv = await _strategyProfileVersionRepo.AddAsync(new StrategyProfileVersion
        {
            StrategyProfileId = strategyProfileId,
            VersionCode = $"TEST-S-MR-{_now:yyyyMMddHHmmss}",
            RuleSetVersionId = _testRuleSetVersionId,
            ParameterSetVersionId = _testParameterSetVersionId,
            Status = "PUBLISHED",
            IsDefault = true,
            EffectiveFrom = _now.AddDays(-1),
            EffectiveTo = _now.AddDays(1),
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        });
        _testStrategyProfileVersionId = spv.Id;

        // COMPLETED 运行 + DomainKey 对应 ACTIVE Base 计划版本（白天 Candidate 锚定，只读）
        _testScheduleRunId = await _cm.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[ScheduleRun]
                ([RunType], [Status], [TriggeredBy], [DataCutoffTime],
                 [StrategyProfileVersionId], [ExpectedDomainKeysJson], [StartedAt], [CompletedAt], [CreatedAt])
              VALUES
                ('MANUAL_RESCHEDULE', 'COMPLETED', 'Hangfire', GETDATE(),
                 @StrategyProfileVersionId, @ExpectedDomainKeysJson, GETDATE(), GETDATE(), GETDATE());
              SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                StrategyProfileVersionId = _testStrategyProfileVersionId,
                ExpectedDomainKeysJson = $"""["{domainKey}"]""",
            },
            db: DatabaseId.APS);

        _testBasePlanVersionId = await _cm.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[PlanVersion]
                ([VersionCode], [VersionCategory], [DomainKey], [Status], [SourceScheduleRunId],
                 [PlanHorizonStart], [PlanHorizonEnd], [ComputeMode], [CreatedBy], [CreatedAt])
              VALUES
                (@VersionCode, 'RESCHEDULE', @DomainKey, 'ACTIVE', @SourceScheduleRunId,
                 @PlanHorizonStart, @PlanHorizonEnd, 'SIMULATION', 'IntegrationTest', GETDATE());
              SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                VersionCode = $"V-BASE-{_uniqueSuffix}",
                DomainKey = domainKey,
                SourceScheduleRunId = _testScheduleRunId,
                PlanHorizonStart = DateTime.Today,
                PlanHorizonEnd = DateTime.Today.AddDays(90),
            },
            db: DatabaseId.APS);
    }

    // ==================== 数据准备 ====================

    /// <summary>建 FAILED ScheduleRun（真实策略包版本引用 + 冻结 Domain 基线）</summary>
    private async Task SetupFailedRunAsync()
    {
        await EnsureStrategyProfileVersionAsync();

        _testScheduleRunId = await _cm.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[ScheduleRun]
                ([RunType], [Status], [TriggeredBy], [DataCutoffTime],
                 [StrategyProfileVersionId], [ExpectedDomainKeysJson], [StartedAt], [CompletedAt], [ErrorMessage], [CreatedAt])
              VALUES
                ('FULL_SCHEDULE', 'FAILED', 'Hangfire', GETDATE(),
                 @StrategyProfileVersionId, @ExpectedDomainKeysJson, GETDATE(), GETDATE(), @ErrorMessage, GETDATE());
              SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                StrategyProfileVersionId = _testStrategyProfileVersionId,
                ExpectedDomainKeysJson = """["D1","D2"]""",
                ErrorMessage = "致命错误",
            },
            db: DatabaseId.APS);
    }

    /// <summary>建完整追溯链：策略包版本 + COMPLETED 运行 + ACTIVE PlanVersion</summary>
    private async Task SetupTraceChainAsync()
    {
        await EnsureStrategyProfileVersionAsync();

        _testScheduleRunId = await _cm.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[ScheduleRun]
                ([RunType], [Status], [TriggeredBy], [DataCutoffTime],
                 [StrategyProfileVersionId], [ExpectedDomainKeysJson], [StartedAt], [CompletedAt], [CreatedAt])
              VALUES
                ('FULL_SCHEDULE', 'COMPLETED', 'Hangfire', GETDATE(),
                 @StrategyProfileVersionId, @ExpectedDomainKeysJson, GETDATE(), GETDATE(), GETDATE());
              SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                StrategyProfileVersionId = _testStrategyProfileVersionId,
                ExpectedDomainKeysJson = """["D1"]""",
            },
            db: DatabaseId.APS);

        _testPlanVersionId = await _cm.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[PlanVersion]
                ([VersionCode], [VersionCategory], [DomainKey], [Status], [SourceScheduleRunId],
                 [PlanHorizonStart], [PlanHorizonEnd], [ComputeMode], [CreatedBy], [CreatedAt])
              VALUES
                (@VersionCode, 'RESCHEDULE', 'D1', 'ACTIVE', @SourceScheduleRunId,
                 @PlanHorizonStart, @PlanHorizonEnd, 'SIMULATION', 'IntegrationTest', GETDATE());
              SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                VersionCode = $"V-{_uniqueSuffix}",
                SourceScheduleRunId = _testScheduleRunId,
                PlanHorizonStart = _now,
                PlanHorizonEnd = _now.AddDays(30),
            },
            db: DatabaseId.APS);
    }

    /// <summary>建 CANDIDATE PlanVersion（关联一条真实 ScheduleRun）</summary>
    private async Task SetupCandidateAsync()
    {
        _testScheduleRunId = await _cm.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[ScheduleRun]
                ([RunType], [Status], [TriggeredBy], [DataCutoffTime], [StartedAt], [CreatedAt])
              VALUES
                ('MANUAL_RESCHEDULE', 'COMPLETED', 'API', GETDATE(), GETDATE(), GETDATE());
              SELECT CAST(SCOPE_IDENTITY() AS INT);",
            null,
            db: DatabaseId.APS);

        _testPlanVersionId = await _cm.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO [dbo].[PlanVersion]
                ([VersionCode], [VersionCategory], [DomainKey], [Status], [SourceScheduleRunId],
                 [PlanHorizonStart], [PlanHorizonEnd], [ComputeMode], [CreatedBy], [CreatedAt])
              VALUES
                (@VersionCode, 'CANDIDATE', 'D1', 'Computed', @SourceScheduleRunId,
                 @PlanHorizonStart, @PlanHorizonEnd, 'SIMULATION', 'IntegrationTest', GETDATE());
              SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                VersionCode = $"V-CAND-{_uniqueSuffix}",
                SourceScheduleRunId = _testScheduleRunId,
                PlanHorizonStart = _now,
                PlanHorizonEnd = _now.AddDays(30),
            },
            db: DatabaseId.APS);
    }

    /// <summary>建真实策略包版本（RuleSet/ParameterSet 父 + 版本，PUBLISHED 引用链）</summary>
    private async Task EnsureStrategyProfileVersionAsync()
    {
        if (_testStrategyProfileVersionId > 0)
        {
            return;
        }

        var ruleSetId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[RuleSet] ([RuleSetCode], [RuleSetName], [Description], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-RS-{_uniqueSuffix}", Name = "集成测试规则集", Description = "Integration Test", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);
        var parameterSetId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[ParameterSet] ([ParameterSetCode], [ParameterSetName], [Description], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-PS-{_uniqueSuffix}", Name = "集成测试参数集", Description = "Integration Test", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);
        var strategyProfileId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[StrategyProfile] ([StrategyProfileCode], [StrategyProfileName], [Description], [RunType], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, @RunType, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-SP-{_uniqueSuffix}", Name = "集成测试策略包", Description = "Integration Test", RunType = "FULL_SCHEDULE", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);

        var ruleSetVersion = await _ruleSetVersionRepo.AddAsync(new RuleSetVersion
        {
            RuleSetId = ruleSetId,
            VersionCode = $"TEST-R-{_now:yyyyMMddHHmmss}",
            Status = "PUBLISHED",
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        });
        _testRuleSetVersionId = ruleSetVersion.Id;

        var parameterSetVersion = await _parameterSetVersionRepo.AddAsync(new ParameterSetVersion
        {
            ParameterSetId = parameterSetId,
            VersionCode = $"TEST-P-{_now:yyyyMMddHHmmss}",
            Status = "PUBLISHED",
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        });
        _testParameterSetVersionId = parameterSetVersion.Id;

        var spv = await _strategyProfileVersionRepo.AddAsync(new StrategyProfileVersion
        {
            StrategyProfileId = strategyProfileId,
            VersionCode = $"TEST-S-{_now:yyyyMMddHHmmss}",
            RuleSetVersionId = _testRuleSetVersionId,
            ParameterSetVersionId = _testParameterSetVersionId,
            Status = "PUBLISHED",
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        });
        _testStrategyProfileVersionId = spv.Id;
    }

    public void Dispose()
    {
        CleanupTestDataAsync().GetAwaiter().GetResult();
    }

    private async Task CleanupTestDataAsync()
    {
        // FK 依赖倒序：PlanVersion(→SourceScheduleRunId) → ScheduleRun(→StrategyProfileVersionId) → StrategyProfileVersion(→RuleSet/ParameterSetVersion)
        if (_testBasePlanVersionId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM PlanVersion WHERE Id = @Id", new { Id = _testBasePlanVersionId }, db: DatabaseId.APS);
        }
        if (_testPlanVersionId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM PlanVersion WHERE Id = @Id", new { Id = _testPlanVersionId }, db: DatabaseId.APS);
        }
        if (_testCreatedRunId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM ScheduleRun WHERE Id = @Id", new { Id = _testCreatedRunId }, db: DatabaseId.APS);
        }
        if (_testScheduleRunId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM ScheduleRun WHERE Id = @Id", new { Id = _testScheduleRunId }, db: DatabaseId.APS);
        }
        if (_testRecoveredRunId > 0)
        {
            await _cm.ExecuteAsync("DELETE FROM ScheduleRun WHERE Id = @Id", new { Id = _testRecoveredRunId }, db: DatabaseId.APS);
        }
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
    }
}
