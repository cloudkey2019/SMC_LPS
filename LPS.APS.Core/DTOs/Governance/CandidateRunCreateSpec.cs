namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 白天候选运行创建入参（B-1）
/// 语义：3号位 创建白天候选 ScheduleRun（RUNNING，冻结 RunType × Purpose × 策略版本 × 预期 Domain）
///       + 新建 Candidate PlanVersion 壳（BUILDING）；触发 2号位 主流程留契约接缝。
/// 校验规则见 IRunLifecycleService.CreateCandidateRunAsync（任一失败抛 InvalidOperationException）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class CandidateRunCreateSpec
{
    /// <summary>运行类型（白天候选类：MANUAL_RESCHEDULE / LOCAL_RESCHEDULE / INSERT_ORDER_WHATIF）</summary>
    public string RunType { get; set; } = string.Empty;

    /// <summary>用途（须 ∈ RunType 冻结合法组合，见 StrategyProfilePurpose）</summary>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>目标 Domain（白天候选严格单 Domain；对应 PlanVersion.DomainKey 与 ExpectedDomainKeysJson）</summary>
    public string DomainKey { get; set; } = string.Empty;

    /// <summary>所基于的当前 ACTIVE 计划版本（可选；缺省由服务按 DomainKey 解析当前 ACTIVE，无 ACTIVE 拒绝）</summary>
    public int? BasePlanVersionId { get; set; }

    /// <summary>本次运行统一数据切片边界（可选；缺省 now）</summary>
    public DateTime? DataCutoffTime { get; set; }

    /// <summary>操作人（必填；写 ScheduleRun.TriggeredBy / 壳 CreatedBy / 审计 OperatedBy）</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Candidate 壳计划窗口起点（可选；由服务按 Base ACTIVE 复制，缺省今天）</summary>
    public DateTime? PlanHorizonStart { get; set; }

    /// <summary>Candidate 壳计划窗口终点（可选；由服务按 Base ACTIVE 复制，缺省今天 + 90 天）</summary>
    public DateTime? PlanHorizonEnd { get; set; }
}
