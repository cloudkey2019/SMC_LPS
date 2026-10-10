using LPS.APS.Core.Dto;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// **B-005 Direction 上下文决策器（Owner = 1号位）**。
///
/// 【为什么需要它】0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§三 / §十四 第二优先级 判 **P1-DIR-01**：
///   冻结要求是「Demand / Execution Batch ↓ 综合其**自己的**上下文 ⇒ 由 1号位 得到实际 Direction」，
///   而旧实现是「一个 Run/Strategy 给出一个 <c>SchedulingDirection</c>，所有 Demand 共用」，
///   且 `AUTO` 与 `MIXED` 行为等价 ⇒ 冻结能力**未实现**。本类即该能力的正式落地。
///
/// 【冻结依据（逐条可查）】
///   · 规则清单 v1.5 `B-004`（源：业务基线 v1.8、有限产能 v1.7）：
///     「Direction 支持 AUTO/FORWARD/BACKWARD/MIXED；**OrderType 不得直接决定 Direction**」。
///   · 规则清单 v1.5 `B-005`（源：有限产能 v1.7）：
///     「Direction 由 **DemandGoal、RequiredAvailableTime、Slack、Material、Resource、Execution、
///       Firm/Frozen/Lock** 等上下文综合决定」，Owner = 1号位。
///   · 业务基线 v1.8 §Demand Goal、Batch、Direction、Routing：
///     「Direction 不得由 OrderType 硬映射，按冻结策略综合 Demand Goal、RequiredAvailableTime、Slack、
///       Material、资源、Execution 与 Lock 决定」。
///   · 有限产能 v1.7 §11：「正排适合**最早承诺、物料刚可用**等场景；倒排适合**靠近 DueDate 减少 WIP**。
///       V1 在同一个 Solver 内混合使用」。
///   · Pegging v1.6 边界 2：「Pegging 传播 Qty、RequiredAvailableTime、**DemandGoal**、Demand Protection、
///       DemandSequence 与 lineage；**不传播 Direction**、Batch、Resource、TaskNo 或 Routing 最终选择」
///     ⇒ Direction **必须**由 1号位 自决（不是 2号位 透传），但 DemandGoal **必须**由 Pegging 侧传播进来。
///
/// 【本实现只用「确实存在」的输入】七类上下文与 C# 载体的对应关系：
///   · Firm/Frozen/Lock → <c>ConstraintContext.LockedTasks</c>（锁定任务 = 原地继承的硬锚点）
///   · Execution（连续性）→ <c>LogicalProductionDemand.IsContinuation</c> / <c>NoSplitMerge</c> / <c>ContinuationKey</c>
///   · Material → <c>dynamicMaterialFloor</c>（跨物料子件完成下界）+ <c>ConstraintContext.MaterialAvailability</c>
///   · RequiredAvailableTime → <c>LogicalProductionDemand.RequiredAvailableTime</c>（Due）
///   · Slack → **派生量**（见 <see cref="ComputeSlackMinutes"/>），非契约字段
///   · Resource → <c>PreferredResourceCode</c> / <c>PreferredResourceId</c> / <c>FallbackResourceId</c>
///   · DemandGoal → ⚠ **无载体** —— 见下方「已知缺口」
///
/// 【已知缺口：DemandGoal 载体缺失（**不降目标，如实登记**）】
///   冻结侧 `DemandGoal` 是**已定义**概念：规则清单 v1.5 `P-006`「V1 DemandGoal 包含
///   <c>CUSTOMER_COMMITMENT</c> 与 <c>INVENTORY_REPLENISHMENT</c>」（源：Pegging v1.6），
///   且 `P-008` 要求 Pegging **传播** DemandGoal。但**全仓 grep 证实**：
///   <c>LPS.APS.Core/Dto/LogicalProductionDemand.cs</c> **没有** DemandGoal 属性，
///   全 C# 代码库对 `DemandGoal` / `CUSTOMER_COMMITMENT` / `INVENTORY_REPLENISHMENT` **零命中**。
///   ⇒ 该项**不可达**，属 **2号位（Pegging 传播 Owner）的输入缺口**，**不是** 1号位 可自造字段
///     （自造 = 造字段，且 0号位 §十 明确禁止）。
///   处理方式：本类保留 <c>demandGoal</c> 形参（**函数参数，非契约字段**）与两条完整判据，
///     使该上下文一旦到位即可**零改动生效**；生产路径当前传 <c>null</c>，并在信号里记
///     <see cref="SignalDemandGoalAbsent"/> ⇒ **缺口在决策记录里可见，绝不静默**。
///
/// 【与 MIXED 的关系】0号位 §三：「`AUTO == MIXED` 不能再作为最终 V1 实现」；`MIXED` 本身
///   「可以作为人工/策略明确模式继续保留」⇒ **MIXED 保持字面语义不变**（先倒排、失败转正排），
///   只有 `AUTO` 走本类自决。自决结果可以是 `FORWARD`/`BACKWARD`/`MIXED` 三者之一（信号冲突时为 MIXED）。
/// </summary>
internal static class SchedulingDirectionResolver
{
    internal const string Forward = "FORWARD";
    internal const string Backward = "BACKWARD";
    internal const string Mixed = "MIXED";
    internal const string Auto = "AUTO";

