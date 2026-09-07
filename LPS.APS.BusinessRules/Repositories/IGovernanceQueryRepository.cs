using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// 治理查询Repository接口（G4/G7查询）
/// 5号位直接读取APS_Production事实表
/// </summary>
public interface IGovernanceQueryRepository
{
    /// <summary>
    /// 查询排程运行列表（G4）
    /// </summary>
    Task<List<ScheduleRunGov>> QueryScheduleRunsAsync(
        string? status = null,
        string? runType = null,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// 查询排程域依赖（G7）
    /// </summary>
    Task<List<DomainDependencyDto>> QueryDomainDependenciesAsync(
        string? domainCode = null,
        string? direction = null,
        CancellationToken ct = default);
}
