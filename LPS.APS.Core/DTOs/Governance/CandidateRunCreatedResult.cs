namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 白天候选运行创建结果（B-1）
/// 由 IScheduleRunRepository.CreateCandidateRunAsync 在单事务内返回（Run + 壳任一失败整体回滚）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class CandidateRunCreatedResult
{
    /// <summary>新建 ScheduleRun.Id（RUNNING，冻结基线；待 2号位 按触发契约执行并收口）</summary>
    public int NewScheduleRunId { get; set; }

    /// <summary>新建 Candidate PlanVersion 壳 Id（BUILDING）</summary>
    public int NewPlanVersionId { get; set; }
}
