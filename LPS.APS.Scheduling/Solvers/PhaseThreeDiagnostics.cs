using LPS.APS.Core.Dto;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 3: 可行性与延期诊断
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md》§六 Phase 3
///
/// 职责：
/// - 识别哪些 Demand 未满足
/// - 识别哪些 Task 晚于 RequiredAvailableTime
/// - 识别真实瓶颈
/// - 诊断物料/资源/前序/锁约束
/// - 生成 ScheduleExplanationFact（根因诊断）
///
/// 【ReasonCode 口径 —— 2026-10-07 对齐最新冻结文档】
///   `ScheduleExplanationFact.ReasonCode` **只准**取《APS数据库字段说明文档 v5.1.9》§八.1（:4833）
///   的 **15 码权威枚举**（原文「全文 ReasonCode 必须属于此列表，0号位审批冻结」）。
///   · 实施包 v1.0~v1.4 §5.4 那 9 类根因码是「**例如**」（v1.4:597）**非字典**，v1.6 已整节删除；
///   · 0号位《1号位代码第15轮审核报告》P1-03（**归档**）曾称其中 6 码为「正式冻结要求」——
///     该报告属**辅助**，与最新冻结文档（15 码硬枚举）碰撞时**以最新冻结文档为准**。
///   ⇒ 本类现行 9 码已全部归并进 15 码（逐分支注释见 DiagnoseDelayReason）。
///   `ObjectType` 值域 = `ORDER / TASK / RESOURCE / STAGE / DOMAIN`（v5.1.9:4828）。
///
/// 【双通道 —— 2026-10-07】`ScheduleExplanationFact`（结构化原因事实层，15 码）与
///   `ExplainTrace`（轻量 Task 级追踪日志，自由文本 Message + ContextData）**共存不替代、禁止合并或混用**
///   （v5.1.9 §5.2 注解 :4100）。枚举**外**的原因**不新增码**，细分改由**本表自己的**
///   `EvidenceJson` 外壳 `evidenceType` 承载（v5.1.9:4836 外壳含 evidenceType/summary/details，
///   details schema 阶段一不冻结）—— 与基线 v1.8:1297 对 Setup 的处置同构（细分走轻量通道、
///   不新增专属 `ScheduleExplanationFact.ReasonCode`）。Calendar（日历不可用）即按此处置。
/// </summary>
internal class PhaseThreeDiagnostics
{
    /// <summary>
    /// 执行可行性诊断
    /// </summary>
    public DiagnosticsResult Diagnose(
        DomainSolveRequest request,
        InitialScheduleResult scheduleResult,
        ConstraintContext constraints)
    {
        var result = new DiagnosticsResult();

        // P0-12修复：构建已排程任务的索引，使用ToLookup支持多工序（一个Demand生成多个Task）
        var scheduledTasksLookup = scheduleResult.ScheduledTasks
            .ToLookup(t => t.SourceDraftId);

        // ═══════════════════════════════════════════════
        // 1. 延期识别：PlannedEndTime > RequiredAvailableTime
        // ═══════════════════════════════════════════════
        foreach (var demand in request.LogicalProductionDemands)
        {
            // P0-12修复：使用Lookup支持多工序
            if (!scheduledTasksLookup.Contains(demand.LogicalDemandKey))
            {
                // 未排程需求
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
                continue;
            }

            // 找到该需求的最后一道工序
            var demandTasks = scheduledTasksLookup[demand.LogicalDemandKey]
                .OrderBy(t => t.PlannedEndTime)
                .ToList();

            if (demandTasks.Count == 0) continue;

            var lastTask = demandTasks.Last();
            var effectiveDue = constraints.EffectiveDue(demand);   // M5 第一批：延期诊断口径用覆盖交期
            var delay = lastTask.PlannedEndTime - effectiveDue;

            if (delay > TimeSpan.Zero)
            {
                // 延期
                result.DelayedTaskIds.Add(lastTask.FinalDraftId);

                // 诊断延期原因：reasonCode 取 15 码权威枚举；evidenceType 为该码下的**细分证据类别**
                // （供 EvidenceJson 外壳承载，见类文档【双通道】）。
                var reasonCode = DiagnoseDelayReason(
                    demand,
                    demandTasks,
                    constraints,
                    request,
                    out var evidenceType,
                    out var extraDetails);

                result.ExplanationFacts.Add(new ScheduleExplanationFact
                {
                    FinalDraftId = lastTask.FinalDraftId,
                    // P0-02 整改（0号位 2026-10-07 审核）：权威值域 = ORDER / TASK / RESOURCE / STAGE / DOMAIN
                    // （字段说明 v5.1.9:4828），无 DEMAND。本事实的对象语义 = 该需求（订单）级延期，
                    // 且此处填的是 OrderId（ObjectType=ORDER/TASK 时填 OrderId，:4829）；
                    // TaskId 在 1号位 持久化前不可得（:4830 规定 ObjectType=TASK 才填 TaskId）
                    // ⇒ 落 ORDER。2号位 侧同值（PeggingOrchestrator.cs:774/:817）须同步退出 DEMAND。
                    ObjectType = "ORDER",
                    OrderId = demand.OrderId,
                    StageCode = lastTask.StageCode,
                    ReasonCode = reasonCode,
                    // Severity 值域合规（2026-10-05 冻结《APS数据库字段说明文档 v5.1.9》§八.1）：
                    // ScheduleExplanationFact.Severity 权威值域 = INFO / WARN / ERROR（不含 CRITICAL）。
                    // 原 "HIGH" 不在值域内 ⇒ 改为 ERROR（该需求实际已延期，属最高等级事实）。
                    Severity = "ERROR",
                    ImpactHours = (decimal)delay.TotalHours,
                    // EvidenceJson 外壳结构（2026-10-07 对齐 v5.1.9:4836/:4840）：
                    // 冻结要求外壳**含 evidenceType / summary / details** 三字段（details 内部 schema 阶段一不冻结）。
                    // 原仅写 RequiredTime/ActualTime 两个自定义键、缺外壳三字段 ⇒ 本次补齐。
                    // extraDetails（可空）= 该分支特有的细分证据键（如跨域阻挡的 SourceDomainKey/
                    // SourcePlanVersionId/ResourceId），以逗号前缀拼入 details，不改变既有两键。
                    EvidenceJson =
                        $"{{\"evidenceType\":\"{evidenceType}\"," +
                        $"\"summary\":\"需求 {demand.LogicalDemandKey} 延期 {delay.TotalHours:F2}h（{reasonCode}）\"," +
                        $"\"details\":{{\"RequiredTime\":\"{effectiveDue:O}\",\"ActualTime\":\"{lastTask.PlannedEndTime:O}\"{extraDetails}}}}}"
                });
            }
        }

        // ═══════════════════════════════════════════════
        // 2. 识别瓶颈资源（Load / AvailableCapacity > 阈值）
        // ═══════════════════════════════════════════════
        // P1-02（BottleneckMode 四模式）：Auto 按利用率阈值自动识别；ForceAnchor 强制锚点必入；
        // PreferAnchor 锚点有负荷时优先入（无负荷/编码无效回退 Auto）；NotAnchor 锚点即使超阈值也排除。
        var solverStrategy = request.StrategySnapshot.SolverStrategy;
        var resourceUtilization = CalculateResourceUtilization(
            scheduleResult.ScheduledTasks,
            constraints,
            request.PlanningStart,
            request.PlanningEnd);

        // Auto 基线：利用率超阈值者入瓶颈集。
        var bottleneckIds = resourceUtilization
            .Where(kv => kv.Value > solverStrategy.BottleneckUtilizationThreshold)
            .Select(kv => kv.Key)
            .ToHashSet();

        // 锚点资源编码 → ResourceId（Code→Id 反向映射，Phase1 BuildResourceCodes 已构建）。
        int? anchorResourceId = null;
        if (!string.IsNullOrEmpty(solverStrategy.AnchorResourceCode) &&
            constraints.ResourceIdsByCode.TryGetValue(solverStrategy.AnchorResourceCode, out var anchorId))
        {
            anchorResourceId = anchorId;
        }

        switch (solverStrategy.BottleneckMode)
        {
            case DynamicBottleneckMode.ForceAnchor:
                // 强制锚点：锚点资源无条件入瓶颈集（展示锚点语义，不突破 Capacity/Calendar 等硬约束）。
                if (anchorResourceId.HasValue)
                    bottleneckIds.Add(anchorResourceId.Value);
                break;

            case DynamicBottleneckMode.PreferAnchor:
                // 优先锚点：锚点资源有负荷（利用率>0）时优先入；无负荷/编码无效回退 Auto 动态识别。
                if (anchorResourceId.HasValue &&
                    resourceUtilization.TryGetValue(anchorResourceId.Value, out var anchorUtil) &&
                    anchorUtil > 0m)
                {
                    bottleneckIds.Add(anchorResourceId.Value);
                }
                break;

            case DynamicBottleneckMode.NotAnchor:
                // 排除锚点：即使利用率超阈值也不判瓶颈（其容量约束仍参与求解，只是不作锚点展示）。
                if (anchorResourceId.HasValue)
                    bottleneckIds.Remove(anchorResourceId.Value);
                break;

            case DynamicBottleneckMode.Auto:
            default:
                // Auto：维持基线。
                break;
        }

        foreach (var resourceId in bottleneckIds)
        {
            var utilization = resourceUtilization.TryGetValue(resourceId, out var u) ? u : 0m;
            result.BottleneckResourceIds.Add(resourceId);

            result.ExplanationFacts.Add(new ScheduleExplanationFact
            {
                FinalDraftId = string.Empty,
                ObjectType = "RESOURCE",
                ResourceId = resourceId,
                // 15 码对齐：瓶颈 = 资源产能紧张 ⇒ RESOURCE_CAPACITY_WAIT（原 RESOURCE_CAPACITY_SHORTAGE 非 15 码）
                ReasonCode = "RESOURCE_CAPACITY_WAIT",
                // Severity 值域合规（2026-10-05 冻结）：值域 = INFO / WARN / ERROR。原 "HIGH" 不在值域内
                // ⇒ 改为 WARN（瓶颈资源是风险信号，非硬失败；需求级延期事实另记 ERROR）。
                Severity = "WARN",
                ImpactHours = null,
                // EvidenceJson 外壳结构（2026-10-07 对齐 v5.1.9:4836/:4840）：evidenceType / summary / details
                EvidenceJson =
                    $"{{\"evidenceType\":\"CAPACITY\"," +
                    $"\"summary\":\"资源 {resourceId} 利用率 {utilization:F2} 超瓶颈阈值\"," +
                    $"\"details\":{{\"Utilization\":{utilization:F2}}}}}"
            });
        }

        return result;
    }

