using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Repositories.Governance;
using LPS.APS.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using System.Text.Json;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// DemandPriority 集成测试（阶段 C 端到端验收）
/// </summary>
public class DemandPriorityIntegrationTests : IDisposable
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IRuleSetVersionRepository _ruleSetRepo;
    private readonly DemandPriorityMatcher _matcher;
    private readonly DemandPriorityValidator _validator;

    private long _testRuleSetId;
    private long _testRuleSetVersionId;

    public DemandPriorityIntegrationTests()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();

        var loggerFactory = LoggerFactory.Create(builder => { });

        var dbOptions = configuration.GetSection("Database").Get<LPS.APS.Engine.Configuration.DatabaseOptions>()
            ?? throw new InvalidOperationException("Database configuration not found");

        _connectionManager = new DatabaseConnectionManager(Microsoft.Extensions.Options.Options.Create(dbOptions));
        _ruleSetRepo = new RuleSetVersionRepository(_connectionManager, loggerFactory.CreateLogger<RuleSetVersionRepository>());
        _matcher = new DemandPriorityMatcher();
        _validator = new DemandPriorityValidator();
    }

    [Fact]
    public async System.Threading.Tasks.Task 真实数据库_存储并读取DemandPriorityBlock()
    {
        await SetupTestDataAsync();

        var ruleSetVersion = await _ruleSetRepo.GetByIdAsync(_testRuleSetVersionId);

        ruleSetVersion.Should().NotBeNull();
        ruleSetVersion!.DemandPriorityJson.Should().NotBeNullOrWhiteSpace();

        var priorityBlock = JsonSerializer.Deserialize<DemandPriorityBlock>(ruleSetVersion.DemandPriorityJson);
        priorityBlock.Should().NotBeNull();
        priorityBlock!.Segments.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async System.Threading.Tasks.Task 完整流程_从数据库读取并执行排序()
    {
        await SetupTestDataAsync();

        var ruleSetVersion = await _ruleSetRepo.GetByIdAsync(_testRuleSetVersionId);
        var priorityBlock = JsonSerializer.Deserialize<DemandPriorityBlock>(ruleSetVersion!.DemandPriorityJson);

        var demands = new List<DemandRecord>
        {
            new DemandRecord { DemandId = "D001", OrderId = "O001", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", CustomerTier = "A", RemainingTimeHours = 5 },
            new DemandRecord { DemandId = "D002", OrderId = "O002", CreatedAt = DateTime.UtcNow, DelayStatus = "ONTRACK", CustomerTier = "VIP", RemainingTimeHours = 20 },
            new DemandRecord { DemandId = "D003", OrderId = "O003", CreatedAt = DateTime.UtcNow, DelayStatus = "DELAYED", CustomerTier = "VIP", RemainingTimeHours = 2 }
        };

        var sorted = _matcher.SortDemands(demands, priorityBlock!);

        sorted.Should().HaveCount(3);
        sorted[0].OrderId.Should().Be("O003", "DELAYED订单中Hours最小(2)应排第一");
        sorted[1].OrderId.Should().Be("O001", "DELAYED订单中Hours第二小(5)");
        sorted[2].OrderId.Should().Be("O002", "ONTRACK订单排最后");
    }

    [Fact]
    public async System.Threading.Tasks.Task 验证器集成_检测合法配置()
    {
        await SetupTestDataAsync();

        var ruleSetVersion = await _ruleSetRepo.GetByIdAsync(_testRuleSetVersionId);
        var priorityBlock = JsonSerializer.Deserialize<DemandPriorityBlock>(ruleSetVersion!.DemandPriorityJson);

        var validationResult = _validator.Validate(priorityBlock!);
        validationResult.IsValid.Should().BeTrue();
    }

    private async System.Threading.Tasks.Task SetupTestDataAsync()
    {
        var now = DateTime.UtcNow;
        var uniqueSuffix = $"{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".Substring(0, 30);

        _testRuleSetId = await _connectionManager.QueryFirstOrDefaultAsync<long>(
            "INSERT INTO [dbo].[RuleSet] ([RuleSetCode], [RuleSetName], [Description], [IsActive], [CreatedAt], [CreatedBy]) VALUES (@Code, @Name, @Description, 1, @CreatedAt, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Code = $"TEST-RS-{uniqueSuffix}", Name = "集成测试规则集", Description = "Integration Test", CreatedAt = now, CreatedBy = "IntegrationTest" },
            db: DatabaseId.APS);

        var snapshot = DemandPriorityFixture.GetStandardPrioritySnapshot();
        var demandPriorityJson = JsonSerializer.Serialize(snapshot.DemandPriority);

        var ruleSetVersion = new Core.Entities.APS.RuleSetVersion
        {
            RuleSetId = _testRuleSetId,
            VersionCode = $"TEST-R-{now:yyyyMMddHHmmss}",
            DemandPriorityJson = demandPriorityJson,
            Status = "PUBLISHED",
            PublishedAt = now,
            PublishedBy = "IntegrationTest",
            CreatedAt = now,
            CreatedBy = "IntegrationTest"
        };

        var savedVersion = await _ruleSetRepo.AddAsync(ruleSetVersion);
        _testRuleSetVersionId = savedVersion.Id;
    }

    public void Dispose()
    {
        CleanupTestDataAsync().GetAwaiter().GetResult();
    }

    private async System.Threading.Tasks.Task CleanupTestDataAsync()
    {
        if (_testRuleSetVersionId > 0)
        {
            await _connectionManager.ExecuteAsync("DELETE FROM RuleSetVersion WHERE Id = @Id", new { Id = _testRuleSetVersionId }, db: DatabaseId.APS);
        }
        if (_testRuleSetId > 0)
        {
            await _connectionManager.ExecuteAsync("DELETE FROM RuleSet WHERE Id = @Id", new { Id = _testRuleSetId }, db: DatabaseId.APS);
        }
    }
}
