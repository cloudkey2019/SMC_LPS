using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;

namespace LPS.APS.Application.Services;

/// <summary>
/// Demand 优先级策略配置提供器（生产实现 — 2号位 消费侧）
///
/// 职责边界：
/// - 本类是 <see cref="IDemandPriorityConfigProvider"/> 的生产入口，DI 扫描到它即可构造 PeggingOrchestrator。
/// - 真实来源 = 3号位 冻结快照 <c>FrozenStrategySnapshot.DemandPriority</c>（子块随 RuleSetVersion.ContentSnapshotJson 冻结）。
/// - 快照装载由 <see cref="IFrozenStrategySnapshotProvider"/> 负责（同一 Run 按 StrategyProfileVersionId 只装一次）；
///   本类**只做投影**，不重复读库、不缓存、不判策略语义。
///
/// 历史：本类原为「显式抛 NotSupportedException 的桩」——当时的判断是「3号位 FrozenStrategySnapshot 客户端尚未接通」。
/// 该前提**已不成立**：<see cref="FrozenStrategySnapshotProvider"/> 已在生产 DI 中装配六块+第⑦块快照。
/// 生产 DI 因此长期处于「不走 Fixture 就根本跑不了 Pegging」的状态，而所有集成测试都用
/// <c>DemandPriorityFixtureProvider</c> 覆盖了本类，把这个缺口整个遮住。2026-09-29 接通。
///
/// 红线：PM 裁决「正式排程不得自动回退 Fixture」——本类**不**回退 Fixture；
/// 投影不出的内容（快照缺块 / 字段不在执行器白名单 / 不支持的枚举值）一律**显式抛错**。
/// </summary>
public sealed class DemandPriorityConfigProvider : IDemandPriorityConfigProvider
{
    private readonly IFrozenStrategySnapshotProvider _frozenStrategySnapshotProvider;

    public DemandPriorityConfigProvider(IFrozenStrategySnapshotProvider frozenStrategySnapshotProvider)
    {
        _frozenStrategySnapshotProvider = frozenStrategySnapshotProvider
            ?? throw new ArgumentNullException(nameof(frozenStrategySnapshotProvider));
    }

    /// <summary>
    /// 取 3号位 冻结快照的 DemandPriority 子块，投影为执行器配置。
    /// </summary>
    /// <exception cref="InvalidOperationException">快照装载失败或 DemandPriority 子块缺失。</exception>
    /// <exception cref="NotSupportedException">快照使用了 2号位 执行器不支持的字段/操作符/排序方向。</exception>
    public async Task<DemandPriorityConfig> GetPriorityConfigAsync(
        long strategyProfileVersionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _frozenStrategySnapshotProvider
            .GetFrozenStrategySnapshotAsync(strategyProfileVersionId, cancellationToken);

        return DemandPriorityProjector.Project(snapshot.DemandPriority);
    }
}
