namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// FAILED 恢复壳入参（P1-03，3号位）。
/// 一域一壳：为待恢复 Run 的每个预期 Domain 各建一个 RECOVERY PlanVersion 壳；
/// 计划窗口继承失败 Run 既有 PlanVersion（缺失兜底今天 ~ +90 天，不得静默平移）。
/// 由 3号位 运行治理侧在 <see cref="LPS.APS.Core.Interfaces.IRunLifecycleService.RecoverFailedRunAsync"/> 内装配后，
/// 交 <see cref="LPS.APS.Core.Interfaces.IScheduleRunRepository.InsertForRecoveryWithShellsAsync"/> 与 Run 同一事务原子写。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class RecoveryShellSpec
{
    /// <summary>目标 Domain（对应 PlanVersion.DomainKey；须与 ExpectedDomainKeysJson 逐一对齐）</summary>
    public string DomainKey { get; set; } = string.Empty;

    /// <summary>壳计划窗口起点（继承失败 Run 既有 PlanVersion，缺失兜底今天）</summary>
    public DateTime PlanHorizonStart { get; set; }

    /// <summary>壳计划窗口终点（继承失败 Run 既有 PlanVersion，缺失兜底今天 + 90 天）</summary>
    public DateTime PlanHorizonEnd { get; set; }
}