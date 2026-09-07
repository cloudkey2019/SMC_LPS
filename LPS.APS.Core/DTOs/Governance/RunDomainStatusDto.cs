namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// Run 域级状态汇总 DTO（3-4联调接口 G8；3号位文档 §十六 FULL失败链）
/// 3号位提供：Run 下每个 Domain 的 成功/失败/被阻断 状态与原因展示所需元数据。
/// Status 取值：
///   COMPLETED  - 该域 PlanVersion 已生成且 ACTIVE（成功）
///   CANDIDATE  - 该域 PlanVersion 为 CANDIDATE（待人工确认）
///   RUNNING    - 该域 PlanVersion 仍 BUILDING / Run 未终态
///   FAILED     - 该域 PlanVersion 为 FAILED（失败根因域）
///   BLOCKED    - 因上游失败被阻断，本次未生成 PlanVersion（非根因）
///   NOT_STARTED- 无关 Domain 未参与本次（正常）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class RunDomainStatusDto
{
    /// <summary>域标识（DomainKey，对应 PlanVersion.DomainKey / ExpectedDomainKeysJson 元素）</summary>
    public string DomainKey { get; set; } = string.Empty;

    /// <summary>域级状态：COMPLETED / CANDIDATE / RUNNING / FAILED / BLOCKED / NOT_STARTED</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>原因说明（FAILED=失败信息；BLOCKED=被阻断说明；其余可为空）</summary>
    public string? Reason { get; set; }

    /// <summary>该域本次 Run 的 PlanVersionId（未生成时为空）</summary>
    public long? PlanVersionId { get; set; }

    /// <summary>该域本次 Run 的 PlanVersion 版本号（未生成时为空）</summary>
    public string? PlanVersionCode { get; set; }

    /// <summary>Run 启动时间（ScheduleRun.StartedAt）</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>该域 PlanVersion 计算完成时间（未生成时为空）</summary>
    public DateTime? ComputedAt { get; set; }

    /// <summary>该域 PlanVersion 激活时间（未激活时为空）</summary>
    public DateTime? ActivatedAt { get; set; }
}
