namespace LPS.APS.Engine.Services.Sync.Dto;

/// <summary>
/// sp_ScanDomainDependency 存储过程执行结果
/// 对应 Domain_Dependency 表的全量 TRUNCATE + INSERT 统计
/// </summary>
public class DomainDependencyScanResultDto
{
    /// <summary>批次号（DOMAIN_yyyyMMdd_HHmmss）</summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>
    /// 扫描到的跨域依赖边数量
    /// 正常范围 2000~5000（跨族边稀疏）
    /// </summary>
    public int ScannedEdges { get; set; }

    /// <summary>错误信息（null 表示成功）</summary>
    public string? ErrorMessage { get; set; }

    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}
