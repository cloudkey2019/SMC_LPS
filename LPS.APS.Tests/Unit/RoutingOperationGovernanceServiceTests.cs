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
/// 工序工艺属性（OPM）治理服务单元测试（T1，3号位）。
/// 覆盖：构造空值守卫、ListOperationsAsync 的 materialId 守卫与结果透传、
/// UpdateOperationPlanningModeAsync 的 operationId 守卫 + OPM 非法值拒绝（服务层异常传播）。
/// 说明：与 RoutingOperationPlanningModeTests（仅测纯静态 IsValid / DTO）互补，本文件测服务类行为。
/// Update 的 DB 写库 happy path 依赖非 virtual 的 QueryFirstOrDefaultAsync/ExecuteAsync，须由真实库集成测试覆盖。
/// </summary>
public class RoutingOperationGovernanceServiceTests
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

    private RoutingOperationGovernanceService CreateService(Mock<DatabaseConnectionManager> connection)
        => new(connection.Object, _auditRepo.Object, _dataScope.Object, Mock.Of<ILogger<RoutingOperationGovernanceService>>());

    // ---------- 构造守卫 ----------

    [Fact]
    public void 构造_connectionManager为null_抛ArgumentNullException()
    {
        Action act = () => new RoutingOperationGovernanceService(
            null!, _auditRepo.Object, _dataScope.Object, Mock.Of<ILogger<RoutingOperationGovernanceService>>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionManager");
    }

    [Fact]
    public void 构造_auditLogRepository为null_抛ArgumentNullException()
    {
        Action act = () => new RoutingOperationGovernanceService(
            CreateConnectionMock().Object, null!, _dataScope.Object, Mock.Of<ILogger<RoutingOperationGovernanceService>>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("auditLogRepository");
    }

    [Fact]
    public void 构造_dataScopeService为null_抛ArgumentNullException()
    {
        Action act = () => new RoutingOperationGovernanceService(
            CreateConnectionMock().Object, _auditRepo.Object, null!, Mock.Of<ILogger<RoutingOperationGovernanceService>>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("dataScopeService");
    }

    [Fact]
    public void 构造_logger为null_抛ArgumentNullException()
    {
        Action act = () => new RoutingOperationGovernanceService(
            CreateConnectionMock().Object, _auditRepo.Object, _dataScope.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ---------- ListOperationsAsync ----------

    [Fact]
    public async Task ListOperationsAsync_materialId非正_抛ArgumentException()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.ListOperationsAsync(0, 1, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("materialId");
    }

    [Fact]
    public async Task ListOperationsAsync_返回工序()
    {
        var rows = new List<RoutingOperationDto>
        {
            new() { Id = 1, MaterialId = 100, OperationCode = "OP10", OperationPlanningMode = OperationPlanningModeValues.FiniteResource },
            new() { Id = 2, MaterialId = 100, OperationCode = "OP20", OperationPlanningMode = OperationPlanningModeValues.Unconstrained },
        };
        // 3-NEW-P0-02：默认 Global 用户（不过滤，与旧语义一致）。
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        var conn = CreateConnectionMock();
        conn.Setup(c => c.QueryAsync<RoutingOperationDto>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(rows);

        var result = await CreateService(conn).ListOperationsAsync(100, 1);

        result.Should().HaveCount(2);
        result[0].OperationCode.Should().Be("OP10");
        result[1].OperationPlanningMode.Should().Be(OperationPlanningModeValues.Unconstrained);

        conn.Verify(c => c.QueryAsync<RoutingOperationDto>(
            It.Is<string>(s => s.Contains("MaterialId = @MaterialId")),
            It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Once);
    }

    // ---------- 3-NEW-P0-02（0号位 再审核）Department Scope 列表过滤 ----------

    [Fact]
    public async Task ListOperationsAsync_非Global无部门授权_返回空列表不查库()
    {
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Factory, "BJ") }));
        var conn = CreateConnectionMock();

        var result = await CreateService(conn).ListOperationsAsync(100, 1);

        result.Should().BeEmpty();
        conn.Verify(c => c.QueryAsync<RoutingOperationDto>(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task ListOperationsAsync_非Global仅返回授权部门工序()
    {
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.FromPolicies(new[] { (DataScopeTypes.Department, "DEPT_A") }));
        var conn = CreateConnectionMock();
        conn.Setup(c => c.QueryAsync<int>(
                It.Is<string>(s => s.Contains("SELECT Id FROM ProductionDepartment WHERE DeptCode IN @Codes")),
                It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<int> { 101 });
        conn.Setup(c => c.QueryAsync<RoutingOperationDto>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(new List<RoutingOperationDto>
            {
                new() { Id = 1, MaterialId = 100, ProductionDepartmentId = 101, OperationCode = "OP10", OperationPlanningMode = OperationPlanningModeValues.FiniteResource },
            });

        var result = await CreateService(conn).ListOperationsAsync(100, 1);

        result.Should().ContainSingle().Which.OperationCode.Should().Be("OP10");
        conn.Verify(c => c.QueryAsync<RoutingOperationDto>(
            It.Is<string>(s => s.Contains("ProductionDepartmentId IN @AllowedDeptIds")),
            It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task ListOperationsAsync_Global不过滤()
    {
        _dataScope.Setup(s => s.ResolveScopeAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataScopeContext.Global);
        var rows = new List<RoutingOperationDto>
        {
            new() { Id = 1, MaterialId = 100, ProductionDepartmentId = 101, OperationCode = "OP10", OperationPlanningMode = OperationPlanningModeValues.FiniteResource },
            new() { Id = 2, MaterialId = 100, ProductionDepartmentId = 202, OperationCode = "OP20", OperationPlanningMode = OperationPlanningModeValues.Unconstrained },
        };
        var conn = CreateConnectionMock();
        conn.Setup(c => c.QueryAsync<RoutingOperationDto>(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()))
            .ReturnsAsync(rows);

        var result = await CreateService(conn).ListOperationsAsync(100, 1);

        result.Should().HaveCount(2);
        conn.Verify(c => c.QueryAsync<RoutingOperationDto>(
            It.Is<string>(s => s.Contains("MaterialId = @MaterialId") && !s.Contains("IN @AllowedDeptIds")),
            It.IsAny<object>(), It.IsAny<CommandType>(), It.IsAny<DatabaseId>(), It.IsAny<int?>()), Times.Once);
    }

    // ---------- UpdateOperationPlanningModeAsync ----------

    [Fact]
    public async Task UpdateOperationPlanningModeAsync_operationId非正_抛ArgumentException()
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.UpdateOperationPlanningModeAsync(
            0, OperationPlanningModeValues.Unconstrained, 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("operationId");
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("")]
    public async Task UpdateOperationPlanningModeAsync_mode非法_抛红线异常(string mode)
    {
        var svc = CreateService(CreateConnectionMock());
        Func<Task> act = () => svc.UpdateOperationPlanningModeAsync(1, mode, 1, "u", CancellationToken.None);

        await act.Should().ThrowAsync<SetupRuleDataRedLineException>();
    }
}