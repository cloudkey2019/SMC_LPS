using LPS.APS.Core.Dto;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// 「无 Routing Stage」纯内存时间节点构造器 —— 0号位 2026-09-29 裁决 §十四第 4/5/9 项（④⑤⑨）。
///
/// 【它解决什么】
///   末端（或整条）无 Routing 的 Stage（如 `BJ_FINAL` 完工）**没有对应的真实 Operation Task**，
///   其提前期无法由 <see cref="FinalTaskDraft"/> 表达；而 0号位 §9.2 明文**禁止**为此伪造
///   假 RoutingOperation / Task.Id / TaskNo / MESWorkOrder。
///   ⇒ 本类产出**纯内存**的 <see cref="DemandCompletionFact"/>，随
///   <see cref="DomainSolveResult.DemandCompletions"/> 出口，**绝不进 <see cref="DomainSolveResult.FinalTasks"/>**。
///
/// 【④ 纯内存节点】产出的 <see cref="StageTimingSegmentFact.NodeType"/> 恒为 `"STAGE_TIMING"`；
///   不建 Task、不生成 TaskNo、不进 MES 下发、不进 Execution Batch、不作为真实 Operation。
/// 【⑤ 只认契约】只接受 0号位 定的 4 个 `MatchLevel`；其余（含旧名 `MATERIAL`/`FAMILY`）= 契约违例 ⇒ 兜底。
///   命中级（含 `STAGE_LEADTIME_MISSING`）的**值一律原样透传，我方不做符号判断**。
///
///   ⚠️ **2026-09-29 口径更正（2号位 r13519 载入 Core 契约，取代当日早先口径）**：
///   此前「命中到参数行但折算值 ≤ 0 **同样视同未命中**、同样给 72h 兜底」**已作废**。
///   **命中就是命中** —— 折算值为 0（或负）**按命中产出**：`LeadTimeHours = 0`、
///   `MatchLevel` = 实际命中级（实测 4 行 `*_FINAL` 的 `LeadTimeDays = 0.00` ⇒ 值 0 + `FACTORY_STAGE_DEFAULT`）。
///   ⇒ 消费侧认 `MatchLevel`，但**不要**假定「值 0 ⇒ 一定是兜底」：值 0 也可能是一条真实命中的参数。
///   **0 是参数行写下的值，装载层与消费侧都不得替参数侧改判。**
///
///   注：0号位「**不得默认 0 小时**」管的是**未命中时不得拿 0 顶上**（那是静默丢提前期），
///   与「参数行明确写了 0」是两回事 —— 后者是**真实命中**，透传即可。
/// 【⑨ 时间出口】末端真实 `Task.End` + 其**之后**各无 Routing Stage 的提前期之和 = `CompletionTime`/`AvailableTime`。
///
/// 【边界】本类只用 `DomainSolveRequest` / `ConstraintContext` 的既有数据，**不查库、不查参数表**
///   （0号位 §4.4 末句：1号位 只消费已解析的 StageLeadTimeFact）。
///
/// 【可见性说明】<c>Scheduling</c> 无 <c>InternalsVisibleTo(Tests)</c>（见 `SetupOptimizer` 同款处置），
///   故**纯逻辑入口 <see cref="BuildCore"/> 设为 public 且只收公开类型**，便于单测；
///   依赖 <c>internal</c> 的 <see cref="ConstraintContext"/> 的装配留在 <see cref="Build"/>（internal）。
/// </summary>
public static class StageTimingNodeBuilder
{
    /// <summary>0号位 2026-09-29 裁决：三级均未命中（或折算值 ≤ 0）时的兜底提前期 = 3 天。</summary>
    private const decimal FallbackLeadTimeHours = 72m;

    /// <summary>契约码：三级均未命中 / 值不可用。PM 原码；1号位 回执 §四 定为 V1 合法 4 值之一。</summary>
    private const string MissingLevel = "STAGE_LEADTIME_MISSING";

    /// <summary>④ 纯内存节点的显式标记（0号位 §9.3；2号位 写库口的判据）。</summary>
    private const string StageTimingNodeType = "STAGE_TIMING";

