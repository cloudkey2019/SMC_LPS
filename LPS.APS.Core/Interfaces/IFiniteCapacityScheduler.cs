using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 1号位：纯内存有限产能求解器接口。
/// 不读库、不写库，只对内存 TaskDraft 排资源和时间，返回 FinalTaskDraft 和 AllocationShares。
/// </summary>
public interface IFiniteCapacityScheduler
{
    Task<DomainSolveResult> SolveAsync(
        DomainSolveRequest request,
        CancellationToken cancellationToken = default);
}