    /// <summary>
    /// 诊断延期原因。返回 15 码 ReasonCode；<paramref name="evidenceType"/> 出参给该码下的**细分证据类别**
    /// （写入 `EvidenceJson.evidenceType`，见类文档【双通道】）。
    ///
    /// 【2026-10-07 重写：9 码 → 15 码】原 P1-03 按实施包 §5.4 补的 9 类根因码**不是冻结字典**
    ///   （§5.4:597 明写「**例如**」；v1.6 已整节删除），与《字段说明 v5.1.9》§八.1（:4833）的
    ///   **15 码权威枚举**「全文 ReasonCode 必须属于此列表」直接冲突。按 1号位 执行策略
    ///   （最新冻结文档为红线 / 归档裁决为辅助 / 碰撞以最新为准），本方法一律只投 15 码：
    ///     冻结与执行锁           → FROZEN_ZONE_LOCK        （evidenceType = LOCK）
    ///     跨域资源阻挡           → RESOURCE_CAPACITY_WAIT  （= CROSS_DOMAIN_RESOURCE_BLOCK）
    ///     同域共享资源阻挡       → RESOURCE_CAPACITY_WAIT  （= SHARED_RESOURCE）
    ///     换型受限               → RESOURCE_CAPACITY_WAIT  （= SETUP）
    ///     物料                   → MATERIAL_SHORTAGE        （= MATERIAL）
    ///     产能                   → RESOURCE_CAPACITY_WAIT  （= CAPACITY）
    ///     工艺资格降级           → ROUTING_FALLBACK         （= ROUTING）
    ///     日历不可用             → RESOURCE_CAPACITY_WAIT  （= CALENDAR；2026-10-07 新增，见第 7 步）
    ///     兜底（前序 / 其他约束） → PRECEDENCE_WAIT         （= PRECEDENCE）
    ///   ⇒ 旧码 FIRM_FROZEN_CONSTRAINT / LOCK_CONSTRAINT / SHARED_RESOURCE_BLOCK / SETUP_CONSTRAINT /
    ///     CROSS_DOMAIN_AVAILABILITY / ROUTING_ELIGIBILITY / MATERIAL_NOT_AVAILABLE /
    ///     RESOURCE_CAPACITY_SHORTAGE / PREDECESSOR_DELAY 九个**全部退役，不再产出**。
    ///   换型受限不再出专属码，与 0号位 对 Setup 的口径一致（基线 v1.8:1297「不新增 Setup 专属
    ///   `ScheduleExplanationFact.ReasonCode`」，正常 Setup 走 `SolveTraceNote`）。
    ///
    /// 【2026-10-07 二次整改（0号位 审核 P0-02）】原把**普通跨域共享资源阻挡**并入
    ///   `CROSS_DOMAIN_VERSION_MISMATCH_RISK` 属**语义错误**：该码在《字段说明 v5.1.9:4843》中
    ///   专指「**跨域版本不一致风险**」，且强制 `ObjectType=DOMAIN`、Evidence 须含
    ///   `FailedDomainKeys / AffectedDomainKeys / CurrentActivePlanVersions / NewPlanVersions`。
    ///   其它 Domain 的 ACTIVE Task 合法占用共享资源，本质是**资源不可用/资源等待**，不是版本不一致。
    ///   ⇒ 现改为：跨域与同域阻挡**一律** `RESOURCE_CAPACITY_WAIT`，细分由
    ///   `EvidenceJson.evidenceType`（`CROSS_DOMAIN_RESOURCE_BLOCK` / `SHARED_RESOURCE`）承载，
    ///   `details` 携 `SourceDomainKey / SourcePlanVersionId / ResourceId`。
    ///   **`CROSS_DOMAIN_VERSION_MISMATCH_RISK` 现由 1号位 完全不产出** —— `DomainSolveRequest`
    ///   中不存在「版本不一致」类输入（无 `CurrentActivePlanVersions` / `NewPlanVersions`），
    ///   1号位 无从判定版本风险；该码的真实生产者 = 2号位（跨域发布/版本链比对侧）。
    /// </summary>
    private string DiagnoseDelayReason(
        LogicalProductionDemand demand,
        List<FinalTaskDraft> demandTasks,
        ConstraintContext constraints,
        DomainSolveRequest request,
        out string evidenceType,
        out string extraDetails)
    {
        // 细分证据默认空；仅「跨域资源阻挡」分支填值（逗号前缀的 JSON 片段，拼入 details）。
        extraDetails = string.Empty;

        var lastTask = demandTasks.OrderBy(t => t.PlannedEndTime).Last();

        // 1. 锁定约束：冻结区（FIRM/FROZEN）锁定 vs 其它执行锁
        //    P1-07：复合键 (DraftId, OperationCode)，按 DraftId 匹配该需求任一锁定锚点。
        var locked = constraints.LockedTasks.Values
            .FirstOrDefault(t => t.DraftId == demand.LogicalDemandKey);
        if (locked != null)
        {
            // 15 码对齐：15 码中唯一的锁码 = FROZEN_ZONE_LOCK（冻结区锁）。
            // 原按 ConstraintType 分投 FIRM_FROZEN_CONSTRAINT / LOCK_CONSTRAINT 两码，二者均非 15 码；
            // FIRM / FROZEN / 其它执行锁业务上同属「被锁定的时区不可动」⇒ 统一并入 FROZEN_ZONE_LOCK。
            evidenceType = "LOCK";
            return "FROZEN_ZONE_LOCK";
        }

        // 2. 共享资源/跨域可用性阻挡：**跨域与同域一律 RESOURCE_CAPACITY_WAIT**（0号位 2026-10-07 裁定）。
        //    `CROSS_DOMAIN_VERSION_MISMATCH_RISK` 在《字段说明 v5.1.9:4843》中专指**跨域版本不一致风险**
        //    （强制 ObjectType=DOMAIN + FailedDomainKeys/CurrentActivePlanVersions 等 Evidence）；
        //    其它 Domain 的 ACTIVE Task 合法占用共享资源，本质是「资源不可用 / 资源等待」，非版本不一致。
        //    1号位 的 DomainSolveRequest 中无任何版本不一致类输入 ⇒ 1号位 **不产** 该码（真实生产者 = 2号位）。
        //    细分由 EvidenceJson.evidenceType 承载（CROSS_DOMAIN_RESOURCE_BLOCK / SHARED_RESOURCE），
        //    details 携 SourceDomainKey / SourcePlanVersionId / ResourceId。
        if (HasResourceBlockOverlap(demandTasks, request, out var matchedBlock, out var crossDomain))
        {
            evidenceType = crossDomain ? "CROSS_DOMAIN_RESOURCE_BLOCK" : "SHARED_RESOURCE";
            if (matchedBlock != null)
            {
                var srcPlanVersion = matchedBlock.SourcePlanVersionId.HasValue
                    ? matchedBlock.SourcePlanVersionId.Value.ToString()
                    : "null";
                extraDetails =
                    $",\"SourceDomainKey\":\"{matchedBlock.SourceDomainKey}\"," +
                    $"\"SourcePlanVersionId\":{srcPlanVersion}," +
                    $"\"ResourceId\":{matchedBlock.ResourceId}";
            }
            return "RESOURCE_CAPACITY_WAIT";
        }

        // 3. Setup 边际延期：去掉 Setup 即不延期 → 延期由 Setup 时间决定
        if (lastTask.SetupTime > 0m &&
            lastTask.PlannedEndTime.AddMinutes(-(double)lastTask.SetupTime) <= constraints.EffectiveDue(demand))
        {
            // 15 码对齐：换型受限不出专属码（基线 v1.8:1297「不新增 Setup 专属 ReasonCode」）
            // ⇒ 归入资源等待（换型占用的是资源时间）。
            evidenceType = "SETUP";
            return "RESOURCE_CAPACITY_WAIT";
        }

        // 4. 物料可用时间
        if (constraints.MaterialAvailability.TryGetValue(demand.AllocationSequence, out var segments))
        {
            var earliestMaterialTime = segments.Min(s => s.AvailableTime);
            var firstTaskStart = demandTasks.Min(t => t.PlannedStartTime);

            if (firstTaskStart < earliestMaterialTime)
            {
                evidenceType = "MATERIAL";
                return "MATERIAL_SHORTAGE";
            }
        }

        // 5. 资源容量不足
        // 非资源 Task 跳过：ResourceId 为 null（UNCONSTRAINED/WAIT_ONLY）不占资源，不进入资源键统计
        var resourceIds = new List<int>();
        foreach (var task in demandTasks)
        {
            if (task.ResourceId is int rid && !resourceIds.Contains(rid))
            {
                resourceIds.Add(rid);
            }
        }
        var resourceUtilization = CalculateResourceUtilization(
            demandTasks,
            constraints,
            request.PlanningStart,
            request.PlanningEnd);

        if (resourceIds.Any(rid => resourceUtilization.ContainsKey(rid) && resourceUtilization[rid] > request.StrategySnapshot.SolverStrategy.CapacityShortageUtilizationThreshold))
        {
            evidenceType = "CAPACITY";
            return "RESOURCE_CAPACITY_WAIT";
        }

        // 6. 工艺路线资格降级：任务落到的资源不在该工序资格集内（Routing Fallback）
        foreach (var task in demandTasks)
        {
            // 非资源 Task 跳过：ResourceId 为 null 时无落点资源，不存在「资格降级」判定
            if (task.ResourceId is not int taskResourceId) continue;

            // 0号位 2026-09-29 裁决 §5.3：资格键升维为 EligibilityLookupKey（含 ProductionDepartmentId）。
            // FinalTaskDraft 无部门字段 ⇒ 从本次请求的 Routing 图按 (StageCode, OperationCode) 反查节点取部门。
            // 反查不到时按旧行为跳过该项判定（不新增失败路径）。
            if (!TryResolveEligibilityKey(task, constraints, out var eligibilityKey))
            {
                continue;
            }

            if (constraints.OperationResourceEligibility.TryGetValue(eligibilityKey, out var eligibleResources) &&
                !eligibleResources.Contains(taskResourceId))
            {
                evidenceType = "ROUTING";
                return "ROUTING_FALLBACK";
            }
        }

        // 7. 资源日历不可用造成的等待（2026-10-07 新增）
        //    判据（与 Phase2 槽搜索同源，非猜测）：该需求某 Task 存在等待空档
        //    [可开工时刻, 本Task开始)，且该空档内**存在资源日历未覆盖（不可用）的时间**
        //    —— 即若日历连续可用，该 Task 本可更早开工。
        //    对照：空档内资源**全程可用但被占用** ⇒ 属容量/占用因（第 2 / 5 步已判），不在此列。
        //    ⚠ 空档起点取 max(前序结束, 计划期起点, **该资源首个可用窗起点**) —— 最后一项是关键：
        //      否则「计划期起点到首个开工窗」这段（厂未开门）会被误算成等待，凡首窗开工的延期都会误报。
        //    位置：置于**兜底之前、所有正向归因之后** ⇒ 只把原先笼统的 PRECEDENCE_WAIT（"前序延期或其他约束"）
        //    细化为真实原因，**不抢占**锁 / 跨域 / 阻挡 / 换型 / 物料 / 容量 / 资格 任何正向归因。
        //    ReasonCode 仍取 15 码中语义最近者 RESOURCE_CAPACITY_WAIT（日历不可用 ⇒ 资源在需要时不可用），
        //    细分由 EvidenceJson.evidenceType="CALENDAR" 承载（v5.1.9:4836 evidenceType 无值域枚举、
        //    details schema 阶段一不冻结；与 v1.8:1297「细分走轻量通道、不新增专属 ReasonCode」同构）。
        foreach (var task in demandTasks)
        {
            // 非资源 Task 跳过：ResourceId 为 null 不占资源，不存在日历等待
            if (task.ResourceId is not int calResourceId) continue;
            if (!constraints.ResourceCalendars.TryGetValue(calResourceId, out var calWindows) ||
                calWindows.Count == 0)
            {
                continue;
            }

            // 可开工时刻 = 本需求中「结束于本 Task 开始之前」的最近一个 Task 的结束时刻；无则计划期起点。
            var ready = request.PlanningStart;
            foreach (var prev in demandTasks)
            {
                if (prev.PlannedEndTime <= task.PlannedStartTime && prev.PlannedEndTime > ready)
                {
                    ready = prev.PlannedEndTime;
                }
            }

            // 空档起点不早于该资源首个可用窗起点（否则「厂未开门」段会被误算为等待）
            var firstWindowStart = calWindows.Min(w => w.Start);
            if (ready < firstWindowStart) ready = firstWindowStart;

            if (task.PlannedStartTime <= ready) continue;

            // 空档 [ready, task.PlannedStartTime) 被资源可用窗覆盖的分钟数
            var gapMinutes = (task.PlannedStartTime - ready).TotalMinutes;
            var coveredMinutes = calWindows
                .Where(w => w.End > ready && w.Start < task.PlannedStartTime)
                .Sum(w =>
                {
                    var s = w.Start > ready ? w.Start : ready;
                    var e = w.End < task.PlannedStartTime ? w.End : task.PlannedStartTime;
                    return (e - s).TotalMinutes;
                });

            if (coveredMinutes < gapMinutes)
            {
                evidenceType = "CALENDAR";
                return "RESOURCE_CAPACITY_WAIT";
            }
        }

        // 8. 默认原因：前序延期或其他约束
        evidenceType = "PRECEDENCE";
        return "PRECEDENCE_WAIT";
    }

