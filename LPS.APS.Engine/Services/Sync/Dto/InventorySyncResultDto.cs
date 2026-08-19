namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_SyncInventorySnapshot 执行结果
/// </summary>
public class InventorySyncResultDto
{
    /// <summary>批次号（INVENTORY_yyyyMMdd_HHmmss）</summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>L4 InventoryBalance 写入行数</summary>
    public int BalanceRows { get; set; }

    /// <summary>错误信息（null 表示成功）</summary>
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
