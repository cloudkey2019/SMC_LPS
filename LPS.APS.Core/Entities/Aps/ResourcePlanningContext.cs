namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 资源排程扩展参数表（v5.0新增，APS 本地排程控制信息，不污染外部资源事实层）
/// 对应 APS_Production.ResourcePlanningContext
/// </summary>
public class ResourcePlanningContext
{
    public int Id { get; set; }
    public int ResourceId { get; set; }

    /// <summary>
    /// 排程日历策略ID
    /// </summary>
    public int? CalendarPolicyId { get; set; }

    /// <summary>
    /// 派工优先级（越小越优先）
    /// </summary>
    public int DispatchPriority { get; set; } = 100;

    /// <summary>
    /// APS 本地禁用标记
    /// </summary>
    public bool LocalDisableFlag { get; set; }

    /// <summary>
    /// APS 侧覆盖产能系数（为null时使用 Resource.CapacityFactor）
    /// </summary>
    public decimal? OverrideCapacityFactor { get; set; }

    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
