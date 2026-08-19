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
