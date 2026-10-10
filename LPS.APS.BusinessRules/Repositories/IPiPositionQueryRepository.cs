using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// PI Position查询Repository接口
/// </summary>
public interface IPiPositionQueryRepository
{
    /// <summary>
    /// 查询PI Position列表
    /// </summary>
    Task<List<PiPositionDto>> QueryAsync(
        int planVersionId,
        string? productionInstructionNo = null,
        string? materialCode = null,
        string? positionType = null,
        string? stageCode = null,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null);

    /// <summary>
    /// 查询PI Position汇总
    /// </summary>
    Task<PiPositionSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken ct = default);

    /// <summary>
    /// 根据生产指令号查询该 PI 的所有 Position（单 PI 详情）
    /// </summary>
    Task<List<PiPositionDto>> GetByProductionInstructionAsync(
        int planVersionId,
        string productionInstructionNo,
        CancellationToken ct = default);
}
