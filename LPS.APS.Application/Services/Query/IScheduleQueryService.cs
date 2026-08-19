using LPS.APS.Application.Services.Query.Dto;

namespace LPS.APS.Application.Services.Query;

/// <summary>
/// 排程结果查询服务（3号位接口域职责 — §阶段5.3/第八部分场景1）
///
/// 用途：供 4号位前端甘特图拉取排程结果，不触碰推演期的内存沙盘
/// 数据来源：已落盘的 APS_Production 库（PlanVersion + Task + Resource + Order + Material）
///
/// V1 范围：
///   - 版本列表（供前端切换查看）
///   - 甘特图数据（按 PlanVersionId 返回 Task + Resource 明细）
///   - 排程概要（KPI：已排/未排/延期计数）
///
/// V2 扩展点（预留接口但暂不实现）：
///   - 差异对比（两个版本间的 Task 变化）
///   - 战报查询（ExplainTrace）
///   - 局部重排触发
/// </summary>
public interface IScheduleQueryService
{
    /// <summary>
    /// 获取计划版本列表（最新优先）
    /// </summary>
    Task<IReadOnlyList<PlanVersionSummaryDto>> GetVersionsAsync(
        int take = 30,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定计划版本的甘特图数据
    /// </summary>
    Task<GanttDataDto> GetGanttAsync(
        int planVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取排程概要 KPI（已排 / 未排 / 延期数）
    /// </summary>
    Task<ScheduleSummaryDto> GetSummaryAsync(
        int planVersionId,
        CancellationToken cancellationToken = default);
}
