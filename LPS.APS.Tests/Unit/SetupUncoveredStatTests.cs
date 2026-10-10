using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using LPS.APS.Application.Services;
using LPS.APS.Core.DTOs.Setup;
using LPS.APS.Core.Interfaces;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// Setup 规则缺失聚合（#10 uncovered-stats）服务层测试。
/// 仓储负责 SQL 聚合 + Code 回带（Dapper，产线联调覆盖真实 SQL）；
/// 服务层为薄透传（<see cref="SetupRuleService.GetUncoveredStatsAsync"/>），
/// 本测试验证过滤参数转发 + 空结果 → 空列表 + 结果透传。
/// </summary>
public class SetupUncoveredStatTests
{
    private readonly Mock<ISetupUncoveredStatRepository> _repo = new();
    private readonly SetupRuleService _service;

    public SetupUncoveredStatTests()
    {
        _service = new SetupRuleService(
            Mock.Of<IRuleSetVersionRepository>(),
            Mock.Of<IMasterDataLookupRepository>(),
            Mock.Of<IAuditLogRepository>(),
            Mock.Of<IGovernanceVersionService>(),
            _repo.Object,
            Mock.Of<IDataScopeService>());
    }

    [Fact]
    public async Task GetUncoveredStatsAsync_透传仓储结果()
    {
        var expected = new List<SetupUncoveredStatDto>
        {
            new() { ScheduleRunId = 7, DepartmentId = 3, DepartmentCode = "DEPT-A", OperationCode = "OP-01", ResourceId = 1, ResourceCode = "R1", MaterialId = 100, MaterialCode = "M-100", HitCount = 12, SampleTaskId = 9001 },
        };
        _repo.Setup(r => r.GetUncoveredAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _service.GetUncoveredStatsAsync(null, null, null, null, null, CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Single(result);
        Assert.Equal("DEPT-A", result[0].DepartmentCode);
        Assert.Equal(12, result[0].HitCount);
    }

    [Fact]
    public async Task GetUncoveredStatsAsync_空结果返回空列表()
    {
        _repo.Setup(r => r.GetUncoveredAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SetupUncoveredStatDto>());

        var result = await _service.GetUncoveredStatsAsync(null, null, null, null, null, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUncoveredStatsAsync_转发四级过滤参数()
    {
        _repo.Setup(r => r.GetUncoveredAsync(7, 3, "OP-01", 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SetupUncoveredStatDto>());

        await _service.GetUncoveredStatsAsync(7, 3, "OP-01", 1, 100, CancellationToken.None);

        _repo.Verify(r => r.GetUncoveredAsync(7, 3, "OP-01", 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }
}