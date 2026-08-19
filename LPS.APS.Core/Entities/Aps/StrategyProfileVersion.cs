using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 策略包版本表
/// 对应 APS_Production.StrategyProfileVersion（冻结 DDL v5.1.2 §3.10.6）
/// 关键表：将规则集版本和参数集版本组合为可发布、可追溯、可被 ScheduleRun 引用的策略包版本。
/// 红线：已发布版本不可原地修改；同一策略包下仅一个 IsDefault=1 的 PUBLISHED 版本（UQ_StrategyProfileVersion_DefaultPublished）。
/// Status 取值见 <see cref="GovernanceVersionStatus"/>（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED）
/// </summary>
public class StrategyProfileVersion
{
    public long Id { get; set; }
    public long StrategyProfileId { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public long RuleSetVersionId { get; set; }
    public long ParameterSetVersionId { get; set; }
    public string Status { get; set; } = GovernanceVersionStatus.Draft;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsDefault { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