    /// <summary>0号位 2026-09-29 裁决 §4.2/§4.3：V1 只认的三级命中码（Material / ProductFamily 级不认）。</summary>
    private static readonly HashSet<string> HitLevels = new(StringComparer.Ordinal)
    {
        "DEPT_EXACT",
        "FACTORY_STAGE_DEFAULT",
        "GLOBAL_STAGE_DEFAULT"
    };

    /// <summary>
    /// 装配入口：从 <see cref="ConstraintContext"/> 取出求解范围与依赖图，定出各 Demand 的末端真实 Task，
    /// 再交给纯逻辑 <see cref="BuildCore"/>。
    /// </summary>
    internal static List<DemandCompletionFact> Build(
        DomainSolveRequest request,
        ConstraintContext constraints,
        IReadOnlyList<FinalTaskDraft> allScheduledTasks)
    {
        var tasksByDemand = new Dictionary<string, List<FinalTaskDraft>>(StringComparer.Ordinal);
        foreach (var group in allScheduledTasks.GroupBy(t => t.SourceDraftId, StringComparer.Ordinal))
        {
            tasksByDemand[group.Key] = group.ToList();
        }

        var terminalByDemandKey = new Dictionary<string, FinalTaskDraft>(StringComparer.Ordinal);
        foreach (var demand in request.LogicalProductionDemands)
        {
            tasksByDemand.TryGetValue(demand.LogicalDemandKey, out var demandTasks);
            var terminal = PickTerminalTask(demandTasks, constraints, demand.MaterialId);
            if (terminal != null)
            {
                terminalByDemandKey[demand.LogicalDemandKey] = terminal;
            }
        }

        return BuildCore(request, constraints.EffectiveStages, terminalByDemandKey);
    }

