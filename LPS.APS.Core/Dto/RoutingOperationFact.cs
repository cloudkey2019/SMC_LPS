namespace LPS.APS.Core.Dto;

/// <summary>
/// Routing 工序节点事实（2号位提供给5号位的 Routing DAG 节点）。
/// 来源：RoutingOperation 表（3号位 ETL 同步）。
/// V1 语义：同一 input 内节点归属同一 (MaterialId, ProductionDepartmentId)，
///         故不在此 DTO 重复 MaterialId / ProductionDepartmentId（已在 input 上下文）。
/// </summary>
public sealed class RoutingOperationFact
{
    /// <summary>工序编码（路径内唯一标识，如 J101/J102）</summary>
    public string OperationCode { get; init; } = string.Empty;

    /// <summary>工序名称（MES 工序名称，V1 匹配 OperationProgressFact.OperationName 的主字段）</summary>
    public string OperationName { get; init; } = string.Empty;

    /// <summary>所属大工艺阶段码</summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>工艺路径编码（忠实传 RoutingOperation.RouteCode 真实值，勿硬编码 DEFAULT）</summary>
    public string RouteCode { get; init; } = string.Empty;

    /// <summary>路径序号（忠实传 RoutingOperation.PathId 真实值）</summary>
    public int PathId { get; init; }
}