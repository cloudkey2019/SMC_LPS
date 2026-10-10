namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 人工能力槽日历窗口实体（5号位维护，对应 dbo.ManualCapacitySlotCalendar）
///
/// 语义：某人工能力槽(ManualSlotId)在什么时间可被 APS 排程占用（无日历 = 不可用）
/// 窗口物理删（DELETE）；数据建模/持久化 Owner：2号位；5号位负责业务维护。
/// </summary>
public class ManualCapacitySlotCalendar
{
    /// <summary>窗口 Id</summary>
    public long Id { get; set; }

    /// <summary>人工槽 ManualCapacitySlot.ManualSlotId</summary>
    public int ManualSlotId { get; set; }

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