namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// BOM展开请求服务接口（2号位职责）
/// 每天00:00执行：从Order_Canonical划定活跃根集合，推送到ODS库的BOM展开请求表
/// 
/// 数据路径：
///   APS库: Order_Canonical → sp_GetActiveRootBOMNOs → 活跃BOMNO结果集
///   ODS库: → MES_API_BOM_Request（批次头）+ MES_API_BOM_Request_Detail（BOMNO明细）
///   后续:  SQL Agent Job 00:05 执行 sp_ExpandBOMBatch 展开
/// </summary>
public interface IBOMRequestService
{
    /// <summary>
    /// 划定活跃根集合并推送BOM展开请求到ODS库
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>推送的BOMNO数量</returns>
    Task<BOMRequestResult> PushBOMRequestToODSAsync(CancellationToken cancellationToken = default);
}

