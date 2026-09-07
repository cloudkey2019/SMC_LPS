using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// Demand 优先级策略配置提供器（2号位消费侧 — 3号位产出侧）
///
/// 职责边界：
/// - 3号位负责策略冻结，输出完整的 FrozenStrategySnapshot（其中包含 DemandPriorityConfig）
/// - 2号位通过本接口获取 DemandPriorityConfig 后交给 IDemandPriorityExecutor 执行
///
/// 生产实现：DemandPriorityConfigProvider（3号位 FrozenStrategySnapshot 客户端尚未接通，当前显式抛错）
/// 联调实现：DemandPriorityFixtureProvider（位于 Fixtures 命名空间，仅测试项目注册，禁止生产 Fallback）
/// 后续替换：3号位真实 FrozenStrategySnapshot 客户端接入后，替换 DemandPriorityConfigProvider 的实现体
/// </summary>
public interface IDemandPriorityConfigProvider
{
    Task<DemandPriorityConfig> GetPriorityConfigAsync(
        long strategyProfileVersionId,
        CancellationToken cancellationToken = default);
}
