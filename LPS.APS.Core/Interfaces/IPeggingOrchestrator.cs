using LPS.APS.Core.Dto;
using ApsTask = LPS.APS.Core.Entities.APS.Task;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// Pegging 编排服务接口（2号位）
/// 负责接收 5号位返回的 Voucher，执行状态变更和持久化
/// 对应文档：步骤2.6-2.8 的编排层职责
/// </summary>
public interface IPeggingOrchestrator
{
    /// <summary>
    /// 执行完整的 Pegging 流程（步骤2.1-2.9）
    /// 1. 读 APS_BOM_RAW / APS_BOM_STAGE_PATH_RAW / APS_BOM_CROSS_FACTORY_EDGE_RAW
    /// 2. 读供给池（INVENTORY / WIP / PIPELINE / PRODUCTION_INSTRUCTION / PURCHASE_ORDER）
    /// 3. 跨厂边 → 调5号位 EvaluateCrossFactoryModeAsync 裁决
    /// 4. 枚举供给候选 → 调5号位 SelectSupplyCandidatesByRuleAsync 排序
    /// 5. 遇到 PRODUCTION_INSTRUCTION → 调5号位 ValidateZpBpDocumentMatchAsync 红线校验
    /// 6. 维护内存 PeggingLedgerEntry，执行扣减
    /// 7. NEW_REQUIREMENT 触发 TaskDraft 生成，交1号位排程实例化 Task
    /// 8. 调5号位 ValidateBusinessRuleResultAsync 红线校验
    /// 9. 写 PeggingSupplyAllocation（非NEW_REQUIREMENT）+ 写物理 Pegging（Task-to-Task）
    /// </summary>
    Task<PeggingOrchestrationResult> ExecutePeggingWorkflowAsync(
        PeggingExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量执行 Pegging 流程（并发处理多个订单）
    /// </summary>
    Task<IEnumerable<PeggingOrchestrationResult>> ExecuteBatchPeggingWorkflowAsync(
        PeggingExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 步骤2.6：从 Voucher 中提取 TaskDraft 列表（纯内存，按拓扑序排列，不写库）
    /// </summary>
    IReadOnlyList<TaskDraft> BuildTaskDraftsFromVoucher(PeggingResultVoucher voucher);

    /// <summary>
    /// 步骤2.8：持久化供应分配表（非 Task 供应）
    /// </summary>
    Task<int> PersistSupplyAllocationAsync(
        PeggingResultVoucher voucher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新冻结区快照
    /// </summary>
    Task<int> UpdateFrozenZoneSnapshotAsync(
        int planVersionId,
        DateTime frozenWindowStart,
        DateTime frozenWindowEnd,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 虚拟库存传播（跨域依赖）
    /// </summary>
    Task<int> PropagateVirtualInventoryAsync(
        int planVersionId,
        int sourceProductFamilyId,
        int targetProductFamilyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚 Pegging 流程（事务失败时）
    /// </summary>
    Task RollbackPeggingWorkflowAsync(
        int planVersionId,
        long orderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证 Pegging 结果的一致性（事务提交前）
    /// </summary>
    Task<(bool IsValid, List<string> ValidationErrors)> ValidateWorkflowConsistencyAsync(
        PeggingOrchestrationResult result,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Pegging 编排结果 DTO
/// </summary>
public class PeggingOrchestrationResult
{
    /// <summary>
    /// 计划版本ID
    /// </summary>
    public int PlanVersionId { get; set; }

    /// <summary>
    /// 订单ID
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// 原始 Voucher（5号位返回）
    /// </summary>
    public PeggingResultVoucher Voucher { get; set; } = null!;

    /// <summary>
    /// 生成的 Task 列表
    /// </summary>
    public List<ApsTask> GeneratedTasks { get; set; } = new();

    /// <summary>
    /// 持久化的 PeggingSupplyAllocation 记录数
    /// </summary>
    public int SupplyAllocationCount { get; set; }

    /// <summary>
    /// 持久化的物理 Pegging 记录数（Task-to-Task）
    /// </summary>
    public int PhysicalPeggingCount { get; set; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 警告信息列表
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// 执行耗时（毫秒）
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.Now;
}
