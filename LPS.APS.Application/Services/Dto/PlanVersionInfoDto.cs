namespace LPS.APS.Application.Services.Dto;

/// <summary>
/// PlanVersion 查询映射（跨方法传递，故保持独立文件 + internal 可见性）
/// </summary>
internal class PlanVersionInfoDto
{
    public int Id { get; set; }
    public string VersionCode { get; set; } = string.Empty;
    public DateTime PlanHorizonStart { get; set; }
    public DateTime PlanHorizonEnd { get; set; }
    public int SourceScheduleRunId { get; set; }
}