    // ── 判据信号名（确定性、可断言、可审计）──
    internal const string SignalLockedAnchor = "LOCKED_ANCHOR";
    internal const string SignalContinuationSlice = "CONTINUATION_SLICE";
    internal const string SignalMaterialLate = "MATERIAL_LATE";
    internal const string SignalDueTight = "DUE_TIGHT";
    internal const string SignalDueLoose = "DUE_LOOSE";
    internal const string SignalDueOverdue = "DUE_OVERDUE";
    internal const string SignalDueAbsent = "DUE_ABSENT";
    internal const string SignalGoalCustomerCommitment = "GOAL_CUSTOMER_COMMITMENT";
    internal const string SignalGoalInventoryReplenishment = "GOAL_INVENTORY_REPLENISHMENT";
    internal const string SignalDemandGoalAbsent = "DEMAND_GOAL_ABSENT";
    internal const string SignalPreferredResource = "PREFERRED_RESOURCE";
    internal const string SignalNoContext = "NO_CONTEXT_SIGNAL";
    internal const string SignalConflicting = "CONFLICTING_SIGNALS";

    // ── DemandGoal 冻结取值（P-006；载体缺失，见类注释）──
    internal const string GoalCustomerCommitment = "CUSTOMER_COMMITMENT";
    internal const string GoalInventoryReplenishment = "INVENTORY_REPLENISHMENT";

    /// <summary>方向裁决结果：方向 + 判据信号集合（顺序 = 收集顺序，确定性）。</summary>
    internal sealed record Decision(string Direction, IReadOnlyList<string> Signals)
    {
        /// <summary>是否含某判据信号。</summary>
        public bool Has(string signal) => Signals.Contains(signal, StringComparer.Ordinal);

        /// <summary>可追溯的判据串（写日志/回执用，非契约字段）。</summary>
        public string Reason => $"{string.Join("+", Signals)}⇒{Direction}";
    }

