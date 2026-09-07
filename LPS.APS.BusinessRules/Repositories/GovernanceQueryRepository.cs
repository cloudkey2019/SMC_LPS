using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// 治理查询Repository实现（G4/G7查询）
/// 5号位直接读取APS_Production事实表
/// </summary>
public class GovernanceQueryRepository : IGovernanceQueryRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public GovernanceQueryRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<ScheduleRunGov>> QueryScheduleRunsAsync(
        string? status = null,
        string? runType = null,
        int take = 100,
        CancellationToken ct = default)
    {
        var sql = @"
SELECT TOP (@Take)
    Id,
    RunType,
    Status,
    TriggeredBy,
    DataCutoffTime,
    StrategyProfileVersionId,
    ExpectedDomainKeysJson,
    StartedAt,
    CompletedAt,
    ErrorMessage
FROM ScheduleRun
WHERE 1=1
    AND (@Status IS NULL OR Status = @Status)
    AND (@RunType IS NULL OR RunType = @RunType)
ORDER BY Id DESC";

        var parameters = new
        {
            Status = status,
            RunType = runType,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<ScheduleRunGov>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<List<DomainDependencyDto>> QueryDomainDependenciesAsync(
        string? domainCode = null,
        string? direction = null,
        CancellationToken ct = default)
    {
        string sql;

        if (direction == "upstream")
        {
            // 查本域依赖谁（upstream = 本域是Downstream，查Upstream）
            sql = @"
SELECT
    UpstreamDomainCode,
    DownstreamDomainCode,
    ChildMaterialCode,
    DefaultLeadTimeDays,
    ScannedAt
FROM Domain_Dependency
WHERE DownstreamDomainCode = @DomainCode
ORDER BY UpstreamDomainCode";
        }
        else if (direction == "downstream")
        {
            // 查谁依赖本域（downstream = 本域是Upstream，查Downstream）
            sql = @"
SELECT
    UpstreamDomainCode,
    DownstreamDomainCode,
    ChildMaterialCode,
    DefaultLeadTimeDays,
    ScannedAt
FROM Domain_Dependency
WHERE UpstreamDomainCode = @DomainCode
ORDER BY DownstreamDomainCode";
        }
        else
        {
            // 查所有依赖
            sql = @"
SELECT
    UpstreamDomainCode,
    DownstreamDomainCode,
    ChildMaterialCode,
    DefaultLeadTimeDays,
    ScannedAt
FROM Domain_Dependency
WHERE 1=1
    AND (@DomainCode IS NULL
         OR UpstreamDomainCode = @DomainCode
         OR DownstreamDomainCode = @DomainCode)
ORDER BY UpstreamDomainCode, DownstreamDomainCode";
        }

        var parameters = new
        {
            DomainCode = domainCode
        };

        var results = await _connectionManager.QueryAsync<DomainDependencyDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }
}
