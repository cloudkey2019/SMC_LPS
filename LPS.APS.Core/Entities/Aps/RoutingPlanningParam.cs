namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 排程规划参数表（v5.0新增，从原 Routing 表拆出）
/// 对应 APS_Production.RoutingPlanningParam
/// 
/// MinBatchSize/MaxBatchSize 当前无 ODS 来源，不应污染工艺事实层
/// 未来如 MES 提供，可通过 ODS 视图接回（SourceSystem='MES'）
/// </summary>
public class RoutingPlanningParam
{
    public long Id { get; set; }
    public int MaterialId { get; set; }

    /// <summary>
    /// 工艺路径编码（V1固定'DEFAULT'，V2扩展多路径）
    /// </summary>
    public string RouteCode { get; set; } = "DEFAULT";

    /// <summary>
    /// 路径序号（V1固定1，V2扩展多路径）
    /// </summary>
    public int PathId { get; set; } = 1;

    /// <summary>
    /// 工序编码
    /// </summary>
    public string OperationCode { get; set; } = string.Empty;

    public decimal MinBatchSize { get; set; } = 1;
    public decimal MaxBatchSize { get; set; } = 999999;

    /// <summary>
    /// 转移批量（工序间流转单位）
    /// </summary>
    public decimal? TransferBatchSize { get; set; }

    /// <summary>
    /// 数据来源：MES / APS_LOCAL
    /// </summary>
    public string SourceSystem { get; set; } = "APS_LOCAL";

    public string? MaintainedBy { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
