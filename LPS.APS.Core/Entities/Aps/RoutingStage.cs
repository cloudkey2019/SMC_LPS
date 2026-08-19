using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 大工艺阶段字典表（v5.0.6新增，v5.0.7定位调整）
/// 对应 APS_Production.RoutingStage
/// 数据来源：ext_MES_APS_Routing_Stage_View（ODS 契约视图）
/// 
/// 定位：阶段字典/标准阶段语言（3号位契约→2号位装载），不作为排程权威阶段顺序源
/// 排程权威阶段顺序来自 MES_APS_BOM_Workset_StageDetail（5号位派生结果）
/// 
/// 已知限制：MES工艺侧不包含外协阶段，数据可能不完整
/// 职责分离：RoutingStage=阶段字典，StageDetail=BOM派生结果，不混写
/// </summary>
[Table("RoutingStage")]
public class RoutingStage
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
    /// 大工艺阶段码（如MACH/OUTS/PAINT）
    /// 与 ProcessType 是 N:1 关系，二者不能混用
    /// </summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>
    /// 阶段名称（中文名，如机加/外协/涂装）
    /// </summary>
    public string StageName { get; set; } = string.Empty;

    /// <summary>
    /// 是否外协阶段
    /// </summary>
    public bool IsOutsource { get; set; }

    /// <summary>
    /// 是否半成品库存断点（1=断点，可入库暂存）
    /// </summary>
    public bool IsStockPoint { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
