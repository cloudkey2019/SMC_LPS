namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 工序节点表（v5.0新增，替代原线性 Routing 表）
/// 对应 APS_Production.RoutingOperation
/// 数据来源：ext_MES_APS_Routing_Operation_View（ODS 契约视图）
/// 与 RoutingDependency 配合构成工艺有向图，支持并行/串行混合工艺
/// </summary>
public class RoutingOperation
{
    public long Id { get; set; }
    public int MaterialId { get; set; }
    public int ProductionDepartmentId { get; set; }

    /// <summary>
    /// 工艺路径编码（V1固定'DEFAULT'，V2扩展多路径）
    /// </summary>
    public string RouteCode { get; set; } = "DEFAULT";

    /// <summary>
    /// 路径序号（V1固定1，V2扩展多路径）
    /// </summary>
    public int PathId { get; set; } = 1;

    /// <summary>
    /// 工序编码（路径内唯一）
    /// </summary>
    public string OperationCode { get; set; } = string.Empty;

    public string OperationName { get; set; } = string.Empty;

    /// <summary>
    /// 工序类型：MACHINING / ASSEMBLY / INSPECTION
    /// </summary>
    public string ProcessType { get; set; } = string.Empty;

    /// <summary>
    /// 所属大工艺阶段码（v5.0.6新增）
    /// 业务管理级，与 ProcessType 为 N:1 关系
    /// 来源：MES_APS_Routing_Stage_View
    /// </summary>
    public string? StageCode { get; set; }

    /// <summary>
    /// 标准工时（分钟）
    /// </summary>
    public decimal StandardDuration { get; set; }

    /// <summary>
    /// 准备时间（分钟）
    /// </summary>
    public decimal SetupTime { get; set; }

    /// <summary>
    /// 转移批量（工序间流转单位，用于阈值启动）
    /// 来源：RoutingPlanningParam.TransferBatchSize，由2号位加载
    /// </summary>
    public decimal? TransferBatchSize { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
