using System.Diagnostics.CodeAnalysis;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 1: 硬约束构建器
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md》§六 Phase 1
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
        // 0.0 StageSeq 全序校验（B-1，0号位 2026-09-29 裁决 §二）
        //     冲突物料记入 context.StageSequenceConflictMaterialIds ⇒ 下游不生成正式计划
        // ═══════════════════════════════════════════════
        ValidateStageSequences(request, context);

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
        // 1.5 解析跨物料依赖 DAG（任务喂任务，方案A）：子先父后拓扑分层
        // ═══════════════════════════════════════════════
        BuildCrossMaterialDag(request, context);

        // ═══════════════════════════════════════════════
        // 2. 解析工序资源资格（使用部门锁定后的 Eligibility）
        // ═══════════════════════════════════════════════
        BuildOperationResourceEligibility(lockedEligibilities, context);

        // ═══════════════════════════════════════════════
        // 2.5 解析资源编码（ResourceId → ResourceCode）
        // ═══════════════════════════════════════════════
        BuildResourceCodes(request, context);

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

        // ═══════════════════════════════════════════════
        // 7. 装载 Setup 换型规则（P1-02 item1：第⑦块 → EXACT/DEFAULT 查找字典）
        // ═══════════════════════════════════════════════
        BuildSetupTransitionRules(request, context);

        // ═══════════════════════════════════════════════
        // 8. 装载 RunScope（M5 第一批：Run 级交期覆盖 + Task 软目标）
        // ═══════════════════════════════════════════════
        BuildRunScope(request, context);

        // ═══════════════════════════════════════════════
        // 9. 装载 ⑧块 Batch Policy（P0-01：Material + ProductionDepartment 键控）
        // ═══════════════════════════════════════════════
        BuildExecutionBatchPolicies(request, context);

        return context;
    }

    /// <summary>
    /// B-1：StageSeq 全序校验（0号位 2026-09-29 裁决 §二，逐字口径）。
    ///
    /// 裁决原文要点：
    /// - §2.1 `StageSeq` 采用**全序链**语义，不采用「相同 StageSeq 表示并行 Stage」的偏序语义；
    ///   唯一性范围是**一个已解析好的具体 StageSequenceChain 内**，不是 Material 全局；
    /// - §2.2 同 StageCode 重复 → 取最小 StageSeq 收敛（DTO 既有兼容规则，「可以继续保留」）；
    ///   收敛后**不同 StageCode 不得拥有相同 StageSeq**；
    /// - §2.3 若冲突：不允许静默按数组顺序、不允许静默并行、输出明确 Issue、
    ///   **不生成可能顺序错误的正式计划**。
    ///
    /// 本方法只做「校验 + 登记冲突」，**不**在此建立有序链：有序链是 ④（无 Routing Stage 的
    /// StageTimingNode 时间层）的输入，尚无消费者，提前落库即死代码。冲突登记本身已是完整能力
    /// ——它直接改变该物料需求的排程结果（Unscheduled + 明确 Reason）。
    /// </summary>
    private void ValidateStageSequences(DomainSolveRequest request, ConstraintContext context)
    {
        foreach (var chain in request.StageSequenceChains)
        {
            // ① 同 StageCode 取最小 StageSeq 收敛（裁决 §2.2 明文保留的既有兼容规则）。
            //    2号位 装载时已做（StageSequenceChain 文档「同一 StageCode 取最小 StageSeq」），
            //    此处再收敛一次是防御：契约口径不因上游实现细节而失效。
            var converged = chain.Stages
                .GroupBy(s => s.StageCode ?? string.Empty, StringComparer.Ordinal)
                .Select(g => g.OrderBy(s => s.StageSeq).First())
                .ToList();

            // ② 全序校验：收敛后不同 StageCode 的 StageSeq 必须互不相同
            var hasDuplicateSeq = converged
                .GroupBy(s => s.StageSeq)
                .Any(g => g.Count() > 1);

            if (hasDuplicateSeq)
            {
                context.StageSequenceConflictMaterialIds.Add(chain.MaterialId);
            }
        }
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

        // 1. 识别缺失 Context 的 Material：仅校验「本次求解在范围」的工序 Stage。
        //    P1-06修复：StartOperation/StartStage 之前的已完成 Stage 不再参与本次求解，
        //    即使其缺 Department Context 也不应误杀当前剩余 Routing。
        //    无 Start 信息的需求（新单从首工序起）仍校验全量 Stage。
        var reachableStages = BuildReachableStages(request);

        // 供 Phase5 计算「无 Routing Stage」时间节点（0号位 2026-09-29 裁决 §十四第4项）。
        // 见 ConstraintContext.EffectiveStages 注释：语义不变，仅把原本的局部变量挂出来复用。
        context.EffectiveStages = reachableStages;
        foreach (var op in request.RoutingOperations)
        {
            var stageCode = op.StageCode ?? string.Empty;

            // 防御：该 Material 无任何需求时，不参与缺失判定。
            if (!reachableStages.TryGetValue(op.MaterialId, out var inScopeStages))
            {
                continue;
            }

            // P1-06修复：仅当该工序 Stage 在「本次求解范围」内时，才纳入缺失判定。
            if (!inScopeStages.Contains(stageCode))
            {
                continue;
            }

            if (!deptContext.ContainsKey((op.MaterialId, stageCode)))
            {
                context.MissingDepartmentContextMaterialIds.Add(op.MaterialId);
            }
        }

        // 2. 过滤 RoutingOperation：缺失 Material 整条剔除 + 部门不符剔除
        // v5.0.16 冻结：Routing 三件套唯一键升级为含 ProductionDepartmentId 的三元/四元组；
        // 部门=「物料×阶段」联合属性，同物料同阶段不同部门有不同小工序集合，
        // 因此 validOperationKeys 必须带 ProductionDepartmentId，否则会把别的部门同名 Operation 的
        // Dependency / Eligibility 漏进当前部门的锁定图。
        lockedOperations = new List<RoutingOperation>();
        var validOperationKeys = new HashSet<(int MaterialId, int ProductionDepartmentId, string RouteCode, string OperationCode)>();

        foreach (var op in request.RoutingOperations)
        {
            if (context.MissingDepartmentContextMaterialIds.Contains(op.MaterialId))
            {
                continue; // 缺失 Context 的 Material 整条剔除，Phase2 无路由 → Unscheduled
            }

            var stageCode = op.StageCode ?? string.Empty;

            // P1-06 对齐：与第一轮同口径——无需求 Material 不参与部门锁定；范围外 Stage
            // （续排起点之前已完成）不参与，避免索引 deptContext 时 KeyNotFound。
            if (!reachableStages.TryGetValue(op.MaterialId, out var inScopeStages2) ||
                !inScopeStages2.Contains(stageCode))
            {
                continue;
            }

            var expectedDeptId = deptContext[(op.MaterialId, stageCode)];

            if (op.ProductionDepartmentId != expectedDeptId)
            {
                continue; // 部门不符，剔除
            }

            lockedOperations.Add(op);
            validOperationKeys.Add((op.MaterialId, op.ProductionDepartmentId, op.RouteCode, op.OperationCode));
        }

        // 3. 过滤 RoutingDependency：两端 Operation 都必须已锁定。
        // ⚠ PM 0928-1 §五 终裁：「跨 Stage Dependency 不能要求 FromDepartment = ToDepartment，
        //   否则会错误限制真实制造流程」——`RoutingDependency.ProductionDepartmentId` 是**单值**，
        //   跨 Stage 边两端分属不同部门时，该行无论填哪个部门值都不可能同时命中两端 ⇒ 必被误杀。
        //   故此处按 (MaterialId, RouteCode, OperationCode) 判定：lockedOperations 已在上面按各自
        //   Stage 的期望部门过滤过，键内已隐含「部门正确」，无需再要求 dep 的部门值同时命中两端。
        //   同 Stage 边**零回归**（其 dep 部门本就等于两端部门）。
        //
        // ⚠ 残留（诚实登记，不静默）：本层只能用三元组做**必要**条件 —— `RoutingDependency` 契约内
        //   无 StageCode，无法在此判定边的 Stage 归属。同码跨 Stage 时，「另一 Stage 的同名工序仍在锁定集内」
        //   会让一条本应随其 Stage 一起被剔除的边在此存活。精确 Stage 归属在 `BuildRoutingGraphs` 里
        //   由图内节点反查消解（唯一 ⇒ 取；歧义 ⇒ 计数 + 丢边，不猜）。已核：同 Stage 边不会误接
        //   （同部门 ⇒ 两端节点都锁定 ⇒ 消解歧义 ⇒ 丢边计数，不产生错边）。
        var lockedOperationKeys = lockedOperations
            .Select(op => (op.MaterialId, op.RouteCode, op.OperationCode))
            .ToHashSet();

        lockedDependencies = request.RoutingDependencies
            .Where(dep =>
                lockedOperationKeys.Contains((dep.MaterialId, dep.RouteCode, dep.FromOperationCode)) &&
                lockedOperationKeys.Contains((dep.MaterialId, dep.RouteCode, dep.ToOperationCode)))
            .ToList();

        // 4. 过滤 OperationResourceEligibility：Operation 必须合法，且部门与锁定 Operation 一致
        lockedEligibilities = request.OperationResourceEligibility
            .Where(e => validOperationKeys.Contains((e.MaterialId, e.ProductionDepartmentId, e.RouteCode, e.OperationCode)))
            .ToList();
    }

    /// <summary>
    /// P1-06修复：按需求计算「本次求解在范围」的工序 Stage 集合。
    /// 续排（有 StartOperationCode/StartStageCode）时，起点之前的已完成 Stage 不在本次求解范围，
    /// 即使其缺 Department Context 也不应把整条 Routing 误判为 MISSING_PRODUCTION_DEPARTMENT_CONTEXT。
    /// 无 Start 信息的需求（新单从首工序起）仍校验全量 Stage（BFS 自根工序覆盖整图）。
    ///
    /// 返回：MaterialId → 该物料在范围内（= 所有相关需求可达 Stage 的并集）的 StageCode 集合。
    /// </summary>
    private Dictionary<int, HashSet<string>> BuildReachableStages(DomainSolveRequest request)
    {
        var result = new Dictionary<int, HashSet<string>>();

        // 预处理：物料 → 工序行 与依赖行（V1 默认 DEFAULT 路径）
        var opsByMaterial = request.RoutingOperations
            .GroupBy(op => op.MaterialId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var depsByMaterial = request.RoutingDependencies
            .GroupBy(dep => dep.MaterialId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var demand in request.LogicalProductionDemands)
        {
            if (!opsByMaterial.TryGetValue(demand.MaterialId, out var ops))
            {
                continue; // 该物料无 Routing，无 Stage 可校验
            }

            if (!result.TryGetValue(demand.MaterialId, out var stageSet))
            {
                stageSet = new HashSet<string>();
                result[demand.MaterialId] = stageSet;
            }

            var depsForMaterial = depsByMaterial.TryGetValue(demand.MaterialId, out var dl)
                ? dl
                : new List<RoutingDependency>();

            // 按 (RouteCode, PathId) **逐路径**计算可达 Stage 后取**并集**（v1.6 + Q1：PathId 进图键、禁止跨 Path 连边）。
            // V1 装载层归一化为单路径（'DEFAULT'/1）⇒ 恰好一组，与旧实现（只取 'DEFAULT'）**行为等价**；
            // V2 多路径 ⇒ 逐路径各算再并集：只并**集合**、不跨路径连边（并集是保守口径，
            // 宁可判「该 Stage 在范围内」也不误报缺失，延续 P1-06 初衷）。
            foreach (var pathGroup in ops.GroupBy(op => RoutePathKey.Of(op.RouteCode, op.PathId)))
            {
                CollectReachableStagesForPath(
                    pathGroup,
                    depsForMaterial.Where(d => d.RouteCode == pathGroup.Key.RouteCode
                                            && d.PathId == pathGroup.Key.PathId),
                    demand,
                    stageSet);
            }
        }

        return result;

        // 单条 (RouteCode, PathId) 路径内的可达 Stage 收集（局部函数：复用外层实例方法 ResolveNodeKey）
        void CollectReachableStagesForPath(
            IEnumerable<RoutingOperation> pathOps,
            IEnumerable<RoutingDependency> pathDeps,
            LogicalProductionDemand demand,
            HashSet<string> stageSet)
        {
            // 工序节点集合（单条路径内）：键 = (StageCode, OperationCode)。
            // ⚠ 0号位 2026-09-29 裁决 §5.3：原实现是「工序码 → StageCode」单键 + First() 任取，
            //   同码跨 Stage 时会**判错整个可达 Stage 集合**（连带影响 P1-06 缺失判定与部门锁定范围），
            //   比单点覆盖更严重。此处升维。
            var nodes = pathOps
                .GroupBy(op => OperationNodeKey.Of(op.StageCode, op.OperationCode))
                .ToDictionary(g => g.Key, g => g.First());

            // 工序码 → 候选节点键（消解依赖端点 / 起点工序用；同码跨 Stage 时 >1 个候选）
            var nodesByOperationCode = new Dictionary<string, List<OperationNodeKey>>();
            foreach (var nodeKey in nodes.Keys)
            {
                if (!nodesByOperationCode.TryGetValue(nodeKey.OperationCode, out var keyList))
                {
                    keyList = new List<OperationNodeKey>();
                    nodesByOperationCode[nodeKey.OperationCode] = keyList;
                }

                keyList.Add(nodeKey);
            }

            // 依赖邻接表：From → List&lt;To&gt; + 入度（用于识别根工序）
            var adjacency = new Dictionary<OperationNodeKey, List<OperationNodeKey>>();
            var inDegree = new Dictionary<OperationNodeKey, int>();
            foreach (var nodeKey in nodes.Keys)
            {
                inDegree[nodeKey] = 0;
            }

            foreach (var dep in pathDeps)
            {
                var fromKey = nodesByOperationCode.TryGetValue(dep.FromOperationCode, out var fromCandidates)
                    ? ResolveNodeKey(fromCandidates, k => nodes[k].ProductionDepartmentId, dep.ProductionDepartmentId)
                    : null;

                var toKey = nodesByOperationCode.TryGetValue(dep.ToOperationCode, out var toCandidates)
                    ? ResolveNodeKey(toCandidates, k => nodes[k].ProductionDepartmentId, dep.ProductionDepartmentId)
                    : null;

                if (fromKey == null || toKey == null)
                {
                    continue; // 无法消解 ⇒ 不猜（丢边由 BuildRoutingGraphs 统一计数）
                }

                if (!adjacency.TryGetValue(fromKey.Value, out var tos))
                {
                    tos = new List<OperationNodeKey>();
                    adjacency[fromKey.Value] = tos;
                }
                tos.Add(toKey.Value);

                if (inDegree.ContainsKey(toKey.Value))
                {
                    inDegree[toKey.Value]++;
                }
            }

            // 起点工序集合
            var hasStartOperation = !string.IsNullOrWhiteSpace(demand.StartOperationCode);
            var hasStartStage = !string.IsNullOrWhiteSpace(demand.StartStageCode);
            var startOps = new List<OperationNodeKey>();

            if (hasStartOperation)
            {
                // 5号位 交付的工序级起点。同码跨 Stage 时须用 StartStageCode 收窄，否则会把
                // 另一 Stage 的同名工序也当作起点（旧实现按工序码单键命中，是同一根因的另一面）。
                if (nodesByOperationCode.TryGetValue(demand.StartOperationCode!, out var startCandidates))
                {
                    foreach (var candidate in startCandidates)
                    {
                        if (hasStartStage && candidate.StageCode != demand.StartStageCode)
                        {
                            continue;
                        }

                        startOps.Add(candidate);
                    }
                }
            }
            else
            {
                // 无工序级起点：新单从根工序起；若有 StartStageCode 则收窄到该 Stage 的根工序
                foreach (var nodeKey in nodes.Keys)
                {
                    if (inDegree[nodeKey] != 0)
                    {
                        continue; // 非根工序
                    }
                    if (hasStartStage && nodeKey.StageCode != demand.StartStageCode)
                    {
                        continue; // 不在起始 Stage
                    }
                    startOps.Add(nodeKey);
                }
            }

            // BFS 可达工序 → 收集 StageCode
            var visited = new HashSet<OperationNodeKey>();
            var queue = new Queue<OperationNodeKey>();
            foreach (var s in startOps)
            {
                if (visited.Add(s))
                {
                    queue.Enqueue(s);
                }
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                stageSet.Add(cur.StageCode); // 键即含 Stage，不再有「查不到」分支

                if (adjacency.TryGetValue(cur, out var tos))
                {
                    foreach (var to in tos)
                    {
                        if (visited.Add(to))
                        {
                            queue.Enqueue(to);
                        }
                    }
                }
            }
        }
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
            // 按 (RouteCode, PathId) 再分组（v1.6 + Q1：PathId 必须进图键，禁止跨 Path 连边）
            var operationsByPath = operations
                .GroupBy(op => RoutePathKey.Of(op.RouteCode, op.PathId))
                .ToDictionary(g => g.Key, g => g.ToList());

            var routeGraphs = new Dictionary<RoutePathKey, RoutingGraph>();

            foreach (var (pathKey, routeOps) in operationsByPath)
            {
                var graph = new RoutingGraph();

                // 构建工序节点：键 = (StageCode, OperationCode)（0号位 2026-09-29 裁决 §5.2/§5.3）
                foreach (var op in routeOps)
                {
                    var nodeKey = OperationNodeKey.Of(op.StageCode, op.OperationCode);

                    if (graph.Operations.ContainsKey(nodeKey))
                    {
                        // 同 (StageCode, OperationCode) 重复行 = MES 数据异常。
                        // 旧实现是**静默覆盖**（后者覆盖前者 ⇒ 边被错接）；此处改为保留首行 + 计数，不静默。
                        context.DuplicateOperationNodeKeyCount++;
                        continue;
                    }

                    graph.Operations[nodeKey] = new OperationNode
                    {
                        OperationCode = op.OperationCode,
                        OperationName = op.OperationName,
                        ProcessType = op.ProcessType,
                        StageCode = op.StageCode,
                        StandardDuration = op.StandardDuration,
                        // item1 接线（阶段二）：不再拷贝 op.SetupTime——v1.2 §1.2/§20.3 废止其运行真相地位，
                        // Setup 一律走 SetupTransitionRule 规则查找（SetupExactRules/SetupDefaultRules）。
                        TransferBatchSize = op.TransferBatchSize,
                        OperationPlanningMode = op.OperationPlanningMode ?? "FINITE_RESOURCE",
                        RouteCode = op.RouteCode,
                        PathId = op.PathId,
                        ProductionDepartmentId = op.ProductionDepartmentId
                    };
                }

                // 构建依赖边
                // ⚠ 契约 RoutingDependency **无 StageCode 字段**（只有 From/ToOperationCode + 单值
                //   ProductionDepartmentId）⇒ 边的 Stage 归属必须从本图节点反查消解。消解不唯一时
                //   计数并丢弃该边，**不猜**（0号位 §5.3「禁止半升级」+ 不静默原则）。
                var dependencies = routingDependencies
                    .Where(dep => dep.MaterialId == materialId
                                  && dep.RouteCode == pathKey.RouteCode
                                  && dep.PathId == pathKey.PathId)
                    .ToList();

                // 工序码 → 候选节点键索引（消解依赖端点用；同码跨 Stage 时 >1 个候选）
                var nodesByOperationCode = new Dictionary<string, List<OperationNodeKey>>();
                foreach (var nodeKey in graph.Operations.Keys)
                {
                    if (!nodesByOperationCode.TryGetValue(nodeKey.OperationCode, out var keyList))
                    {
                        keyList = new List<OperationNodeKey>();
                        nodesByOperationCode[nodeKey.OperationCode] = keyList;
                    }

                    keyList.Add(nodeKey);
                }

                foreach (var dep in dependencies)
                {
                    var fromKey = nodesByOperationCode.TryGetValue(dep.FromOperationCode, out var fromCandidates)
                        ? ResolveNodeKey(fromCandidates, k => graph.Operations[k].ProductionDepartmentId, dep.ProductionDepartmentId)
                        : null;

                    var toKey = nodesByOperationCode.TryGetValue(dep.ToOperationCode, out var toCandidates)
                        ? ResolveNodeKey(toCandidates, k => graph.Operations[k].ProductionDepartmentId, dep.ProductionDepartmentId)
                        : null;

                    if (fromKey == null || toKey == null)
                    {
                        context.UnresolvedDependencyEdgeCount++;
                        continue;
                    }

                    if (!graph.Dependencies.TryGetValue(toKey.Value, out var preds))
                    {
                        preds = new List<DependencyEdge>();
                        graph.Dependencies[toKey.Value] = preds;
                    }

                    preds.Add(new DependencyEdge
                    {
                        From = fromKey.Value,
                        To = toKey.Value,
                        DependencyType = dep.DependencyType,
                        LagTime = dep.LagTime
                    });
                }

                // 识别根工序（无前驱的工序）
                var allToOps = graph.Dependencies.Keys.ToHashSet();
                graph.RootOperations = graph.Operations.Keys
                    .Where(nodeKey => !allToOps.Contains(nodeKey))
                    .ToList();

                routeGraphs[pathKey] = graph;
            }

            context.RoutingGraphs[materialId] = routeGraphs;
        }
    }

    /// <summary>
    /// 从候选节点中消解出唯一节点键：
    ///   ① 唯一 → 直接取（跨 Stage 边的常规情形）；
    ///   ② 多个（2号位 实测 117 物料同码跨 Stage）→ 用部门区分（部门是「物料×阶段」联合属性）；
    ///   ③ 仍不唯一 → 返回 null，调用方计数丢弃，**不猜**。
    /// 契约 <c>RoutingDependency</c> 无 StageCode 字段（只有单值 ProductionDepartmentId），
    /// 故同码跨 Stage 时只能靠部门消解 —— 这是该契约下能做到的最强口径。
    /// </summary>
    private static OperationNodeKey? ResolveNodeKey(
        IReadOnlyList<OperationNodeKey> candidates,
        Func<OperationNodeKey, int> departmentOf,
        int departmentId)
    {
        if (candidates.Count == 0)
        {
            return null; // 悬空：端点不在本上下文
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        OperationNodeKey? byDepartment = null;
        foreach (var key in candidates)
        {
            if (departmentOf(key) != departmentId)
            {
                continue;
            }

            if (byDepartment != null)
            {
                return null; // 部门也不唯一 ⇒ 歧义，不猜
            }

            byDepartment = key;
        }

        return byDepartment; // null = 无法消解
    }

    /// <summary>
    /// 构建跨物料依赖 DAG（任务喂任务，方案A）：子先父后拓扑分层
    /// 文档：0号位 2026-09-10 裁决 方案A —— 跨物料时序作为硬约束在求解内实现
    ///
    /// 消费 request.MaterialRequirementLinks（父 ConsumerLogicalDemandKey → 子 ProducerLogicalDemandKey）
    /// 产出：
    ///   - context.CrossMaterialOrder：按「子层先、父层后」拍平的有序 DemandKey 列表（层内按 DemandSequence）
    ///   - context.CrossMaterialLayers：分层结构（每层一个 DemandKey 列表），供 Phase2 层内排序
    ///   - context.CrossMaterialHasCycle：是否检测到 BOM 依赖环（技术失败）
    /// 无 link（如子件全库存/全PI，不产 link）时：CrossMaterialOrder 保持原 DemandSequence 平铺顺序，零影响。
    /// </summary>
    private void BuildCrossMaterialDag(DomainSolveRequest request, ConstraintContext context)
    {
        var links = request.MaterialRequirementLinks;
        if (links == null || links.Count == 0)
        {
            // 无跨物料依赖：直接按 DemandSequence 平铺（等价于旧行为）
            context.CrossMaterialOrder = request.LogicalProductionDemands
                .OrderBy(d => d.DemandSequence)
                .Select(d => d.LogicalDemandKey)
                .ToList();
            context.CrossMaterialLayers = new List<List<string>> { context.CrossMaterialOrder };
            return;
        }

        // 参与拓扑的节点 = 所有 demand 的 LogicalDemandKey
        var allDemandKeys = request.LogicalProductionDemands
            .Select(d => d.LogicalDemandKey)
            .ToHashSet();

        // 邻接表：child → list<parent>（子先排，父后排）
        var childrenToParents = new Dictionary<string, List<string>>();
        var parentToChildren = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();
        foreach (var key in allDemandKeys)
        {
            inDegree[key] = 0;
        }

        foreach (var link in links)
        {
            // 0910 §二十 Case B：子件全库存 / 采购占位 → ProducerLogicalDemandKey = null
            //（2号位 20260924 方案 (a) 落码：link 仍产，但无子件生产身份）。
            // null 不进 DAG（不参与跨物料时序）；link 本身仍保留「父需求→子需求」真相。
            // 显式判空（而非依赖 Contains(null)==false）：语义更清楚，且消除 CS8604。
            var child = link.ProducerLogicalDemandKey;
            if (child is null)
            {
                continue;
            }

            // 只处理两端节点都在当前 Demand 集合内的 link（防御：忽略脏数据/域外引用）
            if (!allDemandKeys.Contains(child) ||
                !allDemandKeys.Contains(link.ConsumerLogicalDemandKey))
            {
                continue;
            }

            var parent = link.ConsumerLogicalDemandKey;

            if (!childrenToParents.TryGetValue(child, out var parents))
            {
                parents = new List<string>();
                childrenToParents[child] = parents;
            }
            parents.Add(parent);

            if (!parentToChildren.TryGetValue(parent, out var children))
            {
                children = new List<string>();
                parentToChildren[parent] = children;
            }
            children.Add(child);

            // P1-12：记录 父→子 边界的滞后时间（分钟），供 GetDynamicMaterialFloor 累加。
            context.CrossMaterialLagMinutes[(parent, child)] = link.LagMinutes;

            // 父的入度 = 它依赖的子件数
            inDegree[parent]++;
        }

        context.CrossMaterialParentToChildren = parentToChildren;

        // 层内按 DemandSequence 排序（保持 2号位 业务优先级）——索引只建一次
        var demandByKey = request.LogicalProductionDemands
            .ToDictionary(d => d.LogicalDemandKey);

        // Kahn 拓扑排序：入度为 0 的（不依赖任何子件的）先入队
        var queue = new Queue<string>();
        foreach (var key in allDemandKeys)
        {
            if (inDegree[key] == 0)
            {
                queue.Enqueue(key);
            }
        }

        var layers = new List<List<string>>();
        var ordered = new List<string>();
        var visited = 0;

        while (queue.Count > 0)
        {
            var layer = new List<string>(queue.Count);
            var nextLayer = new List<string>();

            foreach (var node in queue)
            {
                layer.Add(node);
                ordered.Add(node);
                visited++;

                if (childrenToParents.TryGetValue(node, out var parents))
                {
                    foreach (var parent in parents)
                    {
                        inDegree[parent]--;
                        if (inDegree[parent] == 0)
                        {
                            nextLayer.Add(parent);
                        }
                    }
                }
            }

            layer.Sort((a, b) => DemandSeq(a).CompareTo(DemandSeq(b)));
            nextLayer.Sort((a, b) => DemandSeq(a).CompareTo(DemandSeq(b)));

            layers.Add(layer);
            queue = new Queue<string>(nextLayer);
        }

        int DemandSeq(string key)
            => demandByKey.TryGetValue(key, out var d) ? d.DemandSequence : int.MaxValue;

        // 环检测：visited < 总节点数 → 存在环
        if (visited < allDemandKeys.Count)
        {
            context.CrossMaterialHasCycle = true;
            context.CrossMaterialOrder = ordered;
            context.CrossMaterialLayers = layers;
            return;
        }

        context.CrossMaterialOrder = ordered;
        context.CrossMaterialLayers = layers;
    }

    /// <summary>
    /// 构建工序资源资格映射（使用部门锁定后的 Eligibility）
    /// </summary>
    private void BuildOperationResourceEligibility(
        List<OperationResourceEligibility> eligibilities,
        ConstraintContext context)
    {
        // 按 EligibilityLookupKey(MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode) → ResourceId 列表（按 Priority 排序）
        // P0-01修复：使用冻结接口 OperationResourceEligibility，不再使用旧的 ResourceEligibility
        // 第4轮C1修复：索引加入MaterialId，避免不同物料共享资源资格
        // 0号位 2026-09-29 §5.3 落实：**补上 ProductionDepartmentId** —— 上游 lockedEligibilities 已按
        //   四元组（含部门）过滤，故此处以同一四元组分组才是自洽口径；旧三元键会把两个部门的同名工序
        //   资格合并成一份（跨部门串资源）。契约无 StageCode 字段，无法再细到 Stage（残留已在键类型上注明）。
        var eligibilityGroups = eligibilities
            .GroupBy(e => new EligibilityLookupKey(e.MaterialId, e.ProductionDepartmentId, e.RouteCode, e.PathId, e.OperationCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.Priority)
                      .Select(e => e.ResourceId)
                      .ToList()
            );

        context.OperationResourceEligibility = eligibilityGroups;

        // P0-04修复：同时构建 ResourceCapacityFactors 映射
        // 第4轮C1修复：索引加入MaterialId；本次再补部门（同上）
        // (EligibilityLookupKey, ResourceId) → CapacityFactor
        var capacityFactors = eligibilities
            .GroupBy(e => new EligibilityLookupKey(e.MaterialId, e.ProductionDepartmentId, e.RouteCode, e.PathId, e.OperationCode))
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
    /// 构建资源编码映射（ResourceId → ResourceCode）
    /// P1-08修复：供 Phase2/Phase4 生成 FinalTaskDraft 时回填 ResourceCode。
    /// </summary>
    private void BuildResourceCodes(DomainSolveRequest request, ConstraintContext context)
    {
        context.ResourceCodes = request.Resources
            .GroupBy(r => r.ResourceId)
            .ToDictionary(g => g.Key, g => g.First().ResourceCode);

        // P1-02（BottleneckMode 锚点）：反向映射 Code → ResourceId，供 Phase3 消费 AnchorResourceCode 反查。
        // 空编码（string.Empty）跳过；编码重复时取首个 ResourceId（业务编码应唯一，防御 GroupBy）。
        context.ResourceIdsByCode = request.Resources
            .Where(r => !string.IsNullOrEmpty(r.ResourceCode))
            .GroupBy(r => r.ResourceCode)
            .ToDictionary(g => g.Key, g => g.First().ResourceId);
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
        // P1-07：复合键。同一 LogicalDemand 多操作锚点（多 ExecutionConstraint）不再因重复 DraftId 抛异常；
        //   StageCode / OperationCode 归一化为 string.Empty（2号位 口径：恒非空）。
        // P0-02（0号位 2026-10-09《APS_V1_2_20261009.md》§三）：键域**必须含 StageCode** ——
        //   已生效工序身份规则要求 StageCode 与 OperationCode **共同**定位工序，不得假设 OperationCode 全局唯一。
        //   并改**显式循环 + 重复检测**（`ToDictionary` 的重复 Key 抛的是未受控 ArgumentException）：
        //   同一三元键出现两条 ⇒ 输入契约无法唯一标识锚点 ⇒ **不得静默抹除锚点**，须受控 Fail Closed
        //   （抛 `SolverInputContractException`，由 `FiniteCapacitySolver.SolveAsync` 捕获）。
        var lockedTasks = new Dictionary<(string DraftId, string StageCode, string OperationCode), LockedTaskConstraint>();
        foreach (var ec in request.ExecutionConstraints)
        {
            var lockedKey = (ec.DraftId, ec.StageCode ?? string.Empty, ec.OperationCode ?? string.Empty);
            if (lockedTasks.ContainsKey(lockedKey))
            {
                throw new SolverInputContractException(
                    "锁定约束键域冲突：同一 (DraftId, StageCode, OperationCode) 出现多条 ExecutionConstraint ⇒ " +
                    $"无法唯一标识锚点，不得静默抹除。DraftId={ec.DraftId}, " +
                    $"StageCode={ec.StageCode ?? "(null)"}, OperationCode={ec.OperationCode ?? "(null)"}");
            }

            lockedTasks[lockedKey] = new LockedTaskConstraint
            {
                DraftId = ec.DraftId,
                ResourceId = ec.ResourceId,
                LockedStart = ec.LockedStart,
                LockedEnd = ec.LockedEnd,
                ConstraintType = ec.ConstraintType,
                StageCode = ec.StageCode,
                OperationCode = ec.OperationCode,
                LockedQuantity = ec.LockedQuantity,
                LockedNetOutputQty = ec.LockedNetOutputQty,
                LockedPlannedProcessQty = ec.LockedPlannedProcessQty,
                TaskKey = ec.TaskKey
            };
        }

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

    /// <summary>
    /// P1-02 item1 接线（阶段一）：装载第⑦块冻结 Setup 换型规则 → 运行时 EXACT/DEFAULT 查找字典。
    /// 数据链路（0号位 裁决项2）：RuleSetVersion 已发布规则 → 本 Run 一次性 FrozenStrategySnapshot.SetupTransitionRules
    /// → 2号位 按 Domain（Dept+Stage）裁剪投影进 SolverStrategySnapshot（r13392）→ 此处经
    /// SetupOptimizer.BuildRuleLookups 适配（EXACT/DEFAULT 按 RuleType 分流、无效行防御跳过、同键取第一条不随机）。
    /// Solver 运行中不查 3号位 规则库；规则为空 = 无规则（三层命中按 0 分钟 + 追踪兜底）。
    /// </summary>
    private void BuildSetupTransitionRules(DomainSolveRequest request, ConstraintContext context)
    {
        var (exact, defaults) = SetupOptimizer.BuildRuleLookups(request.StrategySnapshot?.SetupTransitionRules);
        context.SetupExactRules = exact;
        context.SetupDefaultRules = defaults;
    }

    /// <summary>
    /// P0-01 接线：装载第⑧块冻结 Batch Policy 规则 → <see cref="ConstraintContext.ExecutionBatchPolicies"/>。
    ///
    /// 数据链路：`TaskSplitRuleConfig`（3号位 治理）→ 本 Run 一次性 `FrozenStrategySnapshot.BatchPolicies`
    ///   → 2号位 投影进 `SolverStrategySnapshot.BatchPolicies`（1↔2 契约，0号位 (7).md §十）
    ///   → 此处**整块逐字收进** `ConstraintContext`（**不在装载层裁剪、不按 Domain 塌成单值**）
    ///   → `PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy` 按 `(MaterialId, Dept)` **逐需求**解析。
    ///
    /// **不得**在装载层做「同键合并 / 取第一条」等裁剪：键域语义（精确命中 + Material 级默认回落）
    ///   由解析侧 `ResolveExecutionBatchPolicy` 统一负责，装载层只做**保真搬运**。
    /// 空（`StrategySnapshot` 为 null 或 ⑧块为空）⇒ 策略集为空 ⇒ 每需求恒 1 批，行为与旧版**逐字一致**。
    /// </summary>
    private static void BuildExecutionBatchPolicies(DomainSolveRequest request, ConstraintContext context)
    {
        var policies = request.StrategySnapshot?.BatchPolicies;
        if (policies is null || policies.Count == 0)
        {
            context.ExecutionBatchPolicies = new List<LPS.APS.Core.Dto.BatchPolicyRuleSnapshot>();
            return;
        }

        context.ExecutionBatchPolicies = new List<LPS.APS.Core.Dto.BatchPolicyRuleSnapshot>(policies);
    }

    /// <summary>
    /// M5 第一批：装载 RunScope 投影（Run 级交期覆盖 + Task 软目标）成内存词典。
    /// 键体系已由 2号位 转为内存键（DueDateOverrides=LogicalDemandKey；TaskTargetOverrides=(DraftId, OperationCode)）。
    /// null/空 RunScope = FULL 语义 → 两词典均空，现有行为零改变（向后兼容）。
    /// </summary>
    private void BuildRunScope(DomainSolveRequest request, ConstraintContext context)
    {
        var scope = request.RunScope;
        if (scope == null) return;

        foreach (var o in scope.DueDateOverrides)
        {
            if (!string.IsNullOrEmpty(o.LogicalDemandKey))
                context.DueDateOverrides[o.LogicalDemandKey] = o.ManualTargetDueDate;
        }

        foreach (var t in scope.TaskTargetOverrides)
        {
            if (!string.IsNullOrEmpty(t.DraftId) && !string.IsNullOrEmpty(t.OperationCode))
                context.TaskTargetOverrides[(t.DraftId, t.OperationCode)] = t.TargetTime;
        }
    }
}

/// <summary>
/// 硬约束上下文（Phase 1 输出）
/// </summary>
internal class ConstraintContext
{
    /// <summary>
    /// 工序依赖图：MaterialId → <see cref="RoutePathKey"/>(RouteCode, PathId) → 工序依赖关系。
    /// 键升维依据见 <see cref="RoutePathKey"/>（v1.6 新增要求 + Q1「禁止跨 Path 连边」）。
    /// 旧键为 RouteCode 单键 ⇒ V2 多路径时把互斥备选路径并成一张图（跨 Path 连边）。
    /// </summary>
    public Dictionary<int, Dictionary<RoutePathKey, RoutingGraph>> RoutingGraphs { get; set; } = new();

    /// <summary>
    /// V1 单路径解析：取该物料**唯一**一条 (RouteCode, PathId) 的 Routing 图。
    /// ⚠ **2026-10-07 起口径收窄**：`LogicalProductionDemand` 已补 `RouteCode`/`PathId` 契约字段
    ///   （v1.6 §新增/替换实施要求 + 0号位 裁决 §六 授权落 Core），需求**已能自述走哪条路径**
    ///   ⇒ 常规调用应改用 <see cref="TryGetRoutingGraph"/> / <see cref="TryGetDemandRoutingGraph"/>。
    ///   本方法**仅保留两处兜底**（均要求「唯一」才命中，**任何情况下都不猜**，与 Q-3 红线不冲突）：
    ///     · 需求**未**声明固定路径、且该物料在本请求内**只有一条**路径时直接取用；
    ///     · 锁定任务继承时的路径身份反查（需求无固定路径时）。
    ///   · 0 条 → false（无 Routing，调用方按既有语义处理）；
    ///   · 1 条 → 命中（与升维前 <c>TryGetValue("DEFAULT")</c> 行为等价）；
    ///   · &gt;1 条 → **false 且登记**（多路径必须由需求固定路径或 C 桶候选择优定夺，
    ///     此处**不猜**、不取 First()，与全仓「不静默」原则一致）。
    /// </summary>
    public bool TryGetSingleRoutingGraph(int materialId, [NotNullWhen(true)] out RoutingGraph? graph)
    {
        graph = null;
        if (!RoutingGraphs.TryGetValue(materialId, out var byPath) || byPath.Count == 0)
        {
            return false;
        }

        if (byPath.Count == 1)
        {
            graph = byPath.Values.First();
            return true;
        }

        AmbiguousRoutingPathMaterialIds.Add(materialId);
        return false;
    }

    /// <summary>
    /// 【Path-aware 解析（0号位 2026-10-07 裁决 Q-3，**必须整改**）】按**给定的** (RouteCode, PathId)
    /// 精确取图，不再假设「一物料一图」。
    ///
    /// 任务级调用点一律用**任务自身**的 (RouteCode, PathId) 调用本方法：已有 FinalTask 却按 MaterialId
    /// 取「物料唯一图」是旧的单路径假设，多 Path 下会**串 Path**（拿另一条备选路径的前驱/后继边接本任务工序）。
    ///
    /// **Fail Closed（0号位 2026-10-07 裁决 §三 新增红线）**：routeCode / pathId 任一缺失 ⇒ 返回 false，
    ///   **不得**退回 <see cref="TryGetSingleRoutingGraph"/> 按物料猜唯一 Path，也不得跨 Path 找替代节点。
    ///   调用方按既有语义处理（跳过该项判定 / 该需求 Unscheduled）。
    /// </summary>
    public bool TryGetRoutingGraph(
        int materialId,
        string? routeCode,
        long? pathId,
        [NotNullWhen(true)] out RoutingGraph? graph)
    {
        graph = null;

        // Fail Closed：缺路径身份不猜（空串不是合法 RouteCode）。
        // pathId 用 long? 承载（FinalTaskDraft.PathId 为 long?），越 int 域视为非法身份 ⇒ 同样不猜。
        if (string.IsNullOrEmpty(routeCode) || pathId is null
            || pathId.Value < int.MinValue || pathId.Value > int.MaxValue)
        {
            return false;
        }

        if (!RoutingGraphs.TryGetValue(materialId, out var byPath) || byPath.Count == 0)
        {
            return false;
        }

        return byPath.TryGetValue(RoutePathKey.Of(routeCode, (int)pathId.Value), out graph);
    }

    /// <summary>
    /// 取该物料在本次请求内的**全部**候选路径（(RouteCode, PathId) → 图），按 (RouteCode, PathId) 升序 ——
    /// 顺序确定、可重放（C桶候选内择优要求确定性）。无路径 ⇒ false。
    /// </summary>
    public bool TryGetRoutingGraphs(
        int materialId,
        [NotNullWhen(true)] out List<KeyValuePair<RoutePathKey, RoutingGraph>>? candidates)
    {
        candidates = null;
        if (!RoutingGraphs.TryGetValue(materialId, out var byPath) || byPath.Count == 0)
        {
            return false;
        }

        candidates = byPath
            .OrderBy(kv => kv.Key.RouteCode, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.PathId)
            .ToList();
        return true;
    }

    /// <summary>
    /// 需求 → **已选中路径** 登记表（Scheduling 内部，不动 Core）。
    ///
    /// Phase2 需求级选路后登记；Phase4 局部修复 / Phase5 各需求级消费点复用。
    /// 依据 RT-002「A/B 固定真实 RouteCode + PathId，**局部修复不得换路径**」——
    /// 局部修复**不得重选路径**，只能复用 Phase2 的选中结果。
    /// 未登记（单 Path 退化 / 需求自带固定路径）⇒ 调用方回落需求自身 RouteCode/PathId 或唯一那条。
    /// </summary>
    public Dictionary<string, RoutePathKey> ChosenRoutePaths { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// **批键 → 本批选中路径键**（P0-03 升维，0号位 (7).md §六/§七）。
    ///
    /// 为什么必须升维：`ChosenRoutePaths` 是「**需求** → 单一路径键」的单值表，**无法承载**多批各自选路
    ///   （`D1 → Batch-001 走 RouteA`、`Batch-002 走 RouteB` 完全合法，RT-003/RT-004）。
    ///   本表按**执行批**登记，Phase4 局部修复据此**逐批**取图 —— 不串批、不重选路径（RT-002）。
    ///
    /// 登记时机：Phase2 步骤 4 落定后**每批**登记（单批亦登记，与 `ChosenRoutePaths` 同值 ⇒ 行为零回归）。
    /// 未登记（该批未及落定）⇒ 调用方回落需求固定路径 → 唯一那条（同 <see cref="TryGetDemandRoutingGraph"/>）。
    /// </summary>
    public Dictionary<string, RoutePathKey> ChosenBatchRoutePaths { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// **需求 → 本 Run 该需求的执行批清单（批键 + 本批数量）**（P0-03）。
    ///
    /// Phase4 局部修复的基本单位 = **执行批**（不是 `LogicalProductionDemand`）：据此**逐批**重建，
    ///   **不得**用整份需求数量重建，**不得**把 Batch-002 的修复结果写回 Batch-001 的键。
    /// Phase2 `FormExecutionBatches` 产出后**在需求循环开头即登记**（先于试排/择优/落定）。
    /// 空（未登记）⇒ 该需求按**单批**回落（键 = `EB|{需求键}|001`，数量 = 需求数量）—— 与升维前逐字一致。
    /// </summary>
    public Dictionary<string, List<ExecutionBatchPlanEntry>> ExecutionBatchPlans { get; set; }
        = new(StringComparer.Ordinal);

    /// <summary>
    /// 需求 → **本 Run 该需求的全部执行批归批键**（Scheduling 内部，不动 Core）。
    ///
    /// Phase2 `FormExecutionBatches` 产出后**在需求循环开头即登记**（先于试排/择优/落定），
    /// Phase4 **逐批**修复（`PhaseFourLocalRepair.ExpandRepairUnits` 读本表 + <see cref="ExecutionBatchPlans"/>）
    /// 按**本批键**重建 —— 保证「局部修复不得把同一执行批劈成两个键」，
    /// 且不得把 Batch-002 写回 Batch-001 的键（P0-03；原 `ResolveExecutionBatchKeyForRebuild` 已删除）。
    /// </summary>
    public Dictionary<string, List<string>> ExecutionBatchDraftKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 需求 → **本 Run 该需求自决后的排程方向**（`LogicalDemandKey` → `FORWARD`/`BACKWARD`/`MIXED`）。
    ///
    /// Phase2 在**本需求进入批/路由择优之前**用 `SchedulingDirectionResolver` 解析一次并登记
    ///   （P1-01 整改：同一 ResolvedDirection 贯穿 试排/候选比较/落定）；Phase5 的空隙压实与
    ///   Setup 序列优化据此判定「本需求是否 FORWARD」，**不再使用 Run 级原始策略值**。
    ///
    /// ⚠ 为什么必须登记而不是在 Phase5 重算：Phase2 的解析输入含**本需求的候选工序集**与
    ///   `dynamicMaterialFloor`（试排上下文）；Phase5 已无该上下文 ⇒ 重算会**漂移**。登记 = 一次解析、全链一致。
    ///
    /// Run 级 `FORWARD`/`BACKWARD` ⇒ 全量同向（Phase2 不解析、直接取原值）⇒ Phase5 行为与整改前**逐字一致**；
    /// Run 级 `AUTO` ⇒ 逐需求方向不同 ⇒ Phase5 得以按需求粒度门控（整改前 Phase5 见到 `AUTO` 恒跳过两项优化）。
    /// 未登记（旁路/早期 continue 的需求）⇒ 消费侧回落 Run 级原值（保守，与整改前一致）。
    /// </summary>
    public Dictionary<string, string> ResolvedDirections { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// **执行批键 → 该批实际落定所用方向**（NEW-P1-01，0号位 2026-10-09《APS_V1_3_20261009.md》§三）。
    ///
    /// 【为什么单靠 <see cref="ResolvedDirections"/> 不够】
    ///   冻结模型是「**每个 Execution Batch 独立**做 Direction + RoutingCandidate + Resource + Calendar + Setup
    ///   联合求解」⇒ 同一 `LogicalDemand` 的不同执行批**可以各自落定到不同方向**（例如 Batch-001 选中长 Lead 的
    ///   Path 自决 BACKWARD，Batch-002 选中短 Lead 的 Path 自决 FORWARD）。
    ///   而 <see cref="ResolvedDirections"/> 是**需求级单值**，只能承载「最后一个落定批」的方向
    ///   ⇒ 用它驱动 Phase5 会**把某一批的方向套到全需求**（复审判词：「多批方向被需求级单值覆盖」）。
    ///
    /// 【消费口径】Phase5 先按 **Task 自己的 `ExecutionBatchDraftKey`** 查本表；未命中才回落
    ///   <see cref="ResolvedDirections"/>（需求级，兼容旁路/单批），再回落 Run 级原值。
    ///   回调顺序保证**零回归**：Run 级 `FORWARD`/`BACKWARD` ⇒ 两级登记的均是该原值；
    ///   单批需求 ⇒ 批级与需求级同值。
    /// </summary>
    public Dictionary<string, string> ResolvedBatchDirections { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// ⑧块 Batch Policy —— **按 `(MaterialId, ProductionDepartmentId?)` 键控的策略集**
    /// （P0-01 整改，0号位 2026-10-07《未命名的Markdown文件 (7).md》§四）。
    ///
    /// **为什么必须是集合而不是单值**：正式业务粒度 = **Material + ProductionDepartment**（B-001；
    ///   `TaskSplitRuleConfig` / `BatchPolicyRuleSnapshot` 同口径）。一个 Domain 内
    ///   `Material A + Dept X`、`Material A + Dept Y`、`Material B + Dept X` 各有不同策略是**正常的**；
    ///   用单值代表整个 Domain 会让不同物料/部门**错误共用批量硬约束**，直接改变排程业务结果。
    ///
    /// **解析**：<see cref="PhaseTwoInitialScheduler.ResolveExecutionBatchPolicy"/> 按需求
    ///   `(MaterialId, StartStageCode → ProductionDepartmentId)` 精确命中；未命中回落
    ///   `(MaterialId, ProductionDepartmentId == null)` 的 **Material 级默认**（该默认语义由 ⑧块 DTO 自身承载）；
    ///   仍无命中 ⇒ **缺策略**。⚠ P0-01（0号位 2026-10-08 §四）后缺策略**不再**等于「不拆、恒 1 批」：
    ///   C 桶需求一律判 `BATCH_POLICY_MISSING` 并 **Fail Closed**（不产 FinalTask、不进 Phase4）。
    ///   正式兜底链第③级「Global Batch Default」由 2/3号位 在上游**投影成有效 Policy** 后再交 1号位，
    ///   1号位 不得自行展开该级、更不得把末级 `null` 解释成「单批合法」。
    ///
    /// **载体**：`SolverStrategySnapshot.BatchPolicies`（⑧块，1↔2 契约，0号位 (7).md §十/§十一）⇒
    ///   `PhaseOneConstraintBuilder.BuildExecutionBatchPolicies` **整块逐字**收进本集合（**不在装载层裁剪**，
    ///   键域解析统一由 `ResolveExecutionBatchPolicy` 负责）。
    ///   空集合 ⇒ 每个 C 桶需求均判 `BATCH_POLICY_MISSING`（Fail Closed）。
    /// </summary>
    public List<LPS.APS.Core.Dto.BatchPolicyRuleSnapshot> ExecutionBatchPolicies { get; set; } = new();

    /// <summary>
    /// 需求级取图（Phase4 / Phase5 消费点用），解析顺序：
    ///   ① **Phase2 已登记选中路径**（<see cref="ChosenRoutePaths"/>）—— RT-002「局部修复不得换路径」，**优先且不重选**；
    ///   ② 需求自带固定路径（A/B，<c>LogicalProductionDemand.RouteCode/PathId</c>）；
    ///   ③ 唯一那条（单路径退化，行为与升维前等价）；多路径且未登记 ⇒ **false，不猜**。
    /// </summary>
    public bool TryGetDemandRoutingGraph(
        string logicalDemandKey,
        int materialId,
        string? demandRouteCode,
        int? demandPathId,
        [NotNullWhen(true)] out RoutingGraph? graph)
    {
        graph = null;

        if (ChosenRoutePaths.TryGetValue(logicalDemandKey, out var chosen))
        {
            return RoutingGraphs.TryGetValue(materialId, out var byPath)
                   && byPath.TryGetValue(chosen, out graph);
        }

        if (TryGetRoutingGraph(materialId, demandRouteCode, demandPathId, out graph))
        {
            return true;
        }

        return TryGetSingleRoutingGraph(materialId, out graph);
    }

    /// <summary>
    /// **批级取图**（P0-03，Phase4 局部修复用）。解析顺序：
    ///   ① **本批已登记选中路径**（<see cref="ChosenBatchRoutePaths"/>）—— RT-002「局部修复不得换路径」，优先且不重选；
    ///   ② 需求自带固定路径（A/B，`RouteCode/PathId`）；
    ///   ③ 唯一那条；多路径且未登记 ⇒ **false，不猜**（Fail Closed）。
    ///
    /// 与 <see cref="TryGetDemandRoutingGraph"/> 的唯一差别是①的键：本方法按**批键**，后者按**需求键**。
    /// </summary>
    public bool TryGetBatchRoutingGraph(
        string batchKey,
        int materialId,
        string? demandRouteCode,
        int? demandPathId,
        [NotNullWhen(true)] out RoutingGraph? graph)
    {
        graph = null;

        if (ChosenBatchRoutePaths.TryGetValue(batchKey, out var chosen))
        {
            return RoutingGraphs.TryGetValue(materialId, out var byPath)
                   && byPath.TryGetValue(chosen, out graph);
        }

        if (TryGetRoutingGraph(materialId, demandRouteCode, demandPathId, out graph))
        {
            return true;
        }

        return TryGetSingleRoutingGraph(materialId, out graph);
    }

    /// <summary>
    /// 需求级取**路径键**（Calendar 判据 Path 隔离用，P1-04）。解析顺序与
    /// <see cref="TryGetDemandRoutingGraph"/> 完全一致：选中路径 → 需求固定路径 → 唯一那条。
    /// 多路径且未登记 ⇒ false（不猜）。
    /// </summary>
    public bool TryGetDemandRoutePath(
        string logicalDemandKey,
        int materialId,
        string? demandRouteCode,
        int? demandPathId,
        out RoutePathKey pathKey)
    {
        pathKey = default;

        if (ChosenRoutePaths.TryGetValue(logicalDemandKey, out var chosen))
        {
            pathKey = chosen;
            return true;
        }

        if (!string.IsNullOrEmpty(demandRouteCode) && demandPathId is not null)
        {
            pathKey = RoutePathKey.Of(demandRouteCode, demandPathId.Value);
            return true;
        }

        if (RoutingGraphs.TryGetValue(materialId, out var byPath) && byPath.Count == 1)
        {
            pathKey = byPath.Keys.First();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 同一物料存在 &gt;1 条 (RouteCode, PathId) 路径、而需求侧无路径选择载体的物料集合。
    /// V1 装载层归一化为单路径时恒为空；Q1 停用归一化后若出现，即 V2 多路径选路缺口的可见信号。
    /// </summary>
    public HashSet<int> AmbiguousRoutingPathMaterialIds { get; set; } = new();

    /// <summary>
    /// 本次求解范围内的可达 Stage：MaterialId → StageCode 集合（Phase1 <c>BuildReachableStages</c> 产出）。
    ///
    /// 【为什么需要】0号位 2026-09-29 裁决 §十四第4项：无 Routing 的 Stage 要建纯内存时间节点。
    ///   判定「哪些 Stage 在本需求范围内、但没有任何 RoutingOperation」必须用**本求解范围**，
    ///   不能用全量 BOM StagePath —— 否则会把 StartOperation/StartStage 之前已完成、
    ///   本次根本不求解的 Stage 也算进来，凭空多出提前期。
    ///   （该集合原为 <c>BuildReachableStages</c> 的局部变量，P1-06 起只用于部门缺失判定；
    ///   此处挂到 Context 上供 Phase5 复用，**语义不变、不改任何既有判定**。）
    /// </summary>
    public Dictionary<int, HashSet<string>> EffectiveStages { get; set; } = new();

    /// <summary>
    /// 工序资源资格：<see cref="EligibilityLookupKey"/> → 合法 ResourceId 列表（按优先级排序）。
    /// ⚠ 旧键为字符串 <c>"{MaterialId}::{RouteCode}::{OperationCode}"</c> —— **缺部门维度**，
    /// 会把「同物料同工序码但分属不同部门」的两套资格**合并成一份** ⇒ 跨部门串资源。
    /// 现键补上 ProductionDepartmentId，与部门锁定谓词（<c>validOperationKeys</c>）同口径。
    /// 残留：契约 <c>OperationResourceEligibility</c> 无 StageCode 字段，无法再细到 Stage（见 <see cref="EligibilityLookupKey"/> 注释）。
    /// </summary>
    public Dictionary<EligibilityLookupKey, List<int>> OperationResourceEligibility { get; set; } = new();

    /// <summary>
    /// P0-04修复：工序资源产能系数映射：(<see cref="EligibilityLookupKey"/>, ResourceId) → CapacityFactor
    /// 用于Duration计算：StandardDuration × PlannedProcessQty ÷ CapacityFactor
    /// </summary>
    public Dictionary<EligibilityLookupKey, Dictionary<int, decimal>> ResourceCapacityFactors { get; set; } = new();

    /// <summary>
    /// 资源编码映射：ResourceId → ResourceCode（源 ResourceDefinition.ResourceCode）。
    /// P1-08修复：供 Phase2/Phase4 生成 FinalTaskDraft 时回填 ResourceCode，不再留空。
    /// </summary>
    public Dictionary<int, string> ResourceCodes { get; set; } = new();

    /// <summary>
    /// 资源编码反向映射：ResourceCode → ResourceId。
    /// P1-02（BottleneckMode 锚点）：供 Phase3 消费 AnchorResourceCode（业务编码）反查 ResourceId。
    /// 空编码（string.Empty）不纳入，避免无效键。
    /// </summary>
    public Dictionary<string, int> ResourceIdsByCode { get; set; } = new();

    /// <summary>
    /// Setup EXACT 换型规则（P1-02 item1 接线）：(当前工序, 当前设备, 前产品, 后产品) → 换型分钟。
    /// 方向性键（A→B ≠ B→A）；源 = 第⑦块 SetupTransitionRules（2号位 按 Domain 裁剪投影，r13392）。
    /// </summary>
    public Dictionary<SetupOptimizer.SetupExactKey, decimal> SetupExactRules { get; set; } = new();

    /// <summary>Setup DEFAULT 换型规则：(当前工序, 当前设备) → 默认换型分钟（同源；EXACT 未命中时第二优先）。</summary>
    public Dictionary<SetupOptimizer.SetupDefaultKey, decimal> SetupDefaultRules { get; set; } = new();

    /// <summary>
    /// 资源级已排产品时间线（item1 接线阶段二）：FromMaterial = 时间邻接的上一 Task 产品。
    /// Phase2 Schedule 入口以锁定继承 Task 重置种子，放置/移动/合并时与 resourceOccupancy 成对维护；
    /// Phase4 兜底重跑 Schedule 时自动重置。
    /// </summary>
    public ResourceProductTimeline ProductTimeline { get; set; } = new();

    /// <summary>
    /// M5 第一批：Run 级交期覆盖 LogicalDemandKey → ManualTargetDueDate（OrderCanonicalId 已由 2号位 按需求展开，
    /// 仅非空手工目标交期行投影；空 = 无覆盖）。Phase2/3/5 经 <see cref="EffectiveDue"/> 统一取「覆盖 ?? RequiredAvailableTime」。
    /// </summary>
    public Dictionary<string, DateTime> DueDateOverrides { get; set; } = new();

    /// <summary>M5 第一批：Task 软目标 (DraftId=SourceDraftId, OperationCode) → TargetTime（软偏好，非硬约束，未达走决策说明）。</summary>
    public Dictionary<(string DraftId, string OperationCode), DateTime> TaskTargetOverrides { get; set; } = new();

    /// <summary>
    /// B.2 求解过程追溯收集器：各阶段产出的「决策说明」（非排程结果，**不进 ReasonCode 体系** —— 0号位 Q4 裁决）。
    /// Phase2/4 产出，Phase5 经 <c>DomainSolveResult.SolveTraceNotes</c> 导出，2号位 原样落库/透传。
    /// 载体 = 2号位 r13494 落地的 <see cref="SolveTraceNote"/>；本收集器由 Phase 1 建立、贯穿 Phase 2–5。
    /// 只承载「求解过程决策上下文」，不承载排程硬结果 —— 排程事实走 FinalTasks/ExplanationFacts。
    /// </summary>
    public List<SolveTraceNote> TraceNotes { get; set; } = new();

    /// <summary>demand 的有效交期 = Run 级覆盖（ManualTargetDueDate）?? 正式 RequiredAvailableTime（M5 第一批统一换用点）。</summary>
    public DateTime EffectiveDue(LogicalProductionDemand demand)
        => DueDateOverrides.TryGetValue(demand.LogicalDemandKey, out var due) ? due : demand.RequiredAvailableTime;

    /// <summary>
    /// 资源日历：ResourceId → 可用时间窗列表（已排序）
    /// </summary>
    public Dictionary<int, List<TimeWindow>> ResourceCalendars { get; set; } = new();

    /// <summary>
    /// 物料多段可用性：AllocationSequence → Quantity-Time 分段列表（已按时间排序）
    /// </summary>
    public Dictionary<long, List<MaterialAvailabilitySegment>> MaterialAvailability { get; set; } = new();

    /// <summary>
    /// 锁定任务约束：复合键 <c>(DraftId, StageCode, OperationCode)</c> → 锁定信息。
    ///
    /// · P1-07：同一 LogicalDemand 可有多操作锚点 ⇒ 不能再用 DraftId 单键。
    /// · P0-02（0号位 2026-10-09 第四轮复审《APS_V1_2_20261009.md》§三）：**必须含 StageCode**。
    ///   已生效的工序身份规则要求 `StageCode` 与 `OperationCode` **共同**定位工序，不得假设
    ///   `OperationCode` 全局唯一；否则「同 DraftId、Stage A/OP10 与 Stage B/OP10 同时锁定」
    ///   会在建字典阶段抛重复 Key 异常。此前仅二维键 ⇒ 键域不足。
    /// </summary>
    public Dictionary<(string DraftId, string StageCode, string OperationCode), LockedTaskConstraint> LockedTasks { get; set; } = new();

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

    /// <summary>
    /// 0号位 2026-09-29 裁决 §5.3 落实（不静默）：同 (StageCode, OperationCode) 的 RoutingOperation 重复行计数。
    /// 旧实现为静默覆盖（后者覆盖前者 ⇒ 边被错接、一道工序静默丢失）。保留首行 + 计数，供 Phase3 出诊断。
    /// </summary>
    public int DuplicateOperationNodeKeyCount { get; set; }

    /// <summary>
    /// 0号位 2026-09-29 裁决 §5.3 落实（不静默）：<c>RoutingDependency</c> 端点 Stage 归属**无法消解**的边计数。
    /// 成因：契约 RoutingDependency 无 StageCode 字段，而该工序码在同物料内跨多个 Stage 且部门也无法区分
    /// （2号位 实测 117 物料同码跨 Stage）。此类边被丢弃并计数，**不猜**（不静默塞给任一 Stage）。
    /// </summary>
    public int UnresolvedDependencyEdgeCount { get; set; }

    /// <summary>
    /// 跨物料依赖（任务喂任务，方案A）：子先父后的有序 LogicalDemandKey 列表（已按层拍平）。
    /// 无跨物料 link 时等价于按 DemandSequence 平铺。
    /// </summary>
    public List<string> CrossMaterialOrder { get; set; } = new();

    /// <summary>
    /// 跨物料依赖分层：每层一个 LogicalDemandKey 列表（子件层在前，父件层在后）。
    /// </summary>
    public List<List<string>> CrossMaterialLayers { get; set; } = new();

    /// <summary>
    /// 父 → 子 映射（父 LogicalDemandKey → 其直接子件 LogicalDemandKey 列表）。
    /// 供 Phase2 在排父件时取子件的真实完成时间（动态物料可用时间合并）。
    /// </summary>
    public Dictionary<string, List<string>> CrossMaterialParentToChildren { get; set; } = new();

    /// <summary>
    /// P1-12：父 → (父, 子) 边界的滞后时间（分钟）。键为 (父 LogicalDemandKey, 子 LogicalDemandKey)。
    /// 供 Phase2/Phase4 的 GetDynamicMaterialFloor 在子件完成时间上累加滞后。
    /// </summary>
    public Dictionary<(string, string), decimal> CrossMaterialLagMinutes { get; set; } = new();

    /// <summary>
    /// 跨物料 BOM 依赖存在环（技术失败标记）。
    /// </summary>
    public bool CrossMaterialHasCycle { get; set; } = false;

    /// <summary>
    /// B-1（0号位 2026-09-29 裁决 §二）落实：StageSeq 全序链冲突的 MaterialId 集合。
    ///
    /// 判定口径（逐字对齐裁决 §2.1/§2.2）：唯一性范围 = **一个已解析好的具体 StageSequenceChain 内**，
    /// 不是 Material 全局。同 StageCode 取最小 StageSeq 收敛后，**不同 StageCode 不得拥有相同 StageSeq**。
    /// 出现即登记本集合 ⇒ 该物料的需求**不生成正式计划**（§2.3：不允许静默按数组顺序、不允许静默并行、
    /// 必须输出明确 Issue）。下游 Reason = <c>STAGE_SEQUENCE_CONFLICT</c>。
    /// </summary>
    public HashSet<int> StageSequenceConflictMaterialIds { get; set; } = new();
}

/// <summary>
/// 工序节点身份：**(StageCode, OperationCode)**。
/// 依据：0号位 2026-09-29 裁决《Stage顺序、无Routing Stage、Operation标识与Task/MES粒度》§5.1/§5.2 逐字——
/// 「OperationCode 不是全局唯一业务身份。同一个物料不同Stage可能出现相同OperationCode」
/// 「在一个已经限定 Material / ProductionDepartment / Route / Path 的 Routing 图内部，最小节点键采用 (StageCode, OperationCode)」。
/// 唯一性范围 = 单个已限定上下文的 Routing 图内部（**不是 Material 全局**）。
/// ⚠ 禁止再退回 OperationCode 单键（裁决 §5.3 明文「不能出现 Phase1 用 (Stage,Operation)、Phase2 又退回 OperationCode 这种半升级」）。
/// </summary>
internal readonly record struct OperationNodeKey(string StageCode, string OperationCode)
{
    /// <summary>空安全构造：null 归一化为 string.Empty（与既有 2号位 口径「OperationCode 恒非空」一致）。</summary>
    public static OperationNodeKey Of(string? stageCode, string? operationCode)
        => new(stageCode ?? string.Empty, operationCode ?? string.Empty);

    public override string ToString() => $"{StageCode}::{OperationCode}";
}

/// <summary>
/// Routing 路径身份：**(RouteCode, PathId)**。
/// 依据：《APS_V1_1号位有限产能排程开发实施包 v1.6》新增实施要求 + 本轮统一执行红线 Q1——
/// 「PathId 必须进入 Graph/Dependency/Eligibility 节点和匹配键，**禁止跨 Path 连边或 Eligibility 混用**」。
/// 契约 <c>RoutingOperation.PathId</c> / <c>RoutingDependency.PathId</c> / <c>OperationResourceEligibility.PathId</c>
/// 均为真实值 ⇒ 图/边的分组键可携带。升维后，同 (物料, RouteCode) 但不同 Path 的工序与依赖
/// **不再被并入同一张图**（旧实现按 RouteCode 单键分组，V2 多路径时会把互斥备选路径并成一张图）。
/// V1 装载层目前把所有工序归一化为 'DEFAULT'/1（2号位 <c>NormalizeToSingleRoute</c>），
/// 故本键在 V1 恒为唯一一组，行为与升维前等价；Q1 要求装载层停用该归一化后，本键即真实生效。
/// </summary>
/// <summary>
/// 执行批的**求解态身份**（P0-03）：Phase2 `FormExecutionBatches` 形成后登记，
/// 供 Phase4 局部修复**逐批**重建（键 + 本批数量）。Scheduling 内部类型，非 1↔2 契约面。
/// </summary>
internal readonly record struct ExecutionBatchPlanEntry(
    string BatchKey,
    decimal NetOutputQty,
    decimal PlannedProcessQty);

internal readonly record struct RoutePathKey(string RouteCode, int PathId)
{
    /// <summary>空安全构造：null RouteCode 归一化为 string.Empty（与 2号位「RouteCode 恒非空」口径一致）。</summary>
    public static RoutePathKey Of(string? routeCode, int pathId)
        => new(routeCode ?? string.Empty, pathId);

    public override string ToString() => $"{RouteCode}::{PathId}";
}

/// <summary>
/// 工序资源资格 / 产能系数查找键：MaterialId + ProductionDepartmentId + RouteCode + OperationCode。
/// ⚠ **结构上限**：契约 <c>OperationResourceEligibility</c>（`LPS.APS.Core/Entities/Aps/OperationResourceEligibility.cs`）
/// **没有 StageCode 字段** —— 故该查找键**无法**携带 Stage 上下文（裁决 §5.2 的完整复合键在此不可达）。
/// 本键相对旧实现的唯一可行动改进 = **补上 ProductionDepartmentId**（旧键连部门都没有，会把两个部门
/// 同名工序的资源资格合并成一份 ⇒ 跨部门串资源）。部门维度是「物料×阶段」的联合属性，可作 Stage 的近似区分。
/// **残留（数据模型限制，非 1号位 可修）**：同物料 + 同部门下若两个 Stage 出现同一 OperationCode，
/// 其资格仍合并 —— 该情形由装载层诊断登记，1号位 不猜。
///
/// 2026-10-05 冻结补齐（《APS_V1_1号位有限产能排程开发实施包 v1.6》新增要求 + Q1）：
/// **补上 PathId**。原文：「PathId 必须进入 Graph/Dependency/Eligibility 节点和匹配键，
/// 禁止跨 Path 连边或 Eligibility 混用」。契约 <c>OperationResourceEligibility.PathId</c>
/// 与 <c>RoutingOperation.PathId</c> 均为真实值 ⇒ 键可携带。补 PathId 后，同
/// (物料, 部门, RouteCode, OperationCode) 但不同 Path 的资格不再被合并
/// （2号位 已在 PeggingOrchestrator.cs:3967 记录过该塌组现象）。
/// </summary>
internal readonly record struct EligibilityLookupKey(
    int MaterialId, int ProductionDepartmentId, string RouteCode, int PathId, string OperationCode);

/// <summary>
/// 工序依赖图（单个物料单条路径）
/// </summary>
internal class RoutingGraph
{
    /// <summary>
    /// 工序节点：(StageCode, OperationCode) → 工序信息。
    /// 0号位 2026-09-29 裁决 §5.3：本字典为必须升维的三处之一（原为 OperationCode 单键 ⇒ 后者覆盖前者、
    /// 边被错接、其中一道工序静默丢失；2号位 实测 117 物料受影响）。
    /// </summary>
    public Dictionary<OperationNodeKey, OperationNode> Operations { get; set; } = new();

    /// <summary>
    /// 依赖边：To 节点 → 前驱列表。
    /// 键与边的两端均为 <see cref="OperationNodeKey"/>，不再用 OperationCode 单键。
    /// </summary>
    public Dictionary<OperationNodeKey, List<DependencyEdge>> Dependencies { get; set; } = new();

    /// <summary>
    /// 根工序（无前驱的工序）
    /// </summary>
    public List<OperationNodeKey> RootOperations { get; set; } = new();
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
    // OperationPlanningMode 消费接线（0号位 20260922）：工序规划模式随 RoutingOperation 透传，
    // Phase2 正排/倒排据此分型——FINITE_RESOURCE 走资源找槽；UNCONSTRAINED/WAIT_ONLY 跳过资源、
    // 保留工艺时间走链（Task ResourceId=NULL）。DDL v5.1.8.2 定义，默认与 DDL DEFAULT 对齐。
    public string OperationPlanningMode { get; set; } = "FINITE_RESOURCE";
    // item1 接线（阶段二）：原 SetupTime 字段已删除——Setup 走 SetupTransitionRule 规则查找（v1.2 §1.2/§20.3），
    // 工序节点不再携带静态换型值，防止误读。
    public decimal? TransferBatchSize { get; set; }

    // P1-08修复：工序节点补齐工艺路径编码与路径序号（源 RoutingOperation.RouteCode/PathId）。
    // 供 Phase2/Phase4 生成 FinalTaskDraft 时回填 RouteCode/PathId，不再留 null。
    public string RouteCode { get; set; } = "DEFAULT";
    public int PathId { get; set; } = 1;

    // 0号位 2026-09-29 裁决 §5.2 落实：节点携带 ProductionDepartmentId，
    // 供 (a) 资源资格/产能系数查找键 EligibilityLookupKey 补齐部门维度；
    // (b) RoutingDependency 无 StageCode 字段时，用「工序码 + 部门」消解同名工序的归属歧义。
    public int ProductionDepartmentId { get; set; }
}

/// <summary>
/// 依赖边。两端为 <see cref="OperationNodeKey"/>（含 Stage 上下文）。
/// ⚠ 契约 <c>RoutingDependency</c>（`LPS.APS.Core/Entities/Aps/RoutingDependency.cs`）**只有
/// From/To OperationCode 与单值 ProductionDepartmentId，没有 StageCode 字段** ⇒ 边的 Stage 归属
/// 必须由 1号位 在 <c>BuildRoutingGraphs</c> 里从 RoutingOperation 反查消解；
/// 消解不唯一时**登记 Issue 并丢弃该边，不猜**（0号位 2026-09-29 §5.3「禁止半升级」+ 不静默原则）。
/// </summary>
internal class DependencyEdge
{
    public OperationNodeKey From { get; set; }
    public OperationNodeKey To { get; set; }
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

    // P1-01：净合格数量（YIELD 场景，锁定 Task 的净产出，!= 加工量）
    public decimal? LockedNetOutputQty { get; set; }

    // P1-01：产能加工数量（锁定 Task 的计划加工量）
    public decimal? LockedPlannedProcessQty { get; set; }

    public string? TaskKey { get; set; }
}

/// <summary>
/// 求解器**输入契约**异常：输入无法满足冻结契约要求的唯一标识 / 完整身份（不是业务「排不下」）。
///
/// 由 <see cref="FiniteCapacitySolver.SolveAsync"/> 捕获 ⇒ **受控 Fail Closed**（`Success=false` + `ErrorMessage`），
/// 不向外抛未处理异常、也不静默抹除锚点。
/// 依据：0号位 2026-10-09《APS_V1_2_20261009.md》§三 P0-02（「SolveAsync 没有对该异常做受控 Fail Closed」）。
/// </summary>
internal sealed class SolverInputContractException : Exception
{
    public SolverInputContractException(string message) : base(message) { }
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
