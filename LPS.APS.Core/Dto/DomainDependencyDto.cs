namespace LPS.APS.Core.Dto;

/// <summary>
/// 排程域依赖DTO（G7：5号位提供给4号位）
///
/// 展示Domain之间的上下游依赖关系
/// 数据来源：Domain_Dependency表（2号位sp_ScanDomainDependency扫描结果）
/// </summary>
public sealed class DomainDependencyDto
{
    /// <summary>
    /// 上游DomainKey（被依赖方）
    /// </summary>
    public string UpstreamDomainCode { get; init; } = string.Empty;

    /// <summary>
    /// 下游DomainKey（依赖方）
    /// </summary>
    public string DownstreamDomainCode { get; init; } = string.Empty;

    /// <summary>
    /// 关联的半成品物料编码
    /// </summary>
    public string ChildMaterialCode { get; init; } = string.Empty;

    /// <summary>
    /// 默认提前期天数（兼容缓存，0=尚未按真实LT派生）
    /// </summary>
    public int DefaultLeadTimeDays { get; init; }

    /// <summary>
    /// 扫描时间戳
    /// </summary>
    public DateTime ScannedAt { get; init; }
}
