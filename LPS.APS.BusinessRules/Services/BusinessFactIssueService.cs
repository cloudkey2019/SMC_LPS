using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// ODS/复杂事实Issue业务服务（5号位提供给4号位）
///
/// 用于Explanation辅助、事实异常展示
/// 统一聚合来自多个5号位事实源的Issue
/// </summary>
public class BusinessFactIssueService
{
    private readonly IBusinessFactIssueRepository _repository;

    public BusinessFactIssueService(IBusinessFactIssueRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 聚合查询所有ODS/复杂事实Issues
    /// </summary>
    public async Task<List<BusinessFactIssueDto>> QueryAllAsync(
        string? source = null,
        string? materialCode = null,
        string? factoryCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        if (take <= 0 || take > 500) take = 100;

        return await _repository.QueryAllAsync(
            source, materialCode, factoryCode, severity, reviewStatus,
            skip, take, ct);
    }
}
