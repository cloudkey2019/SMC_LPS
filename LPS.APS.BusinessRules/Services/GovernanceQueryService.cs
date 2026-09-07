using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// 治理查询业务服务（G4/G7查询）
///
/// 5号位提供给4号位的只读查询服务
/// 直接读取APS_Production事实表，不重算业务
/// </summary>
public class GovernanceQueryService
{
    private readonly IGovernanceQueryRepository _repository;

    public GovernanceQueryService(IGovernanceQueryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 查询排程运行列表（G4）
    /// </summary>
    public async Task<List<ScheduleRunGov>> QueryScheduleRunsAsync(
        string? status = null,
        string? runType = null,
        int take = 100,
        CancellationToken ct = default)
    {
        if (take <= 0 || take > 200) take = 100;

        return await _repository.QueryScheduleRunsAsync(status, runType, take, ct);
    }

    /// <summary>
    /// 查询排程域依赖（G7）
    /// </summary>
    public async Task<List<DomainDependencyDto>> QueryDomainDependenciesAsync(
        string? domainCode = null,
        string? direction = null,
        CancellationToken ct = default)
    {
        return await _repository.QueryDomainDependenciesAsync(domainCode, direction, ct);
    }
}
