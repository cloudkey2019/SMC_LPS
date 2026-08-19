namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_SyncPipelineSupply 执行结果
/// </summary>
public class PipelineSupplySyncResultDto
{
    /// <summary>批次号（PIPELINE_yyyyMMdd_HHmmss）</summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>SupplyFact_Pipeline 写入行数（V1 恒为 0）</summary>
    public int RowsAffected { get; set; }

    /// <summary>错误信息（null 表示成功）</summary>
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
