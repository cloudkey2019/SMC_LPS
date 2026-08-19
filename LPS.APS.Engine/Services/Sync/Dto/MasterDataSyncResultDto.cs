namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_SyncMasterData 存储过程执行结果
/// </summary>
public class MasterDataSyncResultDto
{
    /// <summary>数据源（ERP/MES）</summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>批次号</summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>影响行数（Material+Mapping+SupplyContext 合计）</summary>
    public int RowsAffected { get; set; }

    /// <summary>错误信息（null表示成功）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>是否成功</summary>
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
