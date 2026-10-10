namespace LPS.APS.Application.Services.Dto;

/// <summary>
/// PlanVersion 查询映射（跨方法传递，故保持独立文件 + internal 可见性）
/// </summary>
internal class PlanVersionInfoDto
{
    public int Id { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public string DomainKey { get; set; } = string.Empty;
    public DateTime PlanHorizonStart { get; set; }
    public DateTime PlanHorizonEnd { get; set; }
    public int SourceScheduleRunId { get; set; }

    /// <summary>
    /// 执行进度状态（Created / Computing / Computed / ComputeFailed）。
    /// 【2026-09-29 新增】仅供 <c>SchedulingOrchestrator.ExecuteRunAsync</c> 的重入守卫读终态用，
    /// 不参与其它查询的投影 —— 未 SELECT 该列的旧调用点保持 default(null)，不受影响。
    /// </summary>
    public string? Status { get; set; }
}
