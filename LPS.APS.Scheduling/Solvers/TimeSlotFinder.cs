using LPS.APS.Scheduling.Algorithms;
using LPS.APS.Core.Models.Scheduling;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// 时间槽寻址器
/// 【1号位核心组件】阶段3步骤3.2
/// 
/// 职责：
/// - 倒排寻址：从交期往前推，寻找最晚可开工时间
/// - 撞墙翻转正排：倒排撞到产能墙时，自动翻转为正排（从当前时间往后推）
/// - 虚拟库存硬约束：检查 AvailableTime，物料未到则不能开工
/// - 设备日历约束：班次、节假日、维修时间均不可排产
/// </summary>
public class TimeSlotFinder
{
    /// <summary>
    /// 为单个Task寻找可用时间槽
    /// </summary>
    /// <param name="task">优先级最高的task</param>
    /// <param name="context">总的上下文</param>
    /// <param name="options"></param>
    /// <returns></returns>
    public TimeWindow? FindSlot(SchedulingTask task, SchedulingContext context, SchedulingOptions options)
    {
        // L24-25: 检查目标设备是否存在，不存在则无法排程
        var resource = context.GetResourceById(task.ResourceId);
        if (resource is null) return null;

        // L27: 计算此Task的最早可开工时间（考虑前驱Task完工时间、虚拟库存AvailableTime、计划期起点）
        var earliestStart = CalculateEarliestStart(task, context);

        // L29-30: 获取目标设备的可用日历（班次时间窗口列表），无日历则无法排程
        var calendar = context.GetResourceCalendar(task.ResourceId);
        if (calendar is null || calendar.Count == 0) return null;

        // L32-33: 将Task的工时（分钟数）转换为TimeSpan，工时≤0则无效
        var requiredDuration = TimeSpan.FromMinutes(task.DurationMinutes);
        if (requiredDuration <= TimeSpan.Zero) return null;

        // L36-42: 收集同一设备上已占用的时间段
        //         LINQ筛选条件：
        //         1. t.TaskId != task.TaskId  → 排除当前Task自己（避免自冲突）
        //         2. t.ResourceId == task.ResourceId  → 只看同一设备上的Task
        //         3. t.PlannedStartTime/EndTime.HasValue  → 只看已排程的Task（未排程的没有占用时间）
        //         结果：已排程Task的时间窗口列表，用于后续冲突检测
        var occupiedSlots = context.Tasks
            .Where(t => t.TaskId != task.TaskId
                     && t.ResourceId == task.ResourceId
                     && t.PlannedStartTime.HasValue
                     && t.PlannedEndTime.HasValue)
            .Select(t => new TimeWindow(t.PlannedStartTime!.Value, t.PlannedEndTime!.Value))
            .ToList();

        // L44: 读取排程策略模式（Backward倒排 / Forward正排 / BackwardThenForward混合）
        var mode = context.Strategy.Mode;

        // L46-59: 倒排逻辑分支（Backward 或 BackwardThenForward 模式才进入）
        if (mode == SchedulingMode.Backward || mode == SchedulingMode.BackwardThenForward)
        {
            // L48: 检查是否有客户交期（倒排的必要前提）
            if (task.CustomerDueDate.HasValue)
            {
                // L50-52: 尝试倒排寻址：从交期往前推工时，找最晚可开工时间槽
                //         参数：交期、最早开工时间、所需工时、日历、已占用槽、时间粒度
                var backSlot = FindBackwardSlot(
                    task.CustomerDueDate.Value, earliestStart,
                    requiredDuration, calendar, occupiedSlots, options.TimeGranularityMinutes);

                // L53-54: 倒排成功 → 直接返回（倒排优先，不再尝试正排）
                if (backSlot.HasValue)
                    return backSlot;
            }

            // L57-58: 纯倒排模式（Backward）且倒排失败 → 直接返回null（不翻转正排）
            //         只有BackwardThenForward模式才会继续向下执行正排
            if (mode == SchedulingMode.Backward)
                return null;
        }

        // L62-63: 正排寻址（Forward模式 或 BackwardThenForward倒排失败后翻转）
        //         从earliestStart往后扫描日历，找第一个可用空闲槽
        //         参数：最早开工时间、所需工时、日历、已占用槽、计划期终点、时间粒度
        return FindForwardSlot(earliestStart, requiredDuration, calendar, occupiedSlots,
            context.PlanHorizonEnd, options.TimeGranularityMinutes);
    }

