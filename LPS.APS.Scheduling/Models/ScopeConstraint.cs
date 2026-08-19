namespace LPS.APS.Scheduling.Models;

/// <summary>
/// 局部重排范围约束（对应文档二 LOCAL_RESCHEDULE 模式的 ScopeJson）
/// 定义哪些资源可调度、哪些Task必须冻结、哪些Task可移动
/// </summary>
public class ScopeConstraint
{
    /// <summary>
    /// 允许调度的资源ID列表（null 或空列表 = 全部资源可用）
    /// 场景：只重排某几台设备的Task，其他设备保持不动
    /// </summary>
    public List<string>? AllowedResourceIds { get; set; }

    /// <summary>
    /// 必须冻结的Task ID列表（这些Task不会被重新寻址，保持原时间不变）
    /// 典型场景：
    /// - 已开工的Task（IsStarted = true）
    /// - 用户手动锁定的Task（IsLocked = true）
    /// - 冻结区内的Task（PlannedStartTime < Now + FrozenHorizonDays）
    /// </summary>
    public List<string> FrozenTaskIds { get; set; } = new();

    /// <summary>
    /// 允许移动的Task ID列表（null = 除冻结外全部可移动）
    /// 场景：插单影响分析时，只重排与插单相关的Task，其他Task保持不动
    /// 注意：FrozenTaskIds 优先级更高，如果一个Task同时在两个列表中，以冻结为准
    /// </summary>
    public List<string>? MovableTaskIds { get; set; }

    /// <summary>
    /// 检查指定Task是否被冻结
    /// </summary>
    public bool IsFrozen(string taskId)
    {
        return FrozenTaskIds.Contains(taskId);
    }

    /// <summary>
    /// 检查指定Task是否可移动
    /// </summary>
    public bool IsMovable(string taskId)
    {
        // 如果在冻结列表中，直接返回false
        if (FrozenTaskIds.Contains(taskId))
            return false;

        // 如果MovableTaskIds为null，表示除冻结外全部可移动
        if (MovableTaskIds == null)
            return true;

        // 否则必须在MovableTaskIds列表中
        return MovableTaskIds.Contains(taskId);
    }

    /// <summary>
    /// 检查指定资源是否在允许范围内
    /// </summary>
    public bool IsResourceAllowed(string resourceId)
    {
        // 如果AllowedResourceIds为null或空，表示全部资源可用
        if (AllowedResourceIds == null || AllowedResourceIds.Count == 0)
            return true;

        return AllowedResourceIds.Contains(resourceId);
    }
}
