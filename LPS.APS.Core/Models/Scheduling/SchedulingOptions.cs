namespace LPS.APS.Core.Models.Scheduling;

/// <summary>
/// 排程执行选项（运行时参数）
/// </summary>
public class SchedulingOptions
{
    /// <summary>
    /// 最大迭代次数（防止死循环）
    /// </summary>
    public int MaxIterations { get; set; } = 1_000_000;

    /// <summary>
    /// 单域排程超时时间（默认30分钟，超时则中断）
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 是否启用换型优化
    /// </summary>
    public bool EnableSetupOptimization { get; set; } = true;

    /// <summary>
    /// 是否降级模式（粗排：关闭换型优化、使用更大时间粒度）
    /// </summary>
    public bool DegradedMode { get; set; }

    /// <summary>
    /// 时间粒度（分钟）：正常=1，降级粗排=60
    /// </summary>
    public int TimeGranularityMinutes { get; set; } = 1;
}
