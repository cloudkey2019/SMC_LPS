using System.Collections.Generic;

namespace LPS.APS.Application.Services.Query.Dto;

/// <summary>
/// 白天候选运行摘要（E：GET api/candidate/{candidateId}/summary）。
/// candidateId = ScheduleRun.Id（候选 Run）；runStatus 取 ScheduleRun.Status；订单/Task 数按候选 PlanVersion 统计。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class CandidateSummaryDto
{
    /// <summary>候选 Run Id（= ScheduleRun.Id）</summary>
    public int CandidateId { get; set; }

    /// <summary>运行状态（RUNNING / COMPLETED / PARTIAL_SUCCESS / FAILED）</summary>
    public string RunStatus { get; set; } = string.Empty;

    /// <summary>候选 PlanVersion 下订单数</summary>
    public int OrderCount { get; set; }

    /// <summary>候选 PlanVersion 下 Task 数</summary>
    public int TaskCount { get; set; }

    /// <summary>创建人（候选 PlanVersion 壳 CreatedBy）</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>创建时间（候选 PlanVersion 壳 CreatedAt）</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 候选 vs 基础 差异（E：GET api/candidate/{candidateId}/comparison；最小集字段，2号位 2026-09-24 E-3）。
/// 订单级差异列表由 Task 主键差集去重 OrderId 派生；与 H1 富对比（CandidateComparisonDto）互补、非替代。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class CandidateDiffDto
{
    /// <summary>基础计划版本 Id（= ScheduleRun.BasePlanVersionId）</summary>
    public int BasePlanVersionId { get; set; }

    /// <summary>候选计划版本 Id（= 候选 PlanVersion 壳）</summary>
    public int CandidatePlanVersionId { get; set; }

    /// <summary>候选新增订单（OrderId 列表；候选存在、基础不存在）</summary>
    public IReadOnlyList<int> AddedOrders { get; set; } = [];

    /// <summary>候选移除订单（OrderId 列表；基础存在、候选不存在）</summary>
    public IReadOnlyList<int> RemovedOrders { get; set; } = [];

    /// <summary>变化 Task（同主键、时间或资源变化；取候选侧）</summary>
    public IReadOnlyList<ChangedTaskDto> ChangedTasks { get; set; } = [];
}

/// <summary>变化 Task 明细（跨版本稳定主键 + 候选侧新排程）</summary>
public class ChangedTaskDto
{
    public int OrderId { get; set; }
    public int MaterialId { get; set; }
    public int OperationSeq { get; set; }
    public string OperationCode { get; set; } = string.Empty;
    public string? RouteCode { get; set; }
    public int? PathId { get; set; }
    public int? ResourceId { get; set; }
    public DateTime? PlannedStartTime { get; set; }
    public DateTime? PlannedEndTime { get; set; }
}