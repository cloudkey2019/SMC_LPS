namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 发布前校验结果（阶段 A-5：3号位 Core 层 DTO）
/// 用于返回发布前完整性检查的结果
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class PublishValidationResult
{
    /// <summary>是否通过校验</summary>
    public bool IsValid { get; set; }

    /// <summary>校验错误列表</summary>
    public List<ValidationError> Errors { get; set; } = new();

    /// <summary>校验警告列表</summary>
    public List<ValidationWarning> Warnings { get; set; } = new();

    /// <summary>校验时间</summary>
    public DateTime ValidatedAt { get; set; }

    /// <summary>汇总全部错误为分号分隔消息（P0-05：发布被拒时的错误摘要）</summary>
    public string GetErrorMessage() => string.Join("; ", Errors.Select(e => e.Message));
}

/// <summary>
/// 校验错误（阻止发布）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class ValidationError
{
    /// <summary>错误代码</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>错误消息</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>字段名称</summary>
    public string? FieldName { get; set; }

    /// <summary>错误详情</summary>
    public string? Details { get; set; }
}

/// <summary>
/// 校验警告（不阻止发布）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class ValidationWarning
{
    /// <summary>警告代码</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>警告消息</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>字段名称</summary>
    public string? FieldName { get; set; }
}
