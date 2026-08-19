using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Algorithms;

/// <summary>
/// 时间线段树（Interval Tree）
/// 用于极速检索设备日历中的空闲时间槽，支持 O(log n + k) 查询
/// 【1号位核心数据结构】
/// </summary>
public class IntervalTree
{
    private IntervalNode? _root;
    private int _count;

    /// <summary>
    /// 树中区间总数
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// 插入一个时间窗口
    /// </summary>
    public void Insert(TimeWindow interval)
    {
        _root = Insert(_root, interval);
        _count++;
    }

    /// <summary>
    /// 批量构建（从已有时间窗口列表）
    /// </summary>
    public void BuildFrom(IReadOnlyList<TimeWindow> intervals)
    {
        _root = null;
        _count = 0;
        foreach (var interval in intervals)
        {
            Insert(interval);
        }
    }

    /// <summary>
    /// 查询所有与指定时间窗口重叠的区间
    /// </summary>
    public List<TimeWindow> QueryOverlapping(TimeWindow query)
    {
        var results = new List<TimeWindow>();
        QueryOverlapping(_root, query, results);
        return results;
    }

    /// <summary>
    /// 查询指定时间点之后的第一个空闲时间槽（持续时间 >= requiredDuration）
    /// 这是排程引擎"时间槽寻址"的核心操作
    /// </summary>
    public TimeWindow? FindFirstAvailableSlot(DateTime earliest, TimeSpan requiredDuration, IReadOnlyList<TimeWindow> occupiedSlots)
    {
        // 按开始时间排序，合并重叠/相邻占用段，然后扫描间隙
        var sorted = occupiedSlots
            .Where(s => s.End > earliest)
            .OrderBy(s => s.Start)
            .ToList();

        var cursor = earliest;

        foreach (var occupied in sorted)
        {
            // cursor 到 occupied.Start 之间是空闲
            var gapStart = cursor;
            var gapEnd   = occupied.Start > cursor ? occupied.Start : cursor;

            if (gapEnd - gapStart >= requiredDuration)
                return new TimeWindow(gapStart, gapStart + requiredDuration);

            // 推进 cursor 到当前占用段结束之后
            if (occupied.End > cursor)
                cursor = occupied.End;
        }

        // 所有占用段扫完后的尾部空闲（无上界，调用方负责边界）
        return new TimeWindow(cursor, cursor + requiredDuration);
    }

    /// <summary>
    /// 检查指定时间窗口是否与树中任何区间冲突
    /// </summary>
    public bool HasConflict(TimeWindow candidate)
    {
        return QueryOverlapping(candidate).Count > 0;
    }

    #region 内部实现

    private sealed class IntervalNode
    {
        public TimeWindow Interval { get; init; }
        public DateTime MaxEnd { get; set; }
        public IntervalNode? Left { get; set; }
        public IntervalNode? Right { get; set; }
    }

    private static IntervalNode Insert(IntervalNode? node, TimeWindow interval)
    {
        if (node is null)
        {
            return new IntervalNode
            {
                Interval = interval,
                MaxEnd = interval.End,
                Left = null,
                Right = null
            };
        }

        if (interval.Start < node.Interval.Start)
            node.Left = Insert(node.Left, interval);
        else
            node.Right = Insert(node.Right, interval);

        if (interval.End > node.MaxEnd)
            node.MaxEnd = interval.End;

        return node;
    }

    private static void QueryOverlapping(IntervalNode? node, TimeWindow query, List<TimeWindow> results)
    {
        if (node is null) return;

        if (node.Interval.Overlaps(query))
            results.Add(node.Interval);

        if (node.Left is not null && node.Left.MaxEnd > query.Start)
            QueryOverlapping(node.Left, query, results);

        if (node.Right is not null && node.Interval.Start < query.End)
            QueryOverlapping(node.Right, query, results);
    }

    #endregion
}
