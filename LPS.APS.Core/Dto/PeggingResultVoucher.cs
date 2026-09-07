using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// Pegging 执行结果（2号位 PeggingOrchestrator 的内存输出，不落库）
/// </summary>
[Obsolete("Use PeggingResult instead.")]
public class PeggingResultVoucher : PeggingResult { }

public class PeggingResult
{
    /// <summary>
    /// 凭证ID（用于追踪和关联）
    /// </summary>
    public string VoucherId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 计划版本ID
    /// </summary>
    public int PlanVersionId { get; set; }

    /// <summary>
    /// 真实 DomainKey（透传自 PeggingExecutionRequest.DomainKey，供 LogicalProductionDemand 等结果项携带）。
    /// </summary>
    public string DomainKey { get; set; } = string.Empty;

    /// <summary>
    /// 需求侧订单ID
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// 需求侧物料ID
    /// </summary>
    public int DemandMaterialId { get; set; }

    /// <summary>
    /// 需求数量
    /// </summary>
    public decimal DemandQuantity { get; set; }

    /// <summary>
    /// 单位
    /// </summary>
    public string UOM { get; set; } = string.Empty;

    /// <summary>
    /// 供应分配明细列表
    /// </summary>
    public List<SupplyAllocationItem> SupplyAllocations { get; set; } = new();

    /// <summary>
    /// AllocationSequence计数器（v5.1.2：在供需扣减成功时自增生成）
    /// </summary>
    public long NextAllocationSequence { get; set; } = 1;

    /// <summary>
    /// 是否完全满足（true = 供应充足，false = 供应短缺）
    /// </summary>
    public bool IsFullyAllocated { get; set; }

    /// <summary>
    /// 短缺数量
    /// </summary>
    public decimal ShortageQuantity { get; set; }

    /// <summary>
    /// BOM 遍历路径（用于调试和审计）
    /// </summary>
    public List<BomTraversalNode> BomPath { get; set; } = new();

    /// <summary>
    /// 内存账本（BOM 遍历的扣减过程，供调试和5号位内部使用）
    /// </summary>
    public List<PeggingLedgerEntry> LedgerEntries { get; set; } = new();

    /// <summary>
    /// 逻辑生产需求列表（V1.2）
    /// Pegging阶段形成，交给1号位Solver决定FinalTask
    /// 不持久化到数据库，仅运行时内存DTO
    /// </summary>
    public List<LogicalProductionDemand> LogicalProductionDemands { get; set; } = new();

    /// <summary>
    /// 生成的 TaskDraft 列表（待1号位排程，SourceType = NEW_REQUIREMENT 的供给对应此列表）
    /// V1.2：已废弃，使用LogicalProductionDemands代替
    /// </summary>
    [Obsolete("V1.2: Use LogicalProductionDemands instead")]
    public List<TaskDraft> TaskDrafts { get; set; } = new();

    /// <summary>
    /// 物理 Pegging 草稿（Task-to-Task 血缘，统一持久化阶段写库）
    /// </summary>
    public List<PhysicalPeggingDraft> PhysicalPeggingDrafts { get; set; } = new();

    /// <summary>
    /// Pegging 执行时间戳
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 执行耗时（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 警告信息列表
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// 供应分配明细项（2号位执行扣减后的实际分配结果）
///
/// SourceType 使用定稿口径：
///   INVENTORY / WIP / PIPELINE / INTER_FACTORY_ORDER /
///   PRODUCTION_INSTRUCTION / PURCHASE_ORDER / NEW_REQUIREMENT
///
/// NEW_REQUIREMENT 类型不写入 PeggingSupplyAllocation，
/// 而是通过 TaskDrafts → 1号位排程实例化 Task → 写物理 Pegging 表（Task-to-Task）
/// </summary>
public class SupplyAllocationItem
{
    /// <summary>
    /// 分配序列号（v5.1.2：在供需扣减成功时生成，非持久化时生成）
    /// </summary>
    public long AllocationSequence { get; set; }

