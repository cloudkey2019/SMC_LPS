namespace LPS.APS.Application.Services.Query.Dto;

/// <summary>
/// 候选 vs 基础 计划版本对比复合查询结果（H1；U10 候选对比页）
/// 结构：任务变化汇总 + 影响汇总 + 双方版本概要（v1.2 §三 3.2）
/// </summary>
public class CandidateComparisonDto
{
    /// <summary>任务变化汇总（新增/移除/时间平移/资源变更/跨域阻挡）</summary>
    public TaskChangeSummaryDto TaskChangeSummary { get; set; } = new();

    /// <summary>影响汇总（受影响订单数/新增延期数/跨域影响/订单级估算）</summary>
    public ImpactSummaryDto ImpactSummary { get; set; } = new();

    /// <summary>基础版本概要</summary>
    public PlanVersionBriefDto BaseSummary { get; set; } = new();

    /// <summary>候选版本概要</summary>
    public PlanVersionBriefDto CandidateSummary { get; set; } = new();
}

/// <summary>
/// 任务变化汇总（H1；基于 Task 主键差集）
/// 主键 = OrderId + MaterialId + OperationSeq + OperationCode + RouteCode + PathId（跨版本稳定标识一道工序任务）
/// </summary>
public class TaskChangeSummaryDto
{
    /// <summary>新增任务数（候选存在、基础不存在）</summary>
    public int Added { get; set; }

    /// <summary>移除任务数（基础存在、候选不存在）</summary>
    public int Removed { get; set; }

    /// <summary>时间平移任务数（同主键且计划开始/结束时间任一变化；与资源变更可重叠）</summary>
    public int TimeShifted { get; set; }

    /// <summary>资源变更任务数（同主键且资源任一变化；与时间平移可重叠）</summary>
    public int ResourceChanged { get; set; }

    /// <summary>
    /// 跨域阻挡任务数（复用 G7 域依赖）
    /// ⚠️ 任务级跨域阻挡判定未落盘（与 G2-b 同一缺口），v1.2 保守返回 0，待 2号位 对齐（v1.2 §九-b）
    /// </summary>
    public int CrossDomainBlocked { get; set; }
}

/// <summary>影响汇总（H1）</summary>
public class ImpactSummaryDto
{
    /// <summary>受影响订单数（发生新增/移除/时间平移/资源变更任务的去重订单数）</summary>
    public int ImpactedOrderCount { get; set; }

    /// <summary>新增延期数（候选延期且基础同主键任务未延期的任务数，含候选新增即延期的任务）</summary>
    public int NewDelayCount { get; set; }

    /// <summary>
    /// 跨域影响数（复用 G7 域依赖）
    /// ⚠️ 任务级跨域影响计数未对齐，v1.2 保守返回 0，待 2号位 对齐（v1.2 §九-b）
    /// </summary>
    public int CrossDomainImpacted { get; set; }

    /// <summary>订单级估算数（⚠️ 2号位 边界，v1.2 未对齐返回 null，见 v1.2 §三 3.3）</summary>
    public int? EstimatedOnlyCount { get; set; }
}

/// <summary>计划版本概要（H1 双方版本；与 PlanVersionSummaryDto 同表投影）</summary>
public class PlanVersionBriefDto
{
    public int PlanVersionId { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public string VersionCategory { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ComputedAt { get; set; }
}
