using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// BOM原始数据表（从ODS拉取的本地缓存）
/// 对应 APS_Production.APS_BOM_RAW
///
/// 数据来源：2号位从 ODS 库 MES_APS_BOM_Workset 拉取（v5.0.7：含 ChildRequiredStageCode）
/// 消费方：
///   - 2号位：调用 sp_CalculateLLC 基于本表填充 LLC
///   - 5号位：Pegging 时作为 BOM 展开真相源（按 BatchNo 定位当前计划版本的工作集）
///
/// 生命周期：每日 00:30 夜间批次拉取一次，批次号=PlanVersion.BatchNo
/// </summary>
[Table("APS_BOM_RAW")]
public class ApsBomRaw
{
    public long Id { get; set; }

    /// <summary>
    /// 批次号（关联ODS批次 / PlanVersion.BatchNo）
    /// </summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>
    /// MES 系统的 BOM 编号（同一产品可能对应多个 BOMNO，代表工艺变体）
    /// </summary>
    public string BOMNO { get; set; } = string.Empty;

    /// <summary>
    /// 父物料编码（成品或半成品）
    /// </summary>
    public string ParentMaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// 子物料编码（半成品或原材料）
    /// </summary>
    public string ChildMaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// 单位用量（⚠️ 不累乘！每条边只存本层父子间的用量比）
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// BOM 层级（根为 0，向下递增）
    /// </summary>
    public int? Level { get; set; }

    /// <summary>
    /// 低阶码（LLC - Low Level Code）
    /// 由 sp_CalculateLLC 在本表载入后计算填充，0=顶层
    /// </summary>
    public int? LLC { get; set; }

    /// <summary>
    /// 是否为叶子节点（采购件/原材料，无下级 BOM 展开）
    /// </summary>
    public bool IsLeaf { get; set; }

    /// <summary>
    /// 从根到该节点的展开路径（诊断用，非业务字段）
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// 子件供给所需大工艺阶段码（v5.0.7 从 Workset 最终结果透传）
    /// NULL = 保守策略：子件必须完成全部工艺后才能向父件供给
    /// 非NULL = 子件完成指定阶段即可向父件供给（允许流水线并行）
    /// </summary>
    public string? ChildRequiredStageCode { get; set; }

    /// <summary>
    /// 同步时间（2号位拉取时间）
    /// </summary>
    public DateTime SyncedAt { get; set; }
}
