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
using Moq;
using Xunit;
using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;

namespace LPS.APS.Tests.Integration;

/// <summary>
/// DomainDefinition 治理（E-1，3号位）集成测试
/// 测试范围（真实 APS_Production / APS_Auth 库 + 真实仓储 + 真实服务编排）：
///   G-D01 新建（CRUD 主流程 + 审计）
///   G-D02/G-D04 DomainKey 唯一性
///   G-D03 ScopeType 合法性
///   G-D05 FACTORY_FAMILY 必须工厂 / FAMILY 不得工厂 / 引用合法性
///   G-D16 停用/启用 + 审计
/// 依赖 APS_Auth.AuditLog 表（审计），缺表时动态 Skip。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class DomainDefinitionGovernanceIntegrationTests : IDisposable
{
    private readonly DatabaseConnectionManager _cm;
    private readonly DomainDefinitionRepository _repo;
    private readonly IAuditLogRepository _auditRepo;
    private readonly Mock<IDataScopeService> _scopeService;
    private readonly DomainDefinitionGovernanceService _service;
    private readonly string _uniqueSuffix;

    private readonly List<int> _createdDomainIds = new();
    private int _testProductFamilyId;
    private int _testFactoryId;

    public DomainDefinitionGovernanceIntegrationTests()
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
            _scopeService.Object,
            loggerFactory.CreateLogger<DomainDefinitionGovernanceService>());
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
        return new AuditLogRepository(new AuthDbContext(options));
    }

    /// <summary>插入测试用 ProductFamily / Factory 主数据（唯一 Code，Dispose 清理）</summary>
    private async Task SetupMasterDataAsync()
    {
        _testProductFamilyId = await _cm.QueryFirstOrDefaultAsync<int>(
            "INSERT INTO [dbo].[ProductFamily] ([Code], [Name]) VALUES (@Code, @Name); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Code = $"PF_{_uniqueSuffix}", Name = $"测试产品族_{_uniqueSuffix}" },
            db: DatabaseId.APS);

        _testFactoryId = await _cm.QueryFirstOrDefaultAsync<int>(
            "INSERT INTO [dbo].[Factory] ([Code], [Name]) VALUES (@Code, @Name); SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Code = $"F_{_uniqueSuffix}", Name = $"测试工厂_{_uniqueSuffix}" },
            db: DatabaseId.APS);
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
    public async Task G_D01_新建FAMILY域_成功并审计_当前有效集合含新域()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();
        var input = BuildFamilyInput();

        var created = await _service.CreateAsync(input, 1, _uniqueSuffix);
        _createdDomainIds.Add(created.Id);

        created.Id.Should().BeGreaterThan(0);
        created.IsActive.Should().BeTrue();
        created.SortOrder.Should().Be(100); // 未指定 SortOrder → 默认 100

        var loaded = await _service.GetByIdAsync(created.Id);
        loaded.Should().NotBeNull();
        loaded!.DomainKey.Should().Be(input.DomainKey);
        loaded.ScopeType.Should().Be("FAMILY");
        loaded.FactoryId.Should().BeNull();

        var active = await _service.GetActiveAsync();
        active.Should().Contain(d => d.Id == created.Id);

        var logs = await _auditRepo.GetByEntityAsync("DomainDefinition", created.Id.ToString());
        logs.Should().NotBeEmpty();
        logs.First().ActionCode.Should().Be("Create");
        logs.First().NewValue.Should().Be("Active");
    }

    [SkippableFact]
    public async Task G_D02_DomainKey重复_拒绝()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();
        var input = BuildFamilyInput();
        var created = await _service.CreateAsync(input, 1, _uniqueSuffix);
        _createdDomainIds.Add(created.Id);

        Func<Task> act = () => _service.CreateAsync(input, 1, _uniqueSuffix);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*DomainKey*已存在*");
    }

    [SkippableFact]
    public async Task G_D03_ScopeType非法_拒绝()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();
        var input = BuildFamilyInput();
        input.ScopeType = "CUSTOMER";

        Func<Task> act = () => _service.CreateAsync(input, 1, _uniqueSuffix);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*ScopeType*");
    }

    [SkippableFact]
    public async Task G_D05_FACTORY_FAMILY必须工厂_FAMILY不得工厂_引用合法性()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();

        // FACTORY_FAMILY 未指定工厂 → 拒绝
        var missingFactory = new DomainDefinition
        {
            DomainKey = $"DOM_FF_{_uniqueSuffix}",
            DomainName = "测试工厂族域",
            ScopeType = "FACTORY_FAMILY",
            ProductFamilyId = _testProductFamilyId,
            FactoryId = null
        };
        Func<Task> actMissingFactory = () => _service.CreateAsync(missingFactory, 1, _uniqueSuffix);
        (await actMissingFactory.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*必须指定 FactoryId*");

        // FACTORY_FAMILY 工厂不存在 → 拒绝
        var badFactory = new DomainDefinition
        {
            DomainKey = $"DOM_FF_{_uniqueSuffix}",
            DomainName = "测试工厂族域",
            ScopeType = "FACTORY_FAMILY",
            ProductFamilyId = _testProductFamilyId,
            FactoryId = 999999999
        };
        Func<Task> actBadFactory = () => _service.CreateAsync(badFactory, 1, _uniqueSuffix);
        (await actBadFactory.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*工厂不存在*");

        // FAMILY 指定工厂 → 拒绝
        var familyWithFactory = BuildFamilyInput();
        familyWithFactory.FactoryId = _testFactoryId;
        Func<Task> actFamilyWithFactory = () => _service.CreateAsync(familyWithFactory, 1, _uniqueSuffix);
        (await actFamilyWithFactory.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*不得指定 FactoryId*");

        // 产品族不存在 → 拒绝
        var badFamily = BuildFamilyInput("BADFAMILY");
        badFamily.ProductFamilyId = 999999999;
        Func<Task> actBadFamily = () => _service.CreateAsync(badFamily, 1, _uniqueSuffix);
        (await actBadFamily.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*产品族不存在*");

        // FACTORY_FAMILY 合法 → 成功
        var validFactoryFamily = new DomainDefinition
        {
            DomainKey = $"DOM_FF_{_uniqueSuffix}",
            DomainName = "测试工厂族域",
            ScopeType = "FACTORY_FAMILY",
            ProductFamilyId = _testProductFamilyId,
            FactoryId = _testFactoryId
        };
        var created = await _service.CreateAsync(validFactoryFamily, 1, _uniqueSuffix);
        _createdDomainIds.Add(created.Id);
        created.FactoryId.Should().Be(_testFactoryId);
    }

    [SkippableFact]
    public async Task G_D04_编辑_DomainKey不可变更_其余字段可更新()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();
        var created = await _service.CreateAsync(BuildFamilyInput(), 1, _uniqueSuffix);
        _createdDomainIds.Add(created.Id);

        // 变更 DomainKey → 拒绝
        var changedKey = BuildFamilyInput("CHANGED");
        Func<Task> act = () => _service.UpdateAsync(created.Id, changedKey, 1, _uniqueSuffix);
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*不可变更*");

        // 同 Key 改 DomainName → 成功
        var renamed = BuildFamilyInput();
        renamed.DomainName = "改名后的测试域";
        var updated = await _service.UpdateAsync(created.Id, renamed, 1, _uniqueSuffix);
        updated.DomainKey.Should().Be(created.DomainKey);
        updated.DomainName.Should().Be("改名后的测试域");

        var loaded = await _service.GetByIdAsync(created.Id);
        loaded!.DomainName.Should().Be("改名后的测试域");
    }

    [SkippableFact]
    public async Task G_D16_停用与启用_有效集合联动并审计()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();
        var created = await _service.CreateAsync(BuildFamilyInput(), 1, _uniqueSuffix);
        _createdDomainIds.Add(created.Id);

        // 停用
        var disabled = await _service.SetActiveAsync(created.Id, false, 1, _uniqueSuffix);
        disabled.IsActive.Should().BeFalse();
        (await _service.GetActiveAsync()).Should().NotContain(d => d.Id == created.Id);

        // 启用
        var enabled = await _service.SetActiveAsync(created.Id, true, 1, _uniqueSuffix);
        enabled.IsActive.Should().BeTrue();
        (await _service.GetActiveAsync()).Should().Contain(d => d.Id == created.Id);

        // 审计：含 Disable + Enable
        var logs = await _auditRepo.GetByEntityAsync("DomainDefinition", created.Id.ToString());
        logs.Select(l => l.ActionCode).Should().Contain(new[] { "Disable", "Enable" });
    }

    [SkippableFact]
    public async Task G_D17_业务范围未授权Domain_创建拒绝()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable() || !TestEnvironment.HasAuditLogTable(), SkipReason);

        await SetupMasterDataAsync();

        // 操作者仅授权 OTHER_DOMAIN，非目标域 → 业务范围校验 fail-closed 拒绝
        _scopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Domain, "OTHER_DOMAIN") }));

        var input = BuildFamilyInput();

        var act = async () => await _service.CreateAsync(input, 1, _uniqueSuffix);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*无权操作*");
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

        try
        {
            _cm.ExecuteAsync(
                "DELETE FROM [dbo].[ProductFamily] WHERE [Id] = @Id", new { Id = _testProductFamilyId }, db: DatabaseId.APS)
                .GetAwaiter().GetResult();
        }
        catch { }
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
