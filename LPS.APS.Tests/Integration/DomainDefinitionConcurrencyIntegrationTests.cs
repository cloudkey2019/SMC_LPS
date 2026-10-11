using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Repositories.Auth;
using LPS.APS.Engine.Repositories.Governance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// DomainDefinition 活跃 scope 组合唯一性并发测试（DOMAIN-05，3号位，0号位 复审 v1.1 N-05）。
/// 验证：sp_getapplock（ScopeType+ProductFamilyId+FactoryId 键）串行化治理写路径后，
/// 活跃组合唯一性在并发下保持（Create× / Enable× / Update×Enable 交错）；不同 Scope 不串行化。
/// 并发形态：每个并发任务独立 DatabaseConnectionManager + AuthDbContext（独立连接，指向同一共享库），
/// 与生产多连接并发一致——应用锁须跨连接串行化。主数据造数/活跃集合断言用共享 _cm 顺序执行。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class DomainDefinitionConcurrencyIntegrationTests : IDisposable
{
    private readonly DatabaseConnectionManager _cm;
    private readonly DomainDefinitionRepository _repo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly Mock<IDataScopeService> _scopeService;
    private readonly DomainDefinitionGovernanceService _service;
    private readonly string _uniqueSuffix;

    private readonly List<int> _createdDomainIds = new();
    private readonly List<int> _createdProductFamilyIds = new();
    private int _testProductFamilyId;
    private int _testFactoryId;

    public DomainDefinitionConcurrencyIntegrationTests()
    {
        _uniqueSuffix = $"{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".ToUpperInvariant()[..30];

        _cm = TestEnvironment.GetConnectionManager();
        var loggerFactory = LoggerFactory.Create(builder => { });

        _repo = new DomainDefinitionRepository(_cm);
        _auditRepo = CreateAuditRepository(loggerFactory);
        _scopeService = new Mock<IDataScopeService>();
        _scopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        _service = new DomainDefinitionGovernanceService(
            _repo,
            _auditRepo,
            _scopeService.Object);
    }

    /// <summary>构造审计仓储（Auth 库 EF Core）</summary>
    private static IAuditLogRepository CreateAuditRepository(ILoggerFactory loggerFactory)
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
        return new AuditLogRepository(new AuthDbContext(options), NullLogger<AuditLogRepository>.Instance);
    }

    /// <summary>
    /// 独立并发作用域：每个并发任务一个实例（独立连接 + 独立 AuthDbContext + 独立服务），
    /// 指向同一共享测试库——sp_getapplock 跨连接串行化（生产多连接并发的等价形态）。
    /// </summary>
    private sealed class IsolatedScope : IDisposable
    {
        public DatabaseConnectionManager Cm { get; }
        public AuthDbContext AuthDb { get; }
        public DomainDefinitionGovernanceService Service { get; }

        public IsolatedScope()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Test.json", optional: false)
                .AddJsonFile("appsettings.Test.Local.json", optional: true)
                .Build();

            var dbOptions = configuration.GetSection("Database").Get<LPS.APS.Engine.Configuration.DatabaseOptions>()
                ?? throw new InvalidOperationException("appsettings.Test.json 未配置 Database 节");
            Cm = new DatabaseConnectionManager(Options.Create(dbOptions));

            var authConn = configuration.GetSection("Database:Auth:ConnectionString").Value ?? string.Empty;
            AuthDb = new AuthDbContext(
                new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(authConn).Options);
            var auditRepo = new AuditLogRepository(AuthDb, NullLogger<AuditLogRepository>.Instance);

            var scopeService = new Mock<IDataScopeService>();
            scopeService
                .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(DataScopeContext.Global);

            Service = new DomainDefinitionGovernanceService(
                new DomainDefinitionRepository(Cm),
                auditRepo,
                scopeService.Object);
        }

        public void Dispose()
        {
            Cm.Dispose();
            AuthDb.Dispose();
        }
    }

    /// <summary>插入测试用 ProductFamily / Factory 主数据（唯一 Code，Dispose 清理）</summary>
    private async Task SetupMasterDataAsync()
    {
        _testProductFamilyId = await _cm.QueryFirstOrDefaultAsync<int>(
            "INSERT INTO [dbo].[ProductFamily] ([Code], [Name]) VALUES (@Code, @Name); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Code = $"PF_{_uniqueSuffix}", Name = $"测试产品族_{_uniqueSuffix}" },
            db: DatabaseId.APS);
        _createdProductFamilyIds.Add(_testProductFamilyId);

        _testFactoryId = await _cm.QueryFirstOrDefaultAsync<int>(
            "INSERT INTO [dbo].[Factory] ([Code], [Name]) VALUES (@Code, @Name); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Code = $"F_{_uniqueSuffix}", Name = $"测试工厂_{_uniqueSuffix}" },
            db: DatabaseId.APS);
    }

    /// <summary>插入额外测试用 ProductFamily（唯一 Code，Dispose 清理），用于 C4 不同组合并发</summary>
    private async Task<int> InsertAdditionalProductFamilyAsync(string suffix)
    {
        var id = await _cm.QueryFirstOrDefaultAsync<int>(
            "INSERT INTO [dbo].[ProductFamily] ([Code], [Name]) VALUES (@Code, @Name); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Code = $"PF_{_uniqueSuffix}_{suffix}", Name = $"测试产品族_{_uniqueSuffix}_{suffix}" },
            db: DatabaseId.APS);
        _createdProductFamilyIds.Add(id);
        return id;
    }

    /// <summary>构造 FAMILY 域定义入参（DomainKey 唯一）</summary>
    private DomainDefinition BuildFamilyInput(string? domainKeySuffix = null) => new()
    {
        DomainKey = $"DOM_{_uniqueSuffix}{(domainKeySuffix is null ? string.Empty : $"_{domainKeySuffix}")}",
        DomainName = $"测试域_{_uniqueSuffix}",
        ScopeType = "FAMILY",
        ProductFamilyId = _testProductFamilyId,
        FactoryId = null
    };

    private string SkipReason =>
        "测试环境缺 APS_Auth 库或 AuditLog 表（审计 DDL 未迁移），需 2号位 部署后转绿";

    [SkippableFact]
    public async Task C1_CreateXCreate_同scope组合并发_恰一个成功_活跃组合唯一()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();
        var input = BuildFamilyInput("C1");

        // 两个并发 Create（独立连接，同一 ScopeType+ProductFamilyId+FactoryId 组合，sp_getapplock 串行化）
        var successCount = 0;
        var exceptionMessages = new List<string>();
        await Task.WhenAll(
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try
                {
                    var created = await iso.Service.CreateAsync(input, 1, _uniqueSuffix);
                    Interlocked.Increment(ref successCount);
                    lock (_createdDomainIds) { _createdDomainIds.Add(created.Id); }
                }
                catch (Exception ex) { lock (exceptionMessages) { exceptionMessages.Add(ex.Message); } }
            }),
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try
                {
                    var created = await iso.Service.CreateAsync(input, 1, _uniqueSuffix);
                    Interlocked.Increment(ref successCount);
                    lock (_createdDomainIds) { _createdDomainIds.Add(created.Id); }
                }
                catch (Exception ex) { lock (exceptionMessages) { exceptionMessages.Add(ex.Message); } }
            }));

        // 恰一个成功，另一个被事务内唯一性复核拒绝（活跃组合唯一性保持）
        successCount.Should().Be(1, "同一 scope 组合并发 Create 只允许一个成功");
        exceptionMessages.Should().ContainSingle();
        exceptionMessages.Single().Should().Match("*已存在启用中的*仅允许一个启用域*");

        var active = await _service.GetActiveAsync();
        active.Count(d => d.ScopeType == "FAMILY" && d.ProductFamilyId == _testProductFamilyId).Should().Be(1);
    }

    [SkippableFact]
    public async Task C2_EnableXEnable_同scope组合并发_恰一个激活_活跃组合唯一()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();

        // 建 A（启用）→ 停用；再建 B（启用）→ 停用 → 两记录同组合均停用
        var a = await _service.CreateAsync(BuildFamilyInput("C2A"), 1, _uniqueSuffix);
        lock (_createdDomainIds) { _createdDomainIds.Add(a.Id); }
        await _service.SetActiveAsync(a.Id, false, 1, _uniqueSuffix);

        var b = await _service.CreateAsync(BuildFamilyInput("C2B"), 1, _uniqueSuffix);
        lock (_createdDomainIds) { _createdDomainIds.Add(b.Id); }
        await _service.SetActiveAsync(b.Id, false, 1, _uniqueSuffix);

        // 并发 Enable A + B（独立连接，同一组合，sp_getapplock 串行化）
        var successCount = 0;
        var exceptionMessages = new List<string>();
        await Task.WhenAll(
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try { await iso.Service.SetActiveAsync(a.Id, true, 1, _uniqueSuffix); Interlocked.Increment(ref successCount); }
                catch (Exception ex) { lock (exceptionMessages) { exceptionMessages.Add(ex.Message); } }
            }),
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try { await iso.Service.SetActiveAsync(b.Id, true, 1, _uniqueSuffix); Interlocked.Increment(ref successCount); }
                catch (Exception ex) { lock (exceptionMessages) { exceptionMessages.Add(ex.Message); } }
            }));

        successCount.Should().Be(1, "同一 scope 组合并发 Enable 只允许一个激活成功");
        exceptionMessages.Should().ContainSingle();
        exceptionMessages.Single().Should().Match("*已存在启用中的*仅允许一个启用域*");

        var active = await _service.GetActiveAsync();
        active.Count(d => d.ScopeType == "FAMILY" && d.ProductFamilyId == _testProductFamilyId).Should().Be(1);
    }

    [SkippableFact]
    public async Task C3_UpdateXEnable交错_活跃组合唯一_无竞态插入()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();

        // 建 A（启用）→ 停用；再建 B（启用）→ 此时组合被 B 占用
        var a = await _service.CreateAsync(BuildFamilyInput("C3A"), 1, _uniqueSuffix);
        lock (_createdDomainIds) { _createdDomainIds.Add(a.Id); }
        await _service.SetActiveAsync(a.Id, false, 1, _uniqueSuffix);

        var b = await _service.CreateAsync(BuildFamilyInput("C3B"), 1, _uniqueSuffix);
        lock (_createdDomainIds) { _createdDomainIds.Add(b.Id); }

        // 并发：Update B（改名，组合不变） + Enable A（组合被 B 占用 → 应被拒绝）
        var renamed = BuildFamilyInput("C3B");
        renamed.DomainName = "C3 交错改名";
        var updateSucceeded = false;
        var enableSucceeded = false;
        var enableError = string.Empty;
        await Task.WhenAll(
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try { await iso.Service.UpdateAsync(b.Id, renamed, 1, _uniqueSuffix); updateSucceeded = true; } catch { }
            }),
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try { await iso.Service.SetActiveAsync(a.Id, true, 1, _uniqueSuffix); enableSucceeded = true; }
                catch (Exception ex) { enableError = ex.Message; }
            }));

        updateSucceeded.Should().BeTrue("Update 不改组合，不受唯一性影响");
        enableSucceeded.Should().BeFalse("A 的组合已被 B 占用，Enable 必须 fail-closed");
        enableError.Should().Match("*已存在启用中的*仅允许一个启用域*");

        var active = await _service.GetActiveAsync();
        active.Count(d => d.ScopeType == "FAMILY" && d.ProductFamilyId == _testProductFamilyId).Should().Be(1);
    }

    [SkippableFact]
    public async Task C4_不同scope组合并发_均成功_不串行化()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();

        // 第二个不同 ProductFamily（不同 scope 组合），与第一个组合并发 Create
        var otherProductFamilyId = await InsertAdditionalProductFamilyAsync("C4OTHER");
        var inputX = BuildFamilyInput("C4X");
        var inputY = BuildFamilyInput("C4Y");
        inputY.ProductFamilyId = otherProductFamilyId;

        var successCount = 0;
        var exceptionMessages = new List<string>();
        await Task.WhenAll(
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try
                {
                    var created = await iso.Service.CreateAsync(inputX, 1, _uniqueSuffix);
                    Interlocked.Increment(ref successCount);
                    lock (_createdDomainIds) { _createdDomainIds.Add(created.Id); }
                }
                catch (Exception ex) { lock (exceptionMessages) { exceptionMessages.Add(ex.Message); } }
            }),
            Task.Run(async () =>
            {
                using var iso = new IsolatedScope();
                try
                {
                    var created = await iso.Service.CreateAsync(inputY, 1, _uniqueSuffix);
                    Interlocked.Increment(ref successCount);
                    lock (_createdDomainIds) { _createdDomainIds.Add(created.Id); }
                }
                catch (Exception ex) { lock (exceptionMessages) { exceptionMessages.Add(ex.Message); } }
            }));

        successCount.Should().Be(2, "不同 scope 组合并发不应被锁串行化");
        exceptionMessages.Should().BeEmpty();
    }

    public void Dispose()
    {
        // 清理顺序：先删 DomainDefinition（FK → ProductFamily/Factory），再删主数据，最后清审计
        foreach (var id in _createdDomainIds)
        {
            try
            {
                _cm.ExecuteAsync("DELETE FROM [dbo].[DomainDefinition] WHERE [Id] = @Id", new { Id = id }, db: DatabaseId.APS)
                    .GetAwaiter().GetResult();
            }
            catch
            {
                // 清理尽力而为
            }
        }

        foreach (var familyId in _createdProductFamilyIds)
        {
            try
            {
                _cm.ExecuteAsync(
                    "DELETE FROM [dbo].[ProductFamily] WHERE [Id] = @Id", new { Id = familyId }, db: DatabaseId.APS)
                    .GetAwaiter().GetResult();
            }
            catch { }
        }
        try
        {
            _cm.ExecuteAsync(
                "DELETE FROM [dbo].[Factory] WHERE [Id] = @Id", new { Id = _testFactoryId }, db: DatabaseId.APS)
                .GetAwaiter().GetResult();
        }
        catch { }

        try
        {
            _cm.ExecuteAsync(
                "DELETE FROM [dbo].[AuditLog] WHERE [EntityType] = 'DomainDefinition' AND [UserCode] = @UserCode",
                new { UserCode = _uniqueSuffix },
                db: DatabaseId.Auth).GetAwaiter().GetResult();
        }
        catch { }
    }
}
