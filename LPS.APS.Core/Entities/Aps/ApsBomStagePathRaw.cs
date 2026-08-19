using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 统一阶段路径本地缓存（v5.0.7新增，v5.0.8升级支持ROOT）
/// 对应 APS_Production.APS_BOM_STAGE_PATH_RAW
/// 数据来源：从ODS库 MES_APS_BOM_Workset_StageDetail 拉取（2号位搬运，与 APS_BOM_RAW 同批次）
/// 
/// 消费方：1号位（必须按 StageScopeType 区分查询）
/// - 读取 StageSeq + StageCode → 对每个阶段串接 RoutingOperation 小工序排 Task
/// - 对无小工序的外协阶段查 StageLeadTimeParam 生成标准 Task
/// 
/// StageScopeType 区分两类记录：
/// - EDGE：子件供给路径（某条BOM边对应的子件在供给父件之前的完整大工艺顺序）
/// - ROOT：根产品完工路径（最上层产品自身完工所需的完整大工艺顺序）
/// </summary>
[Table("APS_BOM_STAGE_PATH_RAW")]
public class ApsBomStagePathRaw
{
    public long Id { get; set; }

    /// <summary>
    /// 关联ODS批次
    /// </summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>
    /// MES系统的BOM编号
    /// </summary>
    public string BOMNO { get; set; } = string.Empty;

    /// <summary>
    /// 阶段路径类型：EDGE=子件供给路径 / ROOT=根产品完工路径
    /// </summary>
    public string StageScopeType { get; set; } = "EDGE";

    /// <summary>
    /// 父物料编码（EDGE=父件编码；ROOT=NULL）
    /// </summary>
    public string? ParentMaterialCode { get; set; }

    /// <summary>
    /// 子/根物料编码（EDGE=子件编码；ROOT=根产品自身编码）
    /// </summary>
    public string ChildMaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// 阶段顺序号（10/20/30，间隔10）
    /// </summary>
    public int StageSeq { get; set; }

    /// <summary>
    /// 大工艺阶段码（如TJ_MACH/TJ_OUTS/BJ_PAINT）
    /// </summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>
    /// 是否供给阈值点（仅EDGE有效；ROOT恒为0）
    /// </summary>
    public bool IsSupplyThreshold { get; set; }

    /// <summary>
    /// 2号位拉取时间
    /// </summary>
    public DateTime SyncedAt { get; set; }
}