    /// <summary>
    /// 纯逻辑：为每个 Demand 构造 Demand 级完成时间事实。**无副作用、不读 Context、可单测。**
    /// </summary>
    /// <param name="request">求解请求（用其 StageLeadTimes / StageSequenceChains / LogicalProductionDemands / PlanningStart）。</param>
    /// <param name="effectiveStages">本次求解范围内的可达 Stage：MaterialId → StageCode 集合。</param>
    /// <param name="terminalByDemandKey">各 Demand 的末端真实 Task；无真实 Task 的 Demand 不出现在此字典中。</param>
    public static List<DemandCompletionFact> BuildCore(
        DomainSolveRequest request,
        IReadOnlyDictionary<int, HashSet<string>> effectiveStages,
        IReadOnlyDictionary<string, FinalTaskDraft> terminalByDemandKey)
    {
        var result = new List<DemandCompletionFact>();
        if (request.LogicalProductionDemands.Count == 0)
        {
            return result;
        }

        var leadTimeByKey = new Dictionary<(int MaterialId, string StageCode), StageLeadTimeFact>();
        foreach (var fact in request.StageLeadTimes)
        {
            leadTimeByKey[(fact.MaterialId, fact.StageCode)] = fact;
        }

        var chainByMaterial = new Dictionary<int, StageSequenceChain>();
        foreach (var chain in request.StageSequenceChains)
        {
            chainByMaterial[chain.MaterialId] = chain;
        }

        foreach (var demand in request.LogicalProductionDemands)
        {
            terminalByDemandKey.TryGetValue(demand.LogicalDemandKey, out var terminal);

            // 有末端真实 Task ⇒ 从它的 End 起累加；整条需求无真实 Operation Task ⇒ 自计划起点纯提前期累计
            // （0号位 §10.3：整条无真实 Operation Task 时 1号位 仍必须返回 Demand 级 CompletionTime）
            var cursor = terminal?.PlannedEndTime ?? request.PlanningStart;

            var segments = new List<StageTimingSegmentFact>();
            DateTime? requiredStageEnd = null;

            var routelessStages = GetRoutelessStages(demand.MaterialId, effectiveStages, request);
            if (routelessStages.Count > 0 &&
                chainByMaterial.TryGetValue(demand.MaterialId, out var chain) &&
                chain.Stages.Count > 0)
            {
                var seqByStage = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var step in chain.Stages)
                {
                    seqByStage[step.StageCode] = step.StageSeq;
                }

                int? terminalSeq = null;
                if (terminal != null && seqByStage.TryGetValue(terminal.StageCode, out var terminalStageSeq))
                {
                    terminalSeq = terminalStageSeq;
                }

                // 按 StageSeq 升序（`StageSequenceChain.Stages` 契约已是升序，此处再排一次防上游乱序）；
                // 同 Seq 时按 StageCode 定序，保证结果可复现。
                foreach (var stageCode in routelessStages
                             .Where(seqByStage.ContainsKey)
                             .OrderBy(s => seqByStage[s])
                             .ThenBy(s => s, StringComparer.Ordinal))
                {
                    var seq = seqByStage[stageCode];

                    // 只取末端真实 Task **之后**的 Stage；整条需求无真实 Operation Task 时全取。
                    if (terminalSeq.HasValue && seq <= terminalSeq.Value)
                    {
                        continue;
                    }

                    var (hours, level) = ResolveLeadTime(leadTimeByKey, demand.MaterialId, stageCode);
                    var end = cursor.AddHours((double)hours);

                    segments.Add(new StageTimingSegmentFact
                    {
                        NodeType = StageTimingNodeType,
                        StageCode = stageCode,
                        LeadTimeHours = hours,
                        MatchLevel = level,
                        StartTime = cursor,
                        EndTime = end
                    });

                    cursor = end;

                    if (!string.IsNullOrEmpty(demand.RequiredStageCode) &&
                        string.Equals(stageCode, demand.RequiredStageCode, StringComparison.Ordinal))
                    {
                        requiredStageEnd = end;
                    }
                }
            }

            // RequiredStageCompletionTime：做到供给阈值 Stage 为止的完成时间（0号位 §10.3 两个口径都给）
            DateTime? requiredCompletion = null;
            if (!string.IsNullOrEmpty(demand.RequiredStageCode))
            {
                if (requiredStageEnd.HasValue)
                {
                    requiredCompletion = requiredStageEnd;
                }
                else if (terminal != null &&
                         chainByMaterial.TryGetValue(demand.MaterialId, out var requiredChain))
                {
                    var requiredStep = requiredChain.Stages.FirstOrDefault(
                        s => string.Equals(s.StageCode, demand.RequiredStageCode, StringComparison.Ordinal));
                    var terminalStep = requiredChain.Stages.FirstOrDefault(
                        s => string.Equals(s.StageCode, terminal.StageCode, StringComparison.Ordinal));

                    // 供给阈值 Stage 在末端真实 Task 处或之前已完成 ⇒ 完成时间即末端 Task.End
                    if (requiredStep != null && terminalStep != null && requiredStep.StageSeq <= terminalStep.StageSeq)
                    {
                        requiredCompletion = terminal.PlannedEndTime;
                    }
                }
            }

            result.Add(new DemandCompletionFact
            {
                LogicalDemandKey = demand.LogicalDemandKey,
                MaterialId = demand.MaterialId,
                AllocationSequence = demand.AllocationSequence,
                RequiredStageCode = demand.RequiredStageCode,
                CompletionTime = cursor,
                AvailableTime = cursor, // V1 与 CompletionTime 同值（已对齐）
                RequiredStageCompletionTime = requiredCompletion,
                TerminalFinalDraftId = terminal?.FinalDraftId,
                StageTimings = segments
            });
        }

