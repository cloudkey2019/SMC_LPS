namespace LPS.APS.Core.Dto;

/// <summary>
/// PI Position查询DTO（5号位提供给4号位）
///
/// 数据来源：ProductionInstructionPositionSnapshot表（2号位落盘）
/// 5号位只读取展示，不重算Position
/// </summary>
public sealed class PiPositionDto
{
    public long Id { get; init; }
    public int ScheduleRunId { get; init; }
    public int PlanVersionId { get; init; }
    public string ProductionInstructionNo { get; init; } = string.Empty;
    public int MaterialId { get; init; }
    public string MaterialCode { get; init; } = string.Empty;

    /// <summary>位置类型：FIRST_STAGE_PENDING / STAGE_WAITING / XC / INTERPLANT_TRANSIT / UNLOCATED</summary>
    public string PositionType { get; init; } = string.Empty;

    /// <summary>数量</summary>
    public decimal Quantity { get; init; }

    /// <summary>当前Stage代码</summary>
    public string? CurrentStageCode { get; init; }

    /// <summary>下一Stage代码</summary>
    public string? NextStageCode { get; init; }

    /// <summary>可用时间</summary>
    public DateTime? AvailableTime { get; init; }

    /// <summary>来源类型</summary>
    public string? SourceType { get; init; }

    /// <summary>来源键</summary>
    public string? SourceKey { get; init; }

    /// <summary>问题代码</summary>
    public string? IssueCode { get; init; }

    /// <summary>置信度</summary>
    public string? Confidence { get; init; }

    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// PI Position汇总DTO
/// </summary>
public sealed class PiPositionSummaryDto
{
    /// <summary>PlanVersionId</summary>
    public int PlanVersionId { get; init; }

    /// <summary>PI数量</summary>
    public int PiCount { get; init; }

    /// <summary>总Position数</summary>
    public int TotalPositions { get; init; }

    /// <summary>按PositionType分组统计</summary>
    public List<PositionTypeCountDto> PositionTypeCounts { get; init; } = new();

    /// <summary>Position列表</summary>
    public List<PiPositionDto> Items { get; init; } = new();
}

/// <summary>
/// PositionType统计
/// </summary>
public sealed class PositionTypeCountDto
{
    public string PositionType { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal TotalQuantity { get; init; }
}
