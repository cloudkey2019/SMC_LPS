using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// Explanation查询业务服务
///
/// 5号位提供给4号位的排程原因解释查询
/// 直接读取ScheduleExplanationFact表，不重新裁决延期原因
/// </summary>
public class ExplanationQueryService
{
    private readonly IExplanationQueryRepository _repository;

    public ExplanationQueryService(IExplanationQueryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 查询Explanation列表
    /// </summary>
    public async Task<List<ExplanationDto>> QueryAsync(
        int planVersionId,
        string? objectType = null,
        long? orderId = null,
        long? taskId = null,
        string? reasonCode = null,
        string? severity = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        if (planVersionId <= 0) throw new ArgumentException("planVersionId is required");
        if (take <= 0 || take > 500) take = 100;

        return await _repository.QueryAsync(
            planVersionId, objectType, orderId, taskId,
            reasonCode, severity, skip, take, ct);
    }

    /// <summary>
    /// 查询Explanation汇总
    /// </summary>
    public async Task<ExplanationSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken ct = default)
    {
        if (planVersionId <= 0) throw new ArgumentException("planVersionId is required");

        return await _repository.GetSummaryAsync(planVersionId, ct);
    }
}