        return result;
    }

    /// <summary>
    /// 取该 Demand 的**末端真实生产 Task**（0号位 §10.2「唯一末端真实 Task」）。
    /// 规则与 Phase5 <c>GenerateAllocationShares</c> 的末端判定**必须一致** —— 改其一请同步另一处。
    /// </summary>
    private static FinalTaskDraft? PickTerminalTask(
        List<FinalTaskDraft>? tasks,
        ConstraintContext constraints,
        int materialId)
    {
        if (tasks == null || tasks.Count == 0)
        {
            return null;
        }

        if (tasks.Count == 1)
        {
            return tasks[0];
        }

        // 标记所有有 downstream 的 Task（非末端）。节点身份 = (StageCode, OperationCode)（0号位 §5.3）。
        // Path-aware（0号位 2026-10-07 裁决 Q-3）：本列表全属同一需求 ⇒ 路径身份取自首个 Task，不按物料猜。
        var downstream = new HashSet<string>(StringComparer.Ordinal);
        var sample = tasks[0];
        if (constraints.TryGetRoutingGraph(sample.MaterialId, sample.RouteCode, sample.PathId, out var graph))
        {
            foreach (var depList in graph.Dependencies.Values)
            {
                foreach (var dep in depList)
                {
                    var upstream = tasks.FirstOrDefault(t =>
                        t.OperationCode == dep.From.OperationCode &&
                        string.Equals(t.StageCode, dep.From.StageCode, StringComparison.Ordinal));

                    if (upstream != null)
                    {
                        downstream.Add(upstream.FinalDraftId);
                    }
                }
            }
        }

        var terminals = tasks.Where(t => !downstream.Contains(t.FinalDraftId)).ToList();
        if (terminals.Count == 0)
        {
            // 兜底：全部被标为非末端（异常输入）⇒ 退回最晚结束者，**不抛、不静默丢**
            terminals = tasks;
        }

        return terminals
            .OrderByDescending(t => t.PlannedEndTime)
            .ThenBy(t => t.FinalDraftId, StringComparer.Ordinal)
            .First();
    }

    /// <summary>
    /// 本**求解范围**内「有效 Stage 但零 RoutingOperation」的 Stage 集合。
    /// 用求解范围（Phase1 可达 Stage）而非全量 BOM StagePath ——
    /// 否则会把 StartOperation/StartStage 之前已完成、本次不求解的 Stage 也算进来，凭空多出提前期。
    /// </summary>
    private static List<string> GetRoutelessStages(
        int materialId,
        IReadOnlyDictionary<int, HashSet<string>> effectiveStages,
        DomainSolveRequest request)
    {
        if (!effectiveStages.TryGetValue(materialId, out var inScopeStages) || inScopeStages.Count == 0)
        {
            return new List<string>();
        }

        var stagesWithRouting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in request.RoutingOperations)
        {
            if (op.MaterialId != materialId ||
                op.RouteCode != "DEFAULT" ||
                string.IsNullOrEmpty(op.StageCode))
            {
                continue;
            }

            stagesWithRouting.Add(op.StageCode!);
        }

        return inScopeStages.Where(stage => !stagesWithRouting.Contains(stage)).ToList();
    }

    /// <summary>
    /// ⑤ 提前期解析：只认契约 4 值；**命中级的值原样透传，不做符号判断**。
    /// 契约违例值（含旧名 `MATERIAL` / `FAMILY`）**不向外透传**，避免下游把它当合法层级审计。
    ///
    /// ⚠️ 2026-09-29 口径更正：**值 ≤ 0 不再视同未命中** —— 命中就是命中，0 是参数行写下的值。
    /// （早先那版带 `fact.LeadTimeHours > 0m` 的守卫即被作废的口径，已删。）
    /// </summary>
    private static (decimal Hours, string Level) ResolveLeadTime(
        Dictionary<(int MaterialId, string StageCode), StageLeadTimeFact> index,
        int materialId,
        string stageCode)
    {
        if (index.TryGetValue((materialId, stageCode), out var fact))
        {
            var level = fact.MatchLevel;
            var isLegalLevel = !string.IsNullOrEmpty(level) &&
                               (HitLevels.Contains(level) ||
                                string.Equals(level, MissingLevel, StringComparison.Ordinal));

            if (isLegalLevel)
            {
                return (fact.LeadTimeHours, level);
            }
        }

        return (FallbackLeadTimeHours, MissingLevel);
    }
}
