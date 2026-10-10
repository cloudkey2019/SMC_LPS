namespace LPS.APS.Application.Services.Dto;

/// <summary>
/// 设备日历批量铺窗请求（一次生成连续多天窗口）
/// 传 Days=1 即等价"逐条加一天"，覆盖逐条场景。
/// </summary>
public sealed class ResourceCalendarBulkRequest
{
    /// <summary>设备 Resource.Id</summary>
    public int ResourceId { get; set; }

    /// <summary>起始日期（含）</summary>
    public DateTime StartDate { get; set; }

    /// <summary>连续天数（含起始日）</summary>
    public int Days { get; set; } = 1;

    /// <summary>每天开始时间（如 08:00）</summary>
    public TimeSpan StartTime { get; set; } = new(8, 0, 0);

    /// <summary>每天结束时间（如 17:00）</summary>
    public TimeSpan EndTime { get; set; } = new(17, 0, 0);

    /// <summary>1=可用 / 0=禁用</summary>
    public bool AvailableFlag { get; set; } = true;

    /// <summary>备注</summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 设备日历窗口查询结果
/// </summary>
public sealed class ResourceCalendarEntryDto
{
    public long Id { get; set; }
    public int ResourceId { get; set; }
    public string? ResourceCode { get; set; }
    public string? ResourceName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool AvailableFlag { get; set; }
    public string? Remark { get; set; }
}

/// <summary>
/// 人工能力槽主档 DTO（软删 IsActive）
/// </summary>
public sealed class ManualCapacitySlotDto
{
    public int ManualSlotId { get; set; }
    public int ProductionDepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public string SlotCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>是否已配置有效窗口（真源 = ManualCapacitySlotCalendar 存在任一记录；空日历=不可用，呼应 v1.3 §九）</summary>
    public bool HasCalendar { get; set; }
}

/// <summary>
/// 人工能力槽日历窗口 DTO
/// </summary>
public sealed class ManualSlotCalendarDto
{
    public long Id { get; set; }
    public int ManualSlotId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool AvailableFlag { get; set; }
    public string? Remark { get; set; }
}

/// <summary>
/// 人工槽日历批量铺窗请求（同设备日历语义）
/// </summary>
public sealed class ManualSlotCalendarBulkRequest
{
    /// <summary>人工槽 ManualSlotId</summary>
    public int ManualSlotId { get; set; }

    /// <summary>起始日期（含）</summary>
    public DateTime StartDate { get; set; }

    /// <summary>连续天数</summary>
    public int Days { get; set; } = 1;

    /// <summary>每天开始时间</summary>
    public TimeSpan StartTime { get; set; } = new(8, 0, 0);

    /// <summary>每天结束时间</summary>
    public TimeSpan EndTime { get; set; } = new(17, 0, 0);

    /// <summary>1=可用 / 0=禁用</summary>
    public bool AvailableFlag { get; set; } = true;

    /// <summary>备注</summary>
    public string? Remark { get; set; }
}

/// <summary>
/// 资源主表列表项 DTO（G1：设备下拉数据源；含日历状态）
/// 数据源：dbo.Resource（资源主表）
/// </summary>
public sealed class ResourceListItemDto
{
    public int ResourceId { get; set; }
    public string ResourceCode { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;   // MACHINE / LINE / MANUAL_STATION
    public int ProductionDepartmentId { get; set; }
    public string? ProductionDepartmentName { get; set; }
    public bool IsActive { get; set; }

    /// <summary>是否已配置日历窗口（真源 = ResourceCalendarSlot 存在任一记录；空日历=不可用）</summary>
    public bool HasCalendar { get; set; }
}

/// <summary>
/// 生产部门列表项 DTO（G2：部门下拉数据源）
/// 数据源：dbo.ProductionDepartment
/// </summary>
public sealed class ProductionDepartmentDto
{
    public int Id { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}