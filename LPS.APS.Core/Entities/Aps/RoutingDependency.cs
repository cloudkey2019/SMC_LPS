namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 工序依赖边表（v5.0新增，工艺有向图的边）
/// 对应 APS_Production.RoutingDependency
/// 数据来源：ext_MES_APS_Routing_Dependency_View（ODS 契约视图）
/// 
/// 并行表达：若工序B和C都依赖工序A（A→B, A→C），则B和C可并行执行
/// 汇合表达：若工序D依赖B和C（B→D, C→D），则D必须等B和C都完成
/// </summary>
public class RoutingDependency
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
    /// 前驱工序编码
    /// </summary>
    public string FromOperationCode { get; set; } = string.Empty;

    /// <summary>
    /// 后继工序编码
    /// </summary>
    public string ToOperationCode { get; set; } = string.Empty;

    /// <summary>
    /// 依赖类型：ES=结束-开始（默认）, SS=开始-开始, FF=结束-结束
    /// V1先只实现ES
    /// </summary>
    public string DependencyType { get; set; } = "ES";

    /// <summary>
    /// 延迟时间（分钟，0=紧跟前驱完成）
    /// </summary>
    public decimal LagTime { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
