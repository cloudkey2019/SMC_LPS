namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 虚拟库存余额（步骤2.4）
/// 用于跨域调度时的虚拟供应量传递
/// 对应文档：拓扑排序 + 单向硬约束传播
/// </summary>
public class VirtualInventoryBalance
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
    /// 物料ID
    /// </summary>
    public int MaterialId { get; set; }

    /// <summary>
    /// 工厂代码
    /// </summary>
    public string FactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 产品族ID（上游域）
    /// </summary>
    public int SourceProductFamilyId { get; set; }

    /// <summary>
    /// 产品族ID（下游域）
    /// </summary>
    public int TargetProductFamilyId { get; set; }

    /// <summary>
    /// 虚拟可用量（上游域产出的供应量）
    /// </summary>
    public decimal VirtualAvailableQuantity { get; set; }

    /// <summary>
    /// 已分配量（下游域消耗的虚拟库存）
    /// </summary>
    public decimal AllocatedQuantity { get; set; }

    /// <summary>
    /// 剩余可用量（VirtualAvailableQuantity - AllocatedQuantity）
    /// </summary>
    public decimal RemainingQuantity { get; set; }

    /// <summary>
    /// 单位
    /// </summary>
    public string UOM { get; set; } = string.Empty;

    /// <summary>
    /// 供应可用时间（上游域完成时间）
    /// </summary>
    public DateTime AvailableAt { get; set; }

    /// <summary>
    /// 上游 Task ID（产生虚拟库存的任务）
    /// </summary>
    public long? UpstreamTaskId { get; set; }

    /// <summary>
    /// BOM 层级
    /// </summary>
    public int BomLevel { get; set; }

    /// <summary>
    /// 拓扑排序序号（01:50 静态扫描结果）
    /// 数字越小越先执行
    /// </summary>
    public int TopologicalOrder { get; set; }

    /// <summary>
    /// 是否已传播（单向传播标记，避免回写）
    /// </summary>
    public bool IsPropagated { get; set; }

    /// <summary>
    /// 依赖关系类型：CROSS_DOMAIN | CROSS_FACTORY | SAME_DOMAIN
    /// </summary>
    public string DependencyType { get; set; } = string.Empty;

    /// <summary>
    /// 上游工厂代码（跨工厂场景）
    /// </summary>
    public string? UpstreamFactoryCode { get; set; }

    /// <summary>
    /// 下游工厂代码（跨工厂场景）
    /// </summary>
    public string? DownstreamFactoryCode { get; set; }

    /// <summary>
    /// 跨工厂模式：STAGE_HANDOFF | INTER_FACTORY_ORDER | null（同工厂）
    /// </summary>
    public string? CrossFactoryMode { get; set; }

    /// <summary>
    /// 计算时间戳（虚拟库存生成时间）
    /// </summary>
    public DateTime ComputedAt { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 导航属性：关联的计划版本
    /// </summary>
    public PlanVersion? PlanVersion { get; set; }

    /// <summary>
    /// 导航属性：关联的上游任务
    /// </summary>
    public Task? UpstreamTask { get; set; }
}
