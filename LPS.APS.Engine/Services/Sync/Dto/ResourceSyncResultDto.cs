namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_SyncResourceData 存储过程执行结果
/// </summary>
public class ResourceSyncResultDto
{
    public string BatchNo { get; set; } = string.Empty;
    public int RowsAffected { get; set; }
    public int Skipped { get; set; }
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
