using LPS.APS.Core.Dto;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// 冻结策略快照提供者（契约 v0.2 §三）
/// 3号位提供、2号位在 ScheduleRun 启动时按已冻结的指定 VersionId 调用一次（C2-1）。
/// 准确过程（C2-1 冻结）：创建 ScheduleRun → 选择/校验当时有效的 PUBLISHED StrategyProfileVersion
/// → ScheduleRun.StrategyProfileVersionId 冻结 → 按该 VersionId 获取 Snapshot 一次 → 整个 Run 使用同一 Snapshot。
/// 禁止：每个 Domain 开始再去找"现在最新的 PUBLISHED"（理论会漂移）；不逐笔调用本方法。
/// 接口定义位置：Core（本文件）；实现位置：Application 层服务（清单 52 交付物 2）。
/// </summary>
public interface IFrozenStrategySnapshotProvider
{
    /// <summary>
    /// 按已冻结的 StrategyProfileVersionId 构建一次 Run 的完整冻结配置
    /// 3号位提供；2号位 Run 启动按该 VersionId 装载一次，本 Run 内存使用，不逐笔调用。
    /// 六块 + PlanningYield + 三 VersionId 元数据（C2-2/C2-5）。
    /// </summary>
    /// <remarks>
    /// 失败语义（P0-04）：DemandPriority/Lock/Supply/Procurement 四块 JSON 缺失或内容损坏
    /// 一律抛 <see cref="InvalidOperationException"/>，Snapshot 装载失败——不静默回退空 Block，
    /// 保证版本追溯不失真。显式空块（"{}"）为合法表达，字段级冻结默认值可用。
    /// 历史版本（DISABLED/ARCHIVED）仍可按 Id 读取不可变内容；版本状态校验不属于本 Provider 职责。
    /// </remarks>
    Task<FrozenStrategySnapshot> GetFrozenStrategySnapshotAsync(
        long strategyProfileVersionId,
        CancellationToken ct);
}
