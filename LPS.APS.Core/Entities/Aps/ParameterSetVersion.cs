using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 参数集版本表
/// 对应 APS_Production.ParameterSetVersion（冻结 DDL v5.1.2 §3.10.4）
/// 红线：已发布版本不可原地修改；正式排程只允许 PUBLISHED 状态。
/// Status 取值见 <see cref="GovernanceVersionStatus"/>（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED）
/// </summary>
public class ParameterSetVersion
{
    public long Id { get; set; }
    public long ParameterSetId { get; set; }
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
    public bool IsDefault { get; set; }
    public string? Remarks { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public string? LockJson { get; set; }
    public string? SupplyJson { get; set; }
    public string? ProcurementJson { get; set; }
}
