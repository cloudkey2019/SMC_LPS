namespace LPS.APS.Application.Services.Query.Dto;

/// <summary>
/// 甘特图数据（前端一次性拉取整个版本的 Task + Resource 明细）
/// </summary>
public class GanttDataDto
{
    public int PlanVersionId { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public DateTime PlanHorizonStart { get; set; }
    public DateTime PlanHorizonEnd { get; set; }

    /// <summary>
    /// 资源行（甘特图 Y 轴）
    /// </summary>
    public IReadOnlyList<GanttResourceDto> Resources { get; set; } = Array.Empty<GanttResourceDto>();

    /// <summary>
    /// 任务条（甘特图 X 轴上的矩形）
    /// </summary>
    public IReadOnlyList<GanttTaskDto> Tasks { get; set; } = Array.Empty<GanttTaskDto>();
}

public class GanttResourceDto
{
    public int ResourceId { get; set; }
    public string ResourceCode { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public int? FactoryId { get; set; }
    public int? ProductionDepartmentId { get; set; }

    /// <summary>
    /// 资源所属域标识（G2-b 档①；U01 多 Domain 染色分组）
    /// V1.2：域 = 工厂维度（FACTORY_{FactoryId}，与 LogicalProductionDemand.DomainKey 一致）
    /// </summary>
    public string? DomainKey { get; set; }

    /// <summary>
    /// 工厂名称（G2-b 档①；域主数据投影）
    /// </summary>
    public string? FactoryName { get; set; }

    /// <summary>
    /// 生产部门名称（G2-b 档①；域主数据投影）
    /// </summary>
    public string? ProductionDepartmentName { get; set; }

    /// <summary>
    /// 工序阶段标识/名称（G2-b 档①；ProductionDepartment.StageCode，部门 vs 阶段 1:1）
    /// </summary>
    public string? Stage { get; set; }

    /// <summary>
    /// 设备不可用窗口（G2-b 档②；依赖排程/根因事实，未就绪返回 null，v1.3 待 2号位 对齐）
    /// </summary>
    public IReadOnlyList<GanttUnavailableWindowDto>? UnavailableWindows { get; set; }
}

public class GanttTaskDto
{
    public long TaskId { get; set; }
    public string TaskNo { get; set; } = string.Empty;
    public long OrderId { get; set; }
    public string? OrderNo { get; set; }
    public int MaterialId { get; set; }
    public string? MaterialCode { get; set; }
    public string? MaterialName { get; set; }
    public int? ResourceId { get; set; }
    public string OperationCode { get; set; } = string.Empty;
    public int OperationSeq { get; set; }
    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public DateTime? PlannedStartTime { get; set; }
    public DateTime? PlannedEndTime { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 是否延期（PlannedEndTime > CustomerDueDate）
    /// </summary>
    public bool IsDelayed { get; set; }

    /// <summary>
    /// 任务所属域标识（G2-b 档①；U01，从 Order.DomainKey 投影）
    /// </summary>
    public string? DomainKey { get; set; }

    /// <summary>
    /// 净需求数量（G2-b 档①；U05，落盘 Task.Quantity = NetOutputQty 净合格产出）
    /// </summary>
    public decimal? NetQty { get; set; }

    /// <summary>
    /// 含良率补偿数量（G2-b 档①；U05 净需求 vs 含良率）
    /// ⚠️ Task 表当前无此列（FinalTaskDraft.PlannedProcessQty 不落盘），未就绪返回 null，待 2号位 DDL
    /// </summary>
    public decimal? PlannedProcessQty { get; set; }

    /// <summary>
    /// MES 下发资格（G2-b 档①；U16）
    /// ⚠️ FinalTaskDraft 无 MesEligible 字段，MES 资格判定尚未落库，未就绪返回 false（保守默认）
    /// </summary>
    public bool MesEligible { get; set; }

    /// <summary>
    /// 不可下发原因（G2-b 档①；U17，硬枚举见 v1.2 §二 2.3）
    /// ⚠️ 未就绪返回 null，待 5号位/2号位 对齐
    /// </summary>
    public IReadOnlyList<string>? MesIneligibleReasons { get; set; }

    /// <summary>
    /// 跨域阻挡标记（G2-b 档①；U14）
    /// ⚠️ Task 表无此列，未就绪返回 false（保守默认），待 2号位 对齐
    /// </summary>
    public bool CrossDomainBlocked { get; set; }

    /// <summary>
    /// 跨域阻挡原因（G2-b 档①；复用 G7 域依赖）
    /// ⚠️ 未就绪返回 null，待 2号位 对齐
    /// </summary>
    public string? CrossDomainBlockReason { get; set; }

    /// <summary>
    /// Demand Protection 锁标记（G2-b 档②；U08，未就绪返回 null，v1.3）
    /// </summary>
    public string? LockMarker { get; set; }

    /// <summary>
    /// 多 Order 共担一个 Task 的份额（G2-b 档②；U04，未就绪返回 null，v1.3）
    /// </summary>
    public IReadOnlyList<GanttTaskShareDto>? TaskShares { get; set; }

    /// <summary>
    /// 数量-时间分段（G2-b 档②；U06，40+60 不压成 100，未就绪返回 null，v1.3）
    /// </summary>
    public IReadOnlyList<GanttTaskSegmentDto>? TaskSegments { get; set; }

    /// <summary>
    /// 延期原因明细（G2-b 档②；根因优先，未就绪返回 null，v1.3 待 2号位/1号位 对齐）
    /// </summary>
    public IReadOnlyList<GanttDelayReasonDto>? DelayReasons { get; set; }

    /// <summary>
    /// 事实类型（G2-b 档②；FACT/RESULT/ESTIMATED/RECOMMENDATION，未就绪返回 null，v1.3）
    /// </summary>
    public string? FactType { get; set; }

    /// <summary>
    /// 上游工序阶段阻挡（G2-b 档②；未就绪返回 null，v1.3）
    /// </summary>
    public bool? UpstreamStageBlocked { get; set; }

    /// <summary>
    /// 上游阻挡阶段编码（G2-b 档②；未就绪返回 null，v1.3）
    /// </summary>
    public string? UpstreamStageCode { get; set; }
}

/// <summary>设备不可用窗口（G2-b 档②）</summary>
public class GanttUnavailableWindowDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string? Reason { get; set; }
    public IReadOnlyList<long>? ImpactedTaskIds { get; set; }
}

/// <summary>多 Order 共担一个 Task 的份额（G2-b 档②）</summary>
public class GanttTaskShareDto
{
    public long OrderId { get; set; }
    public string? OrderNo { get; set; }
    public decimal ShareQty { get; set; }
    public decimal ShareRatio { get; set; }
}

/// <summary>数量-时间分段（G2-b 档②）</summary>
public class GanttTaskSegmentDto
{
    public int SegmentSeq { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public decimal Qty { get; set; }
    public decimal QtyRatio { get; set; }
    public string? Reason { get; set; }
}

/// <summary>延期原因明细（G2-b 档②）</summary>
public class GanttDelayReasonDto
{
    public string ReasonCode { get; set; } = string.Empty;
    public string? IssueCategory { get; set; }
    public string? Severity { get; set; }
    public string? Description { get; set; }
    public string? RelatedObjectRef { get; set; }
    public decimal? ImpactHours { get; set; }
}
