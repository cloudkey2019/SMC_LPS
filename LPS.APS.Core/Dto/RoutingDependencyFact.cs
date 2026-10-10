namespace LPS.APS.Core.Dto;

/// <summary>
/// Routing 依赖边事实（2号位提供给5号位的 Routing DAG 有向边）。
/// 来源：RoutingDependency 表（3号位 ETL 同步）。
/// </summary>
public sealed class RoutingDependencyFact
{
    /// <summary>前驱工序编码（FromOperationCode）</summary>
    public string FromOperationCode { get; init; } = string.Empty;

    /// <summary>后继工序编码（ToOperationCode）</summary>
    public string ToOperationCode { get; init; } = string.Empty;

    /// <summary>依赖类型（忠实传 DependencyType，缺省 ES = 结束-开始）</summary>
    public string DependencyType { get; init; } = string.Empty;

    /// <summary>工艺路径编码（忠实传 RoutingDependency.RouteCode 真实值）</summary>
    public string RouteCode { get; init; } = string.Empty;

    /// <summary>路径序号（忠实传 RoutingDependency.PathId 真实值）</summary>
    public int PathId { get; init; }
}