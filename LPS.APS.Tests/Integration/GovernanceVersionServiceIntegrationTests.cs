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
public class GovernanceVersionServiceIntegrationTests : IDisposable
{
    private readonly DatabaseConnectionManager _cm;
    private readonly GovernanceVersionService _service;
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
    }

    /// <summary>构造审计仓储（Auth 库 EF Core；库不可达时构造成功、首次写入时失败→测试 Skip 条件先行探测）</summary>
    private static GovernanceAuditLogRepository CreateAuditRepository(ILoggerFactory loggerFactory)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
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
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "测试环境缺 APS_Auth 库（审计日志无法落地），需 2号位部署后转绿");

        await SetupBaseVersionsAsync();

        // 校验：三版本发布前均合法
        var ruleSetValidation = await _service.ValidateRuleSetVersionForPublishAsync(_testRuleSetVersionId);
        ruleSetValidation.IsValid.Should().BeTrue();
        var paramSetValidation = await _service.ValidateParameterSetVersionForPublishAsync(_testParameterSetVersionId);
        paramSetValidation.IsValid.Should().BeTrue();
        var spvValidation = await _service.ValidateStrategyProfileVersionForPublishAsync(_testStrategyProfileVersionId);
        spvValidation.IsValid.Should().BeTrue();

        // 发布：DRAFT → PUBLISHED
        await _service.PublishRuleSetVersionAsync(_testRuleSetVersionId, "IntegrationTest");
        await _service.PublishParameterSetVersionAsync(_testParameterSetVersionId, "IntegrationTest");
        await _service.PublishStrategyProfileVersionAsync(_testStrategyProfileVersionId, "IntegrationTest");

        var publishedRuleSet = await _ruleSetVersionRepo.GetByIdAsync(_testRuleSetVersionId);
        publishedRuleSet!.Status.Should().Be("PUBLISHED");
        var publishedSpv = await _strategyProfileVersionRepo.GetByIdAsync(_testStrategyProfileVersionId);
        publishedSpv!.Status.Should().Be("PUBLISHED");
        publishedSpv.IsDefault.Should().BeTrue();

        // 默认解析：RunType 命中唯一 PUBLISHED 默认版本
        var resolved = await _service.ResolveDefaultStrategyProfileVersionAsync("FULL_SCHEDULE");
        resolved.Should().NotBeNull();
        resolved!.Id.Should().Be(_testStrategyProfileVersionId);

        // Run 引用追溯：版本维完整链（父包 + 规则集 + 参数集）
        var trace = await _service.GetRunStrategyProfileTraceAsync(_testStrategyProfileVersionId);
        trace.StrategyProfileVersionId.Should().Be(_testStrategyProfileVersionId);
        trace.StrategyProfileCode.Should().NotBeNullOrWhiteSpace();
        trace.RunType.Should().Be("FULL_SCHEDULE");
        trace.RuleSetVersionId.Should().Be(_testRuleSetVersionId);
        trace.RuleSetVersionCode.Should().NotBeNullOrWhiteSpace();
        trace.ParameterSetVersionId.Should().Be(_testParameterSetVersionId);
        trace.ParameterSetVersionCode.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task 发布前校验_P005_坏配置版本被拒无绕过()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "测试环境缺 APS_Auth 库，需 2号位部署后转绿");

        await SetupBaseVersionsAsync();

        // 篡改为损坏的 DemandPriorityJson（非法 JSON）
        await _ruleSetVersionRepo.UpdateAsync(new RuleSetVersion
        {
            Id = _testRuleSetVersionId,
            RuleSetId = _testRuleSetId,
            VersionCode = $"TEST-R-{_uniqueSuffix}",
            Status = "DRAFT",
            DemandPriorityJson = "{ not valid json",
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
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "测试环境缺 APS_Auth 库，需 2号位部署后转绿");

        // 规则集/参数集保持 DRAFT（不发布）→ 策略包引用未发布版本应被拒
        await SetupBaseVersionsAsync();

        var validation = await _service.ValidateStrategyProfileVersionForPublishAsync(_testStrategyProfileVersionId);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(e => e.Code == "REF_NOT_PUBLISHED");
    }

    [SkippableFact]
    public async Task 校验器集成_P007_真实库存合法配置通过()
    {
        // 不依赖 Auth 库（纯校验），但依赖真实库存配置写入
        await SetupBaseVersionsAsync();

        var ruleSetVersion = await _ruleSetVersionRepo.GetByIdAsync(_testRuleSetVersionId);
        var priorityBlock = JsonSerializer.Deserialize<DemandPriorityBlock>(ruleSetVersion!.DemandPriorityJson!);

        var validator = new DemandPriorityValidator();
        var result = validator.Validate(priorityBlock!);

        result.IsValid.Should().BeTrue();
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

        // 父表：StrategyProfile（RunType=FULL_SCHEDULE 供默认解析命中）
        _testStrategyProfileId = await _cm.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[StrategyProfile] ([StrategyProfileCode], [StrategyProfileName], [Description], [RunType], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, @RunType, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-SP-{_uniqueSuffix}", Name = "集成测试策略包", Description = "Integration Test", RunType = "FULL_SCHEDULE", CreatedAt = _now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);

        // 版本表：RuleSetVersion（DRAFT，合法 DemandPriorityJson）
        var snapshot = DemandPriorityFixture.GetStandardPrioritySnapshot();
        var ruleSetVersion = new RuleSetVersion
        {
            RuleSetId = _testRuleSetId,
            VersionCode = $"TEST-R-{_now:yyyyMMddHHmmss}",
            DemandPriorityJson = JsonSerializer.Serialize(snapshot.DemandPriority),
            Status = "DRAFT",
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        };
        ruleSetVersion = await _ruleSetVersionRepo.AddAsync(ruleSetVersion);
        _testRuleSetVersionId = ruleSetVersion.Id;

        // 版本表：ParameterSetVersion（DRAFT，合法 Lock/Supply/Procurement）
        var parameterSetVersion = new ParameterSetVersion
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
                    WarehousePriority = new List<string> { "WH-IT-01", "WH-IT-02" },
                },
                PiSort = new PiSortParams { SortBy = PiSortBy.IssueDateAsc },
            }),
            ProcurementJson = JsonSerializer.Serialize(new ProcurementBlock
            {
                DefaultPurchaseLt = new List<PurchaseLtRule> { new() { WarehouseCode = "WH-IT-01", DefaultLtDays = 7 } },
                OverdueMargin = new OverdueMarginParams { MarginPercent = 0.1m, MinimumExtraDays = 1 },
                ArrivalToUsableOffsets = new List<WarehouseOffsetRule> { new() { WarehouseCode = "WH-IT-01", OffsetHours = 24 } },
                PlanningYields = new List<PlanningYieldRule> { new() { MaterialId = "MAT-IT-001", YieldPercent = 0.95m } },
            }),
            Status = "DRAFT",
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        };
        parameterSetVersion = await _parameterSetVersionRepo.AddAsync(parameterSetVersion);
        _testParameterSetVersionId = parameterSetVersion.Id;

        // 版本表：StrategyProfileVersion（DRAFT，引用上面两版本，IsDefault=1）
        var spv = new StrategyProfileVersion
        {
            StrategyProfileId = _testStrategyProfileId,
            VersionCode = $"TEST-S-{_now:yyyyMMddHHmmss}",
            RuleSetVersionId = _testRuleSetVersionId,
            ParameterSetVersionId = _testParameterSetVersionId,
            Status = "DRAFT",
            IsDefault = true,
            CreatedAt = _now,
            CreatedBy = "IntegrationTest",
        };
        spv = await _strategyProfileVersionRepo.AddAsync(spv);
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
