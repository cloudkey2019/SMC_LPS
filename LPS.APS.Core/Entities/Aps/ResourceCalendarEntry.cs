namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 设备资源日历窗口实体（5号位维护，对应 dbo.ResourceCalendarSlot）
///
/// 语义：某设备在什么时间可被 APS 排程占用（无日历 = 不可用）
/// 数据建模/持久化 Owner：2号位（建表）；5号位负责业务维护（增删改查）
/// 注意：类名避开 2号位 Solver 输入 DTO《DomainSolveRequest.ResourceCalendarSlot》。
/// </summary>
public class ResourceCalendarEntry
{
    /// <summary>窗口 Id</summary>
    public long Id { get; set; }

    /// <summary>设备 Resource.Id（ResGo 全部设备）</summary>
    public int ResourceId { get; set; }

    /// <summary>窗口开始（含）</summary>
    public DateTime StartTime { get; set; }

    /// <summary>窗口结束（不含）</summary>
    public DateTime EndTime { get; set; }

    /// <summary>1=可用 / 0=禁用停机</summary>
    public bool AvailableFlag { get; set; }

    /// <summary>备注</summary>
    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}