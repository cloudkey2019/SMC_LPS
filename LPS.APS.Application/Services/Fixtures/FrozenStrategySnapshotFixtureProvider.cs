using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;

namespace LPS.APS.Application.Services.Fixtures;

/// <summary>
/// 冻结策略快照 Fixture 提供器（联调/测试专用，不得进入生产 DI）
///
/// 与 <see cref="DemandPriorityFixtureProvider"/> 同理：生产用真实
/// <see cref="IFrozenStrategySnapshotProvider"/>（FrozenStrategySnapshotProvider），
/// 本 Fixture 仅供集成测试在测试库缺失真实 PUBLISHED StrategyProfileVersion 时
/// 通过策略上下文完整性校验，并让 2号位 Supply 多键排序按稳定兜底执行。
///
/// INTEGRATION TODO: 测试库补齐真实六块 ContentSnapshotJson 后，移除本 Fixture，
/// 复用生产真实 Provider。不得将此 Fixture 逻辑作为生产 Fallback。
/// </summary>
public sealed class FrozenStrategySnapshotFixtureProvider : IFrozenStrategySnapshotProvider
{
    /// <summary>
    /// 返回最小有效快照：Supply 块按默认值（WarehousePriority 空、PiSort 默认 IssueDateAsc），
    /// 其余块保持默认空对象。2号位据此按 AvailableAt + 稳定兜底排序。
    /// </summary>
    public Task<FrozenStrategySnapshot> GetFrozenStrategySnapshotAsync(
        long strategyProfileVersionId,
        CancellationToken ct)
    {
        var snapshot = new FrozenStrategySnapshot
        {
            StrategyProfileVersionId = strategyProfileVersionId,
            Supply = new SupplyBlock
            {
                Inventory = new InventoryAvailabilityRule
                {
                    IsEnabled = false,
                    WarehousePriority = []
                },
                PiSort = new PiSortParams
                {
                    SortBy = PiSortBy.IssueDateAsc,
                    UseStablePiNoTieBreak = false
                }
            }
        };

        return Task.FromResult(snapshot);
    }
}
