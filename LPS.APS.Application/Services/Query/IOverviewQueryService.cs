using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services.Query;

/// <summary>
/// Overview概览页查询服务接口（5号位提供给4号位）
/// </summary>
public interface IOverviewQueryService
{
    /// <summary>查询当前ACTIVE计划版本信息</summary>
    Task<OverviewActivePlanDto?> GetActivePlanAsync(
        string? domainKey = null,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedDomains = null);

    /// <summary>查询任务状态摘要</summary>
    Task<OverviewTaskSummaryDto> GetTaskSummaryAsync(
        int planVersionId,
        CancellationToken ct = default);

    /// <summary>查询资源负荷/Bottleneck</summary>
    Task<List<OverviewResourceBottleneckDto>> GetResourceBottleneckAsync(
        int planVersionId,
        int topN = 10,
        CancellationToken ct = default);

    /// <summary>查询Candidate待处理摘要</summary>
    Task<OverviewCandidateSummaryDto> GetCandidateSummaryAsync(
        CancellationToken ct = default);
}