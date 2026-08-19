namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 物料阶段部门上下文表（v5.0.16新增）
/// 对应 APS_Production.MaterialStageDeptContext
///
/// 业务定位：2号位 sp_RebuildMaterialStageDeptContext 的正式产出；1号位排程唯一消费入口
/// 消费键：(MaterialId, StageCode) → DefaultProductionDepartmentId
/// 含义：某物料在某大工艺阶段下，当前默认由哪个生产部门生产
///
/// 数据来源：
/// - AUTO   = MSC 自动归一化（多数）
/// - MANUAL = MaterialStageDeptOverride 人工覆盖
/// - MIXED  = 自动草稿 + 人工补丁混合
///
/// 当前有效约束：同一时点同 (MaterialId, StageCode) 只能有 1 条 IsCurrent=1
///
/// 重建触发：
/// - 每日定时全量重建
/// - MSC 同步后增量重建（ETL 链路触发）
/// - 人工 Override 提交后局部重建
///
/// 1号位接口契约（v5.0.16 红线）：
/// 排程从 StageDetail 拿 (MaterialId, StageCode) → 查本表得 DefaultProductionDepartmentId
/// → 按 (MaterialId, ProductionDepartmentId, StageCode) 锁定 Routing 三件套
/// </summary>
public class MaterialStageDeptContext
{
    public long Id { get; set; }
    public int MaterialId { get; set; }

    /// <summary>
    /// 必须存在于 StageDict
    /// </summary>
    public string StageCode { get; set; } = string.Empty;

    public int DefaultProductionDepartmentId { get; set; }

    /// <summary>
    /// 来源类型：AUTO / MANUAL / MIXED
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 组装来源说明（如"MSC 唯一推导" / "Override#123 覆盖" / "MSC+Override 混合"）
    /// </summary>
    public string? SourceDetail { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    /// <summary>
    /// 是否当前有效版本（SCD Type 2）
    /// </summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>
    /// 最近一次重建的批次号
    /// </summary>
    public string? LastRebuildBatchNo { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
