using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 1: 硬约束构建器
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.0_20260814.md》§六 Phase 1
///
/// 职责：
/// - 建立 Routing（工艺路线）
/// - Resource Eligibility（资源资格约束）
/// - Calendar（日历可用时间）
/// - Material AvailableTime（物料多段可用时间）
/// - Execution/Firm/Frozen（不可逆约束）
/// - Shared-resource blocks（共享资源阻挡）
/// - Quantity-Time（数量-时间约束）
/// - 工序先后关系
/// </summary>
internal class PhaseOneConstraintBuilder
{
    /// <summary>
    /// 构建硬约束上下文
    /// </summary>
    public ConstraintContext BuildConstraints(DomainSolveRequest request)
    {
        var context = new ConstraintContext();

        // ═══════════════════════════════════════════════
        // 0. 部门锁定（最小 B）：按 (MaterialId, StageCode) → ProductionDepartmentId
        //    过滤 Routing 三件套；缺失 Context 的 Material 记入 context.MissingDepartmentContextMaterialIds
        // ═══════════════════════════════════════════════
        ApplyDepartmentLock(request, context,
            out var lockedOperations,
            out var lockedDependencies,
            out var lockedEligibilities);

        // ═══════════════════════════════════════════════
        // 1. 解析工序依赖图（使用部门锁定后的 Routing）
        // ═══════════════════════════════════════════════
        BuildRoutingGraphs(lockedOperations, lockedDependencies, context);

        // ═══════════════════════════════════════════════
        // 2. 解析工序资源资格（使用部门锁定后的 Eligibility）
        // ═══════════════════════════════════════════════
        BuildOperationResourceEligibility(lockedEligibilities, context);

        // ═══════════════════════════════════════════════
        // 3. 解析资源日历（Resource → 可用时间窗列表）
        // ═══════════════════════════════════════════════
        BuildResourceCalendars(request, context);

        // ═══════════════════════════════════════════════
        // 4. 解析物料多段可用性（AllocationSequence → Quantity-Time 分段）
        // ═══════════════════════════════════════════════
        BuildMaterialAvailability(request, context);

        // ═══════════════════════════════════════════════
        // 5. 解析锁定任务约束（DraftId → 锁定信息）
        // ═══════════════════════════════════════════════
        BuildLockedTasks(request, context);

        // ═══════════════════════════════════════════════
        // 6. 解析共享资源占用块（Resource → 占用时间块列表）
        // ═══════════════════════════════════════════════
        BuildResourceBlocks(request, context);

        return context;
    }

    /// <summary>
    /// 部门锁定（最小 B）：按 (MaterialId, StageCode) → ProductionDepartmentId 过滤 Routing 三件套。
    /// 文档：PM 裁定《ProductionDepartment回复.md》最小 B
    /// 1号位只消费 2号位传入的 MaterialStageDepartmentContexts，不得重新推导部门、不得跨部门优化选择。
    /// </summary>
    private void ApplyDepartmentLock(
        DomainSolveRequest request,
        ConstraintContext context,
        out List<RoutingOperation> lockedOperations,
        out List<RoutingDependency> lockedDependencies,
        out List<OperationResourceEligibility> lockedEligibilities)
    {
        // (MaterialId, StageCode) → ProductionDepartmentId（2号位保证 (MaterialId, StageCode) 唯一）
        var deptContext = request.MaterialStageDepartmentContexts
            .GroupBy(c => (c.MaterialId, c.StageCode))
            .ToDictionary(g => g.Key, g => g.First().ProductionDepartmentId);

        // 1. 识别缺失 Context 的 Material：任一工序 Stage 查不到 Context → 整条 Routing 无法锁定
        foreach (var op in request.RoutingOperations)
        {
            var stageCode = op.StageCode ?? string.Empty;
            if (!deptContext.ContainsKey((op.MaterialId, stageCode)))
            {
                context.MissingDepartmentContextMaterialIds.Add(op.MaterialId);
            }
        }

        // 2. 过滤 RoutingOperation：缺失 Material 整条剔除 + 部门不符剔除
        lockedOperations = new List<RoutingOperation>();
        var validOperationKeys = new HashSet<(int MaterialId, string RouteCode, string OperationCode)>();

        foreach (var op in request.RoutingOperations)
        {
            if (context.MissingDepartmentContextMaterialIds.Contains(op.MaterialId))
            {
                continue; // 缺失 Context 的 Material 整条剔除，Phase2 无路由 → Unscheduled
            }

            var stageCode = op.StageCode ?? string.Empty;
            var expectedDeptId = deptContext[(op.MaterialId, stageCode)];

            if (op.ProductionDepartmentId != expectedDeptId)
            {
                continue; // 部门不符，剔除
            }

            lockedOperations.Add(op);
            validOperationKeys.Add((op.MaterialId, op.RouteCode, op.OperationCode));
        }

        // 3. 过滤 RoutingDependency：两端 Operation 都必须合法
        lockedDependencies = request.RoutingDependencies
            .Where(dep =>
                validOperationKeys.Contains((dep.MaterialId, dep.RouteCode, dep.FromOperationCode)) &&
                validOperationKeys.Contains((dep.MaterialId, dep.RouteCode, dep.ToOperationCode)))
            .ToList();

        // 4. 过滤 OperationResourceEligibility：Operation 必须合法
        lockedEligibilities = request.OperationResourceEligibility
            .Where(e => validOperationKeys.Contains((e.MaterialId, e.RouteCode, e.OperationCode)))
            .ToList();
    }

