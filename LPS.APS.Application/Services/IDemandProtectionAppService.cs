using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// Demand Protection 应用服务接口（5号位 Application 层）
///
/// 【职责】中转 DemandProtection 的查询与释放操作，供 DemandProtectionController 调用。
/// - 查询：委托 DemandProtectionService（BusinessRules 层，直接查库）
/// - 释放：委托 IDemandProtectionReleaseService（BusinessRules 层，业务校验 + 实际释放）
///
/// 【架构对齐】与其他 Controller 一致（ScheduleController → IScheduleQueryService、
///  GovernanceController → IGovernanceVersionService），Controller 不直接注入 BusinessRules 服务。
/// </summary>
public interface IDemandProtectionAppService
{
    /// <summary>查询 Demand Protection 列表</summary>
    Task<List<DemandProtectionDto>> QueryAsync(
        string? demandKey = null,
        string? supplyKey = null,
        string? lockType = null,
        string? status = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>查询 Demand Protection 汇总</summary>
    Task<DemandProtectionSummaryDto> GetSummaryAsync(
        string? demandKey = null,
        CancellationToken ct = default);

    /// <summary>释放 Demand Protection（逐 lock 部分成功）</summary>
    Task<List<DemandProtectionReleaseResult>> ReleaseLocksAsync(
        List<long> lockIds,
        string releasedBy,
        string releaseReason,
        CancellationToken ct = default);
}