    /// <summary>
    /// 需求键（用于AllocationLineage关联LogicalProductionDemand）
    /// </summary>
    public string DemandKey { get; set; } = string.Empty;

    public int SupplyMaterialId { get; set; }
    public long? SupplySourceId { get; set; }
    public decimal AllocatedQuantity { get; set; }

    /// <summary>
    /// 供应来源类型（定稿枚举，见 SupplySourceType）
    /// </summary>
    public SupplySourceType SourceType { get; set; }

    /// <summary>
    /// 供应来源引用（批次号 / 在途单号 / 出荷指示号等）
    /// </summary>
    public string? SourceReference { get; set; }

    /// <summary>
    /// 出荷指示号（PRODUCTION_INSTRUCTION 类型专用，经5号位 ValidateZpBpDocumentMatch 校验）
    /// </summary>
    public string? ShippingInstructionNo { get; set; }

    public string FactoryCode { get; set; } = string.Empty;
    public int BomLevel { get; set; }
    public DateTime? AvailableAt { get; set; }

    /// <summary>
    /// 是否在系统滑动冻结窗口内（MES_DISPATCHED，由2号位判断）
    /// </summary>
    public bool IsInFrozenZone { get; set; }
    public int Priority { get; set; }
}

/// <summary>
/// BOM 遍历节点（用于审计）
/// </summary>
public class BomTraversalNode
{
    /// <summary>
    /// 节点ID
    /// </summary>
    public string NodeId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 父节点ID
    /// </summary>
    public string? ParentNodeId { get; set; }

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; set; }

    /// <summary>
    /// 物料编码
    /// </summary>
    public string MaterialCode { get; set; } = string.Empty;

    /// <summary>
    /// BOM 层级
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 需求数量
    /// </summary>
    public decimal RequiredQuantity { get; set; }

    /// <summary>
    /// 已分配数量
    /// </summary>
    public decimal AllocatedQuantity { get; set; }

    /// <summary>
    /// 遍历时间戳
    /// </summary>
    public DateTime TraversedAt { get; set; }
}

/// <summary>
/// Task 草稿（2号位构造的纯内存 DTO，通过 DomainSolveRequest 传给1号位，不落盘）
/// </summary>
public class TaskDraft
{
    public string DraftId { get; set; } = Guid.NewGuid().ToString();
    public int MaterialId { get; set; }
    public string StageCode { get; set; } = string.Empty;
    public string OperationCode { get; set; } = string.Empty;
    public string RouteKey { get; set; } = string.Empty;
    public string? ProductionInstructionNo { get; set; }
    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public string FactoryCode { get; set; } = string.Empty;
    public string? Department { get; set; }
    public int ProductFamilyId { get; set; }
    public DateTime EarliestAvailableTime { get; set; }
    public DateTime DueTime { get; set; }
    public string TaskPlanningMode { get; set; } = "OPERATION_FINITE"; // OPERATION_FINITE | STAGE_LEAD_TIME
    public bool IsVirtual { get; set; }
    public long? ExistingMESPlanReleaseId { get; set; }
    public long? ExecutionLockId { get; set; }
    public List<string> UpstreamDraftIds { get; set; } = new();
    public List<AllocationComponent> Components { get; set; } = new();
    public int Priority { get; set; }
    public bool IsUrgent { get; set; }
}

/// <summary>
/// 分配组件（AllocationSequence → ComponentQty）
/// </summary>
public sealed record AllocationComponent(long AllocationSequence, decimal ComponentQty);

/// <summary>
/// 物理 Pegging 草稿（Task-to-Task 血缘，统一持久化阶段写库）
/// </summary>
public class PhysicalPeggingDraft
{
    public string UpstreamDraftId { get; set; } = string.Empty;
    public string DownstreamDraftId { get; set; } = string.Empty;
    public int UpstreamMaterialId { get; set; }
    public int DownstreamMaterialId { get; set; }
    public decimal Quantity { get; set; }
    public string UOM { get; set; } = string.Empty;
    public int InheritedPriority { get; set; }
}
