namespace LPS.APS.Core.Dto;

/// <summary>
/// 任务规格（5号位拆批返回）
/// 2号位据此 INSERT 到 [Task] 表 + 填充 SchedulingContext
/// </summary>
public class TaskSpec
{
    public string TaskNo { get; init; } = string.Empty;
    public long OrderId { get; init; }
    public int MaterialId { get; init; }
    public int OperationSeq { get; init; }
    public string OperationCode { get; init; } = string.Empty;
    public string OperationName { get; init; } = string.Empty;
    public int? ResourceId { get; init; }
    public string RouteCode { get; init; } = "DEFAULT";
    public int PathId { get; init; } = 1;
    public decimal Quantity { get; init; }
    public string UOM { get; init; } = string.Empty;
    public decimal DurationMinutes { get; init; }
    public string TaskType { get; init; } = "PRODUCTION";

    // 透传给 SchedulingContext.Tasks（1号位求解用）
    public int OrderPriority { get; init; }
    public DateTime CustomerDueDate { get; init; }
}
