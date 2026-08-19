using LPS.APS.Core.Dto;
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
        // 1. 解析工序依赖图（按 MaterialId + RouteCode 分组）
        // ═══════════════════════════════════════════════
        BuildRoutingGraphs(request, context);

        // ═══════════════════════════════════════════════
        // 2. 解析工序资源资格（Operation → 合法 Resource 列表）
        // ═══════════════════════════════════════════════
        BuildOperationResourceEligibility(request, context);

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
    /// 构建工序依赖图
    /// </summary>
    private void BuildRoutingGraphs(DomainSolveRequest request, ConstraintContext context)
    {
        // 按 MaterialId 分组
        var operationsByMaterial = request.RoutingOperations
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
                        SetupTime = op.SetupTime
                    };
                }

                // 构建依赖边
                var dependencies = request.RoutingDependencies
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
    /// 构建工序资源资格映射
    /// </summary>
    private void BuildOperationResourceEligibility(DomainSolveRequest request, ConstraintContext context)
    {
        // 按 (MaterialId, RouteCode, OperationCode) → ResourceId 列表（按 Priority 排序）
        var eligibilityGroups = request.ResourceEligibility
            .GroupBy(e => $"{e.RouteKey}::{e.OperationCode}")
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.Priority)
                      .Select(e => e.ResourceId)
                      .ToList()
            );

        context.OperationResourceEligibility = eligibilityGroups;
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
                    ConstraintType = ec.ConstraintType
                }
            );

        context.LockedTasks = lockedTasks;
    }

    /// <summary>
    /// 构建共享资源占用块
    /// </summary>
    private void BuildResourceBlocks(DomainSolveRequest request, ConstraintContext context)
    {
        if (request.CandidateContext?.ExternalDomainResourceBlocks == null)
        {
            return;
        }

        var blocksByResource = request.CandidateContext.ExternalDomainResourceBlocks
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
