namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 冻结区快照（步骤2.3）
/// 记录 MES 已下发任务的冻结状态
/// 对应文档：冻结区 = 当前时间 + 2小时滑动窗口
/// </summary>
public class FrozenZoneSnapshot
{
    /// <summary>
    /// 主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 计划版本ID（分区键）
    /// </summary>
    public int PlanVersionId { get; set; }

    /// <summary>
    /// 关联的 Task ID（MES 已下发的任务）
    /// </summary>
    public long TaskId { get; set; }

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; set; }

    /// <summary>
    /// 工厂代码
    /// </summary>
    public string FactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 产品族ID
    /// </summary>
    public int ProductFamilyId { get; set; }

    /// <summary>
    /// MES 工单号
    /// </summary>
    public string MESWorkOrderNo { get; set; } = string.Empty;

    /// <summary>
    /// 计划开始时间
    /// </summary>
    public DateTime PlannedStartTime { get; set; }

    /// <summary>
    /// 计划完成时间
    /// </summary>
    public DateTime PlannedEndTime { get; set; }

    /// <summary>
    /// 冻结窗口起始时间
    /// </summary>
    public DateTime FrozenWindowStart { get; set; }

    /// <summary>
    /// 冻结窗口结束时间（通常为 FrozenWindowStart + 2小时）
    /// </summary>
    public DateTime FrozenWindowEnd { get; set; }

    /// <summary>
    /// 是否已下发到 MES
    /// </summary>
    public bool IsDispatched { get; set; }

    /// <summary>
    /// MES 下发时间
    /// </summary>
    public DateTime? DispatchedAt { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// 单位
    /// </summary>
    public string UOM { get; set; } = string.Empty;

    /// <summary>
    /// 资源ID（机台/产线）
    /// </summary>
    public int? ResourceId { get; set; }

    /// <summary>
    /// 资源代码
    /// </summary>
    public string? ResourceCode { get; set; }

    /// <summary>
    /// 冻结原因：MES_DISPATCHED | MANUAL_LOCK | CONSTRAINT_FIXED
    /// </summary>
    public string FrozenReason { get; set; } = string.Empty;

    /// <summary>
    /// 跨工厂模式：STAGE_HANDOFF | INTER_FACTORY_ORDER | null（单工厂）
    /// </summary>
    public string? CrossFactoryMode { get; set; }

    /// <summary>
    /// 上游工厂代码（跨工厂场景）
    /// </summary>
    public string? UpstreamFactoryCode { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// 快照生成时间
    /// </summary>
    public DateTime SnapshotAt { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 导航属性：关联的计划版本
    /// </summary>
    public PlanVersion? PlanVersion { get; set; }

    /// <summary>
    /// 导航属性：关联的任务
    /// </summary>
    public Task? Task { get; set; }
}
