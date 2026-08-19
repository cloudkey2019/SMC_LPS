namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// BOM展开请求结果（IBOMRequestService.PushBOMRequestToODSAsync 返回）
/// </summary>
public class BOMRequestResult
{
    /// <summary>批次号（格式：REQ_yyyyMMdd_xxxxxxxx）</summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>活跃BOMNO数量（去重后）</summary>
    public int RootCount { get; set; }

    /// <summary>活跃订单总数</summary>
    public int TotalOrderCount { get; set; }
}