    /// <summary>
    /// 构建工序依赖图（使用部门锁定后的 Routing）
    /// </summary>
    private void BuildRoutingGraphs(
        List<RoutingOperation> routingOperations,
        List<RoutingDependency> routingDependencies,
        ConstraintContext context)
    {
        // 按 MaterialId 分组
        var operationsByMaterial = routingOperations
            .GroupBy(op => op.MaterialId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (materialId, operations) in operationsByMaterial)
        {
            // 按 RouteCode 再分组
            var operationsByRoute = operations
                .GroupBy(op => op.RouteCode)
                .ToDictionary(g => g.Key, g => g.ToList());

            var routeGraphs = new Dictionary<string, RoutingGraph>();

            foreach (var (routeCode, routeOps) in operationsByRoute)
            {
                var graph = new RoutingGraph();

                // 构建工序节点
                foreach (var op in routeOps)
                {
                    graph.Operations[op.OperationCode] = new OperationNode
                    {
                        OperationCode = op.OperationCode,
                        OperationName = op.OperationName,
                        ProcessType = op.ProcessType,
                        StageCode = op.StageCode,
                        StandardDuration = op.StandardDuration,
                        SetupTime = op.SetupTime,
                        TransferBatchSize = op.TransferBatchSize
                    };
                }

                // 构建依赖边
                var dependencies = routingDependencies
                    .Where(dep => dep.MaterialId == materialId && dep.RouteCode == routeCode)
                    .ToList();

                foreach (var dep in dependencies)
                {
                    if (!graph.Dependencies.ContainsKey(dep.ToOperationCode))
                    {
                        graph.Dependencies[dep.ToOperationCode] = new List<DependencyEdge>();
                    }

                    graph.Dependencies[dep.ToOperationCode].Add(new DependencyEdge
                    {
                        FromOperationCode = dep.FromOperationCode,
                        ToOperationCode = dep.ToOperationCode,
                        DependencyType = dep.DependencyType,
                        LagTime = dep.LagTime
                    });
                }

                // 识别根工序（无前驱的工序）
                var allToOps = graph.Dependencies.Keys.ToHashSet();
                graph.RootOperations = graph.Operations.Keys
                    .Where(opCode => !allToOps.Contains(opCode))
                    .ToList();

                routeGraphs[routeCode] = graph;
            }

            context.RoutingGraphs[materialId] = routeGraphs;
        }
    }