    /// <summary>
    /// **B-005 正式自决**：按 Demand / Execution Batch 自身上下文决定实际 Direction。
    ///
    /// 裁决规则（确定性、可复现；先判硬锚点，再按信号冲突收敛）：
    ///   1. **锁定任务存在** ⇒ `FORWARD`。锁定 = 原地继承的既成事实锚点（`ExecutionConstraint` /
    ///      `LockedTasks`），剩余工序只能**从锚点向前**续排 ⇒ 正排。该判据是硬事实，压过一切软信号。
    ///   2. 否则收集正/反信号：
    ///        正向（最早承诺）: `CONTINUATION_SLICE` / `MATERIAL_LATE` / `DUE_LOOSE` / `DUE_OVERDUE` /
    ///                          `GOAL_INVENTORY_REPLENISHMENT` / `PREFERRED_RESOURCE`
    ///        反向（交期驱动）: `DUE_TIGHT` / `GOAL_CUSTOMER_COMMITMENT`
    ///      · 正反**同时**非空 ⇒ `MIXED`（信号冲突：先倒排、失败转正排，即 v1.7 §11「同一 Solver 内混合」）
    ///      · 仅反向非空 ⇒ `BACKWARD`（靠 DueDate 减少 WIP）
    ///      · 仅正向非空 ⇒ `FORWARD`（最早承诺 / 物料刚可用）
    ///      · **都空** ⇒ `BACKWARD` + `NO_CONTEXT_SIGNAL`（无上下文可依时取系统既有默认方向，
    ///        确定性且**不是** MIXED —— 不再回到「AUTO 恒等 MIXED」）
    /// </summary>
    internal static Decision Resolve(
        LogicalProductionDemand demand,
        IReadOnlyList<OperationNode> operations,
        ConstraintContext constraints,
        DateTime planningStart,
        DateTime dynamicMaterialFloor,
        string? demandGoal = null)
    {
        var signals = new List<string>();

        // ── 判据 1：Firm/Frozen/Lock —— 锁定任务（硬锚点，最高优先）──
        if (HasLockedAnchor(demand, constraints))
        {
            signals.Add(SignalLockedAnchor);
            return new Decision(Forward, signals);
        }

        var forwardSignals = new List<string>();
        var backwardSignals = new List<string>();

        // ── 判据 2：Execution —— 连续份额（A/B 既存执行批：已经开始 ⇒ 最早承诺）──
        if (demand.IsContinuation || demand.NoSplitMerge || !string.IsNullOrEmpty(demand.ContinuationKey))
        {
            forwardSignals.Add(SignalContinuationSlice);
        }

        // ── 判据 3：Resource —— 资源连续性软偏好（P1-11 / v1.6 Q4：与上一执行资源保持一致）──
        if (!string.IsNullOrEmpty(demand.PreferredResourceCode)
            || demand.PreferredResourceId is not null
            || demand.FallbackResourceId is not null)
        {
            forwardSignals.Add(SignalPreferredResource);
        }

        // ── 判据 4：Material —— 物料/子件下界晚于计划起点 ⇒「物料刚可用」⇒ 正排 ──
        var materialFloor = dynamicMaterialFloor > planningStart ? dynamicMaterialFloor : planningStart;
        if (dynamicMaterialFloor > planningStart)
        {
            forwardSignals.Add(SignalMaterialLate);
        }

        // ── 判据 5：RequiredAvailableTime + Slack —— 交期驱动 vs 最早承诺 ──
        var slackMinutes = ComputeSlackMinutes(demand, operations, materialFloor, planningStart, out var dueAbsent);
        if (dueAbsent)
        {
            signals.Add(SignalDueAbsent);
        }
        else if (demand.RequiredAvailableTime <= planningStart)
        {
            // 交期**已过**：倒排（靠 DueDate 减少 WIP）已无意义 ⇒ 尽早产出 ⇒ 正排（最早承诺）。
            forwardSignals.Add(SignalDueOverdue);
        }
        else if (slackMinutes < 0m)
        {
            // 交期未过但余量为负（工艺周期都装不下）⇒ 交期驱动 ⇒ 倒排。
            backwardSignals.Add(SignalDueTight);
        }
        else if (slackMinutes > TotalLeadMinutes(operations))
        {
            // 余量超过一个完整工艺周期（leadMinutes）⇒ 明显不紧 ⇒ 最早承诺（正排）。
            forwardSignals.Add(SignalDueLoose);
        }
        // 0 <= slack <= lead：余量一般 ⇒ 不产生交期信号（交由其它上下文裁决）

        // ── 判据 6：DemandGoal —— 客户承诺（交期驱动）vs 库存补充（非交期驱动）──
        if (string.IsNullOrEmpty(demandGoal))
        {
            // 载体缺失（P-008 传播未达）：**记录缺口**，不静默、不猜。
            signals.Add(SignalDemandGoalAbsent);
        }
        else if (string.Equals(demandGoal, GoalCustomerCommitment, StringComparison.Ordinal))
        {
            backwardSignals.Add(SignalGoalCustomerCommitment);
        }
        else if (string.Equals(demandGoal, GoalInventoryReplenishment, StringComparison.Ordinal))
        {
            forwardSignals.Add(SignalGoalInventoryReplenishment);
        }
        else
        {
            // 未知取值：不认（不猜），同样登记缺口。
            signals.Add(SignalDemandGoalAbsent);
        }

        // ── 收敛裁决 ──
        signals.AddRange(forwardSignals);
        signals.AddRange(backwardSignals);

        if (forwardSignals.Count > 0 && backwardSignals.Count > 0)
        {
            signals.Add(SignalConflicting);
            return new Decision(Mixed, signals);
        }

        if (backwardSignals.Count > 0)
        {
            return new Decision(Backward, signals);
        }

        if (forwardSignals.Count > 0)
        {
            return new Decision(Forward, signals);
        }

        signals.Add(SignalNoContext);
        return new Decision(Backward, signals);
    }

