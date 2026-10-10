using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// Demand Protection 应用服务实现（5号位 Application 层）
///
/// 中转 DemandProtectionController 的查询与释放请求，委托 BusinessRules 层处理。
/// 本类不包含业务逻辑，仅做调用编排。
/// </summary>
public sealed class DemandProtectionAppService : IDemandProtectionAppService
{
    private readonly DemandProtectionService _queryService;
    private readonly IDemandProtectionReleaseService _releaseService;

    public DemandProtectionAppService(
        DemandProtectionService queryService,
        IDemandProtectionReleaseService releaseService)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _releaseService = releaseService ?? throw new ArgumentNullException(nameof(releaseService));
    }

    public async Task<List<DemandProtectionDto>> QueryAsync(
        string? demandKey = null,
        string? supplyKey = null,
        string? lockType = null,
        string? status = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        return await _queryService.QueryAsync(demandKey, supplyKey, lockType, status, skip, take, ct);
    }

    public async Task<DemandProtectionSummaryDto> GetSummaryAsync(
        string? demandKey = null,
        CancellationToken ct = default)
    {
        return await _queryService.GetSummaryAsync(demandKey, ct);
    }

    public async Task<List<DemandProtectionReleaseResult>> ReleaseLocksAsync(
        List<long> lockIds,
        string releasedBy,
        string releaseReason,
        CancellationToken ct = default)
    {
        return await _releaseService.ReleaseLocksAsync(lockIds, releasedBy, releaseReason, ct);
    }
}
