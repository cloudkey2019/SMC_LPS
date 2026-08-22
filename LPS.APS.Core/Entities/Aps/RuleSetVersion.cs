using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 规则集版本表
/// 对应 APS_Production.RuleSetVersion（冻结 DDL v5.1.2 §3.10.2）
/// 红线：已发布版本不可原地修改，须创建新版本；正式排程只允许 PUBLISHED 状态。
/// Status 取值见 <see cref="GovernanceVersionStatus"/>（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED）
/// </summary>
public class RuleSetVersion
{
    public long Id { get; set; }
    public long RuleSetId { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public string Status { get; set; } = GovernanceVersionStatus.Draft;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? Remarks { get; set; }
    /// <summary>需求优先级规则 JSON（发布装配中间态；0号位 方案 A §7.3：退出版本表正式持久化，统一经 ContentSnapshotJson 落库）</summary>
    public string? DemandPriorityJson { get; set; }

    /// <summary>
    /// 发布内容快照 JSON（方案 A 落点，契约 §6.10.5；DDL 由 2号位 按变更申请执行）
    /// 该版本发布时的完整规则内容（含 DemandPriority 子块），可重放载体；PUBLISHED 后不可原地修改。
    /// </summary>
    public string? ContentSnapshotJson { get; set; }
}
