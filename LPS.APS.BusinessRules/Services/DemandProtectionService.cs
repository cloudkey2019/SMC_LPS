using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// Demand Protection查询业务服务
///
/// 5号位提供给4号位的Demand Protection查看
/// 直接读取DemandSupplyHardLock表，不修改保护状态
///
/// 【职责边界】
/// - 查看：5号位自己实现（直接查库）
/// - 释放：必须通过2号位Application Service
/// </summary>
public class DemandProtectionService
{
    private readonly IDemandProtectionRepository _repository;

    public DemandProtectionService(IDemandProtectionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <summary>
    /// 查询Demand Protection列表
    /// </summary>
    public async Task<List<DemandProtectionDto>> QueryAsync(
        string? demandKey = null,
        string? supplyKey = null,
        string? lockType = null,
        string? status = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default)
    {
        if (take <= 0 || take > 500) take = 100;

        return await _repository.QueryAsync(
            demandKey, supplyKey, lockType, status, skip, take, ct);
    }

    /// <summary>
    /// 查询Demand Protection汇总
    /// </summary>
    public async Task<DemandProtectionSummaryDto> GetSummaryAsync(
        string? demandKey = null,
        CancellationToken ct = default)
    {
        return await _repository.GetSummaryAsync(demandKey, ct);
    }
}