    /// <summary>
    /// 由 FinalTaskDraft 反查资格键 EligibilityLookupKey。
    /// MaterialId 取任务自身；ProductionDepartmentId / RouteCode 取本次请求 Routing 图内
    /// 按 (StageCode, OperationCode) 命中的节点（FinalTaskDraft 无部门字段，只能反查）。
    /// 反查失败返回 false —— 调用方按既有语义跳过该项资格判定，不猜、不新增失败路径。
    /// </summary>
    private static bool TryResolveEligibilityKey(
        FinalTaskDraft task,
        ConstraintContext constraints,
        out EligibilityLookupKey key)
    {
        key = default;

        // Path-aware 解析（0号位 2026-10-07 裁决 Q-3，必须整改）：按**任务自身** (RouteCode, PathId) 取图。
        // 旧实现按 task.MaterialId 取「物料唯一图」——隐含「一物料一图」，多 Path 下会**串 Path**
        // （拿另一条备选路径的节点接本任务工序）。缺 RouteCode/PathId ⇒ **Fail Closed**（不猜唯一 Path）。
        if (!constraints.TryGetRoutingGraph(task.MaterialId, task.RouteCode, task.PathId, out var graph))
        {
            return false;
        }

        if (!graph.Operations.TryGetValue(
                OperationNodeKey.Of(task.StageCode, task.OperationCode), out var node))
        {
            return false;
        }

        key = new EligibilityLookupKey(
            task.MaterialId, node.ProductionDepartmentId, node.RouteCode, node.PathId, node.OperationCode);
        return true;
    }