    /// <summary>
    /// 计算任务的最早可开工时间
    /// 考虑：前置工序完工时间、虚拟库存 AvailableTime、当前时间
    /// </summary>
    private DateTime CalculateEarliestStart(SchedulingTask task, SchedulingContext context)
    {
        var earliest = context.PlanHorizonStart;

        // 前置工序约束：所有前驱Task必须完成（v5.0图模型支持多前驱/汇合）
        foreach (var predId in task.PredecessorTaskIds)
        {
            var predecessor = context.Tasks.FirstOrDefault(
                t => t.TaskId == predId);
            if (predecessor?.PlannedEndTime.HasValue == true)
            {
                earliest = Max(earliest, predecessor.PlannedEndTime.Value);
            }
        }

        // 虚拟库存硬约束（跨域场景）：物料的 AvailableTime 是绝对硬性时间屏障
        var materialConstraint = context.GetMaterialAvailableTime(task.MaterialId);
        if (materialConstraint.HasValue)
        {
            earliest = Max(earliest, materialConstraint.Value);
        }

        return earliest;
    }

    /// <summary>
    /// 倒排：从交期往前找最晚可开工时间槽，不早于 earliestStart
    /// </summary>
    /// <param name="dueDate">客户交期（目标完工时刻，倒排的锚点）</param>
    /// <param name="earliestStart">最早可开工时间（下界，由前驱Task完工时间、虚拟库存AvailableTime、计划期起点三者取最大值）</param>
    /// <param name="requiredDuration">任务所需工时（来自 Task.DurationMinutes 转换的 TimeSpan）</param>
    /// <param name="calendar">设备可用日历（班次时间窗口列表，只有这些时段可以排产）</param>
    /// <param name="occupiedSlots">同设备上已占用的时间段列表（已排程Task的时间窗口，用于冲突检测）</param>
    /// <param name="granularityMinutes">时间粒度（分钟，V1暂未使用，预留给未来的时间对齐需求）</param>
    /// <returns>可用时间窗口（Start/End），找不到返回 null</returns>
    private static TimeWindow? FindBackwardSlot(
        DateTime dueDate,
        DateTime earliestStart,
        TimeSpan requiredDuration,
        List<TimeWindow> calendar,
        List<TimeWindow> occupiedSlots,
        int granularityMinutes)
    {
        // L107: 计算候选开工时间 = 交期 - 工时
        //       倒排的核心逻辑：从客户要求的完工时刻往前倒推，算出最晚能什么时候开工
        //       示例：交期 2026-06-26 17:00，工时 3小时 → 候选开工 14:00
        var candidateStart = dueDate - requiredDuration;

        // L108-109: 检查倒排是否"撞墙"
        //           如果倒推出的开工时间早于允许的最早开工时间，说明倒排失败
        //           原因可能是：
        //           1. 前驱工序还没完成（earliestStart > 现在）
        //           2. 虚拟库存物料还没到（AvailableTime 硬约束）
        //           3. 计划期起点限制（不能排在计划窗口之前）
        //           返回 null 表示倒排失败，上层会根据 Mode 决定是否翻转正排
        if (candidateStart < earliestStart)
            return null; // 倒排撞墙

        // L111-112: 构造候选时间窗口（开工时间 + 工时 = 完工时间）
        //           这个窗口将接受两项约束检查：日历约束 + 冲突检测
        var candidateEnd = candidateStart + requiredDuration;
        var candidate = new TimeWindow(candidateStart, candidateEnd);

        // L115-116: 约束1 — 日历约束检查
        //           候选窗口必须**完整落在**设备日历的某个可用时段内
        //           如果跨越班次间隙（如中午休息、夜间停机），则不可用
        //           示例：候选 11:00-14:00，但 12:00-13:00 是休息时间 → 检查失败
        if (!IsWithinCalendar(candidate, calendar))
            return null;

        // L119-120: 约束2 — 冲突检测
        //           检查候选窗口是否与同设备上已排程的Task时间重叠
        //           LINQ.Any() 短路求值：只要有一个已占用槽与候选窗口 Overlaps，立即返回 true
        //           返回 null 表示时间槽被占用，倒排失败
        if (occupiedSlots.Any(o => o.Overlaps(candidate)))
            return null;

        // L122: 所有约束检查通过 → 返回候选窗口
        //       这是倒排模式下的"最佳"时间槽（最接近交期，且满足所有约束）
        return candidate;
    }

