namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// MES 生产进度快照同步结果
/// </summary>
public class MESSnapshotSyncResultDto
{
    /// <summary>当前 ScheduleRunId</summary>
    public int ScheduleRunId { get; set; }

    /// <summary>数据截止时间（三个快照 SP 共享同一值）</summary>
    public DateTime DataCutoffTime { get; set; }

    /// <summary>本次同步写入行数</summary>
    public int RowsAffected { get; set; }

    /// <summary>错误信息（null 表示成功）</summary>
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