    /// <summary>
    /// P1-03修复：判断需求任务是否与「共享资源/跨域」不可移动阻挡块重叠。
    /// 直接读 request 的跨域块来源（保留 SourceDomainKey 语义，避免 ConstraintContext.ResourceBlocks 丢域信息）：
    /// - SourceDomainKey 非空 → 跨域阻挡（调用方投 RESOURCE_CAPACITY_WAIT + evidenceType=CROSS_DOMAIN_RESOURCE_BLOCK）；
    /// - SourceDomainKey 为空 → 同域共享资源阻挡（调用方投 RESOURCE_CAPACITY_WAIT + evidenceType=SHARED_RESOURCE）。
    /// <paramref name="matchedBlock"/> 出参回传命中的原始 <see cref="ResourceBlock"/>，
    /// 供调用方在 EvidenceJson.details 中携带 SourceDomainKey / SourcePlanVersionId / ResourceId。
    /// </summary>
    private static bool HasResourceBlockOverlap(
        List<FinalTaskDraft> demandTasks,
        DomainSolveRequest request,
        out ResourceBlock? matchedBlock,
        out bool crossDomain)
    {
        matchedBlock = null;
        crossDomain = false;

        var blocks = new List<(ResourceBlock Block, bool Cross)>();

        if (request.CandidateContext?.ExternalDomainResourceBlocks != null)
        {
            foreach (var b in request.CandidateContext.ExternalDomainResourceBlocks)
            {
                blocks.Add((b, !string.IsNullOrEmpty(b.SourceDomainKey)));
            }
        }

        if (request.UpstreamDomainResourceBlocks != null)
        {
            foreach (var b in request.UpstreamDomainResourceBlocks)
            {
                blocks.Add((b, !string.IsNullOrEmpty(b.SourceDomainKey)));
            }
        }

        foreach (var task in demandTasks)
        {
            // 非资源 Task 跳过：ResourceId 为 null 不占资源，不可能与任何资源阻挡块重叠
            if (task.ResourceId is not int taskResourceId) continue;

            foreach (var (block, cross) in blocks)
            {
                if (block.ResourceId == taskResourceId &&
                    Overlaps(task.PlannedStartTime, task.PlannedEndTime, block.StartTime, block.EndTime))
                {
                    matchedBlock = block;
                    crossDomain = cross;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// P1-03修复：时间区间重叠判定（左闭右开）。
    /// </summary>
    private static bool Overlaps(DateTime s1, DateTime e1, DateTime s2, DateTime e2)
        => s1 < e2 && s2 < e1;

    /// <summary>
    /// 计算资源利用率
    /// </summary>
    /// <remarks>
    /// OWN-P0-01（2026-10-10）：<paramref name="planningEnd"/> **不再单独决定分母**（90 天非资源时间终点）；
    /// 形参保留为**统计窗的默认上界**（见下方 AUD-1-R03），调用点不变。
    /// </remarks>
    private Dictionary<int, decimal> CalculateResourceUtilization(
        List<FinalTaskDraft> tasks,
        ConstraintContext constraints,
        DateTime planningStart,
        DateTime planningEnd)
    {
        var utilization = new Dictionary<int, decimal>();

        var tasksByResource = tasks.GroupBy(t => t.ResourceId);

        // ── AUD-1-R03（0号位 2026-10-10《APS_V1_2_20261010.md》§4，RISK_UNVERIFIED）：**合理统计区间** ──
        //   复审疑点（本号位核对：**成立**）：OWN-P0-01 把分母从 `[planningStart, planningEnd]` 扩到
        //     「全部未来日历窗」（上界 = ∞）后，若维护日历延伸到数年之后，分母被**无限放大** ⇒ 利用率被
        //     **稀释**到近 0 ⇒ Auto 瓶颈识别（`> BottleneckUtilizationThreshold`）失效、产能短缺根因失真。
        //   修法 = 统计窗 **下界不变**（`planningStart`）、**上界 = max(planningEnd, 本轮全部任务的最晚完成)**：
        //     · 全部任务落在 `[planningStart, planningEnd]` 内（既有测试与常见几何）⇒ 与**整改前逐字相同**（零回归）；
        //     · 任务合法落到 90 天之后（P0-01 修复后的新几何）⇒ 窗口随真实负荷延长，**既不高估（不 >1）也不稀释**；
        //     · **不**把 90 天重新当作排程硬截止 —— 这里只是**诊断统计窗**，不参与任何
        //       可行性 / 搜索 / Merge / Repair / Unscheduled 判定（与 `FindForwardSlot` 的时间口径不冲突）。
        var horizonEnd = planningEnd;
        foreach (var t in tasks)
        {
            if (t.PlannedEndTime > horizonEnd) horizonEnd = t.PlannedEndTime;
        }

        foreach (var group in tasksByResource)
        {
            // 非资源 Task 跳过：ResourceId 为 null 不占资源，不进入利用率/日历查表
            if (group.Key is not int resourceId) continue;

            // 计算总占用时间
            var totalOccupiedMinutes = group
                .Sum(t => (t.PlannedEndTime - t.PlannedStartTime).TotalMinutes);

            // 计算资源可用时间
            var availableMinutes = 0.0;
            if (constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar))
            {
                foreach (var window in calendar)
                {
                    // 与统计窗 `[planningStart, horizonEnd]` 求交后累加（空交 ⇒ 不计）。
                    var start = window.Start > planningStart ? window.Start : planningStart;
                    var end = window.End < horizonEnd ? window.End : horizonEnd;
                    if (end > start)
                    {
                        availableMinutes += (end - start).TotalMinutes;
                    }
                }
            }

            if (availableMinutes > 0)
            {
                utilization[resourceId] = (decimal)(totalOccupiedMinutes / availableMinutes);
            }
        }

        return utilization;
    }
}

/// <summary>
/// 诊断结果（Phase 3 输出）
/// </summary>
internal class DiagnosticsResult
{
    public List<ScheduleExplanationFact> ExplanationFacts { get; set; } = new();
    public List<string> DelayedTaskIds { get; set; } = new();
    public List<string> UnscheduledDemandKeys { get; set; } = new();
    public List<int> BottleneckResourceIds { get; set; } = new();
}
