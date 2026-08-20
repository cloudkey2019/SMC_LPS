using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// Demand 优先级匹配服务（阶段 C-1/C-2）
/// 职责：
/// 1. 按 Segment 有序列表执行 First-match 语义
/// 2. 命中后执行 Segment 内部排序
/// 3. 提供稳定 Tie-break
/// 红线：
/// - 禁止全局 PriorityScore
/// - 第一命中即止，命中后不再匹配后续 Segment
/// - 不建 DSL，强类型表达
/// </summary>
public sealed class DemandPriorityMatcher
{
    /// <summary>
    /// 未匹配任何 Segment 时使用的排序值（排在最后）
    /// </summary>
    private const int UnmatchedSegmentOrder = int.MaxValue;
    /// <summary>
    /// 判断 Demand 是否命中指定 Segment
    /// </summary>
    /// <param name="demand">需求记录（包含可匹配字段）</param>
    /// <param name="segment">优先级 Segment</param>
    /// <returns>是否命中（所有 MatchConditions 均满足）</returns>
    public bool IsSegmentMatch(DemandRecord demand, PrioritySegment segment)
    {
        if (!segment.IsEnabled)
        {
            return false;
        }

        // AND 语义：所有条件必须满足
        foreach (var condition in segment.MatchConditions)
        {
            if (!EvaluateCondition(demand, condition))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 查找 Demand 命中的第一个 Segment（First-match 语义）
    /// </summary>
    /// <param name="demand">需求记录</param>
    /// <param name="segments">Segment 列表（会按 SegmentOrder 升序排序）</param>
    /// <returns>命中的 Segment（未命中返回 null）</returns>
    /// <remarks>
    /// 注意：此方法会对 segments 执行 OrderBy 排序。
    /// 如需优化性能，调用方可预先排序并缓存结果。
    /// </remarks>
    public PrioritySegment? FindFirstMatchSegment(DemandRecord demand, IReadOnlyList<PrioritySegment> segments)
    {
        foreach (var segment in segments.OrderBy(s => s.SegmentOrder))
        {
            if (IsSegmentMatch(demand, segment))
            {
                return segment;
            }
        }

        return null;
    }

    /// <summary>
    /// 对 Demand 列表按优先级规则排序（阶段 C-2 核心逻辑）
    /// </summary>
    /// <param name="demands">待排序的需求列表</param>
    /// <param name="priorityBlock">优先级配置块</param>
    /// <returns>排序后的需求列表</returns>
    public IReadOnlyList<DemandRecord> SortDemands(
        IReadOnlyList<DemandRecord> demands,
        DemandPriorityBlock priorityBlock)
    {
        var result = new List<DemandRecord>(demands.Count);
        var grouped = new Dictionary<int, List<DemandRecord>>();

        // 第一阶段：按 First-match Segment 分组
        foreach (var demand in demands)
        {
            var matchedSegment = FindFirstMatchSegment(demand, priorityBlock.Segments);
            var segmentOrder = matchedSegment?.SegmentOrder ?? UnmatchedSegmentOrder; // 未命中排最后

            if (!grouped.ContainsKey(segmentOrder))
            {
                grouped[segmentOrder] = new List<DemandRecord>();
            }

            grouped[segmentOrder].Add(demand);
        }

        // 第二阶段：按 SegmentOrder 升序处理各组
        foreach (var segmentOrder in grouped.Keys.OrderBy(k => k))
        {
            var group = grouped[segmentOrder];
            var segment = priorityBlock.Segments.FirstOrDefault(s => s.SegmentOrder == segmentOrder);

            if (segment != null && segment.SortFields.Count > 0)
            {
                // 有排序规则：执行 Segment 内部排序
                var sorted = SortBySegmentRules(group, segment);
                result.AddRange(sorted);
            }
            else
            {
                // 无排序规则：保持原顺序（稳定排序）
                result.AddRange(group);
            }
        }

        return result;
    }

    /// <summary>
    /// 按 Segment 排序规则对组内 Demand 排序
    /// </summary>
    private IEnumerable<DemandRecord> SortBySegmentRules(
        List<DemandRecord> group,
        PrioritySegment segment)
    {
        IOrderedEnumerable<DemandRecord>? orderedQuery = null;

        // 依次应用 SortFields
        for (int i = 0; i < segment.SortFields.Count; i++)
        {
            var sortField = segment.SortFields[i];

            if (i == 0)
            {
                orderedQuery = ApplySort(group, sortField.Field, sortField.Direction);
            }
            else
            {
                orderedQuery = ApplyThenSort(orderedQuery!, sortField.Field, sortField.Direction);
            }
        }

        // 应用稳定 Tie-break 字段
        if (orderedQuery != null && segment.StableTieBreakFields.Count > 0)
        {
            foreach (var tieBreakField in segment.StableTieBreakFields)
            {
                orderedQuery = orderedQuery.ThenBy(d => GetTieBreakValue(d, tieBreakField));
            }
        }

        return (IEnumerable<DemandRecord>?)orderedQuery ?? group;
    }

    /// <summary>
    /// 首次排序（强类型避免装箱问题）
    /// </summary>
    private IOrderedEnumerable<DemandRecord> ApplySort(
        List<DemandRecord> group,
        DemandField field,
        SortDirection direction)
    {
        return field switch
        {
            DemandField.RemainingTimeHours => direction == SortDirection.Asc
                ? group.OrderBy(d => d.RemainingTimeHours)
                : group.OrderByDescending(d => d.RemainingTimeHours),
            DemandField.DelayStatus => direction == SortDirection.Asc
                ? group.OrderBy(d => d.DelayStatus)
                : group.OrderByDescending(d => d.DelayStatus),
            DemandField.CustomerTier => direction == SortDirection.Asc
                ? group.OrderBy(d => d.CustomerTier)
                : group.OrderByDescending(d => d.CustomerTier),
            DemandField.OrderType => direction == SortDirection.Asc
                ? group.OrderBy(d => d.OrderType)
                : group.OrderByDescending(d => d.OrderType),
            DemandField.IsPmcProtected => direction == SortDirection.Asc
                ? group.OrderBy(d => d.IsPmcProtected)
                : group.OrderByDescending(d => d.IsPmcProtected),
            DemandField.PriorityLevel => direction == SortDirection.Asc
                ? group.OrderBy(d => d.PriorityLevel)
                : group.OrderByDescending(d => d.PriorityLevel),
            // P0-07：日期排序（冻结示例 "DueDate ASC → ... → IssueDate ASC"）
            // null 语义（LINQ 默认）：Asc 时 null 排最前、Desc 时 null 排最后（CompareValues 同规则）
            DemandField.DueDate => direction == SortDirection.Asc
                ? group.OrderBy(d => d.DueDate)
                : group.OrderByDescending(d => d.DueDate),
            DemandField.IssueDate => direction == SortDirection.Asc
                ? group.OrderBy(d => d.IssueDate)
                : group.OrderByDescending(d => d.IssueDate),
            _ => throw new NotSupportedException($"不支持的排序字段：{field}")
        };
    }

    /// <summary>
    /// 后续排序（强类型避免装箱问题）
    /// </summary>
    private IOrderedEnumerable<DemandRecord> ApplyThenSort(
        IOrderedEnumerable<DemandRecord> query,
        DemandField field,
        SortDirection direction)
    {
        return field switch
        {
            DemandField.RemainingTimeHours => direction == SortDirection.Asc
                ? query.ThenBy(d => d.RemainingTimeHours)
                : query.ThenByDescending(d => d.RemainingTimeHours),
            DemandField.DelayStatus => direction == SortDirection.Asc
                ? query.ThenBy(d => d.DelayStatus)
                : query.ThenByDescending(d => d.DelayStatus),
            DemandField.CustomerTier => direction == SortDirection.Asc
                ? query.ThenBy(d => d.CustomerTier)
                : query.ThenByDescending(d => d.CustomerTier),
            DemandField.OrderType => direction == SortDirection.Asc
                ? query.ThenBy(d => d.OrderType)
                : query.ThenByDescending(d => d.OrderType),
            DemandField.IsPmcProtected => direction == SortDirection.Asc
                ? query.ThenBy(d => d.IsPmcProtected)
                : query.ThenByDescending(d => d.IsPmcProtected),
            DemandField.PriorityLevel => direction == SortDirection.Asc
                ? query.ThenBy(d => d.PriorityLevel)
                : query.ThenByDescending(d => d.PriorityLevel),
            // P0-07：日期后续排序（null 语义同 ApplySort：Asc null 最前 / Desc null 最后）
            DemandField.DueDate => direction == SortDirection.Asc
                ? query.ThenBy(d => d.DueDate)
                : query.ThenByDescending(d => d.DueDate),
            DemandField.IssueDate => direction == SortDirection.Asc
                ? query.ThenBy(d => d.IssueDate)
                : query.ThenByDescending(d => d.IssueDate),
            _ => throw new NotSupportedException($"不支持的排序字段：{field}")
        };
    }

    /// <summary>
    /// 评估单个匹配条件
    /// </summary>
    private bool EvaluateCondition(DemandRecord demand, SegmentMatchCondition condition)
    {
        var fieldValue = GetFieldValue(demand, condition.Field);
        var targetValue = NormalizeJsonValue(condition.Value);

        return condition.Operator switch
        {
            ConditionOperator.Equals => Equals(fieldValue, targetValue),
            ConditionOperator.NotEquals => !Equals(fieldValue, targetValue),
            ConditionOperator.LessThan => CompareValues(fieldValue, targetValue) < 0,
            ConditionOperator.LessOrEqual => CompareValues(fieldValue, targetValue) <= 0,
            ConditionOperator.GreaterThan => CompareValues(fieldValue, targetValue) > 0,
            ConditionOperator.GreaterOrEqual => CompareValues(fieldValue, targetValue) >= 0,
            ConditionOperator.In => IsInList(fieldValue, targetValue),
            _ => throw new NotSupportedException($"不支持的操作符：{condition.Operator}")
        };
    }

    /// <summary>
    /// 规范化 JSON 反序列化的值（处理 JsonElement 类型）
    /// </summary>
    private object? NormalizeJsonValue(object? value)
    {
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString(),
                System.Text.Json.JsonValueKind.Number => jsonElement.TryGetInt32(out var intVal) ? intVal :
                                                          jsonElement.TryGetInt64(out var longVal) ? longVal :
                                                          jsonElement.TryGetDouble(out var doubleVal) ? doubleVal :
                                                          jsonElement.GetDecimal(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => value
            };
        }
        return value;
    }

    /// <summary>
    /// 获取 Demand 字段值（强类型映射）
    /// </summary>
    private object? GetFieldValue(DemandRecord demand, DemandField field)
    {
        return field switch
        {
            DemandField.RemainingTimeHours => demand.RemainingTimeHours,
            DemandField.DelayStatus => demand.DelayStatus,
            DemandField.CustomerTier => demand.CustomerTier,
            DemandField.OrderType => demand.OrderType,
            DemandField.IsPmcProtected => demand.IsPmcProtected,
            DemandField.PriorityLevel => demand.PriorityLevel,
            // P0-07：日期字段（配合 CompareValues 的 IComparable 日期比较，支持 LessThan/GreaterThan 等匹配）
            DemandField.DueDate => demand.DueDate,
            DemandField.IssueDate => demand.IssueDate,
            _ => throw new NotSupportedException($"不支持的字段：{field}")
        };
    }

    /// <summary>
    /// 获取 Tie-break 字段值（通用字符串标识）
    /// </summary>
    private object? GetTieBreakValue(DemandRecord demand, string fieldName)
    {
        return fieldName.ToLowerInvariant() switch
        {
            "orderid" => demand.OrderId,
            "demandid" => demand.DemandId,
            "createdat" => demand.CreatedAt,
            _ => demand.OrderId // 默认用 OrderId 作为最终稳定 Tie-break
        };
    }

    /// <summary>
    /// 比较两个值（支持数值、字符串、日期）
    /// </summary>
    private int CompareValues(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        if (left is IComparable comparableLeft && right is IComparable && left.GetType() == right.GetType())
        {
            return comparableLeft.CompareTo(right);
        }

        // 类型不兼容时回退到字符串比较
        return string.Compare(left.ToString(), right?.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断值是否在列表中（IN 操作符）
    /// </summary>
    private bool IsInList(object? fieldValue, object? targetValue)
    {
        if (targetValue is not System.Collections.IEnumerable list)
        {
            return false;
        }

        foreach (var item in list)
        {
            var normalizedItem = NormalizeJsonValue(item);
            if (Equals(fieldValue, normalizedItem))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 字段值比较器（处理 nullable 值类型装箱问题）
    /// </summary>
    private class FieldValueComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            if (x is IComparable comparableX && y.GetType() == x.GetType())
            {
                return comparableX.CompareTo(y);
            }

            return string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
        }
    }
}

/// <summary>
/// Demand 记录（供匹配与排序）
/// 注：实际生产中由 2号位从 ScheduleRun 数据提供，此处为 3号位独立定义的契约
/// </summary>
public sealed class DemandRecord
{
    public required string DemandId { get; init; }
    public required string OrderId { get; init; }
    public DateTime CreatedAt { get; init; }

    // DemandField 可匹配字段
    public double? RemainingTimeHours { get; init; }
    public string? DelayStatus { get; init; }
    public string? CustomerTier { get; init; }
    public string? OrderType { get; init; }
    public bool IsPmcProtected { get; init; }
    public int? PriorityLevel { get; init; }
    /// <summary>统一交期（P0-07：DemandField.DueDate 排序/匹配；null 语义见排序实现）</summary>
    public DateTime? DueDate { get; init; }
    /// <summary>订单发行日期（P0-07：DemandField.IssueDate 排序/匹配；MTS 订单无值可为 null）</summary>
    public DateTime? IssueDate { get; init; }
}
