using LPS.APS.Core.Dto;
using ApsTask = LPS.APS.Core.Entities.APS.Task;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// Pegging 编排服务接口（2号位）
/// 负责执行 Pegging 编排（BOM遍历 + 供给扣减 + 结果红线校验）并持久化
/// 对应文档：步骤2.6-2.8 的编排层职责
/// </summary>
public interface IPeggingOrchestrator
{
    /// <summary>
    /// 执行完整的 Pegging 流程（步骤2.1-2.8）
    /// 1. 读 APS_BOM_RAW / APS_BOM_STAGE_PATH_RAW / APS_BOM_CROSS_FACTORY_EDGE_RAW
    /// 2. 读供给池（INVENTORY / WIP / PIPELINE / PRODUCTION_INSTRUCTION / PURCHASE_ORDER）
    /// 3. 按3号位 DemandPriorityConfig 排序 Demand
    /// 4. BOM 遍历 + 供给原子扣减（TryAtomicAllocation）
    /// 5. NEW_REQUIREMENT 触发 LogicalProductionDemand 生成，交1号位排程实例化 Task
    /// 6. 结果红线校验（Demand闭合 / SupplyBalance非负 / 同物理不重复消费 / Allocation合法）
    /// 7. 调用1号位 Solver
    /// 8. 写 PeggingSupplyAllocation（非NEW_REQUIREMENT）+ 写物理 Pegging（Task-to-Task）
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
    /// 阶段2 S2.3 双跑对照（不切主链）：同一 PlanVersion + Demand + 供给池，旧 DFS 与新 BFS 各跑一遍，
    /// 产出 PM 0918-3 §八 7 项 + §六 I1-I4 报告。仅供验收/测试显式调用，不影响主链 ExecutePeggingWorkflowAsync。
    /// </summary>
    Task<DualRunReport> RunDualCompareAsync(
        PeggingExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 主链 BFS 单跑（只读，不落库不 Solver）：仅跑 RunBfsLoop，验证新 BFS 展开耗时 + I1/I2 红线校验。
    /// 用户 2026-09-18 裁决弃旧 DFS、主链切 BFS 后，作为快速验收入口（不触碰 Pegging/Task/PSA 落库）。
    /// </summary>
    Task<BfsRunReport> RunBfsOnlyAsync(
        PeggingExecutionRequest request,
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
    /// Pegging 阶段耗时（毫秒）：供给装载 + BOM 遍历扣减 + Routing 装载 + 请求构建
    /// </summary>
    public long PeggingMs { get; set; }

    /// <summary>
    /// 1号位 Solver 求解耗时（毫秒）
    /// </summary>
    public long SolverMs { get; set; }

    /// <summary>
    /// 落盘耗时（毫秒）：Task + Pegging + SupplyAllocation 统一事务
    /// </summary>
    public long PersistMs { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.Now;
}

/// <summary>主链 BFS 单跑报告（只读验收）：红线校验 + 展开统计 + 正确性侧面指标。</summary>
public class BfsRunReport
{
    public int PlanVersionId { get; set; }
    public int OrderCount { get; set; }

    /// <summary>红线校验错误（ValidatePeggingResult 前 4 项：Demand 闭合 / SupplyBalance 非负 / 同物理不重复消费 / Allocation 合法）。</summary>
    public IReadOnlyList<string> RedLineErrors { get; set; } = Array.Empty<string>();

    public int LpdCount { get; set; }
    public int AllocationCount { get; set; }
    public int LineageCount { get; set; }
    public decimal DemandQuantity { get; set; }
    public decimal ShortageQuantity { get; set; }

    public long TraversalVisits { get; set; }
    public long UniqueNodes { get; set; }
    public long DuplicateExpansions { get; set; }
    public long ElapsedMs { get; set; }

    public bool RedLinePass => RedLineErrors.Count == 0;
}
