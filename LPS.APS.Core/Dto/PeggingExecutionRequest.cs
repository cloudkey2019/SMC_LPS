using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// Pegging 执行请求 DTO
/// 2号位（编排层）传递给 5号位（业务规则层）的输入参数
/// 对应文档：步骤2.1 的输入
/// </summary>
public class PeggingExecutionRequest
{
    /// <summary>
    /// 计划版本ID
    /// </summary>
    public int PlanVersionId { get; set; }

    /// <summary>
    /// 订单ID列表（批量 Pegging）
    /// </summary>
    public List<long> OrderIds { get; set; } = new();

    /// <summary>
    /// 快照时间戳（库存快照的基准时间）
    /// </summary>
    public DateTime SnapshotAt { get; set; }

    /// <summary>
    /// 冻结区窗口起始时间
    /// </summary>
    public DateTime FrozenWindowStart { get; set; }

    /// <summary>
    /// 冻结区窗口结束时间（通常为 FrozenWindowStart + 2小时）
    /// </summary>
    public DateTime FrozenWindowEnd { get; set; }

    /// <summary>
    /// 是否允许跨工厂 Pegging
    /// </summary>
    public bool AllowCrossFactory { get; set; }

    /// <summary>
    /// 跨工厂模式
    /// </summary>
    public CrossFactoryMode? CrossFactoryMode { get; set; }

    /// <summary>
    /// 默认 Pegging 策略
    /// </summary>
    public PeggingStrategyType DefaultStrategy { get; set; } = PeggingStrategyType.FIFO;

    /// <summary>
    /// 产品族ID列表（用于虚拟库存传递）
    /// </summary>
    public List<int> ProductFamilyIds { get; set; } = new();

    /// <summary>
    /// 拓扑排序结果（01:50 静态扫描的输出）
    /// </summary>
    public Dictionary<int, int> TopologicalOrder { get; set; } = new();

    /// <summary>
    /// 虚拟库存余额（上游域的产出）
    /// </summary>
    public List<VirtualInventoryItem> VirtualInventory { get; set; } = new();

    /// <summary>
    /// 是否强制重新 Pegging（忽略冻结区）
    /// </summary>
    public bool ForceRePegging { get; set; }

    /// <summary>
    /// 最大 BOM 遍历深度（防止循环依赖）
    /// </summary>
    public int MaxBomDepth { get; set; } = 10;

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 执行模式：FULL_RUN | DRY_RUN | INCREMENTAL
    /// </summary>
    public string ExecutionMode { get; set; } = "FULL_RUN";

    /// <summary>
    /// 排程沙盘上下文（包含Resources、Calendar等1号位所需数据）
    /// V1.2：用于传递给1号位IFiniteCapacityScheduler的完整上下文
    /// </summary>
    public Models.Scheduling.SchedulingContext? SchedulingContext { get; set; }
}

/// <summary>
/// 虚拟库存项（跨域传递）
/// </summary>
public class VirtualInventoryItem
{
    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; set; }

    /// <summary>
    /// 工厂代码
    /// </summary>
    public string FactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 上游产品族ID
    /// </summary>
    public int SourceProductFamilyId { get; set; }

    /// <summary>
    /// 虚拟可用量
    /// </summary>
    public decimal VirtualAvailableQuantity { get; set; }

    /// <summary>
    /// 供应可用时间
    /// </summary>
    public DateTime AvailableAt { get; set; }

    /// <summary>
    /// 上游 Task ID
    /// </summary>
    public long? UpstreamTaskId { get; set; }

    /// <summary>
    /// 拓扑序号
    /// </summary>
    public int TopologicalOrder { get; set; }
}
