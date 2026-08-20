using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// Demand Priority 发布前校验服务（阶段 C-3）
/// 红线校验：
/// 1. 禁止全局 PriorityScore
/// 2. 第一命中即止（命中后不再匹配后续 Segment）
/// 3. Segment 配置合法性
/// </summary>
public sealed class DemandPriorityValidator
{
    /// <summary>
    /// Matcher 实际支持的合法匹配/排序字段白名单（P0-07 "Validator 合法字段映射"）。
    /// 必须与 DemandPriorityMatcher 三处 switch 保持一致；新增 DemandField 枚举值时
    /// 必须在此显式登记，否则发布前校验拒绝（防运行期 NotSupportedException）。
    /// </summary>
    private static readonly HashSet<DemandField> SupportedDemandFields =
    [
        DemandField.RemainingTimeHours,
        DemandField.DelayStatus,
        DemandField.CustomerTier,
        DemandField.OrderType,
        DemandField.IsPmcProtected,
        DemandField.PriorityLevel,
        DemandField.DueDate,     // P0-07
        DemandField.IssueDate,   // P0-07
    ];

    /// <summary>
    /// 验证 DemandPriorityBlock 配置的合法性
    /// </summary>
    public ValidationResult Validate(DemandPriorityBlock block)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (block.Segments.Count == 0)
        {
            errors.Add("至少需要配置一个优先级 Segment");
            return new ValidationResult(false, errors, warnings);
        }

        var orders = block.Segments.Select(s => s.SegmentOrder).ToList();
        if (orders.Count != orders.Distinct().Count())
        {
            errors.Add("Segment SegmentOrder 必须唯一");
        }

        foreach (var segment in block.Segments)
        {
            if (segment.SegmentOrder < 1)
            {
                errors.Add($"Segment {segment.SegmentOrder}: SegmentOrder 必须为正整数");
            }
        }

        foreach (var segment in block.Segments)
        {
            ValidateSegment(segment, errors, warnings);
            ValidateFieldMapping(segment, errors);   // P0-07：合法字段映射（与 Matcher 白名单一致）
        }

        ValidateNoGlobalPriorityScore(block, errors);

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    private void ValidateSegment(PrioritySegment segment, List<string> errors, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(segment.SegmentName))
        {
            errors.Add($"Segment {segment.SegmentOrder}: SegmentName 不能为空");
        }

        if (segment.MatchConditions.Count == 0)
        {
            errors.Add($"Segment {segment.SegmentOrder}: 至少需要一个匹配条件");
        }

        foreach (var condition in segment.MatchConditions)
        {
            ValidateMatchCondition(segment.SegmentOrder, condition, errors);
        }

        if (segment.SortFields.Count == 0)
        {
            warnings.Add($"Segment {segment.SegmentOrder}: 建议配置至少一个排序字段（当前保持原顺序）");
        }
    }

    private void ValidateMatchCondition(int segmentOrder, SegmentMatchCondition condition, List<string> errors)
    {
        // 非 Equals/NotEquals 操作符必须有值；Equals/NotEquals 允许 null（语义为"字段为空"）
        if (condition.Value == null && condition.Operator != ConditionOperator.Equals && condition.Operator != ConditionOperator.NotEquals)
        {
            errors.Add($"Segment {segmentOrder}: {condition.Operator} 操作符的条件值不能为 null");
        }

        // IN 操作符要求值必须是列表类型（排除字符串，因为 string 也实现 IEnumerable<char>）
        if (condition.Operator == ConditionOperator.In &&
            (condition.Value is null or string ||
             condition.Value is not System.Collections.IEnumerable))
        {
            errors.Add($"Segment {segmentOrder}: IN 操作符的值必须是列表类型");
        }
    }

    /// <summary>
    /// 校验 Segment 引用的所有 DemandField 均在 Matcher 支持白名单内（P0-07）。
    /// 防止枚举新增值未同步 Matcher 时，配置越过发布校验、运行期才抛 NotSupportedException。
    /// </summary>
    private void ValidateFieldMapping(PrioritySegment segment, List<string> errors)
    {
        foreach (var condition in segment.MatchConditions)
        {
            if (!SupportedDemandFields.Contains(condition.Field))
            {
                errors.Add($"Segment {segment.SegmentOrder}: 匹配字段 {condition.Field} 不在合法字段白名单内");
            }
        }

        foreach (var sortField in segment.SortFields)
        {
            if (!SupportedDemandFields.Contains(sortField.Field))
            {
                errors.Add($"Segment {segment.SegmentOrder}: 排序字段 {sortField.Field} 不在合法字段白名单内");
            }
        }
    }

    private static readonly string[] ForbiddenGlobalScoreFields =
        ["PriorityScore", "GlobalPriorityScore"];

    private void ValidateNoGlobalPriorityScore(DemandPriorityBlock block, List<string> errors)
    {
        foreach (var segment in block.Segments)
        {
            foreach (var sortField in segment.SortFields)
            {
                var fieldName = sortField.Field.ToString();
                if (ForbiddenGlobalScoreFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Segment {segment.SegmentOrder}: 红线违规：禁止使用全局 {fieldName} 字段");
                }
            }
        }
    }
}

public sealed class ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }

    public ValidationResult(bool isValid, IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null)
    {
        IsValid = isValid;
        Errors = errors;
        Warnings = warnings ?? [];
    }

    public string GetErrorMessage() => string.Join("; ", Errors);
}
