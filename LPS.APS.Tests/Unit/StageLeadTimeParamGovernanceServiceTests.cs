using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Exceptions;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Configuration;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 阶段提前期参数治理服务单元测试（StageLeadTimeParam，3号位 规则参数体系）。
/// 覆盖：构造空值守卫、ListAsync 透传与过滤条件拼接、Create/Update/Deactivate 的输入校验红线
/// （FactoryCode/StageCode 非空、提前期非负且不同时为 0、Priority 非负、EffectiveTo ≥ EffectiveFrom）。
/// 说明：Create/Update/Deactivate 的 DB 写库 happy path 依赖 DatabaseConnectionManager 的非 virtual
/// QueryFirstOrDefaultAsync/ExecuteAsync，须由真实库集成测试覆盖，本单测不企及（见类注释边界）。
/// </summary>
public class StageLeadTimeParamGovernanceServiceTests
{
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<IDataScopeService> _dataScope = new();

    private static Mock<DatabaseConnectionManager> CreateConnectionMock()
    {
        var options = new DatabaseOptions
        {
            APS = new DatabaseConnectionOptions { ConnectionString = "Server=localhost;Database=APS_Production;" },
            ODS = new DatabaseConnectionOptions { ConnectionString = "Server=localhost;Database=MES_Integration;" },
            Auth = new DatabaseConnectionOptions { ConnectionString = "Server=localhost;Database=APS_Auth;" },
        };
        return new Mock<DatabaseConnectionManager>(Options.Create(options));
    }

    /// <summary>构造服务；默认 Scope=Global（全放行），越权用例在测试内单独 Setup。</summary>
    private StageLeadTimeParamGovernanceService CreateService(Mock<DatabaseConnectionManager> connection)
    {
        _dataScope.Reset();
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        return new(connection.Object, _auditRepo.Object, _dataScope.Object, Mock.Of<ILogger<StageLeadTimeParamGovernanceService>>());
    }

    private static SaveStageLeadTimeParamRequest Request(
        string factoryCode = "BJ",
        string stageCode = "BJ_ASSY",
        decimal leadTimeDays = 1,
        decimal leadTimeHours = 0,
        int priority = 0,
        DateTime? effectiveFrom = null,
        DateTime? effectiveTo = null)
        => new()
        {
            FactoryCode = factoryCode,
            StageCode = stageCode,
            LeadTimeDays = leadTimeDays,
            LeadTimeHours = leadTimeHours,
            Priority = priority,
            EffectiveFrom = effectiveFrom ?? new DateTime(2026, 1, 1),
            EffectiveTo = effectiveTo,
        };

    // ---------- 构造守卫 ----------