    /// <summary>
    /// 正排：从 earliestStart 往后找第一个可用空闲槽（在日历范围内）
    /// </summary>
    /// <param name="earliestStart">最早可开工时间（正排扫描的起点，由前驱Task完工时间、虚拟库存AvailableTime、计划期起点三者取最大值）</param>
    /// <param name="requiredDuration">任务所需工时（来自 Task.DurationMinutes 转换的 TimeSpan）</param>
    /// <param name="calendar">设备可用日历（班次时间窗口列表，只有这些时段可以排产）</param>
    /// <param name="occupiedSlots">同设备上已占用的时间段列表（已排程Task的时间窗口，用于冲突检测）</param>
    /// <param name="planHorizonEnd">计划期终点（正排扫描的上界，不能排在计划窗口之外）</param>
    /// <param name="granularityMinutes">时间粒度（分钟，V1暂未使用，预留给未来的时间对齐需求）</param>
    /// <returns>可用时间窗口（Start/End），找不到返回 null</returns>
    private static TimeWindow? FindForwardSlot(
        DateTime earliestStart,
        TimeSpan requiredDuration,
        List<TimeWindow> calendar,
        List<TimeWindow> occupiedSlots,
        DateTime planHorizonEnd,
        int granularityMinutes)
    {
        // L136-137: 构建区间树，加速"某时间点是否被占用"的查询
        //           IntervalTree 将 occupiedSlots 组织成二叉搜索树结构
        //           后续调用 FindFirstAvailableSlot 时可以 O(log n + k) 复杂度查询空闲槽
        //           如果用朴素线性扫描，100k Task 的设备会很慢，区间树是性能关键优化
        var tree = new IntervalTree();
        tree.BuildFrom(occupiedSlots);

        // L140-159: 遍历设备日历的每个可用窗口，在窗口内寻找空闲槽
        //           OrderBy(c => c.Start) 确保按时间顺序扫描（从早到晚）
        //           找到第一个满足条件的槽就立即返回（贪心策略：最早可排产时间）
        foreach (var calWindow in calendar.OrderBy(c => c.Start))
        {
            // L142: 日历窗口已经在 earliestStart 之前结束 → 跳过
            //       示例：earliestStart = 14:00，日历窗口 08:00-12:00 → 已过期，跳过
            if (calWindow.End <= earliestStart) continue;

            // L143: 日历窗口开始时间已经超出计划期 → 后续窗口更晚，直接退出循环
            //       示例：planHorizonEnd = 2026-06-30 23:59，当前窗口从 2026-07-01 开始 → 停止扫描
            if (calWindow.Start >= planHorizonEnd) break;

            // L145-146: 计算当前日历窗口内的有效扫描区间
            //           windowStart = 日历窗口起点 和 earliestStart 取较晚者
            //           windowEnd   = 日历窗口终点 和 计划期终点 取较早者
            //           示例：日历窗口 08:00-17:00，earliestStart = 10:00，planHorizonEnd = 2026-06-30 23:59
            //                → windowStart = 10:00, windowEnd = 17:00
            var windowStart = Max(calWindow.Start, earliestStart);
            var windowEnd   = calWindow.End < planHorizonEnd ? calWindow.End : planHorizonEnd;

            // L148: 检查有效区间是否足够容纳任务工时
            //       如果窗口时长 < 所需工时，直接跳过（连续放不下，没必要扫描）
            //       示例：窗口 10:00-11:00（1小时），任务需要 3 小时 → 跳过
            if (windowEnd - windowStart < requiredDuration) continue;

            // L151-154: 找出此日历窗口内的所有已占用槽（裁剪到窗口边界内）
            //           LINQ 筛选条件：
            //           1. o.Start < windowEnd   → 占用槽在窗口结束前开始（有交集）
            //           2. o.End > windowStart   → 占用槽在窗口开始后结束（有交集）
            //           裁剪逻辑：占用槽的起点和终点都限制在 [windowStart, windowEnd] 内
            //           示例：窗口 10:00-17:00，占用槽 09:00-11:00 → 裁剪为 10:00-11:00
            var windowOccupied = occupiedSlots
                .Where(o => o.Start < windowEnd && o.End > windowStart)
                .Select(o => new TimeWindow(Max(o.Start, windowStart), o.End < windowEnd ? o.End : windowEnd))
                .ToList();

            // L156: 调用区间树的空闲槽查找算法
            //       输入：窗口起点、所需工时、窗口内的占用槽列表
            //       返回：第一个可用空闲槽（Start/End），找不到返回 null
            //       算法逻辑（IntervalTree 内部）：
            //         1. 将 windowOccupied 按 Start 排序
            //         2. 游标从 windowStart 开始，扫描占用槽之间的间隙
            //         3. 找到第一个间隙长度 >= requiredDuration 的位置
            var slot = tree.FindFirstAvailableSlot(windowStart, requiredDuration, windowOccupied);

            // L157-158: 检查找到的槽是否合法（完工时间不能超出日历窗口边界）
            //           如果合法，立即返回（贪心：最早可排产的时间槽）
            //           示例：窗口 10:00-17:00，找到槽 10:00-13:00 → 返回
            if (slot.HasValue && slot.Value.End <= windowEnd)
                return slot;
        }

        // L161: 所有日历窗口扫描完毕，仍未找到可用槽 → 返回 null
        //       原因可能是：
        //       1. 设备日历可用时间太少（班次稀疏、节假日多）
        //       2. 已排程Task占满了所有空闲时间（设备超负荷）
        //       3. 任务工时太长，单个日历窗口放不下
        return null;
    }

    /// <summary>
    /// 检查候选时间窗口是否完整落在日历可用段内
    /// </summary>
    private static bool IsWithinCalendar(TimeWindow candidate, List<TimeWindow> calendar)
    {
        return calendar.Any(c => c.Start <= candidate.Start && c.End >= candidate.End);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
