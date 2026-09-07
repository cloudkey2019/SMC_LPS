using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;

namespace LPS.APS.Application.Services;

/// <summary>
/// Demand 优先级策略配置提供器（生产实现 — 2号位消费侧）
///
/// 职责边界：
/// - 本类是 IDemandPriorityConfigProvider 的生产入口，DI 扫描到它即可构造 PeggingOrchestrator。
/// - 真实实现应从 3号位读取 FrozenStrategySnapshot.DemandPriority（冻结策略）。
/// - 3号位客户端尚未接通，因此当前显式抛出异常：宁可启动成功、运行时报错，
///   也不得静默回退到 DemandPriorityFixtureProvider（PM 裁决：Fixture 仅联调，禁止生产 Fallback）。
/// </summary>
public sealed class DemandPriorityConfigProvider : IDemandPriorityConfigProvider
{
    /// <summary>
    /// 获取 Demand 优先级配置（3号位冻结策略）。
    /// </summary>
    /// <exception cref="NotSupportedException">3号位 FrozenStrategySnapshot 客户端尚未接通。</exception>
    public Task<DemandPriorityConfig> GetPriorityConfigAsync(
        long strategyProfileVersionId,
        CancellationToken cancellationToken = default)
    {
        // TODO(3号位): 接入真实 FrozenStrategySnapshot 客户端后替换此实现：
        //   var snapshot = await _strategyClient.GetFrozenSnapshotAsync(strategyProfileVersionId, cancellationToken);
        //   return snapshot.DemandPriority;
        throw new NotSupportedException(
            "3号位 FrozenStrategySnapshot 客户端尚未接通，无法获取 DemandPriority 冻结策略。"
            + $" strategyProfileVersionId = {strategyProfileVersionId}。"
            + " 请接入 3号位真实客户端后再运行 Pegging；禁止回退 Fixture（DemandPriorityFixtureProvider 仅限联调）。");
    }
}
