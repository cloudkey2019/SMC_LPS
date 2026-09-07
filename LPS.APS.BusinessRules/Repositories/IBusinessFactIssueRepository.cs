using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// ODS/复杂事实Issue查询Repository接口
/// 统一聚合来自多个5号位事实源的Issue
/// </summary>
public interface IBusinessFactIssueRepository
{
    /// <summary>
    /// 查询BOM Workset Issues
    /// </summary>
    Task<List<BusinessFactIssueDto>> QueryBomWorksetIssuesAsync(
        string? batchNo = null,
        string? materialCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>
    /// 查询MaterialStageDeptContext Issues
    /// </summary>
    Task<List<BusinessFactIssueDto>> QueryMaterialStageContextIssuesAsync(
        string? batchNo = null,
        string? materialCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default);

    /// <summary>
    /// 聚合查询所有ODS/复杂事实Issues
    /// </summary>
    Task<List<BusinessFactIssueDto>> QueryAllAsync(
        string? source = null,
        string? materialCode = null,
        string? factoryCode = null,
        string? severity = null,
        string? reviewStatus = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);
}