    /// <summary>
    /// Slack（派生量，**非契约字段**）= `RequiredAvailableTime − 最早可能完成时间`（分钟）。
    ///
    /// 最早可能完成 = `max(PlanningStart, 物料/子件下界) + 该需求工序总标准工时`。
    ///   · 这是**估算**（不含资源争用与 Setup），仅用于「交期紧/松」的**方向判据**，
    ///     **不**作为可行性结论（可行性仍由 Phase2 找槽 / Phase3 诊断 / Phase4 修复给出）。
    ///   · `dueAbsent = true` **仅当** `RequiredAvailableTime == default`（**无交期**），此时**不产生交期信号**。
    ///     ⚠ 「交期已过」**不**归入 absent（那是「已逾期」，是有效交期）—— 由调用方按
    ///     `DUE_OVERDUE ⇒ 正排` 处理，避免把「已逾期」误判成「无交期」而丢失方向信息。
    /// </summary>
    internal static decimal ComputeSlackMinutes(
        LogicalProductionDemand demand,
        IReadOnlyList<OperationNode> operations,
        DateTime materialFloor,
        DateTime planningStart,
        out bool dueAbsent)
    {
        dueAbsent = false;

        if (demand.RequiredAvailableTime == default)
        {
            dueAbsent = true;
            return 0m;
        }

        var earliestStart = materialFloor > planningStart ? materialFloor : planningStart;
        var earliestFinish = earliestStart.AddMinutes((double)TotalLeadMinutes(operations));
        return (decimal)(demand.RequiredAvailableTime - earliestFinish).TotalMinutes;
    }

    /// <summary>该需求（已选定路径上的）工序总标准工时（分钟）。无工序 ⇒ 0。</summary>
    internal static decimal TotalLeadMinutes(IReadOnlyList<OperationNode> operations)
        => operations.Count == 0 ? 0m : operations.Sum(o => o.StandardDuration);

    /// <summary>该需求是否存在锁定任务（`LockedTasks` 键为 `(DraftId, StageCode, OperationCode)`，按 DraftId 归属需求）。</summary>
    private static bool HasLockedAnchor(LogicalProductionDemand demand, ConstraintContext constraints)
        => constraints.LockedTasks.Keys.Any(k => string.Equals(k.DraftId, demand.LogicalDemandKey, StringComparison.Ordinal)
                                                 || string.Equals(k.DraftId, demand.DemandKey, StringComparison.Ordinal));
}
