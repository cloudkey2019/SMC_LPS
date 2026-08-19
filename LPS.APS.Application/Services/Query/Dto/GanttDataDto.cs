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
}