    [Fact]
    public void 构造_connectionManager为null_抛ArgumentNullException()
    {
        Action act = () => new StageLeadTimeParamGovernanceService(
            null!, _auditRepo.Object, Mock.Of<IDataScopeService>(), Mock.Of<ILogger<StageLeadTimeParamGovernanceService>>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionManager");
    }

    [Fact]
    public void 构造_auditLogRepository为null_抛ArgumentNullException()
    {
        Action act = () => new StageLeadTimeParamGovernanceService(
            CreateConnectionMock().Object, null!, Mock.Of<IDataScopeService>(), Mock.Of<ILogger<StageLeadTimeParamGovernanceService>>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("auditLogRepository");
    }

    [Fact]
    public void 构造_dataScopeService为null_抛ArgumentNullException()
    {
        Action act = () => new StageLeadTimeParamGovernanceService(
            CreateConnectionMock().Object, _auditRepo.Object, null!, Mock.Of<ILogger<StageLeadTimeParamGovernanceService>>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("dataScopeService");
    }

    [Fact]
    public void 构造_logger为null_抛ArgumentNullException()
    {
        Action act = () => new StageLeadTimeParamGovernanceService(
            CreateConnectionMock().Object, _auditRepo.Object, Mock.Of<IDataScopeService>(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ---------- ListAsync ----------

    [Fact]
    public async Task ListAsync_返回行()
    {
        var rows = new List<StageLeadTimeParamDto>
        {
            new() { Id = 1, FactoryCode = "BJ", StageCode = "BJ_ASSY", IsActive = true },
            new() { Id = 2, FactoryCode = "BJ", StageCode = "BJ_FINAL", IsActive = true },
        };
        var conn = CreateConnectionMock();
        conn.Setup(c => c.QueryAsync<StageLeadTimeParamDto>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(rows);

        var result = await CreateService(conn).ListAsync(null, null, null, null, actorUserId: 1);

        result.Should().HaveCount(2);
        result[0].StageCode.Should().Be("BJ_ASSY");
        result[1].StageCode.Should().Be("BJ_FINAL");
    }

    [Fact]
    public async Task ListAsync_带过滤_拼入对应条件()
    {
        var conn = CreateConnectionMock();
        conn.Setup(c => c.QueryAsync<StageLeadTimeParamDto>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<StageLeadTimeParamDto>());

        await CreateService(conn).ListAsync("BJ", "BJ_ASSY", "DEPT-1", true, actorUserId: 1);

        conn.Verify(c => c.QueryAsync<StageLeadTimeParamDto>(
            It.Is<string>(s => s.Contains("AND FactoryCode = @FactoryCode")
                            && s.Contains("AND StageCode = @StageCode")
                            && s.Contains("AND ProductionDeptCode = @ProductionDeptCode")
                            && s.Contains("AND IsActive = @IsActive")),
            It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Once);
    }

    // ---------- P0-03 Business Scope（0号位 审核 2026-10-09） ----------

    [Fact]
    public async Task ListAsync_非Global工厂与部门授权_拼入IN条件()
    {
        // P0-03：非 Global 用户按 Factory/Department 授权集合过滤（Auth §9.3 禁止先全量再前端隐藏）。
        var conn = CreateConnectionMock();
        conn.Setup(c => c.QueryAsync<StageLeadTimeParamDto>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<StageLeadTimeParamDto>());

        var svc = CreateService(conn);
        _dataScope.Reset();
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[]
            {
                (DataScopeTypes.Factory, "BJ"),
                (DataScopeTypes.Department, "DEPT-1"),
            }));

        await svc.ListAsync(null, null, null, null, actorUserId: 1);

        conn.Verify(c => c.QueryAsync<StageLeadTimeParamDto>(
            It.Is<string>(s => s.Contains("AND FactoryCode IN @Factories")
                            && s.Contains("AND (ProductionDeptCode IS NULL OR ProductionDeptCode IN @Depts)")),
            It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_无任何授权_返回空列表且不查库()
    {
        // P0-03：无任何业务范围 → fail-closed 空列表（禁止泄露其它工厂/部门数据）。
        var conn = CreateConnectionMock();
        var svc = CreateService(conn);
        _dataScope.Reset();
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Empty);

        var result = await svc.ListAsync(null, null, null, null, actorUserId: 1);

        result.Should().BeEmpty();
        conn.Verify(c => c.QueryAsync<StageLeadTimeParamDto>(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_工厂越权_抛ScopeViolationException()
    {
        // P0-03：Factory=SH 越出授权（仅 BJ）→ 403（写路径 fail-closed）。
        var svc = CreateService(CreateConnectionMock());
        _dataScope.Reset();
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));

        Func<Task> act = () => svc.CreateAsync(Request(factoryCode: "SH"), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<ScopeViolationException>();
    }

    // ---------- Create 校验红线 ----------

    [Fact]
    public async Task CreateAsync_FactoryCode为空_抛ArgumentException()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.CreateAsync(Request(factoryCode: ""), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("FactoryCode");
    }

    [Fact]
    public async Task CreateAsync_StageCode为空_抛ArgumentException()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.CreateAsync(Request(stageCode: "  "), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("StageCode");
    }

    [Fact]
    public async Task CreateAsync_LeadTimeDays为负_抛红线异常()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.CreateAsync(Request(leadTimeDays: -1), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<SetupRuleDataRedLineException>();
    }

    [Fact]
    public async Task CreateAsync_LeadTimeHours为负_抛红线异常()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.CreateAsync(Request(leadTimeHours: -1), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<SetupRuleDataRedLineException>();
    }

    [Fact]
    public async Task CreateAsync_提前期同时为0_抛红线异常()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.CreateAsync(Request(leadTimeDays: 0, leadTimeHours: 0), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<SetupRuleDataRedLineException>();
    }

    [Fact]
    public async Task CreateAsync_Priority为负_抛红线异常()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.CreateAsync(Request(priority: -1), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<SetupRuleDataRedLineException>();
    }

    [Fact]
    public async Task CreateAsync_EffectiveTo早于EffectiveFrom_抛红线异常()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.CreateAsync(
            Request(effectiveFrom: new DateTime(2026, 1, 2), effectiveTo: new DateTime(2026, 1, 1)),
            1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<SetupRuleDataRedLineException>();
    }

    // ---------- Update / Deactivate 守卫 ----------

    [Fact]
    public async Task UpdateAsync_Id非正_抛ArgumentException()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.UpdateAsync(0, Request(), 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public async Task DeactivateAsync_Id非正_抛ArgumentException()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.DeactivateAsync(0, 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("id");
    }
}