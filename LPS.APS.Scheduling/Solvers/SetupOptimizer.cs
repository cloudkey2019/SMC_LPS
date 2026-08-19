using LPS.APS.Core.Models.Scheduling;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// 换型优化启发式
/// 【1号位核心组件】阶段3步骤3.3
/// 
/// 职责：
/// - 当某台设备有多个候选任务时，优先选择与上一任务 SetupAttribute 相同的任务
/// - 这种局部微调不破坏优先级大框架，但能显著提升瓶颈设备产能（5-15%）
/// 
/// 示例：
///   注塑机刚完成模具A的任务 → 队列中有模具A和模具B的任务
///   → 算法优先排模具A，避免换模时间损失
/// </summary>
public class SetupOptimizer
{
    /// <summary>
    /// 从候选任务列表中选择最优任务（考虑换型成本）
    /// </summary>
    /// <param name="candidates">候选任务列表（已按优先级排序）</param>
    /// <param name="lastSetupAttribute">设备上一个任务的换型属性（模具编号/颜色代码/材质规格）</param>
    /// <param name="maxLookAhead">最大前瞻窗口（在前N个高优先级任务中寻找相同属性的任务）</param>
    /// <returns>选中的最优任务</returns>
    public SchedulingTask SelectBestCandidate(
        IReadOnlyList<SchedulingTask> candidates,
        string? lastSetupAttribute,
        int maxLookAhead = 5)
    {
        if (candidates.Count == 0)
            throw new ArgumentException("候选任务列表不能为空", nameof(candidates));

        if (candidates.Count == 1 || string.IsNullOrEmpty(lastSetupAttribute))
            return candidates[0];

        // 在前 maxLookAhead 个任务中寻找与上一任务相同 SetupAttribute 的任务
        int searchRange = Math.Min(maxLookAhead, candidates.Count);

        for (int i = 0; i < searchRange; i++)
        {
            if (string.Equals(candidates[i].SetupAttribute, lastSetupAttribute, StringComparison.Ordinal))
            {
                return candidates[i];
            }
        }

        // 没找到相同属性的任务，返回优先级最高的
        return candidates[0];
    }

    /// <summary>
    /// 计算换型时间（分钟）
    /// </summary>
    /// <param name="fromAttribute">切换前的属性</param>
    /// <param name="toAttribute">切换后的属性</param>
    /// <param name="defaultSetupMinutes">默认换型时间（分钟）</param>
    /// <returns>换型时间（分钟），相同属性返回0</returns>
    public double CalculateSetupTime(string? fromAttribute, string? toAttribute, double defaultSetupMinutes = 30)
    {
        if (string.IsNullOrEmpty(fromAttribute) || string.IsNullOrEmpty(toAttribute))
            return 0;

        if (string.Equals(fromAttribute, toAttribute, StringComparison.Ordinal))
            return 0;

        // TODO: 1号位可根据实际业务扩展换型矩阵（不同属性间的换型时间不同）
        return defaultSetupMinutes;
    }
}
