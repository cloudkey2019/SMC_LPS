namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// 在途库存同步结果
/// </summary>
public class InTransitInventorySyncResultDto
{
    /// <summary>
    /// 批次号
    /// </summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>
    /// 影响行数（MERGE 到 InTransitInventoryFact 的行数）
    /// </summary>
    public int RowsAffected { get; set; }

    /// <summary>
    /// 错误信息（成功时为 null）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
