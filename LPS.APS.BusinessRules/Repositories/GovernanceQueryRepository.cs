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
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedDomains = null)
    {
        // Dapper 列表参数只进 IN；@AllowedDomains IS NULL 标量判空会随列表一起扩成 (…) 导致 4145，用 Has 标志替代；空集 fail-closed 返空。
        // 注意 "all" 分支含两处 IN @AllowedDomains 均保留，仅标量判空改为 Has 标志。
        if (allowedDomains is { Count: 0 })
            return new List<DomainDependencyDto>();
        var hasDomains = allowedDomains is { Count: > 0 };

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
    AND (@HasDomains = 0 OR DownstreamDomainCode IN @AllowedDomains)
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
    AND (@HasDomains = 0 OR UpstreamDomainCode IN @AllowedDomains)
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
    AND (@HasDomains = 0
         OR UpstreamDomainCode IN @AllowedDomains
         OR DownstreamDomainCode IN @AllowedDomains)
ORDER BY UpstreamDomainCode, DownstreamDomainCode";
        }

        var parameters = new
        {
            DomainCode = domainCode,
            HasDomains = hasDomains,
            AllowedDomains = allowedDomains
        };

        var results = await _connectionManager.QueryAsync<DomainDependencyDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }
}
