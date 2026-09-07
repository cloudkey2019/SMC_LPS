using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// Overview查询业务服务
///
/// 5号位提供给4号位的概览页数据聚合
/// 直接读取APS_Production事实表，不重算业务
/// </summary>
public class OverviewQueryService
{
    private readonly IOverviewQueryRepository _repository;

    public OverviewQueryService(IOverviewQueryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 查询当前ACTIVE计划版本信息
    /// </summary>
    public async Task<OverviewActivePlanDto?> GetActivePlanAsync(
        string? domainKey = null,
        CancellationToken ct = default)
    {
        return await _repository.GetActivePlanAsync(domainKey, ct);
    }

    /// <summary>
    /// 查询任务状态摘要
    /// </summary>
    public async Task<OverviewTaskSummaryDto> GetTaskSummaryAsync(
        int planVersionId,
        CancellationToken ct = default)
    {
        return await _repository.GetTaskSummaryAsync(planVersionId, ct);
    }

    /// <summary>
    /// 查询资源负荷/Bottleneck
    /// </summary>
    public async Task<List<OverviewResourceBottleneckDto>> GetResourceBottleneckAsync(
        int planVersionId,
        int topN = 10,
        CancellationToken ct = default)
    {
        if (topN <= 0 || topN > 50) topN = 10;

        return await _repository.GetResourceBottleneckAsync(planVersionId, topN, ct);
    }

    /// <summary>
    /// 查询Candidate待处理摘要
    /// </summary>
    public async Task<OverviewCandidateSummaryDto> GetCandidateSummaryAsync(
        CancellationToken ct = default)
    {
        return await _repository.GetCandidateSummaryAsync(ct);
    }
}