    /// <summary>
    /// 构建工序资源资格映射（使用部门锁定后的 Eligibility）
    /// </summary>
    private void BuildOperationResourceEligibility(
        List<OperationResourceEligibility> eligibilities,
        ConstraintContext context)
    {
        // 按 (MaterialId, RouteCode, OperationCode) → ResourceId 列表（按 Priority 排序）
        // P0-01修复：使用冻结接口 OperationResourceEligibility，不再使用旧的 ResourceEligibility
        // 第4轮C1修复：索引加入MaterialId，避免不同物料共享资源资格
        var eligibilityGroups = eligibilities
            .GroupBy(e => $"{e.MaterialId}::{e.RouteCode}::{e.OperationCode}")
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.Priority)
                      .Select(e => e.ResourceId)
                      .ToList()
            );

        context.OperationResourceEligibility = eligibilityGroups;

        // P0-04修复：同时构建 ResourceCapacityFactors 映射
        // 第4轮C1修复：索引加入MaterialId
        // (MaterialId::RouteCode::OperationCode, ResourceId) → CapacityFactor
        var capacityFactors = eligibilities
            .GroupBy(e => $"{e.MaterialId}::{e.RouteCode}::{e.OperationCode}")
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    e => e.ResourceId,
                    e => e.CapacityFactor
                )
            );

        context.ResourceCapacityFactors = capacityFactors;
    }

    /// <summary>
    /// 构建资源日历（只保留可用时间窗）
    /// </summary>
    private void BuildResourceCalendars(DomainSolveRequest request, ConstraintContext context)
    {
        var calendarsByResource = request.CalendarSlots
            .Where(slot => slot.IsAvailable)  // 只保留可用时间窗
            .GroupBy(slot => slot.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(slot => slot.Start)
                      .Select(slot => new TimeWindow(slot.Start, slot.End))
                      .ToList()
            );

        context.ResourceCalendars = calendarsByResource;
    }

    /// <summary>
    /// 构建物料多段可用性
    /// </summary>
    private void BuildMaterialAvailability(DomainSolveRequest request, ConstraintContext context)
    {
        var availabilityByAllocation = request.MaterialConstraints
            .GroupBy(m => m.AllocationSequence)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(m => m.AvailableTime)
                      .Select(m => new MaterialAvailabilitySegment
                      {
                          Quantity = m.Quantity,
                          AvailableTime = m.AvailableTime,
                          SourceType = m.SourceType,
                          SourceKey = m.SourceKey
                      })
                      .ToList()
            );

        context.MaterialAvailability = availabilityByAllocation;
    }

    /// <summary>
    /// 构建锁定任务约束
    /// 第8轮P0-01修复：完整传递ExecutionConstraint的StageCode/OperationCode/LockedQuantity/TaskKey
    /// </summary>
    private void BuildLockedTasks(DomainSolveRequest request, ConstraintContext context)
    {
        var lockedTasks = request.ExecutionConstraints
            .ToDictionary(
                ec => ec.DraftId,
                ec => new LockedTaskConstraint
                {
                    DraftId = ec.DraftId,
                    ResourceId = ec.ResourceId,
                    LockedStart = ec.LockedStart,
                    LockedEnd = ec.LockedEnd,
                    ConstraintType = ec.ConstraintType,
                    StageCode = ec.StageCode,
                    OperationCode = ec.OperationCode,
                    LockedQuantity = ec.LockedQuantity,
                    TaskKey = ec.TaskKey
                }
            );

        context.LockedTasks = lockedTasks;
    }

    /// <summary>
    /// 构建共享资源占用块
    /// </summary>
    private void BuildResourceBlocks(DomainSolveRequest request, ConstraintContext context)
    {
        // Candidate §11：其它 Domain 当前 ACTIVE 共享资源占用（外部不可移动阻挡块）
        var blocks = new List<ResourceBlock>();
        if (request.CandidateContext?.ExternalDomainResourceBlocks != null)
            blocks.AddRange(request.CandidateContext.ExternalDomainResourceBlocks);

        // FULL §9：前序 Domain 成功后的共享 Resource 占用块（与 Candidate 外部块同语义：不可用时间窗）
        if (request.UpstreamDomainResourceBlocks != null)
            blocks.AddRange(request.UpstreamDomainResourceBlocks);

        if (blocks.Count == 0)
        {
            return;
        }

        var blocksByResource = blocks
            .GroupBy(block => block.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(block => new ResourceBlockInfo
                {
                    ResourceId = block.ResourceId,
                    StartTime = block.StartTime,
                    EndTime = block.EndTime,
                    Reason = block.Reason
                })
                .ToList()
            );

        context.ResourceBlocks = blocksByResource;
    }
}

/// <summary>
/// 硬约束上下文（Phase 1 输出）
/// </summary>
internal class ConstraintContext
{
    /// <summary>
    /// 工序依赖图：MaterialId → RouteCode → 工序依赖关系
    /// </summary>
    public Dictionary<int, Dictionary<string, RoutingGraph>> RoutingGraphs { get; set; } = new();

