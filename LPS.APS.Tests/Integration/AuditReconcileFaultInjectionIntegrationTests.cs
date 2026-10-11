using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Entities.Auth;
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
/// R-01（方案B）审计故障注入集成测试（AUDIT-06，3号位，0号位 复审 v1.1）。
/// 两层真实库证据（APS 用共享测试库真连接，Auth 故障注入只断本运行实例连接）：
///   A1 预检 fail-before-write：Auth 从起点不可达 → EnsureWritableAsync 探针失败 → 业务零落库 + 异常传播（P1-03 把守）；
///   A2 残余窗口（R-01 方案B 核心）：预检通过（真实探针成功）→ APS 业务提交 → AddAsync 注入失败 →
///      业务已落库 + 异常传播（fail-closed）+ AuditReconcile 结构化对账日志（含完整 JSON 供人工补录）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class AuditReconcileFaultInjectionIntegrationTests : IDisposable
{
    private readonly DatabaseConnectionManager _cm;
    private readonly string _uniqueSuffix;

    private readonly List<int> _createdDomainIds = new();
    private int _testProductFamilyId;
    private int _testFactoryId;

    public AuditReconcileFaultInjectionIntegrationTests()
    {
        _uniqueSuffix = $"{DateTime.Now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}".ToUpperInvariant()[..30];
        _cm = TestEnvironment.GetConnectionManager();
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

    private DomainDefinition BuildFamilyInput() => new()
    {
        DomainKey = $"DOM_{_uniqueSuffix}",
        DomainName = $"测试域_{_uniqueSuffix}",
        ScopeType = "FAMILY",
        ProductFamilyId = _testProductFamilyId,
        FactoryId = null
    };

    private async Task<long> CountDomainRowsAsync(string domainKey) =>
        await _cm.QueryFirstOrDefaultAsync<long>(
            "SELECT COUNT(*) FROM [dbo].[DomainDefinition] WHERE [DomainKey] = @DomainKey",
            new { DomainKey = domainKey }, db: DatabaseId.APS);

    /// <summary>构造服务（注入审计仓储与 SpyLogger，主数据共享库为真）</summary>
    private static DomainDefinitionGovernanceService BuildService(
        IAuditLogRepository auditRepo,
        DatabaseConnectionManager cm)
    {
        var scopeService = new Mock<IDataScopeService>();
        scopeService
            .Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        return new DomainDefinitionGovernanceService(
            new DomainDefinitionRepository(cm),
            auditRepo,
            scopeService.Object);
    }

    [SkippableFact]
    public async Task A1_预检fail_before_write_Auth不可达_业务零落库_异常传播()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "共享测试库缺 APS_Auth 环境，无法构造故障注入前提");

        await SetupMasterDataAsync();
        var input = BuildFamilyInput();

        // Auth 从起点不可达 → CreateAsync 前置 EnsureWritableAsync 探针先失败
        var brokenAuthConn = "Server=10.255.255.1;Database=APS_Auth;User Id=sa;Password=broken;Connect Timeout=2;TrustServerCertificate=true;";
        var brokenRepo = new AuditLogRepository(
            new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(brokenAuthConn).Options),
            new SpyLogger());
        var service = BuildService(brokenRepo, _cm);

        var act = async () => await service.CreateAsync(input, 1, _uniqueSuffix);
        await act.Should().ThrowAsync<Exception>("预检失败必须 fail-before-write（P1-03）");

        var rowCount = await CountDomainRowsAsync(input.DomainKey);
        rowCount.Should().Be(0, "预检失败 → 业务不得落库（fail-before-write）");
    }

    [SkippableFact]
    public async Task A2_残余窗口_预检过_AddAsync败_业务已落库_异常传播_对账日志含完整负载()
    {
        Skip.If(!TestEnvironment.IsAuthDbAvailable(), "共享测试库缺 APS_Auth 环境，无法构造故障注入前提");

        await SetupMasterDataAsync();
        var input = BuildFamilyInput();

        // 真实预检（Auth 可达，探针 INSERT+回滚成功）+ 注入点设在 AddAsync（模拟预检后、正式写前连接中断的残余窗口）
        var realAuthConn = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .AddJsonFile("appsettings.Test.Local.json", optional: true)
            .Build()
            .GetSection("Database:Auth:ConnectionString").Value ?? string.Empty;
        var spy = new SpyLogger();
        var innerRepo = new AuditLogRepository(
            new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(realAuthConn).Options),
            new SpyLogger());
        var faultRepo = new AddAsyncFaultInjectionRepository(innerRepo, spy);

        // 残余窗口：预检（EnsureWritableAsync）委托真实探针（成功），AddAsync 注入失败
        var service = BuildService(faultRepo, _cm);

        var act = async () => await service.CreateAsync(input, 1, _uniqueSuffix);
        await act.Should().ThrowAsync<Exception>("残余窗口审计写失败必须原样传播（P1-05 fail-closed）");

        // 业务状态已落库（跨库无 2PC：APS 已提交、审计缺——R-01 需对账的窗口）
        var rowCount = await CountDomainRowsAsync(input.DomainKey);
        rowCount.Should().Be(1, "残余窗口下业务写应已提交落库（审计失败不回滚 APS 业务）");
        var rowId = await _cm.QueryFirstOrDefaultAsync<int>(
            "SELECT [Id] FROM [dbo].[DomainDefinition] WHERE [DomainKey] = @DomainKey",
            new { DomainKey = input.DomainKey }, db: DatabaseId.APS);
        _createdDomainIds.Add(rowId);

        // 对账日志契约：Error 级 + 可检索标记「审计写入失败/需对账」+ 关键字段 + 完整 JSON 回放
        spy.LogLevel.Should().Be(LogLevel.Error);
        spy.Exception.Should().NotBeNull();
        IsReconcileState(spy.FormattedMessage ?? string.Empty, input.DomainKey).Should().BeTrue();
    }

    private static bool IsReconcileState(string text, string domainKey) =>
        text.Contains("审计写入失败")
        && text.Contains("需对账")
        && text.Contains("Action=Create")
        && text.Contains("EntityType=DomainDefinition")
        && text.Contains($"Remark=")
        && text.Contains(domainKey)
        && text.Contains("AuditLogJson=");

    /// <summary>
    /// AddAsync 故障注入仓储：EnsureWritableAsync 委托真实仓储（真实探针成功），
    /// AddAsync 模拟残余窗口失败（调用真实 AuditReconcile.Log 落对账日志后抛异常）。
    /// </summary>
    private sealed class AddAsyncFaultInjectionRepository : IAuditLogRepository
    {
        private readonly AuditLogRepository _inner;
        private readonly ILogger<AuditLogRepository> _reconcileLogger;

        public AddAsyncFaultInjectionRepository(AuditLogRepository inner, ILogger<AuditLogRepository> reconcileLogger)
        {
            _inner = inner;
            _reconcileLogger = reconcileLogger;
        }

        public Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public async Task<AuditLog> AddAsync(AuditLog entity, CancellationToken cancellationToken = default)
        {
            // 注入点：预检已过、业务已提交后，正式审计写失败（模拟连接中断/瞬态故障）
            var ex = new InvalidOperationException("模拟残余窗口：预检后 Auth 正式写连接中断");
            AuditReconcile.Log(_reconcileLogger, ex, entity);
            throw ex;
        }

        public Task EnsureWritableAsync(CancellationToken cancellationToken = default)
            => _inner.EnsureWritableAsync(cancellationToken); // 真实探针（真实 Auth 库）

        public Task<IEnumerable<AuditLog>> GetLogsByUserAsync(int userId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
            => _inner.GetLogsByUserAsync(userId, pageIndex, pageSize, cancellationToken);

        public Task<IEnumerable<AuditLog>> GetLogsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
            => _inner.GetLogsByDateRangeAsync(startDate, endDate, cancellationToken);

        public Task<IReadOnlyList<AuditLog>> GetByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
            => _inner.GetByEntityAsync(entityType, entityId, cancellationToken);

        public Task<IReadOnlyList<AuditLog>> QueryAsync(string? entityType = null, string? entityId = null, DateTime? from = null, DateTime? to = null, int? take = null, CancellationToken cancellationToken = default)
            => _inner.QueryAsync(entityType, entityId, from, to, take, cancellationToken);

        public Task<IReadOnlyList<AuditLog>> QueryPagedAsync(int? userId = null, string? action = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
            => _inner.QueryPagedAsync(userId, action, from, to, page, pageSize, cancellationToken);
    }

    /// <summary>ILogger 间谍：捕获最近一条日志的级别/异常/格式化消息（验证对账日志契约）。</summary>
    private sealed class SpyLogger : ILogger<AuditLogRepository>
    {
        public LogLevel? LogLevel { get; private set; }
        public Exception? Exception { get; private set; }
        public string? FormattedMessage { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogLevel = logLevel;
            Exception = exception;
            FormattedMessage = formatter(state, exception);
        }
    }

    public void Dispose()
    {
        foreach (var id in _createdDomainIds)
        {
            try
            {
                _cm.ExecuteAsync("DELETE FROM [dbo].[DomainDefinition] WHERE [Id] = @Id", new { Id = id }, db: DatabaseId.APS)
                    .GetAwaiter().GetResult();
            }
            catch { }
        }
        try
        {
            _cm.ExecuteAsync("DELETE FROM [dbo].[ProductFamily] WHERE [Id] = @Id", new { Id = _testProductFamilyId }, db: DatabaseId.APS)
                .GetAwaiter().GetResult();
        }
        catch { }
        try
        {
            _cm.ExecuteAsync("DELETE FROM [dbo].[Factory] WHERE [Id] = @Id", new { Id = _testFactoryId }, db: DatabaseId.APS)
                .GetAwaiter().GetResult();
        }
        catch { }
    }
}
