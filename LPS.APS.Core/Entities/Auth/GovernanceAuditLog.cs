namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 治理审计日志实体（阶段 A-7：3号位 Auth 库实体）
/// 记录治理版本发布/禁用操作到 Auth 库的 AuditLog 表。
/// 对应表：APS_Auth.dbo.GovernanceAuditLog
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class GovernanceAuditLog
{
    /// <summary>主键</summary>
    public long Id { get; set; }

    /// <summary>操作类型（Publish / Disable / Archive）</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>实体类型（RuleSetVersion / ParameterSetVersion / StrategyProfileVersion）</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>实体 Id（版本 Id）</summary>
    public long EntityId { get; set; }

    /// <summary>实体版本号</summary>
    public string? VersionCode { get; set; }

    /// <summary>操作前状态</summary>
    public string? BeforeStatus { get; set; }

    /// <summary>操作后状态</summary>
    public string? AfterStatus { get; set; }

    /// <summary>操作人</summary>
    public string? OperatedBy { get; set; }

    /// <summary>操作时间</summary>
    public DateTime OperatedAt { get; set; }

    /// <summary>操作备注（原因、审批单号等）</summary>
    public string? Remarks { get; set; }
}