    /// <summary>
    /// 工序资源资格：(MaterialId, RouteCode, OperationCode) → 合法 ResourceId 列表（按优先级排序）
    /// </summary>
    public Dictionary<string, List<int>> OperationResourceEligibility { get; set; } = new();

    /// <summary>
    /// P0-04修复：工序资源产能系数映射：(RouteCode::OperationCode, ResourceId) → CapacityFactor
    /// 用于Duration计算：StandardDuration × PlannedProcessQty ÷ CapacityFactor
    /// </summary>
    public Dictionary<string, Dictionary<int, decimal>> ResourceCapacityFactors { get; set; } = new();

    /// <summary>
    /// 资源日历：ResourceId → 可用时间窗列表（已排序）
    /// </summary>
    public Dictionary<int, List<TimeWindow>> ResourceCalendars { get; set; } = new();

    /// <summary>
    /// 物料多段可用性：AllocationSequence → Quantity-Time 分段列表（已按时间排序）
    /// </summary>
    public Dictionary<long, List<MaterialAvailabilitySegment>> MaterialAvailability { get; set; } = new();

    /// <summary>
    /// 锁定任务约束：DraftId → 锁定信息
    /// </summary>
    public Dictionary<string, LockedTaskConstraint> LockedTasks { get; set; } = new();

    /// <summary>
    /// 共享资源占用块：ResourceId → 占用时间块列表
    /// </summary>
    public Dictionary<int, List<ResourceBlockInfo>> ResourceBlocks { get; set; } = new();

    /// <summary>
    /// 缺失生产部门 Context 的 MaterialId 集合。
    /// 部门锁定（最小 B）：按 (MaterialId, StageCode) 查 MaterialStageDepartmentContexts，
    /// 若某 Material 的任一工序 Stage 查不到 Context，则该 Material 整条 Routing 无法锁定部门，
    /// 对应的 Demand 应标记 Unscheduled，Reason = MISSING_PRODUCTION_DEPARTMENT_CONTEXT。
    /// </summary>
    public HashSet<int> MissingDepartmentContextMaterialIds { get; set; } = new();
}

/// <summary>
/// 工序依赖图（单个物料单条路径）
/// </summary>
internal class RoutingGraph
{
    /// <summary>
    /// 工序节点：OperationCode → 工序信息
    /// </summary>
    public Dictionary<string, OperationNode> Operations { get; set; } = new();

    /// <summary>
    /// 依赖边：ToOperationCode → 前驱列表
    /// </summary>
    public Dictionary<string, List<DependencyEdge>> Dependencies { get; set; } = new();

    /// <summary>
    /// 根工序（无前驱的工序）
    /// </summary>
    public List<string> RootOperations { get; set; } = new();
}

/// <summary>
/// 工序节点
/// </summary>
internal class OperationNode
{
    public string OperationCode { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public string ProcessType { get; set; } = string.Empty;
    public string? StageCode { get; set; }
    public decimal StandardDuration { get; set; }
    public decimal SetupTime { get; set; }
    public decimal? TransferBatchSize { get; set; }
}

/// <summary>
/// 依赖边
/// </summary>
internal class DependencyEdge
{
    public string FromOperationCode { get; set; } = string.Empty;
    public string ToOperationCode { get; set; } = string.Empty;
    public string DependencyType { get; set; } = "ES";
    public decimal LagTime { get; set; }
}

/// <summary>
/// 物料可用性分段
/// </summary>
internal class MaterialAvailabilitySegment
{
    public decimal Quantity { get; set; }
    public DateTime AvailableTime { get; set; }
    public string? SourceType { get; set; }
    public string? SourceKey { get; set; }
}

/// <summary>
/// 锁定任务约束
/// </summary>
internal class LockedTaskConstraint
{
    public string DraftId { get; set; } = string.Empty;
    public int ResourceId { get; set; }
    public DateTime LockedStart { get; set; }
    public DateTime LockedEnd { get; set; }
    public string ConstraintType { get; set; } = string.Empty;

    // 第8轮P0-01修复：Anchor部分数量和工序信息闭环
    public string? StageCode { get; set; }
    public string? OperationCode { get; set; }
    public decimal? LockedQuantity { get; set; }
    public string? TaskKey { get; set; }
}

/// <summary>
/// 资源占用块信息
/// </summary>
internal class ResourceBlockInfo
{
    public int ResourceId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Reason { get; set; } = string.Empty;
}
