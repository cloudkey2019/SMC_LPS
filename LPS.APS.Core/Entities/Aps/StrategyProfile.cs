namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 策略包主表
/// 对应 APS_Production.StrategyProfile（冻结 DDL v5.1.2 §3.10.5）
/// RunType 取值见 <see cref="StrategyProfileRunType"/>（FULL_SCHEDULE/MANUAL_RESCHEDULE/LOCAL_RESCHEDULE/SIMULATION/INSERT_ORDER_WHATIF），允许 NULL
/// </summary>
public class StrategyProfile
{
    public long Id { get; set; }
    public string StrategyProfileCode { get; set; } = string.Empty;
    public string StrategyProfileName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? RunType { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
