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
    public string? Remarks { get; set; }
    /// <summary>Lock 参数 JSON（发布装配中间态；0号位 方案 A §7.3：退出版本表正式持久化，统一经 ContentSnapshotJson 落库）</summary>
    public string? LockJson { get; set; }

    /// <summary>Supply 参数 JSON（发布装配中间态；同左，经 ContentSnapshotJson 落库）</summary>
    public string? SupplyJson { get; set; }

    /// <summary>Procurement 参数 JSON（含 PlanningYield；发布装配中间态；同左，经 ContentSnapshotJson 落库）</summary>
    public string? ProcurementJson { get; set; }

    /// <summary>Solver 策略块 JSON（Mode/Bottleneck/OnTimeTarget/Split/Setup/StageOverlap；发布装配中间态；经 ContentSnapshotJson 落库）</summary>
    public string? SolverStrategyJson { get; set; }

    /// <summary>Candidate 技术 Guardrail 块 JSON（Normal/Soft/Hard Ms、Repair/Propagation、ResourceTopN 等；发布装配中间态；经 ContentSnapshotJson 落库）</summary>
    public string? CandidateGuardrailJson { get; set; }

    /// <summary>
    /// 发布内容快照 JSON（方案 A 落点，契约 §6.10.5；DDL 由 2号位 按变更申请执行）
    /// 该版本发布时的完整参数内容（Lock/Supply/Procurement/PlanningYield/SolverStrategy/CandidateGuardrail 子块），
    /// 可重放载体；PUBLISHED 后不可原地修改。
    /// </summary>
    public string? ContentSnapshotJson { get; set; }
}
