using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 需求优先级执行器（2号位职责 — 消费3号位的DemandPriorityConfig）
///
/// 执行算法（PM冻结口径，方案A：外部按层调用）：
/// 1. PeggingOrchestrator 逐层形成「当前层 Demand 集合」
/// 2. PeggingOrchestrator 从 Frozen DemandPriority 取「当前层 Segments」后调用本执行器
/// 3. 本执行器只排序单个计算层：按 SegmentOrder 升序遍历 Segment
/// 4. 每个 Demand 从第一个 Segment 开始匹配
/// 5. 命中第一条后停止，不再进入其它 Segment（First Match）
/// 6. 每个 Segment 内部按 SortFields 依次排序
/// 7. 最后 StableTieBreak 确保确定性（最终兜底：DemandKey ASC）
///
/// 职责边界：
/// - 3号位负责策略冻结，输出 FrozenStrategySnapshot.DemandPriority
/// - 2号位负责执行器实现，消费策略并生成 DemandSequence
/// - 本执行器只排序「单个计算层」的 Demand；分层编排由 PeggingOrchestrator 负责
/// </summary>
public sealed class DemandPriorityExecutor : IDemandPriorityExecutor
{
    /// <summary>
    /// 当前执行器支持的业务字段白名单（大小写不敏感）。
    /// 3号位配置中出现白名单外的 FieldName 属于策略配置错误（P1-02），必须显式报错，不得静默当作空值继续排。
    /// </summary>
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORDERTYPE", "DELAYSTATUS", "CUSTOMERTIER", "DUEDATE",
        "ISSUEDATE", "PROTECTIONSTATUS", "DEMANDKEY"
    };

    private readonly ILogger<DemandPriorityExecutor> _logger;

    public DemandPriorityExecutor(ILogger<DemandPriorityExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 执行「单个计算层」的 Demand 排序（config 已由 PeggingOrchestrator 收敛为当前层 Segments）。
    ///
    /// 返回：有序的 Demand 列表，已赋值 DemandSequence = 1, 2, 3...
    /// </summary>
    public List<UpstreamDemand> ExecutePrioritySort(
        IEnumerable<UpstreamDemand> demands,
        DemandPriorityConfig config)
    {
        var demandList = demands.ToList();
        if (demandList.Count == 0)
        {
            return demandList;
        }

        var segments = config.Segments
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.SegmentOrder)
            .ToList();

        if (segments.Count == 0)
        {
            // P1-01：当前层无启用 Segment 时，不按调用方原始集合顺序，改用稳定 DemandKey ASC 兜底（确定性）
            _logger.LogWarning(
                "DemandPriority 当前层无启用 Segment，按稳定 DemandKey ASC 兜底（共 {Count} 条）",
                demandList.Count);
            var fallback = demandList.OrderBy(d => d.DemandKey, StringComparer.Ordinal).ToList();
            StampDemandSequence(fallback);
            return fallback;
        }

        // P1-02：未知 FieldName 显式报错，不允许静默吞掉3号位拼写错误
        ValidateConfigFields(segments);

        var sortedDemands = new List<UpstreamDemand>(demandList.Count);
        var assigned = new HashSet<UpstreamDemand>();

        foreach (var segment in segments)
        {
            var matched = new List<UpstreamDemand>();
            foreach (var demand in demandList)
            {
                if (!assigned.Contains(demand) && IsMatchSegment(demand, segment))
                {
                    matched.Add(demand);
                }
            }

            if (matched.Count == 0)
            {
                continue;
            }

            foreach (var demand in SortWithinSegment(matched, segment))
            {
                sortedDemands.Add(demand);
                assigned.Add(demand);
            }
        }

        // 未命中任何 Segment 的 Demand：稳定 DemandKey ASC 兜底（确定性）
        var unmatched = demandList
            .Where(d => !assigned.Contains(d))
            .OrderBy(d => d.DemandKey, StringComparer.Ordinal)
            .ToList();

        if (unmatched.Count > 0)
        {
            _logger.LogWarning(
                "DemandPriority 当前层 {Count} 条 Demand 未命中任何 Segment，按稳定 DemandKey ASC 兜底",
                unmatched.Count);
            sortedDemands.AddRange(unmatched);
        }

        StampDemandSequence(sortedDemands);
        return sortedDemands;
    }

    private static void StampDemandSequence(List<UpstreamDemand> sorted)
    {
        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i].DemandSequence = i + 1;
        }
    }

    private void ValidateConfigFields(IReadOnlyList<PrioritySegmentConfig> segments)
    {
        foreach (var segment in segments)
        {
            foreach (var condition in segment.MatchConditions)
            {
                EnsureKnownField(condition.FieldName);
            }

            foreach (var sortField in segment.SortFields)
            {
                EnsureKnownField(sortField.FieldName);
            }

            foreach (var tieBreakField in segment.StableTieBreakFields)
            {
                EnsureKnownField(tieBreakField);
            }
        }
    }

    private void EnsureKnownField(string fieldName)
    {
        if (!KnownFields.Contains(fieldName))
        {
            throw new InvalidOperationException(
                $"DemandPriority 配置错误：未知字段名 '{fieldName}'。允许的字段：{string.Join(", ", KnownFields.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))}");
        }
    }

    private bool IsMatchSegment(UpstreamDemand demand, PrioritySegmentConfig segment)
    {
        if (segment.MatchConditions.Count == 0)
        {
            return true;
        }

        foreach (var condition in segment.MatchConditions)
        {
            if (!EvaluateCondition(demand, condition))
            {
                return false;
            }
        }

        return true;
    }

    private bool EvaluateCondition(UpstreamDemand demand, MatchCondition condition)
    {
        var fieldValue = GetFieldValue(demand, condition.FieldName);

        return condition.Operator.ToUpperInvariant() switch
        {
            "EQ" => string.Equals(fieldValue, condition.Value, StringComparison.OrdinalIgnoreCase),
            "IN" => condition.Value.Split(',').Any(v => string.Equals(v.Trim(), fieldValue, StringComparison.OrdinalIgnoreCase)),
            "LT" => CompareNumericOrDate(fieldValue, condition.Value) < 0,
            "LTE" => CompareNumericOrDate(fieldValue, condition.Value) <= 0,
            "GT" => CompareNumericOrDate(fieldValue, condition.Value) > 0,
            "GTE" => CompareNumericOrDate(fieldValue, condition.Value) >= 0,
            _ => throw new NotSupportedException($"Unsupported operator: {condition.Operator}")
        };
    }

    private List<UpstreamDemand> SortWithinSegment(
        List<UpstreamDemand> demands,
        PrioritySegmentConfig segment)
    {
        var query = demands.AsEnumerable();

        // 依次按 SortFields 排序
        if (segment.SortFields.Count > 0)
        {
            IOrderedEnumerable<UpstreamDemand>? orderedQuery = null;

            foreach (var sortField in segment.SortFields)
            {
                var isAscending = string.Equals(sortField.Direction, "ASC", StringComparison.OrdinalIgnoreCase);

                if (orderedQuery == null)
                {
                    orderedQuery = isAscending
                        ? query.OrderBy(d => GetComparableValue(d, sortField.FieldName))
                        : query.OrderByDescending(d => GetComparableValue(d, sortField.FieldName));
                }
                else
                {
                    orderedQuery = isAscending
                        ? orderedQuery.ThenBy(d => GetComparableValue(d, sortField.FieldName))
                        : orderedQuery.ThenByDescending(d => GetComparableValue(d, sortField.FieldName));
                }
            }

            query = orderedQuery ?? query;
        }

        // StableTieBreak
        if (segment.StableTieBreakFields.Count > 0)
        {
            var orderedQuery = query as IOrderedEnumerable<UpstreamDemand>;

            foreach (var tieBreakField in segment.StableTieBreakFields)
            {
                orderedQuery = orderedQuery == null
                    ? query.OrderBy(d => GetComparableValue(d, tieBreakField))
                    : orderedQuery.ThenBy(d => GetComparableValue(d, tieBreakField));
            }

            query = orderedQuery ?? query;
        }

        // 最终兜底：DemandKey ASC（稳定、文化无关）
        var finalOrdered = (query as IOrderedEnumerable<UpstreamDemand>)?.ThenBy(d => d.DemandKey, StringComparer.Ordinal)
                           ?? query.OrderBy(d => d.DemandKey, StringComparer.Ordinal);

        return finalOrdered.ToList();
    }

    private string GetFieldValue(UpstreamDemand demand, string fieldName)
    {
        return fieldName.ToUpperInvariant() switch
        {
            "ORDERTYPE" => demand.OrderType ?? string.Empty,
            "DELAYSTATUS" => demand.DelayStatus ?? string.Empty,
            "CUSTOMERTIER" => demand.CustomerTier ?? string.Empty,
            "DUEDATE" => demand.DueDate?.ToString("O") ?? string.Empty,
            "ISSUEDATE" => demand.IssueDate?.ToString("O") ?? string.Empty,
            "PROTECTIONSTATUS" => demand.ProtectionStatus ?? string.Empty,
            "DEMANDKEY" => demand.DemandKey,
            _ => throw UnknownFieldError(fieldName)
        };
    }

    private IComparable GetComparableValue(UpstreamDemand demand, string fieldName)
    {
        return fieldName.ToUpperInvariant() switch
        {
            "DUEDATE" => demand.DueDate ?? DateTime.MaxValue,
            "ISSUEDATE" => demand.IssueDate ?? DateTime.MaxValue,
            "DEMANDKEY" => demand.DemandKey,
            "ORDERTYPE" => demand.OrderType ?? string.Empty,
            "DELAYSTATUS" => demand.DelayStatus ?? string.Empty,
            "CUSTOMERTIER" => demand.CustomerTier ?? string.Empty,
            "PROTECTIONSTATUS" => demand.ProtectionStatus ?? string.Empty,
            _ => throw UnknownFieldError(fieldName)
        };
    }

    private static InvalidOperationException UnknownFieldError(string fieldName)
        => new($"DemandPriority 配置错误：未知字段名 '{fieldName}'");

    private int CompareNumericOrDate(string value1, string value2)
    {
        if (DateTime.TryParse(value1, out var date1) && DateTime.TryParse(value2, out var date2))
        {
            return date1.CompareTo(date2);
        }

        if (decimal.TryParse(value1, out var num1) && decimal.TryParse(value2, out var num2))
        {
            return num1.CompareTo(num2);
        }

        return string.Compare(value1, value2, StringComparison.OrdinalIgnoreCase);
    }
}
