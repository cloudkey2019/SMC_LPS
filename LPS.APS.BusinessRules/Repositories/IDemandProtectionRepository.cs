using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// Demand Protection查询Repository接口
/// </summary>
public interface IDemandProtectionRepository
{
    /// <summary>
    /// 查询Demand Protection列表
    /// </summary>
    Task<List<DemandProtectionDto>> QueryAsync(
        string? demandKey = null,
        string? supplyKey = null,
        string? lockType = null,
        string? status = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// 查询Demand Protection汇总
    /// </summary>
    Task<DemandProtectionSummaryDto> GetSummaryAsync(
        string? demandKey = null,
        CancellationToken ct = default);
}
