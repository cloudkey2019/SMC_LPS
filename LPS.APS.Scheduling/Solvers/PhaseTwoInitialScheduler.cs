using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// Phase 2: 初始有限产能排程
/// 文档：《APS_V1_1号位有限产能排程开发实施包_v1.2_20260906_PI_Position执行起点上下文冻结对齐版.md》§六 Phase 2
///
/// 职责：
/// - 按冻结策略执行初始排程（Forward/Backward/Mixed）
/// - 优先形成一版可行计划
/// - 为每个 LogicalProductionDemand 生成初步的 FinalTaskDraft
/// </summary>
internal class PhaseTwoInitialScheduler
{
    /// <summary>
    /// 执行初始有限产能排程
    /// </summary>
    public InitialScheduleResult Schedule(
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        var result = new InitialScheduleResult();

        // ── V1_4 NEW-04：**本 Run 唯一**的「新增 Stage 执行批」累计账 ──
        //   生命周期 = 本次 `Schedule` 调用（= 一次 Run）⇒ 同一 Run 内先后形成的 C 桶 Stage 批
        //   在 B-009 门禁上**累计**消费同一 (PI × 物料 × Stage) 的 Stage 自由余额（NEW-04 判词场景：
        //   两条需求 30 / 20、Stage 自由余额 40 ⇒ 逐条过闸但累计 50 越界 ⇒ 必须拦住第二条）。
        //   试排隔离用 `Clone()`；需求未落定时按 `Mark()` / `RollbackTo()` 精确回滚。
        var piStageLedger = new PiStageCommitLedger();

        // 获取排程方向参数
        var direction = request.StrategySnapshot.Parameters.SchedulingDirection;

        // P1-02（StageOverlap）：读冻结大工艺重叠参数（AllowOverlap 开关 + TransferBatchQty 转运批量）
        var stageOverlap = request.StrategySnapshot.SolverStrategy.StageOverlap;

        // P1-02（OnTimeTarget，0号位 20260916 裁决）：整单按期率是冻结的 Level 2 目标——TargetPercent 是「整单按期率保护门槛」而非权重，
        // 达标前禁止 Setup/WIP/利用率等次级目标反压；IsPrimaryObjective 在 V1 恒按 true 语义、不用于开关目标层级。
        // 当前 Phase2 为确定性单次排程（无多候选择优），故以 RequiredAvailableTime（EDD）排序作为「先保按期」的贪心近似；
        // 精确的「达标门槛 → 次级优化」候选比较语义留待候选方案比较框架引入后消费。

        // P0-07修复：构建锁定任务的DraftId集合，用于排除已固定的需求
        // P1-07：复合键 (DraftId, OperationCode)，提取 DraftId 去重。
        var lockedDraftIds = new HashSet<string>(constraints.LockedTasks.Keys.Select(k => k.DraftId));

        // 跨物料时序硬约束（任务喂任务，方案A）：子先父后分层遍历。
        // 若存在 BOM 依赖环，Phase1 已标记，此处直接判技术失败。
        if (constraints.CrossMaterialHasCycle)
        {
            result.TechnicalFailure = true;
            result.TechnicalFailureReason = "跨物料 BOM 依赖存在环（任务喂任务）";
            return result;
        }

        // 按跨物料分层顺序排序（子层先、父层后，层内按 DemandSequence）；无跨物料关系时等价于旧平铺。
        var demandByKey = request.LogicalProductionDemands
            .ToDictionary(d => d.LogicalDemandKey);

        List<LogicalProductionDemand> sortedDemands;
        if (constraints.CrossMaterialOrder.Count > 0)
        {
            sortedDemands = constraints.CrossMaterialOrder
                .Select(key => demandByKey.GetValueOrDefault(key))
                .Where(d => d != null)
                .ToList()!;
        }
        else
        {
            // P1-02（OnTimeTarget）：整单按期率为 Level 2 冻结目标，恒高于次级优化，故始终按交期（EDD）优先排序，
            // DemandSequence 仅作并列稳定 tiebreaker（0号位 20260916：IsPrimaryObjective 不用于开关目标层级）。
            sortedDemands = request.LogicalProductionDemands
                .OrderBy(d => constraints.EffectiveDue(d))   // M5 第一批：Run 级覆盖交期 ?? RequiredAvailableTime
                .ThenBy(d => d.DemandSequence)
                .ToList();
        }

        // P0-08：同 PI 连续份额先于自由份额。稳定重排，仅在同 PI 组内把连续份额移到自由份额之前，
        // 跨 PI 相对顺序按「该 PI 首次出现位置」保持，避免破坏跨物料父子拓扑序。
        sortedDemands = OrderContinuityFirst(sortedDemands);

        // 资源占用追踪：ResourceId → 已占用时间窗列表
        var resourceOccupancy = InitializeResourceOccupancy(request.Resources, constraints);

        // P0-07修复：先将锁定任务直接继承为FinalTask（原地保留）
        // 第8轮P0-01修复：使用LockedQuantity和Stage/Operation，不再写空字符串
        // 2026-10-07 P0-01 二次整改（0号位 审核）：**禁止再硬编码 RouteCode="DEFAULT" / PathId=1**。
        //   锁定任务经 (StageCode, OperationCode) 反查真实路径身份；
        //   图缺失 / 节点缺失 ⇒ **Fail Closed**（不产 Task、不编造伪身份、不猜 Path，见下方 Q-3 注释）。
        foreach (var lockedTask in constraints.LockedTasks.Values)
        {
            // 从对应的Demand获取数量、物料等信息
            var demand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == lockedTask.DraftId);

            // 真实路径身份反查（v1.6 §1：业务真值 RouteCode/PathId 必须真实，禁 DEFAULT/1）：
            //   (StageCode, OperationCode) → OperationNode。
            //   · 需求自带固定路径（A/B 桶，v1.6 `:26`）⇒ 用**该固定路径**的图解析（Path-aware）；
            //   · 无固定路径 ⇒ 回落 `TryGetSingleRoutingGraph`（**仅单路径物料可用**；多路径时该方法
            //     返回 false ⇒ 身份保持 null，**不猜**）。
            //   ⚠ 正式载体（ExecutionConstraint 补 RouteCode/PathId）**尚未冻结**（v1.6 未列此字段）
            //     ⇒ 本号位不造字段。多路径 + 无固定路径的锁定任务解不出身份 ⇒ **Fail Closed**
            //     （记入 `UnscheduledDemandKeys`、不产 Task），**不回传 null 伪身份**。
            string? lockedRouteCode = null;
            long? lockedPathId = null;
            RoutingGraph? lockedGraph = null;
            if (demand != null)
            {
                if (!string.IsNullOrEmpty(demand.RouteCode) || demand.PathId is not null)
                {
                    constraints.TryGetRoutingGraph(demand.MaterialId, demand.RouteCode, demand.PathId, out lockedGraph);
                }
                else
                {
                    constraints.TryGetSingleRoutingGraph(demand.MaterialId, out lockedGraph);
                }
            }

            OperationNode? lockedNode = null;
            if (lockedGraph != null)
            {
                lockedGraph.Operations.TryGetValue(
                    OperationNodeKey.Of(lockedTask.StageCode, lockedTask.OperationCode), out lockedNode);
            }

            // Q-3 Fail Closed（0号位 2026-10-07 裁决 Q-3 `:186`：「Route/Path 缺失时应 Fail Closed 或
            //   进入明确异常，**不得跨 Path 寻找替代节点**」；同件职责表 `:357`：「A/B 固定 Route/Path …
            //   **缺值 Fail Closed**」）：
            //   锁定任务按 A/B「固定真实 RouteCode/PathId」语义处理 —— 必须能解出**真实**路径身份才产出
            //   FinalTask。解不出身份（图缺失 / 节点缺失 / 需求缺失）⇒ **不产伪身份 Task**，明确记为未排程
            //   （与需求级 Fail Closed 同一口径：`UnscheduledDemandKeys` + 不产 Task），
            //   **不得**回退 `TryGetSingleRoutingGraph` 猜唯一 Path、**不得**跨 Path 找替代节点。
            if (lockedNode == null)
            {
                result.UnscheduledDemandKeys.Add(lockedTask.DraftId);

                // ── P0-01（0号位 2026-10-09 第三轮复审 §二）：**区分两类失败，阻断假成功** ──
                //   · 「普通业务无法排下」（产能/日历/物料确实不足）⇒ 只记 `UnscheduledDemandKeys`，
                //     属**业务结果**，Success 仍可为 true；
                //   · 「**已有已锁定执行 Task 身份不可恢复**」= 锁定任务（既成事实锚点）在本次输入里
                //     解不出真实 RouteCode/PathId（图缺失 / 节点缺失 / 对应需求缺失）⇒ 这是**输入完整性
                //     问题**，不是业务结果 ⇒ 必须经**既有技术失败载体**上报。
                //   ⚠ 否则会出现复审点名的「**无锁定 Task、却 Success=true**」假成功：
                //     `PhaseFiveCompression.cs` 的 `Success = !technicalFailure` **只看本标志**
                //     （一般业务 Unscheduled 不会使其为 false）。
                //   ⚠ 只**复用**既有载体（`TechnicalFailure` / `TechnicalFailureReason`，与 `:46`/`:354` 同口径），
                //     **不新增**数据库字段、**不自造** ReasonCode 枚举；真实 Route/Path 输入由 2号位 保证
                //     （复审 §二 P0-01 明文：「由2号位保证真实Route/Path输入，不需1号位新增数据库字段」）。
                var identityGap = lockedGraph is null
                    ? "Route/Path 图缺失"
                    : "图中无该 (StageCode, OperationCode) 节点";
                var identityFailure =
                    $"锁定执行Task身份不可恢复：DraftId={lockedTask.DraftId}, "
                    + $"MaterialId={(demand is null ? "(对应需求缺失)" : demand.MaterialId.ToString())}, "
                    + $"StageCode={lockedTask.StageCode}, OperationCode={lockedTask.OperationCode}（{identityGap}）";
                result.TechnicalFailure = true;
                result.TechnicalFailureReason = string.IsNullOrEmpty(result.TechnicalFailureReason)
                    ? identityFailure
                    : result.TechnicalFailureReason + "；" + identityFailure;

                continue;
            }

            lockedRouteCode = lockedNode.RouteCode;
            lockedPathId = lockedNode.PathId;

            var inheritedTask = new FinalTaskDraft
            {
                FinalDraftId = Guid.NewGuid().ToString(),
                SourceDraftId = lockedTask.DraftId,
                MaterialId = demand?.MaterialId ?? 0,
                FactoryId = demand?.FactoryId ?? 0,
                StageCode = lockedTask.StageCode ?? string.Empty,
                OperationCode = lockedTask.OperationCode ?? string.Empty,
                TaskType = "PRODUCTION", // P0-16修复：锁定任务仍是生产Task，不是ConstraintType
                ResourceId = lockedTask.ResourceId,
                ResourceCode = GetResourceCode(lockedTask.ResourceId, constraints),
                // P0-01 整改：真实路径身份（反查失败时为 null，不再 DEFAULT/1）
                RouteCode = lockedRouteCode,
                PathId = lockedPathId,
                Quantity = lockedTask.LockedNetOutputQty ?? lockedTask.LockedQuantity ?? demand?.NetOutputQty ?? 0m,
                PlannedProcessQty = lockedTask.LockedPlannedProcessQty ?? lockedTask.LockedQuantity ?? demand?.PlannedProcessQty ?? 0m,
                UOM = demand?.UOM ?? string.Empty,
                PlannedStartTime = lockedTask.LockedStart,
                PlannedEndTime = lockedTask.LockedEnd,
                SetupTime = 0m,
                SetupSource = null,   // SetupSource 填充：锁定继承任务无 1号位 Setup 解析来源 → null（2号位 落库留空）
                Priority = demand?.DemandSequence ?? 0,
                IsVirtual = false,
                ExecutionLockId = null, // TODO: 关联ExecutionConstraint.Id
                // v1.6 §1：FinalTask 一律原样回传 ContinuationKey；路径身份可解出时才生成执行批键
                //（与上方「不编造 RouteCode/PathId」同口径 —— 解不出身份就不给批键，不留伪真值）。
                // P0-02 后键域改为 (需求键, 批序号)：无真实 Path ⇒ 该 Task 不构成「一条完整 Path 的执行批」
                // ⇒ 仍不给批键（守卫理由由「身份反推」改为「无完整 Path 不构成执行批」，语义更贴合冻结口径）。
                ContinuationKey = demand?.ContinuationKey,
                ExecutionBatchDraftKey = lockedPathId is null
                    ? null
                    : ExecutionBatchKey(lockedTask.DraftId)
            };
            result.ScheduledTasks.Add(inheritedTask);
        }

        // P1-02 item1 接线（阶段二）：产品时间线以继承的锁定 Task 重置种子（它们是可追溯上一产品，v1.2 §14.3）。
        // Schedule 每次进入都重置——Phase4 Fallback 重跑本方法时避免重复累计。
        constraints.ProductTimeline = ResourceProductTimeline.FromTasks(result.ScheduledTasks);

        // P0-08：记录各 PI 连续份额的完成时间，供同 PI 自由份额做「不得早于连续份额」的时间下界。
        // 先登记已锁定的连续份额（原地继承，不参与后续排程循环）。
        var continuityCompletionByPI = new Dictionary<string, DateTime>();
        foreach (var lockedTask in constraints.LockedTasks.Values)
        {
            var lockedDemand = request.LogicalProductionDemands
                .FirstOrDefault(d => d.LogicalDemandKey == lockedTask.DraftId);
            if (lockedDemand?.IsContinuation == true && !string.IsNullOrEmpty(lockedDemand.ProductionInstructionNo))
            {
                var pi = lockedDemand.ProductionInstructionNo!;
                if (!continuityCompletionByPI.TryGetValue(pi, out var existing) || lockedTask.LockedEnd > existing)
                    continuityCompletionByPI[pi] = lockedTask.LockedEnd;
            }
        }

        // 逐个需求排程
        // 第4轮Merge修复：记录Demand到Task的份额追溯，支持合批
        var allocationTaskShare = new Dictionary<string, List<(string DemandKey, decimal ShareQty)>>();

        // 跨物料时序（块3/块4）：记录每个已排需求的最晚完成时间，供父件取子件完成时间作为动态物料下界。
        var demandCompletion = new Dictionary<string, DateTime>();

        foreach (var demand in sortedDemands)
        {
            // B-1（0号位 2026-09-29 裁决 §2.3）：StageSeq 全序冲突的物料
            // **不生成可能顺序错误的正式计划** —— 直接置 Unscheduled（Reason 由 Phase5 补 STAGE_SEQUENCE_CONFLICT）。
            // 注意：此处不删 Routing（Routing 本身合法，问题在 StagePath 数据），只拒绝出计划。
            if (constraints.StageSequenceConflictMaterialIds.Contains(demand.MaterialId))
            {
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
                continue;
            }

            // 第8轮P0-01修复：部分数量冻结处理
            // 第9轮P0-01完整闭环：真正减掉锁定数量，只排剩余份额
            // 如果该Demand有锁定任务，检查锁定数量：
            // - 锁定数量 >= Demand总量：完全锁定，跳过
            // - 锁定数量 < Demand总量：部分锁定，只排剩余部分
            LogicalProductionDemand actualDemand = demand;

            if (lockedDraftIds.Contains(demand.LogicalDemandKey))
            {
                // P1-07：同 LogicalDemand 可有多操作锚点（多 ExecutionConstraint），
                // 按复合键 (DraftId, StageCode, OperationCode) 聚合该需求所有锁定任务的锁定数量
                //   （P0-02：键域含 StageCode —— 工序身份 = StageCode + OperationCode，不得假设 OperationCode 全局唯一）。
                var demandLockedTasks = constraints.LockedTasks.Values
                    .Where(t => t.DraftId == demand.LogicalDemandKey)
                    .ToList();

                // ── P0-03（0号位 2026-10-09《APS_V1_2_20261009.md》§三）：**不得按锁定记录条数求和** ──
                //   同一 LogicalDemand 下的多条锁定记录（P0-02 后键域 = (DraftId, StageCode, OperationCode)，
                //   同一工序至多一条）描述的是**同一执行批沿工序流动的同一份物理数量**，不是多份互不重叠的
                //   独立产出。旧实现直接 `Sum` ⇒ 把同一数量按工序重复扣除：两工序各报 10、需求 20 时
                //   锁定覆盖被判为 20 ⇒ 「剩余 10」被误判为 0 ⇒ **静默漏排**。
                //   冻结身份原则：多 Operation 同一执行批不得重复扣业务量。
                //
                //   覆盖量口径（单执行批）：取该需求下各锁定工序记录的**最大覆盖量**。
                //     推导：单批数量 Q 沿工序流动时，任一工序持有的数量 ≤ Q，且整批必在某工序上被完整持有
                //     ⇒ max(各工序锁定量) = 该执行批的实际数量 Q。按条数求和 = 把 Q 重复计了工序数次。
                //
                //   并行多 Slice 的**独立性**由独立 Demand（各自 LogicalDemandKey / AllocationSequence）表达，
                //   外层逐需求循环已保证每个 Slice 各计一次；同需求内不存在「独立 Slice」这一载体。
                //
                //   ⚠ **未闭合的输入契约缺口**（如实登记，属 2号位 职责，本号位不自行造字段、不造假判据）：
                //     契约未提供**分量身份**（Slice/Batch 键）与**按工序序的产量语义**（YIELD 下各工序净产出
                //     本应逐工序不同）。当前口径仅在「同需求单批流动」前提下成立；若输入实际含同需求多批
                //     部分重叠，`Max` **低估**覆盖 ⇒ **多排**。
                //     **0号位 2026-10-09《APS_V1_4_20261009.md》§三 CONTRACT-P0 明判**：
                //       「**不能用『保守多排』定义为合法业务结果**；制造计划多排与漏排一样属于数量真相违规。」
                //     ⇒ 故此处**不得**把 `Max` 当作已达标口径 —— 本形态**未闭合**，件已出致 2号位
                //       （《APS_V1_20261009_锁定分量身份与跨Stage同名工序依赖边_1号位致2号位_技术说明》§三 G-2）。
                //     **为何不能自行落 Fail Closed**（本号位已逐路核验）：
                //       ① 两处载体（`Core/Dto/DomainSolveRequest.cs:272-294` `ExecutionConstraint` 与本文件
                //          `LockedTaskConstraint`）**均无**分量身份字段；
                //       ② 唯一候选判别器 `TaskKey` **不可用** —— 其语义是「跨轮次识别同一 Task」
                //          （`DomainSolveRequest.cs:293`），且本模型**每工序各自一个 Task** ⇒ 同一份单批锁定的
                //          `OP10`/`OP20` 两条记录 `TaskKey` **本就不同** ⇒ 相异不能推出「分量相异」（会误杀合法输入）；
                //       ③ 数量相等/相异两个方向**都不成立**（复审例子：两个独立锁定批**各 3 件**，数量相等）；
                //       ④ 「同一需求 >1 条锁定记录一律 Fail Closed」会**误杀**契约允许的
                //          「多个 Operation 指向同一份数量」合法形态。
                //     ⇒ 唯一不误判且不造假字段的处置 = 请求 2号位 消歧（件已出，等待回执）。
                var lockedNetOutputQty = demandLockedTasks.Count == 0
                    ? 0m
                    : demandLockedTasks.Max(t => t.LockedNetOutputQty ?? t.LockedQuantity ?? demand.NetOutputQty);
                var lockedPlannedProcessQty = demandLockedTasks.Count == 0
                    ? 0m
                    : demandLockedTasks.Max(t => t.LockedPlannedProcessQty ?? t.LockedQuantity ?? demand.PlannedProcessQty);

                // ── P0-03（0号位 2026-10-09《APS_V1_3_20261009.md》§三）+ NEW-P1-02（《APS_V1_4_20261009.md》§三）
                //    **锁定数量的完整输入有效性判定必须先于「完全锁定直接跳过」** ──
                //   复审判词（成立）：旧实现把校验放在 `if (lockedNetOutputQty >= demand.NetOutputQty) continue;`
                //   **之后** ⇒ 超界/负量都必先 `continue`，Fail Closed 分支**不可达**。
                //   现顺序（V1_3 §六.1「先受控 Fail Closed，再判断全量覆盖」）：
                //     ① **非负**：任一锁定记录任一锁定量为负 ⇒ 受控 Fail Closed。
                //        （V1_4 §三 NEW-P1-02 点名的漏检：`需求净产出=10 / 锁定净产出=-1` 曾被算成「剩余 11」
                //          而在本闸门放行 ⇒ 不得以 Phase5 事后检查替代输入校验。）
                //     ② **上界**：任一覆盖量**超出**其需求总量 ⇒ 受控 Fail Closed（口径不自洽）。
                //     ③ **双数量一致性**（只判「可排程性」，**不推导良率**——V1_4 §三「不能仅凭数值大小推导
                //        良率关系」）：
                //        - 净产出**仍有剩余**、加工量**已无剩余** ⇒ 剩余净产出无任何加工承载 ⇒ 计划**不可表达** ⇒ Fail Closed；
                //        - 净产出**已无剩余**、加工量**仍有剩余** ⇒ 只有一侧全量覆盖，该组合的业务语义未经
                //          2号位 书面定义 ⇒ **不擅自当作合法而跳过** ⇒ Fail Closed（待契约后按其口径放行）。
                //     ④ 仅当**两类覆盖量均全量**（各 `>=` 其需求总量）才是「完全锁定」⇒ 跳过排程。
                //   依据 V1_3 §三：「覆盖量**严格等于**已声明需求且**其它量也合法**才能跳过」。
                var negativeLockRecord = demandLockedTasks.FirstOrDefault(t =>
                    (t.LockedQuantity ?? 0m) < 0m
                    || (t.LockedNetOutputQty ?? 0m) < 0m
                    || (t.LockedPlannedProcessQty ?? 0m) < 0m);
                if (negativeLockRecord != null || lockedNetOutputQty < 0m || lockedPlannedProcessQty < 0m)
                {
                    throw new SolverInputContractException(
                        "锁定数量输入非法：锁定量为负（口径不自洽）。" +
                        $"LogicalDemandKey={demand.LogicalDemandKey}, " +
                        $"锁定量={negativeLockRecord?.LockedQuantity}, " +
                        $"锁定净产出={negativeLockRecord?.LockedNetOutputQty}, " +
                        $"锁定加工量={negativeLockRecord?.LockedPlannedProcessQty}, " +
                        $"锁定工序={negativeLockRecord?.StageCode}/{negativeLockRecord?.OperationCode}, " +
                        $"聚合净产出覆盖={lockedNetOutputQty}, 聚合加工量覆盖={lockedPlannedProcessQty}");
                }

                if (lockedNetOutputQty > demand.NetOutputQty || lockedPlannedProcessQty > demand.PlannedProcessQty)
                {
                    throw new SolverInputContractException(
                        "锁定数量闭合失败：锁定覆盖量超出需求总量（口径不自洽）。" +
                        $"LogicalDemandKey={demand.LogicalDemandKey}, " +
                        $"NetOutputQty={demand.NetOutputQty}, 锁定净产出覆盖={lockedNetOutputQty}, " +
                        $"PlannedProcessQty={demand.PlannedProcessQty}, 锁定加工量覆盖={lockedPlannedProcessQty}");
                }

                var netFullyCovered = lockedNetOutputQty >= demand.NetOutputQty;
                var procFullyCovered = lockedPlannedProcessQty >= demand.PlannedProcessQty;

                // ④ 两类覆盖量**均全量** ⇒ 完全锁定，跳过排程（不产新 Task；锚点由 Phase2 物化保留）。
                if (netFullyCovered && procFullyCovered)
                {
                    continue;
                }

                // ③ 只覆盖一侧 ⇒ 口径不自洽 / 语义未定 ⇒ 受控 Fail Closed（**不得**静默跳过或静默排剩余）。
                if (netFullyCovered != procFullyCovered)
                {
                    var which = netFullyCovered
                        ? "净产出已全量覆盖、加工量仍有剩余（该组合语义未经 2号位 契约定义）"
                        : "净产出仍有剩余、加工量已无剩余（剩余净产出无加工承载）";
                    throw new SolverInputContractException(
                        "锁定数量口径不一致：" + which + "。" +
                        $"LogicalDemandKey={demand.LogicalDemandKey}, " +
                        $"NetOutputQty={demand.NetOutputQty}, 锁定净产出覆盖={lockedNetOutputQty}, " +
                        $"PlannedProcessQty={demand.PlannedProcessQty}, 锁定加工量覆盖={lockedPlannedProcessQty}");
                }

                // 部分锁定：计算剩余数量，创建剩余需求对象
                var remainingNetOutputQty = demand.NetOutputQty - lockedNetOutputQty;
                var remainingPlannedProcessQty = demand.PlannedProcessQty - lockedPlannedProcessQty;

                // 创建剩余需求对象（只排这部分）
                actualDemand = new LogicalProductionDemand
                {
                    LogicalDemandKey = demand.LogicalDemandKey,
                    PlanVersionId = demand.PlanVersionId,
                    DomainKey = demand.DomainKey,
                    AllocationSequence = demand.AllocationSequence,
                    DemandKey = demand.DemandKey,
                    OrderId = demand.OrderId,
                    MaterialId = demand.MaterialId,
                    FactoryId = demand.FactoryId,
                    StartStageCode = demand.StartStageCode,
                    StartOperationCode = demand.StartOperationCode,
                    // 供给阈值 Stage（PM《BOM取用…完整链路说明》§八）：2号位 回填、1号位 消费。
                    // 必须随克隆一起带走 —— 漏拷会让「部分锁定」需求静默丢失阈值语义（与 2号位 2026-09-28 回执 §5.1 对应）。
                    RequiredStageCode = demand.RequiredStageCode,
                    NetOutputQty = remainingNetOutputQty,
                    PlannedProcessQty = remainingPlannedProcessQty,
                    RequiredAvailableTime = demand.RequiredAvailableTime,
                    DemandSequence = demand.DemandSequence,
                    ProductionInstructionNo = demand.ProductionInstructionNo,
                    IsUnlocated = demand.IsUnlocated,
                    UOM = demand.UOM,
                    PreferredResourceId = demand.PreferredResourceId,
                    FallbackResourceId = demand.FallbackResourceId,
                    IsContinuation = demand.IsContinuation,
                    // ── 以下 5 项随克隆一并带走（与上方 RequiredStageCode 同款理由）──
                    // 漏拷 = 「部分锁定」需求静默丢失路径身份/连续键/拆合批约束/软偏好，
                    // 表现为：C桶多路径退化为「无固定路径 ⇒ 候选择优」（可接受）但 A/B 固定路径
                    // 退化为候选择优（**违反 v1.6「A/B 固定 Route/Path」**），且 ContinuationKey 断链。
                    ContinuationKey = demand.ContinuationKey,
                    RouteCode = demand.RouteCode,
                    PathId = demand.PathId,
                    NoSplitMerge = demand.NoSplitMerge,
                    PreferredResourceCode = demand.PreferredResourceCode
                };
            }
            // ── P0-05（0号位 2026-10-07 (5).md）：连续份额**输入完整性 Fail Closed** ──
            //   判据必须**从 `IsContinuation` 出发**，而**不是**从「有没有 RouteCode」倒推。
            //   旧实现用 `RouteCode 非空 || PathId 非空` 决定「固定路径 / 自由候选」⇒
            //   `IsContinuation=true` 但缺 Route/Path 的 A/B 会掉进自由候选选路（把 A/B 当 C 桶），
            //   违反 v1.6「A/B 输入必须带真实固定 RouteCode / PathId / StartOperation 且不得切换 Routing」。
            //
            //   硬校验（任一缺失 ⇒ 明确 Unscheduled，**禁止**进入 Routing 竞争；
            //   指定 Route/Path 是否**真实存在于图中**由下方 `TryGetRoutingGraph` 继续 Fail Closed）。
            if (actualDemand.IsContinuation)
            {
                var missing = new List<string>(5);
                if (string.IsNullOrEmpty(actualDemand.ContinuationKey)) missing.Add("ContinuationKey");
                if (string.IsNullOrEmpty(actualDemand.RouteCode)) missing.Add("RouteCode");
                if (actualDemand.PathId is null) missing.Add("PathId");
                if (string.IsNullOrEmpty(actualDemand.StartOperationCode)) missing.Add("StartOperationCode");
                if (!actualDemand.NoSplitMerge) missing.Add("NoSplitMerge");

                if (missing.Count > 0)
                {
                    // 与 Phase2 其它 Fail Closed 点同口径（:129/:203/:314/:325/:364…）：**只记 UnscheduledDemandKeys、
                    // 不产 Task、不写 TraceNotes**（TraceNotes 会经 SolveTraceNote 落库给 2号位，不得自造码）。
                    result.UnscheduledDemandKeys.Add(actualDemand.LogicalDemandKey);
                    continue;
                }
            }

            // ── 需求级路径候选解析（Path-aware，0号位 2026-10-07 裁决 Q-3 / §四）──
            //
            //   ① 需求**自带固定路径**（v1.6：「A/B 输入必须带真实固定 RouteCode / PathId / StartOperation」）
            //      ⇒ 固定该条，**不选路**；图中不存在该路径 ⇒ **Fail Closed**
            //      （Q-3 红线：不得回退 TryGetSingleRoutingGraph 猜唯一 Path）。
            //   ② 无固定路径（Free Slice / C桶）
            //      ⇒ 取该物料全部候选 Path；1 条 = 既有单路径行为（零回归），N 条 = 候选内联合择优。
            //
            //   ⚠ 禁止用「候选条数」反推 A/B/C 身份（0号位 裁决 §四）—— 桶身份只认 IsContinuation / 固定路径。
            if (!constraints.TryGetRoutingGraphs(actualDemand.MaterialId, out var allPaths))
            {
                // 无工艺路线 → 无法排程
                result.UnscheduledDemandKeys.Add(actualDemand.LogicalDemandKey);
                continue;
            }

            List<KeyValuePair<RoutePathKey, RoutingGraph>> candidatePaths;
            if (!string.IsNullOrEmpty(actualDemand.RouteCode) || actualDemand.PathId is not null)
            {
                if (!constraints.TryGetRoutingGraph(
                        actualDemand.MaterialId, actualDemand.RouteCode, actualDemand.PathId, out var fixedGraph))
                {
                    // Fail Closed：需求声明了固定路径但图缺失 ⇒ 不排（不猜、不跨 Path 找替代节点）
                    result.UnscheduledDemandKeys.Add(actualDemand.LogicalDemandKey);
                    continue;
                }

                candidatePaths = new List<KeyValuePair<RoutePathKey, RoutingGraph>>
                {
                    new(RoutePathKey.Of(actualDemand.RouteCode, actualDemand.PathId!.Value), fixedGraph)
                };
            }
            else
            {
                candidatePaths = allPaths;
            }

            // 步骤 1：为每条候选求工序列表（纯读，不改任何上下文）。
            var plannedCandidates = new List<(RoutePathKey Key, RoutingGraph Graph, List<OperationNode> Ops)>();
            foreach (var (pathKey, graph) in candidatePaths)
            {
                var ops = GetOperationsFromStage(
                    actualDemand.StartStageCode,
                    actualDemand.StartOperationCode,
                    graph,
                    constraints,
                    out var reason);

                if (ops.Count == 0 && reason == null)
                {
                    // P0-03修复：Routing有环或非法，属于技术失败（与既有单路径语义一致）
                    result.TechnicalFailure = true;
                    result.TechnicalFailureReason = $"Routing图非法或存在环：MaterialId={actualDemand.MaterialId}, Route={pathKey}";
                    return result;
                }

                plannedCandidates.Add((pathKey, graph, ops));
            }

            if (plannedCandidates.All(p => p.Ops.Count == 0))
            {
                // S26：全部候选的 StartStage/StartOperation 均非法 ⇒ 业务 Unscheduled（非技术失败，不静默回退）
                result.UnscheduledDemandKeys.Add(actualDemand.LogicalDemandKey);
                continue;
            }

            // 块3/块4（任务喂任务，方案A）：子件完成时间 → 父件动态物料下界。
            // 拓扑序保证排父件时子件已排过；子件未成功排程则父件缺料 → Unscheduled。
            var dynamicMaterialFloor = GetDynamicMaterialFloor(
                demand.LogicalDemandKey, constraints, demandCompletion, out bool childUnavailable);

            if (childUnavailable)
            {
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);
                continue;
            }

            // P0-08：同 PI 自由份额不得排到连续份额之前（也不得与其时间重叠）。
            // 自由份额的最早开始下界 = max(跨物料子件下界, 同 PI 连续份额完成时间)。
            if (!actualDemand.IsContinuation && !string.IsNullOrEmpty(actualDemand.ProductionInstructionNo)
                && continuityCompletionByPI.TryGetValue(actualDemand.ProductionInstructionNo!, out var continuityEnd)
                && continuityEnd > dynamicMaterialFloor)
            {
                dynamicMaterialFloor = continuityEnd;
            }

            // ── P0-01 / P0-02 / P0-03（0号位 2026-10-07《未命名的Markdown文件 (5).md》）──
            //   执行批**先于 Routing 选择**形成，判词根因是旧实现把 `LogicalDemand` **当成了** Execution Batch
            //   （批身份由 Route/Path 反推 —— 同一 Demand 拆出的两批会撞成同一个键）。
            //
            //   0号位 指定链：
            //     `Free Slice → Batch Policy → 1..N ExecutionBatchDraft(BatchDraftKey, Qty)
            //      → 每个 Batch 分别进行 Direction + RoutingCandidate + Resource + Calendar + Setup 联合求解
            //      → 每个 Batch 选择一条完整 Path
            //      → 同 Batch 全部 Operation FinalTask 共享 BatchDraftKey → 不同 Batch 必定不同 Key`
            // P0-01：按 `(MaterialId, StartStageCode→Dept)` **键控**解析本需求的 ⑧块 Batch Policy。
            var batchPolicy = ResolveExecutionBatchPolicy(actualDemand, request, constraints);

            // AUD-1-006：`piFacts` 传 null —— `PiRemainingFact` 尚未投影进 `DomainSolveRequest`
            //   （AUD-1-D04，归 2号位 装载）。一旦 2号位 投影，此处换成 `request.PiRemainingFacts` 即可
            //   激活 B-010 兜底（分支已就位，见 `TryFormConservativePiBatch`），**不需要再改 1号位 结构**。
            // V1_4 NEW-04：传入**本 Run 唯一**累计账 ⇒ 一旦事实投影，B-009 的 Stage 余额即按 Run 累计消费。
            //   ⚠ 当前生产入口 `piFacts: null` ⇒ 门禁不可评估 ⇒ 账**恒空**（如实声明，不宣称 B-009 生产态达标）。
            var ledgerMark = piStageLedger.Mark();
            var formation = FormExecutionBatches(actualDemand, batchPolicy, piStageLedger: piStageLedger);

            // P0-02：**无合法批方案**（Min/Max/AllowSplit 冲突）⇒ 本需求 fail-closed（不排），与既有
            //   Fail Closed 点同口径（`UnscheduledDemandKeys` + 不产 Task）；**绝不产出非法批**。
            //   冲突原因单独登记，供诊断/出口（出口 ReasonCode 口径另件确认）。
            if (!formation.IsLegal)
            {
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);

                // ── P0-01 / P0-02（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§四 / §五）──
                //   `BATCH_POLICY_MISSING`（缺有效策略）与 `BATCH_POLICY_CONFLICT`（无合法切分）
                //   同属 **NonRepairable / HardFailure**：除登记外写入**硬失败表**，
                //   Phase4 据此**禁止进入普通 Local Repair**（§五：Phase4 不得绕过 Min/Max 硬约束），
                //   Phase5 据此给出口 Reason（§十二：C 桶无有效策略 ⇒ `BATCH_POLICY_MISSING`、0 FinalTask）。
                var failureReason = formation.IsMissingPolicy ? "BATCH_POLICY_MISSING" : "BATCH_POLICY_CONFLICT";
                result.BatchPolicyHardFailures[demand.LogicalDemandKey] = failureReason;
                // AUD-1-006：`formation.IsPiContractPending` ⇒ B-010 保守兜底**适用条件成立但前置事实缺失**
                //   ⇒ 原因文本自带 `B010_PI_QUANTITY_CONTRACT_PENDING`（与笼统「无策略」可区分）。
                //   出口码仍为 `BATCH_POLICY_MISSING`：ReasonCode 值域是**冻结封闭集**（`PhaseThreeDiagnostics`
                //   权威 15 码），1号位 **不新增码**；契约待核信息走原因文本 / 报告，不改冻结值域。
                if (formation.IsMissingPolicy)
                {
                    result.BatchPolicyMissingDemandKeys.Add($"[{demand.LogicalDemandKey}] {formation.ConflictReason}");
                }
                else
                {
                    result.BatchPolicyConflicts.Add($"[{demand.LogicalDemandKey}] {formation.ConflictReason}");
                }
                continue;
            }

            // P0-02：Merge 的**正式控制源 = ⑧块 Batch Policy.AllowMerge**（0号位 (7).md §五：「不能出现
            //   Batch Policy 禁止 Merge，但旧全局参数允许，就仍然 Merge」）。有策略 ⇒ 以策略为准；无策略 ⇒ 回落旧全局参数（零回归）。
            //   本判据与批数无关 ⇒ 提到候选择优之前（择优需要它）。
            var mergeAllowed = batchPolicy?.AllowMerge ?? request.StrategySnapshot.Parameters.AllowMerge;

            // ═══ P1-01（0号位 2026-10-09 第三轮复审 §二 → 2026-10-09《APS_V1_2_20261009.md》§三 P1-01）═══
            //   上一轮已把 `AUTO` 从「只在 `ScheduleDemandOperations` 内本地解析」提到「进入批/路由择优之前
            //   自决一次」；本轮复审指出该整改**仍不完整**：自决所用工序集是
            //   `plannedCandidates.First(p => p.Ops.Count > 0).Ops`（输入顺序上的第一条 Path）⇒ 用一个由任意
            //   首条外推的方向套所有 Batch / Route。详见下方整改说明。
            // ── P1-01 整改（0号位 2026-10-09《APS_V1_2_20261009.md》§三 P1-01）──
            //   复审判词（成立）：旧实现取 `plannedCandidates.First(p => p.Ops.Count > 0).Ops` 解析**一个**
            //   `resolvedDirection` 再传给所有 Batch / Route ⇒ 方向由**输入顺序上的第一条 Path** 外推全路由。
            //   而 Slack 判据（`DUE_TIGHT`/`DUE_LOOSE`）依赖该候选的**总标准工时**，两条合法 Route 工时不同
            //   即可让一条判 FORWARD、另一条判 BACKWARD ⇒ **候选比较结果随候选输入顺序漂移**。
            //   （旧注释「取首条不改变方向判据的语义」与此实现自相矛盾，已删除。）
            //
            //   整改分两层，职责不同、互不替代：
            //     ① **需求级方向**（本处）= 仅作**登记回落**与批方案层兜底。工序输入取**规范首候选** ——
            //        按 `(RouteCode, PathId)` 序排序后的首条（**与输入顺序无关**），不再是「任意第一 Path」。
            //     ② **候选/批级方向**（`RunBatchPlan` 试排与落定内）= 每条候选路径按其**自身工序**解析
            //        （见 `ResolveDirectionForOps`），参与候选评分，并由**胜出候选自己的方向**落定。
            var canonicalCandidate = plannedCandidates
                .Where(p => p.Ops is { Count: > 0 })
                .OrderBy(p => p.Key.RouteCode, StringComparer.Ordinal)
                .ThenBy(p => p.Key.PathId)
                .FirstOrDefault();

            var resolvedDirection = direction;
            if (resolvedDirection == SchedulingDirectionResolver.Auto && canonicalCandidate.Ops is { Count: > 0 })
            {
                var decision = SchedulingDirectionResolver.Resolve(
                    actualDemand,
                    canonicalCandidate.Ops,
                    constraints,
                    request.PlanningStart,
                    dynamicMaterialFloor,
                    demandGoal: null);
                resolvedDirection = decision.Direction;

                // ── P1-02（复审 §二）：缺口经**既有合法追溯通道**（`SolveTraceNote`）表达 ──
                //   复审判词：「`DEMAND_GOAL_ABSENT` 只加到 `Decision.Signals`，未写入 `SolveTraceNotes`」
                //   ⇒ 「生产路径恒记 DemandGoal 缺失」只对内部计算对象成立，**不等于**用户/日志可见证据。
                //   处置：仅当该信号出现时写一条 trace。`ReasonCode` 位承载的是**决策说明层**取值 ——
                //     与 `:1909` 附近 Setup 的 `ExplanationType` **同一先例**，**不是**冻结字典
                //     `ScheduleExplanationFact.ReasonCode`（0号位 Q4：决策说明不进 ReasonCode 体系）
                //     ⇒ **未自造 ReasonCode 枚举**（取值直接复用 Resolver 的既有信号常量）。
                //   体积：每个 AUTO 需求至多一条；当前 DemandGoal 载体缺失 ⇒ 每需求一条，
                //     如实记录这一**系统性输入缺口**（真实传播由 2号位 补齐后本信号自然消失）。
                if (decision.Has(SchedulingDirectionResolver.SignalDemandGoalAbsent))
                {
                    constraints.TraceNotes.Add(new SolveTraceNote
                    {
                        Key = actualDemand.LogicalDemandKey,
                        ReasonCode = SchedulingDirectionResolver.SignalDemandGoalAbsent,
                        Message = "Direction 自决（B-005）缺 DemandGoal 载体（2号位 Pegging 传播未达）：" + decision.Reason,
                        Level = "Warning"
                    });
                }
            }

            // ── Phase5 衔接（0号位 2026-10-09《未命名的Markdown文件 (3)(1).md》§一 第 3 行）──
            //   复审判词：Phase5 的空隙压实 / Setup 序列优化**仍按 Run 级原始值**判 FORWARD，
            //     即使 AUTO 内部已判为 FORWARD 也不会进入这两项优化 ⇒ 与 Phase2 的逐需求方向**脱节**。
            //   处置：把**本需求自决后的方向**登记到 `ConstraintContext`，Phase5 据此按需求粒度门控
            //     （**不另建求解器**、**不重算**——重算会因缺 Phase2 的试排上下文而漂移）。
            //   零回归：Run 级 FORWARD/BACKWARD ⇒ 此处登记的即原值，Phase5 判定与整改前逐字一致。
            constraints.ResolvedDirections[actualDemand.LogicalDemandKey] = resolvedDirection;

            // ── P1-01（0号位 2026-10-07《未命名的Markdown文件 (7).md》§八）：有界优化候选择优 ──
            //   结构上有界候选（≤5）：合法不拆 / 合法 2 批 / 合法 3 批 / Preferred 附近切分；
            //   受**技术预算** `SolverBatchBudget` 上限约束（只收不放，不新增业务默认值）。
            //   ⚠ AUD-1-005：预算**取自 `ResolveSolverBatchBudget(request)`**（正式参数版本载体 / 1号位 版本化
            //     安全默认），**不再读业务 `batchPolicy` 的 `MaxOptimizationSplitCount` / `MaxBatchCandidates`**。
            //   候选 == 1（V1 常态：无策略 / A/B / 唯一合法批数）⇒ **不进入择优**，与既有逐字一致（零回归）。
            //   择优复用**已有的** Direction + Routing + Resource + Calendar + Setup 联合评价（`RunBatchPlan` 试跑），
            //   四层目标 + 批数 tiebreak；**无改善时基线（nMin）胜出** ⇒ 既有行为不被改写。
            var planCandidates = EnumerateLegalBatchPlanCandidates(
                actualDemand, batchPolicy, formation, ResolveSolverBatchBudget(request));
            if (planCandidates.Count > 1)
            {
                formation = SelectBestBatchPlan(
                    planCandidates, actualDemand, plannedCandidates, direction, constraints,
                    resourceOccupancy, result.ScheduledTasks, allocationTaskShare, demandByKey,
                    request, dynamicMaterialFloor, stageOverlap, mergeAllowed);
            }

            var batches = formation.Batches;

            // 批键登记 ⇒ Phase4 **逐批**修复时按本批键复用同键
            //（`PhaseFourLocalRepair.ExpandRepairUnits` 读本表 + `ExecutionBatchPlans`；
            //  局部修复不得把同一执行批劈成两个键，也不得把 Batch-002 写回 Batch-001 的键）。
            //  P1-01：登记**在候选择优之后** —— 登记的必须是**最终选中**批方案的批键与批数量。
            constraints.ExecutionBatchDraftKeys[actualDemand.LogicalDemandKey] =
                batches.Select(b => b.BatchDraftKey).ToList();

            constraints.ExecutionBatchPlans[actualDemand.LogicalDemandKey] = batches
                .Select(b => new LPS.APS.Scheduling.Solvers.ExecutionBatchPlanEntry(
                    b.BatchDraftKey, b.NetOutputQty, b.PlannedProcessQty))
                .ToList();

            // 多批时**禁止 Merge**（判据在 `RunBatchPlan` 内：`mergeAllowed && !multiBatch`）。
            var outcome = RunBatchPlan(
                batches, actualDemand, plannedCandidates, direction, constraints,
                resourceOccupancy, result.ScheduledTasks, allocationTaskShare, demandByKey,
                request, dynamicMaterialFloor, stageOverlap, mergeAllowed,
                constraints.ChosenBatchRoutePaths, constraints.ChosenRoutePaths);

            // P1-01：登记**实际落定**方向（= 胜出候选自己的方向），供 Phase5 逐需求门控与出口追溯。
            //   覆盖上方 `:542` 的需求级回落值 —— 二者仅在 `AUTO` 且候选 lead 不同时才可能不同，
            //   此时**真实落定**的方向才是权威（复审 §五.4「候选比较与真实落定同方向」）。
            constraints.ResolvedDirections[demand.LogicalDemandKey] = outcome.Direction;

            // ── NEW-P1-01：登记**逐执行批**落定方向（批键 → 方向）──
            //   Phase5 按 Task 的 `ExecutionBatchDraftKey` 消费本表（`IsForwardTask`），
            //   需求级 `ResolvedDirections` 降为**回落**（Task 无批键 / 批键未登记时用）。
            //   单批时两表同向 ⇒ Phase5 判定与整改前逐字一致（零回归）。
            foreach (var kv in outcome.BatchDirections)
            {
                constraints.ResolvedBatchDirections[kv.Key] = kv.Value;
            }

            var demandTasksAll = outcome.NewTasks;
            var batchFailed = outcome.BatchFailed;

            // 批循环后的聚合。
            //   ⚠ 判据必须区分「**批失败**」与「**批以 Merge 落定**（无新 Task）」—— 后者不是失败：
            //     旧实现即「tasks==0 且 isMerged ⇒ 不标 Unscheduled」。若此处把 Merge 也当失败，
            //     需求会被误标 Unscheduled ⇒ Phase4 会把它当未排需求**整链重建**（等于把 Merge 撤销）。
            if (batchFailed)
            {
                // 任一批判失败 ⇒ 需求整体记 Unscheduled。
                //   已落定的前序批**保留**（「能排下的排下」，与「排不下不得静默丢弃」同向），
                //   需求仍进 `UnscheduledDemandKeys` 以示**未按完整执行批全部满足**。
                result.UnscheduledDemandKeys.Add(demand.LogicalDemandKey);

                // ── P0-03（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§六 / §十一-2）──
                //   登记**未落定执行批身份** = 首个失败批 **及其后所有未试批**
                //   （`RunBatchPlan` 遇首个失败批即 `break` ⇒ 其后的批根本没被尝试，同属未落定）。
                //   Phase4 只能修这批；**已落定的前序批禁止重新展开** —— 否则同一批键产出第二套完整链，
                //   造成「重复生产 / 漏排 / 错误成功状态」三合一（§六）。
                for (int i = outcome.FailedBatchIndex; i >= 0 && i < batches.Count; i++)
                {
                    var failedBatch = batches[i];
                    string? failedRoute = null;
                    long? failedPath = null;
                    if (constraints.ChosenBatchRoutePaths.TryGetValue(failedBatch.BatchDraftKey, out var failedKey))
                    {
                        failedRoute = failedKey.RouteCode;
                        failedPath = failedKey.PathId;
                    }

                    result.FailedExecutionBatches.Add(new FailedExecutionBatch(
                        demand.LogicalDemandKey,
                        failedBatch.BatchDraftKey,
                        failedBatch.Ordinal,
                        failedBatch.NetOutputQty,
                        failedBatch.PlannedProcessQty,
                        failedRoute,
                        failedPath));
                }

                // ── V1_4 NEW-04 回滚：需求**零落定** ⇒ 撤销本需求对 Stage 余额的占用声明 ──
                //   判据：`demandTasksAll.Count == 0` 且**无批以 Merge 落定**（有 Task 或已并入既有批
                //   = 物理占用真实存在 ⇒ **保留**登记；本账按**已形成批**累计，是**保守上界**，
                //   宁可多算占用，绝不放大可用量）。
                //   回滚按**日志身份逆序**（`RollbackTo`），与 F-01 同一纪律：登记身份、不截尾。
                if (demandTasksAll.Count == 0)
                {
                    piStageLedger.RollbackTo(ledgerMark);
                }

                continue;
            }

            if (demandTasksAll.Count == 0)
            {
                // 全部批均以 Merge 落定（份额已并入既有 Task，无新 Task）：与旧版同口径 ——
                // 不登记完成时间、不标 Unscheduled。
                continue;
            }

            // 块3/块4：登记本需求完成时间（所有批所有工序 Task 的最晚 End），供父件取动态物料下界。
            var demandCompletionTime = demandTasksAll.Max(t => t.PlannedEndTime);
            demandCompletion[demand.LogicalDemandKey] = demandCompletionTime;

            // P0-08：登记连续份额完成时间，供同 PI 自由份额做时间下界。
            if (actualDemand.IsContinuation && !string.IsNullOrEmpty(actualDemand.ProductionInstructionNo))
            {
                var pi = actualDemand.ProductionInstructionNo!;
                if (!continuityCompletionByPI.TryGetValue(pi, out var existing) || demandCompletionTime > existing)
                    continuityCompletionByPI[pi] = demandCompletionTime;
            }
        }

        // P0-04修复：把 Merge M:N 份额追溯返回给 Phase5，避免合批份额血缘丢失
        result.AllocationTaskShare = allocationTaskShare;

        return result;
    }

    /// <summary>
    /// 块4（任务喂任务，方案A）：计算某需求的「子件完成时间」动态物料下界。
    /// 取该需求所有直接子件（CrossMaterialParentToChildren）已排程完成时间的最大值。
    /// 若任一子件尚未成功排程（缺料），返回 childUnavailable=true，调用方应标记父件 Unscheduled。
    /// 无子件时返回 DateTime.MinValue（无动态下界，等价于旧行为）。
    /// </summary>
    private DateTime GetDynamicMaterialFloor(
        string demandKey,
        ConstraintContext constraints,
        Dictionary<string, DateTime> demandCompletion,
        out bool childUnavailable)
    {
        childUnavailable = false;

        if (!constraints.CrossMaterialParentToChildren.TryGetValue(demandKey, out var children)
            || children.Count == 0)
        {
            return DateTime.MinValue;
        }

        DateTime floor = DateTime.MinValue;
        foreach (var childKey in children)
        {
            if (!demandCompletion.TryGetValue(childKey, out var childEnd))
            {
                // 子件未成功排程 → 父件缺料，硬约束无法满足
                childUnavailable = true;
                continue;
            }

            // P1-12：父件消费相对子件完工的滞后时间（分钟），累加到子件完成时间上。
            if (constraints.CrossMaterialLagMinutes.TryGetValue((demandKey, childKey), out var lagMinutes))
            {
                childEnd = childEnd.AddMinutes((double)lagMinutes);
            }

            if (childEnd > floor)
            {
                floor = childEnd;
            }
        }

        return floor;
    }

    /// <summary>
    /// 初始化资源占用追踪
    /// 文档：§四 4.8、§六 Phase 1
    /// 预填充 Execution/Firm/Frozen 锁定任务的资源占用
    /// P0-06修复：同时预填充Candidate外Domain ACTIVE共享资源阻挡块
    /// </summary>
    private Dictionary<int, List<TimeWindow>> InitializeResourceOccupancy(
        IReadOnlyList<ResourceDefinition> resources,
        ConstraintContext constraints)
    {
        var occupancy = new Dictionary<int, List<TimeWindow>>();
        foreach (var resource in resources)
        {
            occupancy[resource.ResourceId] = new List<TimeWindow>();
        }

        // 预填充锁定任务的资源占用
        foreach (var lockedTask in constraints.LockedTasks.Values)
        {
            if (occupancy.ContainsKey(lockedTask.ResourceId))
            {
                AddOccupancyWindow(occupancy, null, lockedTask.ResourceId,
                    new TimeWindow(lockedTask.LockedStart, lockedTask.LockedEnd));
            }
        }

        // P0-06修复：预填充外Domain ResourceBlock（Candidate外ACTIVE共享资源阻挡）
        foreach (var blockList in constraints.ResourceBlocks.Values)
        {
            foreach (var block in blockList)
            {
                if (occupancy.ContainsKey(block.ResourceId))
                {
                    AddOccupancyWindow(occupancy, null, block.ResourceId,
                        new TimeWindow(block.StartTime, block.EndTime));
                }
            }
        }

        return occupancy;
    }

    /// <summary>
    /// 获取从指定阶段/工序开始的工序列表（拓扑排序）
    /// 文档：§四 4.4 RoutingDependency，§七 Level 0硬约束
    /// PI Position 执行起点上下文专项：支持 Operation 粒度裁剪（StartOperationCode）
    /// P0-01修复：改为 internal static，供 Phase4 TryResourceSwitch 复用，
    /// 使 Phase4 与 Phase2 使用同一套 Kahn 拓扑排序 + StartOperationCode/StartStageCode 裁剪逻辑。
    /// </summary>
    internal static List<OperationNode> GetOperationsFromStage(
        string startStageCode,
        string? startOperationCode,
        RoutingGraph routingGraph,
        ConstraintContext constraints,
        out string? failureReason)
    {
        failureReason = null;

        // 构建邻接表和入度表。节点身份 = (StageCode, OperationCode)（0号位 2026-09-29 裁决 §5.3）
        var adjacency = new Dictionary<OperationNodeKey, List<OperationNodeKey>>();
        var inDegree = new Dictionary<OperationNodeKey, int>();

        foreach (var nodeKey in routingGraph.Operations.Keys)
        {
            adjacency[nodeKey] = new List<OperationNodeKey>();
            inDegree[nodeKey] = 0;
        }

        // 根据 RoutingDependency 构建图。边的两端已是节点键（Phase1 已消解 Stage 归属），
        // 故不再有「按工序码对不上 ⇒ 静默漏接」的旧问题。
        foreach (var dep in routingGraph.Dependencies.Values.SelectMany(list => list))
        {
            if (adjacency.ContainsKey(dep.From) &&
                adjacency.ContainsKey(dep.To))
            {
                adjacency[dep.From].Add(dep.To);
                inDegree[dep.To]++;
            }
        }

        // Kahn 算法：拓扑排序
        var queue = new Queue<OperationNodeKey>();
        var result = new List<OperationNode>();

        // 将入度为0的工序加入队列
        foreach (var nodeKey in routingGraph.Operations.Keys)
        {
            if (inDegree[nodeKey] == 0)
            {
                queue.Enqueue(nodeKey);
            }
        }

        // BFS 拓扑排序
        while (queue.Count > 0)
        {
            var currentKey = queue.Dequeue();
            result.Add(routingGraph.Operations[currentKey]);

            // 减少后继工序的入度
            foreach (var successor in adjacency[currentKey])
            {
                inDegree[successor]--;
                if (inDegree[successor] == 0)
                {
                    queue.Enqueue(successor);
                }
            }
        }

        // P0-03修复：Routing有环时返回空列表，由调用方判定为技术失败
        if (result.Count < routingGraph.Operations.Count)
        {
            // Routing图存在环，属于输入数据结构非法
            // failureReason 保持 null，调用方据此判定为技术失败
            return new List<OperationNode>();
        }

        // PI Position 执行起点上下文专项：Operation 粒度裁剪优先
        // S24/S26：从 StartOperationCode 继续，裁掉已完成前序；非法时失败，不静默回退
        if (!string.IsNullOrEmpty(startOperationCode))
        {
            // ⚠ 同码跨 Stage（2号位 实测 117 物料）：起点须用 StartStageCode 消解，
            //   旧实现按工序码单键命中 ⇒ 任取一个 Stage 的同名工序，起点可能取错。
            var candidates = routingGraph.Operations.Keys
                .Where(k => string.Equals(k.OperationCode, startOperationCode, StringComparison.Ordinal))
                .ToList();

            if (candidates.Count == 0)
            {
                // S26：StartOperationCode 不存在于 Routing，输入/求解失败
                failureReason = $"StartOperationCode '{startOperationCode}' 不存在于 Routing";
                return new List<OperationNode>();
            }

            OperationNodeKey startKey;

            if (candidates.Count == 1)
            {
                startKey = candidates[0];

                // S26：StartOperationCode 与 StartStageCode 明显不一致
                var resolvedStage = routingGraph.Operations[startKey].StageCode;
                if (!string.IsNullOrEmpty(startStageCode) &&
                    !string.IsNullOrEmpty(resolvedStage) &&
                    !string.Equals(resolvedStage, startStageCode, StringComparison.Ordinal))
                {
                    failureReason = $"StartOperationCode '{startOperationCode}' 的 StageCode '{resolvedStage}' 与 StartStageCode '{startStageCode}' 不一致";
                    return new List<OperationNode>();
                }
            }
            else
            {
                var matched = candidates
                    .Where(k => string.Equals(k.StageCode, startStageCode, StringComparison.Ordinal))
                    .ToList();

                if (matched.Count != 1)
                {
                    // 起点自身歧义：不猜（0号位 §5.3 禁半升级 + 不静默原则）
                    failureReason = $"StartOperationCode '{startOperationCode}' 在 Routing 中对应多个 Stage"
                        + $"（{string.Join("/", candidates.Select(c => c.StageCode))}），无法唯一确定起点";
                    return new List<OperationNode>();
                }

                startKey = matched[0];
            }

            // 从该 Operation 开始，找所有可达后续工序（含自己）
            return CropToReachable(result, new[] { startKey }, routingGraph);
        }

        // P0-02修复 + 第4轮修复：根据StartStageCode裁剪已完成的Stage
        // 第4轮修复：StartStage不存在时不返回整条Routing，而是返回空（数据不一致）
        if (!string.IsNullOrEmpty(startStageCode))
        {
            var startOperations = result.Where(op => op.StageCode == startStageCode).ToList();

            if (startOperations.Count == 0)
            {
                // StartStageCode 在 Routing 中不存在，业务 Unscheduled（非技术失败）
                failureReason = $"StartStageCode '{startStageCode}' 不存在于 Routing";
                return new List<OperationNode>();
            }

            // 从 StartStage 工序开始，找所有可达后续工序（含自己）
            return CropToReachable(
                result,
                startOperations.Select(op => OperationNodeKey.Of(op.StageCode, op.OperationCode)),
                routingGraph);
        }

        // 两者都空：返回完整 Routing（S23，从首工序开始）
        return result;
    }

    /// <summary>
    /// 从给定起点工序集合出发，裁剪拓扑序为"起点 + 所有可达后续工序"
    /// PI Position 执行起点上下文专项：Operation/Stage 粒度裁剪共用
    /// P0-01修复：改为 internal static，供 Phase4 TryResourceSwitch 复用。
    /// </summary>
    internal static List<OperationNode> CropToReachable(
        List<OperationNode> orderedOperations,
        IEnumerable<OperationNodeKey> startNodes,
        RoutingGraph routingGraph)
    {
        var reachableOps = new HashSet<OperationNodeKey>();
        var bfsQueue = new Queue<OperationNodeKey>();

        foreach (var startNode in startNodes)
        {
            if (reachableOps.Add(startNode))
            {
                bfsQueue.Enqueue(startNode);
            }
        }

        // BFS遍历：从Dependencies找每个工序的所有后续工序
        while (bfsQueue.Count > 0)
        {
            var currentOp = bfsQueue.Dequeue();

            // 遍历所有依赖边，找以currentOp为前驱的后续工序
            foreach (var kvp in routingGraph.Dependencies)
            {
                var toOp = kvp.Key;
                var edges = kvp.Value;

                // 如果存在从currentOp到toOp的边，且toOp未访问过
                if (edges.Any(e => e.From == currentOp) && !reachableOps.Contains(toOp))
                {
                    reachableOps.Add(toOp);
                    bfsQueue.Enqueue(toOp);
                }
            }
        }

        // 过滤：只保留可达的工序（节点身份须带 Stage 上下文，与图键同口径）
        return orderedOperations
            .Where(op => reachableOps.Contains(OperationNodeKey.Of(op.StageCode, op.OperationCode)))
            .ToList();
    }

    /// <summary>
    /// P1-02（StageOverlap）：取有效转运批量——优先路由 OperationNode.TransferBatchSize，缺省回落冻结 TransferBatchQty。
    /// </summary>
    private static decimal? GetEffectiveTransferBatch(OperationNode operation, StageOverlapParams stageOverlap)
    {
        if (operation.TransferBatchSize.HasValue && operation.TransferBatchSize.Value > 0)
            return operation.TransferBatchSize;
        if (stageOverlap.TransferBatchQty > 0)
            return stageOverlap.TransferBatchQty;
        return null;
    }

    /// <summary>
    /// P1-02（StageOverlap）：上游完成转运批量后下游即可提前开工；倒排时把上游结束时间向后延伸 overlapExtension。
    /// overlapExtension = 加工时长 × (1 - 转运批量/计划加工量)。AllowOverlap=false 或未配置批量时返回 Zero（严格串行）。
    /// </summary>
    private static TimeSpan GetOverlapExtension(OperationNode operation, decimal plannedProcessQty, TimeSpan processDuration, StageOverlapParams stageOverlap)
    {
        if (!stageOverlap.AllowOverlap)
            return TimeSpan.Zero;
        var transferBatch = GetEffectiveTransferBatch(operation, stageOverlap);
        if (transferBatch == null || transferBatch.Value <= 0 || transferBatch.Value >= plannedProcessQty)
            return TimeSpan.Zero;
        var ratio = transferBatch.Value / plannedProcessQty;
        return TimeSpan.FromMinutes(processDuration.TotalMinutes * (1 - (double)ratio));
    }

    /// <summary>
    /// P1-01：一个候选**批方案**在给定工作缓冲上的试跑/落定结果。
    ///   · <c>BatchFailed</c>：任一批落不下（与既有 `batchFailed` 同口径）；
    ///   · <c>NewTasks</c>：本方案新产出的 Task（Merge 落定的批不产新 Task）；
    ///   · <c>SetupTotal</c> / <c>Completion</c>：供四层目标第 ④/③ 层比较。
    /// </summary>
    private readonly record struct BatchPlanRunOutcome(
        bool BatchFailed,
        List<FinalTaskDraft> NewTasks,
        decimal SetupTotal,
        DateTime Completion,
        int FailedBatchIndex,   // P0-03：首个**未落定**批在 batches 中的下标（-1 = 全部落定）
        string Direction,       // P1-01：本批方案**实际落定**所用方向（= 胜出候选自己的方向；无批落定 ⇒ 传入的原值）
        IReadOnlyDictionary<string, string> BatchDirections);   // NEW-P1-01：**批键 → 该批自身落定方向**（逐批独立）

    /// <summary>
    /// P1-01：在**给定工作缓冲**上执行一个**批方案**（原 Phase2 批循环主体，逐字抽出）。
    ///
    /// 抽出目的：让「候选批方案试跑」与「最终落定」共用同一段逻辑 —— 试跑传克隆缓冲
    ///   （<paramref name="resourceOccupancy"/> / <paramref name="scheduledTasks"/> /
    ///    <paramref name="allocationTaskShare"/> / <paramref name="chosenBatchRoutePaths"/> /
    ///    <paramref name="chosenRoutePaths"/> 均为克隆），落定传真实对象。
    /// ⚠ `constraints.ProductTimeline` 仍由**调用方**负责隔离与还原（试跑前后 clone/restore），
    ///   因为它不是参数而是 `ConstraintContext` 上的共享可变状态。
    ///
    /// 每批流程（与 P0-03/P0-04 落码逐字一致）：
    ///   步骤 1 多批时按本批数量克隆需求（单批用原实例，零回归）；
    ///   步骤 2 各候选路径试排（隔离 `ProductTimeline` / occupancy / tasks / shares，回滚 TraceNotes）；
    ///   步骤 3 <see cref="SelectBestRoutingCandidate"/> 本批择优（Q-1 四层目标）；
    ///   步骤 4 真实上下文重跑落定 + 逐批登记选中路径（RT-002）。
    /// </summary>
    private BatchPlanRunOutcome RunBatchPlan(
        IReadOnlyList<ExecutionBatchDraft> batches,
        LogicalProductionDemand actualDemand,
        List<(RoutePathKey Key, RoutingGraph Graph, List<OperationNode> Ops)> plannedCandidates,
        string direction,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        List<FinalTaskDraft> scheduledTasks,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> allocationTaskShare,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        DomainSolveRequest request,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        bool mergeAllowed,
        Dictionary<string, RoutePathKey> chosenBatchRoutePaths,
        Dictionary<string, RoutePathKey> chosenRoutePaths,
        Dictionary<int, List<TimeWindow>>? occupancyPristine = null)   // 2026-10-08 COW：本表为克隆时传入其冻结快照
    {
        // 多批时 Merge 受**身份保持**判据约束（见循环内 P1-01）。
        var multiBatch = batches.Count > 1;

        var newTasks = new List<FinalTaskDraft>();
        var batchFailed = false;
        var failedBatchIndex = -1;
        // P1-01：记录**实际落定**所用方向（逐批覆盖；无批落定 ⇒ 保持传入值）。
        var landedDirection = direction;

        // NEW-P1-01：**逐批**记录各自落定方向（批键 → 方向）。冻结模型是「每个执行批独立联合求解」
        //   ⇒ 同一需求的不同批可各自落定到不同方向；只有逐批留痕才能让 Phase5 按 Task 所归批消费。
        var batchDirections = new Dictionary<string, string>(StringComparer.Ordinal);
        decimal setupTotal = 0m;
        var planCompletion = DateTime.MinValue;

        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            var batch = batches[batchIndex];

            // ── P1-01（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§八）：Merge 门控**不得按批数整类禁用** ──
            //   旧形态 `mergeAllowed && !multiBatch` 把「本需求拆成多批」直接解释成「**永久禁止 Merge**」，
            //   使正式字段 `BatchPolicy.AllowMerge` 对 multiBatch 这一整类 C 桶排程**整体失效**。
            //   0号位 同时明令「**不得简单删 `!multiBatch`**」（Execution Batch 已成正式身份）⇒ 整改分两步：
            //     ① 入口解禁：`allowMergeForThisBatch = mergeAllowed`（不再看批数）；
            //     ② 身份把关下移到**目标侧**：`requireIdentityPreserving = multiBatch` ⇒ 合并目标必须是
            //        **尚未归属任何执行批**（`ExecutionBatchDraftKey == null`）的 Task，合并结果采用**本批键**
            //        （见 `TryMergeDemandIntoTask`）⇒ 本批身份不丢；目标已属别的执行批则拒绝
            //        （否则一个 Task 混两个批键，违反「同 Batch 共 Key、不同 Batch 必不同键」）。
            //   ⚠ 结构性限制（**如实登记，不声称达标**）：现有 `FinalTaskDraft.ExecutionBatchDraftKey` 为**单值**、
            //     `AllocationTaskShare` 与 Phase5 `mergeLineage` **均无批键** ⇒「一个 Task 承载两个 Execution Batch
            //     身份」在当前载体上**无法表达**。故多批需求合并**只可能落在未归批目标上**，而本求解器在正常装配
            //     路径下**所有生产 Task 均带批键** ⇒ multiBatch 合并当前**实际不发生**。
            //     本次落地的是「入口不再整类禁用」（§八 第一半），**不是**「multiBatch 合并已可用」；
            //     第二半需**载体升级**（多值批键，或 `AllocationTaskShare` 加批键）⇒ 载体归 2号位、语义归 0号位，
            //     已出件提请（不降目标：做不到直说，不以「替代路径也算达标」充数）。
            //   单批需求保持既有行为（零回归）。
            var requireIdentityPreserving = multiBatch;
            var allowMergeForThisBatch = mergeAllowed;
            // 单批：直接用原实例（零回归）；多批才克隆并覆写本批数量。
            var batchDemand = multiBatch
                ? CloneDemandWithBatchQty(actualDemand, batch.NetOutputQty, batch.PlannedProcessQty)
                : actualDemand;

            // 步骤 2：候选试排（每条候选在**同一初始上下文**上跑完整排程，互不污染）。
            //   单候选（V1 常态 / A/B 固定路径）⇒ 试排后在真实上下文重跑，结果与既有完全一致（零回归）。
            var realTimeline = constraints.ProductTimeline;
            // P1-01：试排元组带**本候选自己的方向**（`Direction`）—— 候选比较层据此按各自方向裁决，
            //   不再用一个由「任意第一 Path」外推的方向套所有候选。
            var trials = new List<(RoutePathKey Key, bool Feasible, DateTime Completion, decimal SetupMinutes, string Direction)>();

            // 试排隔离：`constraints.TraceNotes` 是**共享可变**状态（Setup 追踪三元组在此追加），
            // 试排会把它当真实产出写进去 ⇒ 多候选试排会留下 N 份重复 trace。
            // 记下水位，试排结束回滚；步骤 4 在真实上下文重跑会重新写恰好一份。
            var traceNotesWatermark = constraints.TraceNotes.Count;

            foreach (var (pathKey, graph, ops) in plannedCandidates)
            {
                if (ops.Count == 0)
                {
                    trials.Add((pathKey, false, DateTime.MinValue, 0m, direction));
                    continue;
                }

                // ── P1-01 整改核心：**本候选**按其**自身工序**解析实际方向 ──
                //   非 AUTO ⇒ 原样（零回归）；AUTO ⇒ 本路径的 lead 决定本路径的 Slack 判据。
                //   同一 `candidateDirection` 同时用于**试排**（本 trial 的完成时间）与**落定**（步骤 4），
                //   保证「候选比较所用方向」与「真实落定所用方向」**同向**（复审 §五.4）。
                var candidateDirection = ResolveDirectionForOps(
                    direction, batchDemand, ops, constraints, request.PlanningStart, dynamicMaterialFloor);

                // 性能计数（§九「Routing 试跑次数」）：每个（执行批 × Routing 候选）⇒ 一次候选试排。
                SolverDiagnostics.CountRoutingTrial();

                long swTimeline2 = SolverDiagnostics.HotspotStart();
                constraints.ProductTimeline = realTimeline.Clone();
                SolverDiagnostics.HotspotEnd(swTimeline2, SolverDiagnostics.Hotspot.TimelineClone);

                var trialOccupancy = CloneOccupancy(resourceOccupancy, out var trialOccupancyPristine);

                // ── 性能（2026-10-08 第①刀）：把隔离判据从「开了 Merge」收紧为「Merge **结构上可能发生**」 ──
                //   `scheduledTasks` / `allocationTaskShare` 的**唯一**写入方是 Merge 路径
                //   （`TryMergeOrSchedule` → `TryMergeDemandIntoTask` / `TryMergeDemandIntoStageBatch`）。
                //   而 `RunDemandSchedule` 走 Merge 路径还需：
                //     ① `allowMerge == true`；
                //     ② **非**连续份额 —— `TryMergeOrSchedule` 首部 `if (IsContinuation || NoSplitMerge) return
                //        ScheduleDemandOperations(...)`（`NoSplitMerge`＝不拆不合，P0-07 显式落实）。
                //   ⚠ **OWN-P0-02（0号位 2026-10-10）撤销第 ③ 条「工序数为 1」**：
                //     旧第 ③ 条的依据是「`FindMergeableTasks` 对多工序恒返回空 ⇒ 构造上不可能写这两张表」。
                //     该前提已被 OWN-P0-02 整改**移除**（多 Operation Stage 执行批现在**可以**合批，见
                //     `FindMergeableStageBatches` / `TryMergeDemandIntoStageBatch`）⇒ 判据必须同步放宽，
                //     否则试排会直接写真实表（**污染**）—— 这是本项整改的正确性前提，不是性能取舍。
                //   代价：多工序需求在开 Merge 时恢复两张 O(N) 表的克隆（原「多工序不白克隆」的优化随之失效）。
                //     **正确性优先**：旧优化只对「多工序恒不合批」成立，该前提已不成立。
                //   零回归边界：单工序路径逐字保留原克隆条件；关 Merge / 连续份额仍然不克隆（构造上不可能写）。
                var mergeStructurallyPossible = allowMergeForThisBatch
                                                && !batchDemand.IsContinuation
                                                && !batchDemand.NoSplitMerge;

                List<FinalTaskDraft> trialTasks;
                Dictionary<string, List<(string DemandKey, decimal ShareQty)>> trialShares;
                if (mergeStructurallyPossible)
                {
                    // 性能计数（§九「CloneTaskList 次数」）：Merge 分支的全量任务表复制（O(N)）。
                    SolverDiagnostics.CountCloneTaskList();
                    long swTasks2 = SolverDiagnostics.HotspotStart();
                    trialTasks = new List<FinalTaskDraft>(scheduledTasks);
                    SolverDiagnostics.HotspotEnd(swTasks2, SolverDiagnostics.Hotspot.CloneTaskList);
                    trialShares = CloneShares(allocationTaskShare);
                }
                else
                {
                    trialTasks = scheduledTasks;
                    trialShares = allocationTaskShare;
                }

                long swDemand = SolverDiagnostics.HotspotStart();
                var produced = RunDemandSchedule(
                    batchDemand, ops, graph, candidateDirection, constraints, trialOccupancy,
                    trialTasks, trialShares, demandByKey, request.PlanningStart, request.PlanningEnd,
                    dynamicMaterialFloor, stageOverlap, allowMergeForThisBatch,
                    out var mergedIntoTask, batch.BatchDraftKey,
                    requireIdentityPreserving, trialOccupancyPristine, request);
                SolverDiagnostics.HotspotEnd(swDemand, SolverDiagnostics.Hotspot.DemandSchedule);

                // ── 2026-10-08 第②刀：合并与否 + 合并后完成时间，均由 Merge 路径**显式上报**
                //   （`mergedIntoTask`），取代旧的「试排前后整表 PlannedEndTime 比对推断」。
                //   旧判据每次试排都要构造 `trialTasks.ToDictionary(t => t.FinalDraftId, t => t.PlannedEndTime)`
                //   —— 一次**上万个 Entry 的字典** ⇒ 实测该档单次求解 15.9 GB 托管分配的主要来源之一
                //   （38,877 次试排 × O(N)）。同时旧式「end 变了才算」若 end 恰好未变会**误判为未合并**
                //   （把已登记份额的需求记成不可行）；显式上报无此隐患。
                var merged = mergedIntoTask is not null;

                var feasible = produced.Count > 0 || merged;

                // ── P0-04（0号位 2026-10-07 (5).md）：Merge 候选必须取**合并后真实 PlannedEndTime** ──
                //   旧实现 Merge 成功给 `DateTime.MinValue`（「并入既有 Task，不产生新完成时间」）⇒
                //   `DelayMinutes(MinValue, due)` 恒 0（见 :913 短路）⇒ FORWARD 下被误评为「0 延期、完成最早」，
                //   **永远压过真实更优的另一条 Routing**，择优结果反向。
                DateTime completion;
                if (produced.Count > 0)
                {
                    completion = produced.Max(t => t.PlannedEndTime);
                }
                else if (mergedIntoTask is not null)
                {
                    // 合并成功：完成时间 = **合并后任务**的 PlannedEndTime（Merge 路径直接上报，无需再反查）。
                    completion = mergedIntoTask.PlannedEndTime;
                }
                else
                {
                    completion = DateTime.MinValue;   // 不可行候选：仅占位，SelectBestRoutingCandidate 只取可行者
                }

                var setupMinutes = produced.Sum(t => t.SetupTime);

                trials.Add((pathKey, feasible, completion, setupMinutes, candidateDirection));
            }

            constraints.ProductTimeline = realTimeline;   // 试排结束，还原真实上下文（试排不污染）

            // 试排产生的 Setup 追踪一并回滚（见上方 watermark）；落定重跑会重新写一份。
            if (constraints.TraceNotes.Count > traceNotesWatermark)
            {
                constraints.TraceNotes.RemoveRange(
                    traceNotesWatermark, constraints.TraceNotes.Count - traceNotesWatermark);
            }

            // 步骤 3：**本批**候选内择优（Q-1 冻结四层目标；P0-03：Routing 择优的业务单位是执行批，不是需求）。
            var winnerIndex = SelectBestRoutingCandidate(trials, batchDemand, constraints);
            if (winnerIndex < 0)
            {
                // 本批全部候选不可排 ⇒ 该批落不下（需求整体记 Unscheduled，见调用方聚合）。
                // P0-03：登记**首个未落定批**的下标 —— 调用方据此只对「本批及其后未试批」登记失败身份，
                //   已落定的前序批**不得**再进入 Phase4 重新展开（否则重复生产）。
                batchFailed = true;
                failedBatchIndex = batchIndex;
                break;
            }

            // 步骤 4：在**真实上下文**上重跑选中候选落定（排程确定性 ⇒ 与试排同结果）。
            var winner = plannedCandidates[winnerIndex];

            // RT-002：局部修复不得换路径 —— **逐批**登记本批选中路径（P0-03 升维）。
            //   `ChosenRoutePaths`（需求 → 单一路径）是单值表，无法承载多批各自选路；
            //   `ChosenBatchRoutePaths` 按**批键**登记 ⇒ Phase4 逐批取图，不串批、不重选。
            //   单批时两表同值（`ChosenRoutePaths` 仍登记 ⇒ 既有需求级消费点行为零回归）。
            chosenBatchRoutePaths[batch.BatchDraftKey] = winner.Key;
            if (!multiBatch)
            {
                chosenRoutePaths[actualDemand.LogicalDemandKey] = winner.Key;
            }

            // P1-01：落定必须用**胜出候选自己的方向**（= 步骤 2 试排该候选时所用方向 ⇒ 试排/落定同向）。
            var winnerDirection = trials[winnerIndex].Direction;
            landedDirection = winnerDirection;

            // ── NEW-P1-01（0号位 2026-10-09《APS_V1_3_20261009.md》§三 / §六.2）：**逐批**留痕本批落定方向 ──
            //   复审判词：「`RunBatchPlan` 每个Batch选中Winner后执行 `landedDirection = winnerDirection`；
            //     循环结束仅返回**一个** `BatchPlanRunOutcome.Direction`」⇒ 「结果为**最后一个落定Batch的方向**，
            //     不能代表前面的Batch」⇒ Phase5 按**需求 Key** 消费时会用错方向。
            //   冻结模型（0号位 §三）：每个 Execution Batch **独立**做 Direction + Routing + Resource +
            //     Calendar + Setup 联合求解 ⇒ 同一需求的不同批**可以**各有各的方向，必须逐批留痕。
            //   主消费身份 = `ExecutionBatchDraftKey`（本批键）；`Direction` 仍保留（需求级回落 + 既有比较层）。
            //   ⚠ Merge 落定的批此处同样留痕：合并目标 Task 采用**本批键**（见 `TryMergeDemandIntoTask`
            //     的 `requireIdentityPreserving` 口径）⇒ 该 Task 在 Phase5 应受**本批**方向约束。
            batchDirections[batch.BatchDraftKey] = winnerDirection;

            var batchTasks = RunDemandSchedule(
                batchDemand, winner.Ops, winner.Graph, winnerDirection, constraints, resourceOccupancy,
                scheduledTasks, allocationTaskShare, demandByKey,
                request.PlanningStart, request.PlanningEnd, dynamicMaterialFloor, stageOverlap,
                allowMergeForThisBatch, out _, batch.BatchDraftKey, requireIdentityPreserving, occupancyPristine, request);

            // 第5轮Merge修复：Merge成功时返回空List，但Demand已进入TaskShare，不应标记为Unscheduled
            if (batchTasks.Count == 0)
            {
                // 检查该Demand是否已通过Merge进入TaskShare
                bool isMerged = allocationTaskShare.Values.Any(shares =>
                    shares.Any(s => s.DemandKey == actualDemand.DemandKey
                                    || s.DemandKey == actualDemand.LogicalDemandKey));

                if (!isMerged)
                {
                    batchFailed = true;
                    failedBatchIndex = batchIndex;
                    break;
                }
                // Merge 成功：本批无新 Task（份额已并入既有 Task），继续下一批。
                continue;
            }

            scheduledTasks.AddRange(batchTasks);
            newTasks.AddRange(batchTasks);
            setupTotal += batchTasks.Sum(t => t.SetupTime);

            var batchEnd = batchTasks.Max(t => t.PlannedEndTime);
            if (batchEnd > planCompletion)
            {
                planCompletion = batchEnd;
            }
        }

        return new BatchPlanRunOutcome(batchFailed, newTasks, setupTotal, planCompletion, failedBatchIndex, landedDirection, batchDirections);
    }

    /// <summary>
    /// P1-01：在**有界候选批方案**之间做四层目标择优（0号位 (7).md §八「进入已经存在的
    ///   Direction + Routing + Resource + Calendar + Setup 联合评价」）。
    ///
    /// 每个候选在**克隆缓冲**上跑一次完整批循环（<see cref="RunBatchPlan"/>）—— 这就是「联合评价」：
    ///   方向、路由、资源、日历、Setup 全部真实参与排程，再由结果比较。
    /// 比较顺序（<see cref="CompareBatchPlans"/>）：① 全部批落定 &gt; 有批落不下 → ② 延期短 →
    ///   ③ 交期（受 Direction 控制）→ ④ Setup 总量小 → ④ 批数少（在制/Setup 更少，与 V1 基线 nMin 同向）。
    ///
    /// **零回归保证**：候选列表首元素 = V1 基线（nMin）；在第 ④ 层「批数少者优先」下基线恒不劣于
    ///   更多批的候选（除非候选在 ①②③ 层严格更优）⇒ 无改善时基线胜出，既有行为逐字保留。
    ///
    /// 试跑隔离：`constraints.ProductTimeline` 逐候选 clone/restore；`TraceNotes` 按水位回滚；
    ///   `ChosenBatchRoutePaths` / `ChosenRoutePaths` 写进**一次性副本**（不污染真实选路登记表）。
    /// </summary>
    private ExecutionBatchFormation SelectBestBatchPlan(
        List<ExecutionBatchFormation> candidates,
        LogicalProductionDemand actualDemand,
        List<(RoutePathKey Key, RoutingGraph Graph, List<OperationNode> Ops)> plannedCandidates,
        string direction,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        List<FinalTaskDraft> scheduledTasks,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> allocationTaskShare,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        DomainSolveRequest request,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        bool mergeAllowed)
    {
        var realTimeline = constraints.ProductTimeline;
        var traceNotesWatermark = constraints.TraceNotes.Count;

        int bestIndex = 0;
        BatchPlanRunOutcome bestOutcome = default;

        for (int i = 0; i < candidates.Count; i++)
        {
            // 性能计数（§九「BatchPlanCandidate 试跑次数」）：每个批方案候选 ⇒ 一次完整 RunBatchPlan 试跑。
            SolverDiagnostics.CountBatchPlanCandidateTrial();

            long swTimeline = SolverDiagnostics.HotspotStart();
            constraints.ProductTimeline = realTimeline.Clone();
            SolverDiagnostics.HotspotEnd(swTimeline, SolverDiagnostics.Hotspot.TimelineClone);

            var trialOccupancy = CloneOccupancy(resourceOccupancy, out var trialOccupancyPristine);
            // 性能计数（§九「CloneTaskList 次数」）：这里是 §七.1 点名的**全量任务表复制**（O(N)）。
            SolverDiagnostics.CountCloneTaskList();
            long swTasks = SolverDiagnostics.HotspotStart();
            var trialTasks = new List<FinalTaskDraft>(scheduledTasks);
            SolverDiagnostics.HotspotEnd(swTasks, SolverDiagnostics.Hotspot.CloneTaskList);
            var trialShares = CloneShares(allocationTaskShare);
            // 一次性副本：试跑**不得**污染真实选路登记表（P0-03 逐批登记只在落定批上发生）。
            var trialChosenBatch = new Dictionary<string, RoutePathKey>(
                constraints.ChosenBatchRoutePaths, StringComparer.Ordinal);
            var trialChosen = new Dictionary<string, RoutePathKey>(
                constraints.ChosenRoutePaths, StringComparer.Ordinal);

            long swBatchTrial = SolverDiagnostics.HotspotStart();
            var outcome = RunBatchPlan(
                candidates[i].Batches, actualDemand, plannedCandidates, direction, constraints,
                trialOccupancy, trialTasks, trialShares, demandByKey, request,
                dynamicMaterialFloor, stageOverlap, mergeAllowed, trialChosenBatch, trialChosen,
                trialOccupancyPristine);
            SolverDiagnostics.HotspotEnd(swBatchTrial, SolverDiagnostics.Hotspot.BatchPlanTrial);

            if (i == 0
                || CompareBatchPlans(outcome, candidates[i], bestOutcome, candidates[bestIndex],
                                     constraints, actualDemand) < 0)
            {
                bestIndex = i;
                bestOutcome = outcome;
            }
        }

        constraints.ProductTimeline = realTimeline;   // 试跑结束，还原真实上下文
        if (constraints.TraceNotes.Count > traceNotesWatermark)
        {
            constraints.TraceNotes.RemoveRange(
                traceNotesWatermark, constraints.TraceNotes.Count - traceNotesWatermark);
        }

        return candidates[bestIndex];
    }

    /// <summary>
    /// P1-01：批方案比较（四层目标 + 批数 tiebreak，与 <see cref="SelectBestRoutingCandidate"/> 同序）。
    /// 返回 &lt;0 ⇒ <paramref name="a"/> 更优。
    /// </summary>
    private static int CompareBatchPlans(
        BatchPlanRunOutcome a, ExecutionBatchFormation fa,
        BatchPlanRunOutcome b, ExecutionBatchFormation fb,
        ConstraintContext constraints, LogicalProductionDemand demand)
    {
        // ① 硬约束：全部批落定优于有批落不下
        if (a.BatchFailed != b.BatchFailed)
        {
            return a.BatchFailed ? 1 : -1;
        }

        var due = constraints.EffectiveDue(demand);

        // ② 履约：延期短者优先
        var delayA = DelayMinutes(a.Completion, due);
        var delayB = DelayMinutes(b.Completion, due);
        if (delayA != delayB)
        {
            return delayA < delayB ? -1 : 1;
        }

        // ③ 交期：均按期时受 Direction 控制。
        //   P1-01 整改：方向取**各候选批方案实际落定**的方向（`BatchPlanRunOutcome.Direction`），
        //   不再用外部传入的单值套所有候选。两方案方向不同 ⇒ 本层不可比（目标相反）⇒ 判平，
        //   交由 ④ Setup / 批数 裁决 —— 与候选输入顺序无关。
        if (delayA == 0 && a.Completion != b.Completion
            && string.Equals(a.Direction, b.Direction, StringComparison.Ordinal))
        {
            if (string.Equals(a.Direction, "BACKWARD", StringComparison.Ordinal))
            {
                return a.Completion > b.Completion ? -1 : 1;
            }
            return a.Completion < b.Completion ? -1 : 1;
        }

        // ④ 次级：Setup 总量小者优先
        if (a.SetupTotal != b.SetupTotal)
        {
            return a.SetupTotal < b.SetupTotal ? -1 : 1;
        }

        // ④ 次级：批数少者优先（在制/Setup 更少；与 V1 基线 nMin 同向 ⇒ 无改善时保持基线）
        if (fa.Batches.Count != fb.Batches.Count)
        {
            return fa.Batches.Count < fb.Batches.Count ? -1 : 1;
        }

        return 0;   // 完全等价 ⇒ 调用方保留先出现者（基线在前）
    }

    /// <summary>
    /// 在**给定上下文**上排程一个需求的全部工序（Merge 开关分支）。
    /// 抽出本方法供 C桶候选内择优的**试排**与**落定**共用同一段逻辑，保证试排与落定结果一致。
    /// ⚠ 调用方负责传入隔离的 <paramref name="resourceOccupancy"/> / <paramref name="scheduledTasks"/> /
    ///   <paramref name="allocationTaskShare"/>（试排传克隆，落定传真实对象）。
    /// </summary>
    private List<FinalTaskDraft> RunDemandSchedule(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        RoutingGraph routingGraph,
        string direction,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        List<FinalTaskDraft> scheduledTasks,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> allocationTaskShare,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        DateTime planningStart,
        DateTime planningEnd,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        bool allowMerge,
        out FinalTaskDraft? mergedIntoTask,   // 2026-10-08 第②刀：Merge **自己上报**合并后的 Task（取代整表 end 比对推断）
        string? batchDraftKey = null,   // P0-01/P0-02：本批归批键（null ⇒ 回落 ExecutionBatchKey(demandKey, 1)）
        bool requireIdentityPreservingMerge = false,   // P1-01：多批需求 ⇒ 只合并到「未归属执行批」的目标 Task
        Dictionary<int, List<TimeWindow>>? occupancyPristine = null,   // 2026-10-08 COW：占用表冻结快照（null ⇒ 自有表）
        DomainSolveRequest? request = null)   // OWN-P0-02：多 Operation Stage 合批的 Batch Policy Max 判定
    {
        // 第4轮Merge修复：检测是否可以合并到已有Task
        if (allowMerge)
        {
            return TryMergeOrSchedule(
                demand, operations, routingGraph, direction, constraints,
                resourceOccupancy, scheduledTasks, allocationTaskShare, demandByKey,
                planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
                out mergedIntoTask, batchDraftKey,
                requireIdentityPreservingMerge, occupancyPristine, request);
        }

        mergedIntoTask = null;   // 未走 Merge 路径 ⇒ 构造上不可能合并
        return ScheduleDemandOperations(
            demand, operations, routingGraph, direction, constraints,
            resourceOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap, batchDraftKey, occupancyPristine);
    }

    /// <summary>
    /// C桶候选内择优（0号位 2026-10-07 裁决 Q-1/Q-2）——
    /// 按冻结**四层目标**逐层比较，**不新增** Routing 专属加权目标函数（Q-1 明文禁止）。
    ///
    /// 冻结依据：《APS_有限产能排产与滚动90天计划业务说明 v1.7》+《APS_V1_最终全部流程与业务基线 v1.8》——
    ///   ① 硬约束 → ② 履约优先 → ③ 客户承诺/交期目标 → ④ 次级优化（Setup / WIP / 利用率 / 稳定性）。
    ///   「如果存在能满足客户承诺目标的方案，不能为了 Setup 或利用率选择更差履约方案」。
    ///
    /// 比较顺序（可解释的目标顺序，非加权）：
    ///   1) 不可行候选淘汰（Q-1：「先过滤违反硬约束的候选」）；
    ///   2) ② 履约：按期（延期 = 0）优于延期；都延期 ⇒ 延期更短者优先；
    ///   3) ③ 交期：都按期时受该 Execution Batch 的 **Direction** 控制（Q-1 定死的表）——
    ///        FORWARD    → 更早可行完成优先；
    ///        BACKWARD   → 在不延期前提下更靠近 RequiredAvailableTime / Due（避免过早生产）；
    ///        MIXED      → 沿用现有求解结果比较（取更早完成，稳定）；
    ///        P1-01 整改：`Direction` 取**每条候选自己的**（`trials[i].Direction`；`AUTO` 已在试排前按该
    ///          路径自身工序解析）。两候选方向**不同** ⇒ 本层判平（各自目标相反，硬比即外推），
    ///          由 ④/⑤ 裁决 ⇒ **结果与候选输入顺序无关**。
    ///   4) ④ 次级：Setup 总量小者优先；
    ///   5) 确定性 tiebreak：(RouteCode, PathId) 序。
    ///
    /// ⚠ PreferredResource 只进次级优化、**不得反压履约**（Q-1 明文）。V1 把它放在既有 Phase2 排程内部
    ///   消费（资源连续性软偏好），候选比较层不再额外加权 —— 否则即等于新增 Routing 专属加权目标。
    /// </summary>
    /// <returns>选中候选下标；全部不可行时返回 -1。</returns>
    private static int SelectBestRoutingCandidate(
        List<(RoutePathKey Key, bool Feasible, DateTime Completion, decimal SetupMinutes, string Direction)> trials,
        LogicalProductionDemand demand,
        ConstraintContext constraints)
    {
        var due = constraints.EffectiveDue(demand);

        static int Compare(
            (RoutePathKey Key, bool Feasible, DateTime Completion, decimal SetupMinutes, string Direction) a,
            (RoutePathKey Key, bool Feasible, DateTime Completion, decimal SetupMinutes, string Direction) b,
            DateTime due)
        {
            // ① 可行优于不可行
            if (a.Feasible != b.Feasible)
            {
                return a.Feasible ? -1 : 1;
            }

            // ② 履约：延期短者优先（按期 = 0）—— 与方向无关（真实业务量：延期分钟数）
            var delayA = DelayMinutes(a.Completion, due);
            var delayB = DelayMinutes(b.Completion, due);
            if (delayA != delayB)
            {
                return delayA < delayB ? -1 : 1;
            }

            // ③ 交期：均按期时受 **Direction** 控制。
            //   P1-01 整改：方向是**每条候选自己的**（AUTO 下随该路径 lead 而不同）。
            //   两候选方向**不同** ⇒ 本层**不可比** —— 各自的目标函数相反（BACKWARD 要「更靠近 Due」，
            //   FORWARD 要「更早」），硬比即等于拿一方的方向外推另一方（正是复审所指的缺陷）
            //   ⇒ 判平，交由 ④ Setup / ⑤ 确定性 (RouteCode, PathId) 序裁决，**与输入顺序无关**。
            if (delayA == 0 && a.Completion != b.Completion
                && string.Equals(a.Direction, b.Direction, StringComparison.Ordinal))
            {
                if (string.Equals(a.Direction, "BACKWARD", StringComparison.Ordinal))
                {
                    // 倒排：不延期前提下更靠近 Due（避免过早生产）⇒ 完成更晚者优先（两者均 ≤ Due）
                    return a.Completion > b.Completion ? -1 : 1;
                }

                // FORWARD / MIXED：更早可行完成优先
                return a.Completion < b.Completion ? -1 : 1;
            }

            // ④ 次级：Setup 总量小者优先
            if (a.SetupMinutes != b.SetupMinutes)
            {
                return a.SetupMinutes < b.SetupMinutes ? -1 : 1;
            }

            // ⑤ 确定性 tiebreak：(RouteCode, PathId) 序
            var rc = string.CompareOrdinal(a.Key.RouteCode, b.Key.RouteCode);
            return rc != 0 ? rc : a.Key.PathId.CompareTo(b.Key.PathId);
        }

        var best = -1;
        for (int i = 0; i < trials.Count; i++)
        {
            if (!trials[i].Feasible)
            {
                continue;
            }

            if (best < 0 || Compare(trials[i], trials[best], due) < 0)
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// P1-01（0号位 2026-10-09《APS_V1_2_20261009.md》§三 P1-01）：按**该候选/该批自己的工序**解析实际 Direction。
    ///
    /// · 非 `AUTO`（FORWARD / BACKWARD / MIXED）⇒ **原样返回**（零回归：显式策略方向不因候选而异）。
    /// · `AUTO` ⇒ 用**本候选的工序集**跑 <see cref="SchedulingDirectionResolver"/>（其 Slack 判据依赖
    ///   该路径的**总标准工时**）⇒ 每条候选的方向与其实际工序一致，不再由「任意第一 Path」外推全路由。
    /// · 无工序（`ops` 空）⇒ 无从估算 lead ⇒ 原样返回（调用方按不可行候选处理）。
    /// </summary>
    private static string ResolveDirectionForOps(
        string strategyDirection,
        LogicalProductionDemand demand,
        IReadOnlyList<OperationNode> ops,
        ConstraintContext constraints,
        DateTime planningStart,
        DateTime dynamicMaterialFloor)
    {
        if (strategyDirection != SchedulingDirectionResolver.Auto || ops.Count == 0)
        {
            return strategyDirection;
        }

        return SchedulingDirectionResolver.Resolve(
            demand, ops, constraints, planningStart, dynamicMaterialFloor, demandGoal: null).Direction;
    }

    /// <summary>延期分钟数（0 = 按期）。Merge 无新完成时间 / 无交期 ⇒ 视为按期。</summary>
    private static long DelayMinutes(DateTime completion, DateTime due)
    {
        if (completion == DateTime.MinValue || due == DateTime.MaxValue)
        {
            return 0;
        }

        var minutes = (long)(completion - due).TotalMinutes;
        return minutes > 0 ? minutes : 0;
    }

    /// <summary>
    /// 资源占用**试排隔离副本**（copy-on-write）。
    ///
    /// ── 2026-10-08 性能（0号位《未命名的Markdown文件 (2)(1).md》§7.1/§7.2 + §十 允许的「增量 Delta /
    ///    Copy-on-write」）──
    /// 旧实现**每次** `source.ToDictionary(kv => kv.Key, kv => new List&lt;TimeWindow&gt;(kv.Value))`
    /// ⇒ 每次克隆都为**每一个资源**分配并复制完整占用窗列表。实测 42,261 Task 场景本方法被调用 **94,940 次**，
    /// 8,000 目标档下累计 **4,585 ms（ΣPhase 21.8%）**，并贡献约 **11.7 GB / 20.5 GB** 的全部托管分配
    /// ⇒ 直接推高 Gen2 GC（该档实测 **596 次 Gen2**）。这是「速度慢」单项最大的来源。
    ///
    /// 新实现：**只做两层浅拷**（工作副本 + 冻结快照，均为 O(资源数) 的指针拷贝），
    /// 占用窗列表本体**暂共享**；谁真被写、谁才在写入前深拷**该**资源那一份（见 <see cref="EnsureOwned"/>）。
    /// 单条需求试排最多命中其工序合格资源（本例 ≤3），资源数 200 ⇒ 单次克隆的列表复制量降到约 1/66。
    ///
    /// **语义零变化（构造上安全）**：返回的 `pristine` 是**冻结快照**，两侧写入前都比对
    /// `ReferenceEquals(working[rid], pristine[rid])` —— 相等才拷贝。快照本身永不被改写，
    /// 故「克隆视图」与「源视图」在任何写入顺序下都不会互相污染
    /// （这正是旧实现深拷所保证的不变式，现改为按需支付）。
    /// </summary>
    private static Dictionary<int, List<TimeWindow>> CloneOccupancy(
        Dictionary<int, List<TimeWindow>> source,
        out Dictionary<int, List<TimeWindow>> pristine)
    {
        // 性能计数（0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§九「CloneOccupancy 次数」）。
        SolverDiagnostics.CountCloneOccupancy();
        long sw = SolverDiagnostics.HotspotStart();

        var working = new Dictionary<int, List<TimeWindow>>(source);    // 浅拷：O(资源数)
        pristine = new Dictionary<int, List<TimeWindow>>(source);       // 冻结快照：O(资源数)，永不被改写

        SolverDiagnostics.HotspotEnd(sw, SolverDiagnostics.Hotspot.CloneOccupancy);
        return working;
    }

    /// <summary>
    /// 写入前确保 `working[resourceId]` 是**本实例独占**的列表（copy-on-write）。
    /// `pristine == null` ⇒ 该表是本实例自有（非克隆），直接原地写。
    /// 否则该 list 若仍与冻结快照共享 ⇒ 先深拷一份再写（原表与快照均不受影响）。
    /// </summary>
    private static void EnsureOwned(
        Dictionary<int, List<TimeWindow>> working,
        Dictionary<int, List<TimeWindow>>? pristine,
        int resourceId)
    {
        if (pristine is null) return;
        if (!working.TryGetValue(resourceId, out var list)) return;     // 新键：本就本实例独占
        if (pristine.TryGetValue(resourceId, out var original) && ReferenceEquals(list, original))
        {
            working[resourceId] = new List<TimeWindow>(original);
        }
    }

    /// <summary>
    /// 占用表**有序插入**（2026-10-08 SlotSearch 索引化）。
    /// ── 为什么 ──
    /// 读取端 <see cref="FindFirstAvailableSlot"/> / <see cref="HasConflict"/> 旧实现**每次调用**都要从头扫全表
    /// （前者还先扫一遍确认「表是否有序」）。实测 42,261 Task 档该调用族共 **4,682,936 次**，
    /// 占该档 ΣPhase 的 **36.6%（8k）/ 65%（20k）**，是当前最大单项。
    /// 维持「列表按 `Start` 升序」不变式后，读取端可**二分跳段**，免掉那两趟 O(W)。
    /// ── 代价取舍 ──
    /// 有序插入的 `List.Insert` 是 O(W) 搬移；把成本从**读**（4.68M 次）挪到**写**（约 4 万次），
    /// 且正排天然追加在末尾 ⇒ 二分定位到 `Count` ⇒ `Insert` 退化为 O(1) 追加。
    /// ── 不变式与前提（读取端的正确性依赖它们）──
    /// ① 该资源列表按 `Start` 升序 —— 由本方法保证，且 **Phase2 所有写入点都经本方法**
    ///    （初始构建 / 正排 / 倒排 / Merge 追加）。
    /// ② 该资源上的窗**互不重叠**（⇒ `End` 随 `Start` 非降）—— 由「找槽只在空档落位」
    ///    与「Merge 先冲突检查再改写」保证；读取端据此才敢整体跳过某段前缀。
    /// ⚠ Phase4 自建占用表、自用其 slot finder，**不在本变更范围**（其占比仅 3.5%）。
    /// ── 2026-10-10 V1_3 F-01 整改（0号位 裁决 F-01）──
    /// 「有序插入」意味着**新窗可能落在列表中间**（倒排 / 合批落定即如此），因此**按数量截尾的回滚是错的**。
    /// 本方法新增可选 <paramref name="insertLog"/>：每次成功插入后登记 `(资源, 窗)` **身份**，
    /// 供 <see cref="RollbackInsertedOccupancyWindows"/> **按身份**精确撤销（见其注释）。
    /// </summary>
    internal static void AddOccupancyWindow(
        Dictionary<int, List<TimeWindow>> occupancy,
        Dictionary<int, List<TimeWindow>>? pristine,
        int resourceId,
        TimeWindow window,
        List<(int ResourceId, TimeWindow Window)>? insertLog = null)
    {
        EnsureOwned(occupancy, pristine, resourceId);

        var list = occupancy[resourceId];
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            var w = list[mid];
            if (w.Start < window.Start || (w.Start == window.Start && w.End <= window.End)) lo = mid + 1;
            else hi = mid;
        }
        list.Insert(lo, window);

        // F-01：登记插入**身份**（不是位置、不是数量）——回滚只认这一条，绝不截尾。
        insertLog?.Add((resourceId, window));
    }

    /// <summary>合批份额深拷贝（候选试排隔离用）。</summary>
    private static Dictionary<string, List<(string DemandKey, decimal ShareQty)>> CloneShares(
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> source)
    {
        // 性能计数（§九「Clone 次数」族）：份额表重建为 O(N) 字典操作，是候选试排隔离的成本之一。
        SolverDiagnostics.CountCloneShares();
        long sw = SolverDiagnostics.HotspotStart();
        var result = source.ToDictionary(kv => kv.Key, kv => new List<(string, decimal)>(kv.Value));
        SolverDiagnostics.HotspotEnd(sw, SolverDiagnostics.Hotspot.CloneShares);
        return result;
    }

    /// <summary>
    /// 排程单个需求的所有工序
    /// 文档：§八 Forward/Backward/Mixed
    /// </summary>
    private List<FinalTaskDraft> ScheduleDemandOperations(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        RoutingGraph routingGraph,
        string direction,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart,
        DateTime planningEnd,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        string? batchDraftKey = null,
        Dictionary<int, List<TimeWindow>>? occupancyPristine = null,   // 2026-10-08 COW：透传给写占用表的工序
        List<(int ResourceId, TimeWindow Window)>? occupancyInsertLog = null)   // 2026-10-10 V1_3 F-01：插入身份日志（透传给写占用表的工序）
    {
        var tasks = new List<FinalTaskDraft>();

        // 根据排程方向选择策略
        // 冻结口径：
        //   · 规则清单 v1.5 **B-004**：「Direction 支持 AUTO/FORWARD/BACKWARD/MIXED；OrderType 不得直接决定 Direction」
        //     ⇒ `AUTO` 是**正式冻结取值**，必须显式承载，不再由 `else` 隐式兜底。
        //   · 规则清单 v1.5 **B-005**：Direction 由 DemandGoal / RequiredAvailableTime / Slack / Material /
        //     Resource / Execution / Firm-Frozen-Lock 等上下文综合决定，Owner = 1号位。
        //   · 业务基线 v1.8 §Demand Goal、Batch、Direction、Routing：同义（不得由 OrderType 硬映射）。
        //
        // ── 2026-10-08 复审 P1-DIR-01 整改（0号位《未命名的Markdown文件 (2)(1).md》§三 / §十四 第二优先级）──
        //   **撤销**「`AUTO` 与 `MIXED` 行为等价（沿用 0号位 2026-10-07 Q-1）」的实现 —— 0号位 明判：
        //   「`AUTO == MIXED` 不能再作为最终 V1 实现」，此项关闭前 1号位 不能通过 V1 完整性验收。
        //   `AUTO` 现改为**按本需求/本执行批自身上下文正式自决**（见 <see cref="SchedulingDirectionResolver"/>）：
        //     Firm/Frozen/Lock + Execution 连续性 + Material 下界 + RequiredAvailableTime/Slack + Resource
        //     （+ DemandGoal，载体缺失时登记缺口）⇒ FORWARD / BACKWARD / MIXED 三者之一。
        //   调用点位置正确性：P0-01 链规定「每个 Batch 分别进行 Direction + RoutingCandidate + Resource +
        //     Calendar + Setup 联合求解」⇒ 本方法**逐需求逐批**被调用，正是 `ResolveDirection(demand / executionBatch, context)`。
        //   ⚠ `demandGoal: null`：冻结侧 `DemandGoal`（P-006 两值）**无 C# 载体**（全仓 grep 零命中），
        //     属 2号位 Pegging 传播缺口（P-008）；此处**不猜、不自造字段**，由 Resolver 记 `DEMAND_GOAL_ABSENT`。
        //   `MIXED` 保持字面语义不变（0号位 §三：可作为人工/策略明确模式继续保留）。
        //
        // ── P1-01（0号位 2026-10-09 第三轮复审 §二）：本分支现为**防御性兜底**，生产路径**不再经过** ──
        //   复审判词：AUTO 只在「排程执行时」本地解析，与批/路由候选评分所用方向**不一致**。
        //   整改后，AUTO 已在调用方（`Schedule`，本需求进入批/路由择优之前）**一次性**解析为具体方向，
        //   并经 `SelectBestBatchPlan` / `RunBatchPlan` **同一 `resolvedDirection`** 传入本方法
        //   ⇒ 正常情况下本处 `direction` **恒不为 `AUTO`**。
        //   保留本分支仅为「万一有旁路调用直接传 AUTO」时行为不退化（仍按 B-005 自决，绝不回到 AUTO==MIXED）。
        if (direction == SchedulingDirectionResolver.Auto)
        {
            direction = SchedulingDirectionResolver.Resolve(
                demand, operations, constraints, planningStart, dynamicMaterialFloor,
                demandGoal: null).Direction;
        }

        if (direction == "BACKWARD")
        {
            tasks = ScheduleBackward(demand, operations, routingGraph, constraints, resourceOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap, batchDraftKey, occupancyPristine, occupancyInsertLog);
        }
        else if (direction == "FORWARD")
        {
            tasks = ScheduleForward(demand, operations, routingGraph, constraints, resourceOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap, batchDraftKey, occupancyPristine, occupancyInsertLog);
        }
        else if (direction == "MIXED")
        {
            // MIXED：先尝试倒排，失败则转正排（§八 8.3 Mixed模式）。
            //   本分支**只**服务显式 MIXED；`AUTO` 已在上方按 B-005 自决为具体方向或（信号冲突时）MIXED。
            //   V1_4 NEW-05：倒排失败转正排**必须先撤销倒排残留**（见 `RunBackwardThenForwardOnIsolation`）。
            tasks = RunBackwardThenForwardOnIsolation(
                demand, operations, routingGraph, constraints, resourceOccupancy,
                planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
                batchDraftKey, occupancyPristine, occupancyInsertLog);
        }
        else
        {
            // 未知 Direction：2号位 投影侧 `SolverStrategyModeMap.ToDirection` 已对未知枚举防御为 "BACKWARD"，
            // 故此处理论不可达；万一到达，保持历史行为（等效 MIXED），**不改变结果**。
            //   V1_4 NEW-05：同一条「倒排部分成功后失败」缺陷在本分支**同样存在**（同一段旧代码形状），
            //   故一并走隔离/回滚路径 —— 仅在**确有残留**时才与整改前不同（残留本就是缺陷）。
            tasks = RunBackwardThenForwardOnIsolation(
                demand, operations, routingGraph, constraints, resourceOccupancy,
                planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
                batchDraftKey, occupancyPristine, occupancyInsertLog);
        }

        return tasks;
    }

    /// <summary>
    /// V1_4 NEW-05：MIXED（及等效 MIXED 的未知 Direction）「先倒排、失败转正排」的**隔离 + 回滚**执行。
    ///
    /// 【缺陷（本号位核对：**成立**，且为**历史遗留**，非本 Commit 新引入）】
    ///   旧实现在**同一份** `resourceOccupancy` / `ProductTimeline` / `occupancyInsertLog` 上跑倒排，
    ///   返回空即**原样复用**同一状态再跑正排。而倒排是**逐工序**写状态的
    ///   （`ScheduleBackward` 每成功安排一道即 `AddOccupancyWindow` + `ProductTimeline.Place`），
    ///   中途某道失败只 `return new List<FinalTaskDraft>()` ⇒ **已成功工序的占用残留**（幽灵占用，
    ///   INV-CAL-001）并污染正排找槽 ⇒ 「倒排最后一道成功、上一道失败、正排成功」时
    ///   **最终占用 ≠ FinalTask**。
    ///
    /// 【整改】倒排尝试运行在**独立的插入日志 + 时间线快照 + 追踪水位**上；失败转正排**之前**按身份
    ///   **彻底撤销**倒排写入（占用走 `RollbackInsertedOccupancyWindows` 身份回滚、时间线还原、
    ///   `TraceNotes` 按水位截除）⇒ 正排从**与倒排前一致**的初始状态开始。
    ///   复用 F-01 的**有序回滚原语**（不新增机制），并覆盖**普通非合批主路径**（本方法即该主路径）。
    /// </summary>
    private List<FinalTaskDraft> RunBackwardThenForwardOnIsolation(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        RoutingGraph routingGraph,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart,
        DateTime planningEnd,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        string? batchDraftKey,
        Dictionary<int, List<TimeWindow>>? occupancyPristine,
        List<(int ResourceId, TimeWindow Window)>? occupancyInsertLog)
    {
        // 倒排尝试：独立插入日志 —— 失败即整体撤销，**成功才并入**调用方日志（调用方回滚契约不变）。
        var backwardBaselineResourceIds = CaptureOccupancyResourceIds(resourceOccupancy);
        var backwardInsertLog = new List<(int ResourceId, TimeWindow Window)>();
        var timelineBeforeBackward = constraints.ProductTimeline.Clone();
        var traceWatermark = constraints.TraceNotes.Count;

        var tasks = ScheduleBackward(
            demand, operations, routingGraph, constraints, resourceOccupancy,
            planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
            batchDraftKey, occupancyPristine, backwardInsertLog);

        if (tasks.Count > 0)
        {
            occupancyInsertLog?.AddRange(backwardInsertLog);
            return tasks;
        }

        // ── 倒排失败（可能**部分成功**）⇒ 转正排前彻底撤销倒排残留（占用 / 时间线 / 追踪）──
        var unmatched = RollbackInsertedOccupancyWindows(
            resourceOccupancy, backwardBaselineResourceIds, backwardInsertLog);
        SolverDiagnostics.CountOccupancyRollbackUnmatched(unmatched);
        SolverDiagnostics.CountMixedBackwardRollbackWindows(backwardInsertLog.Count - unmatched);
        constraints.ProductTimeline = timelineBeforeBackward;
        if (constraints.TraceNotes.Count > traceWatermark)
        {
            constraints.TraceNotes.RemoveRange(
                traceWatermark, constraints.TraceNotes.Count - traceWatermark);
        }

        return ScheduleForward(
            demand, operations, routingGraph, constraints, resourceOccupancy,
            planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
            batchDraftKey, occupancyPristine, occupancyInsertLog);
    }

    /// <summary>
    /// 倒排：从 RequiredAvailableTime 往前推
    /// P0-05修复：倒排也必须服从物料时间约束，Task不能早于Material AvailableTime
    /// </summary>
    private List<FinalTaskDraft> ScheduleBackward(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        RoutingGraph routingGraph,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart,
        DateTime planningEnd,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        string? batchDraftKey = null,
        Dictionary<int, List<TimeWindow>>? occupancyPristine = null,   // 2026-10-08 COW：透传
        List<(int ResourceId, TimeWindow Window)>? occupancyInsertLog = null)   // 2026-10-10 V1_3 F-01：插入身份日志（透传）
    {
        var tasks = new List<FinalTaskDraft>();
        var currentEndTime = constraints.EffectiveDue(demand);   // M5 第一批：倒排锚用覆盖交期

        // P0-05修复：获取物料最早可用时间，作为倒排的硬约束下界
        // 块4（任务喂任务）：合并子件完成时间，父件不能早于子件完成
        var materialEarliestTime = GetMaterialEarliestTime(
            demand.AllocationSequence,
            demand.NetOutputQty,
            constraints,
            planningStart,
            dynamicMaterialFloor,
            out bool isMaterialSufficient);

        // P0-05修复：物料总量不足时，标记为业务Unscheduled（不是技术失败）
        if (!isMaterialSufficient)
        {
            return new List<FinalTaskDraft>(); // 物料总量不足
        }

        // 从最后一道工序往前倒推
        for (int i = operations.Count - 1; i >= 0; i--)
        {
            var operation = operations[i];

            // OperationPlanningMode 分型（0号位 20260922）：非资源工序（UNCONSTRAINED/WAIT_ONLY）
            // 不占资源、保留工艺时间走链 —— 以 currentEndTime 为锚倒推产 ResourceId=NULL 的 Task，不判失败。
            if (IsNonResourceMode(operation.OperationPlanningMode))
            {
                var nonResourceTask = CreateNonResourceTask(demand, operation, currentEndTime, backward: true, batchDraftKey);
                tasks.Insert(0, nonResourceTask);   // 倒序插入
                // 更新倒排锚：本工序开始时间（含 LagTime）
                currentEndTime = nonResourceTask.PlannedStartTime;
                if (i > 0)
                {
                    var prevOperation = operations[i - 1];
                    var lagTime = GetLagTime(
                        OperationNodeKey.Of(prevOperation.StageCode, prevOperation.OperationCode),
                        OperationNodeKey.Of(operation.StageCode, operation.OperationCode),
                        routingGraph);
                    currentEndTime = currentEndTime.AddMinutes(-(double)lagTime);
                }
                continue;
            }

            // 查找合格资源（P1-11：软偏好资源优先，Preferred 最前、Fallback 次之）
            var eligibleResources = OrderResourcesByPreference(
                demand,
                GetEligibleResources(demand.MaterialId, operation, constraints),
                constraints,
                operation.OperationCode,
                operation.StageCode);   // P1-02：软偏好仅作用于当前承接工序（身份 = StageCode + OperationCode）
            if (eligibleResources.Count == 0)
            {
                return new List<FinalTaskDraft>(); // 无合格资源（FINITE_RESOURCE fail-closed）
            }

            // 尝试在合格资源上找时间槽
            FinalTaskDraft? scheduledTask = null;
            foreach (var resourceId in eligibleResources)
            {
                // P0-04修复：Duration = StandardDuration × PlannedProcessQty ÷ CapacityFactor
                // 第4轮Setup修复：加上SetupTime占用资源时间轴
                // 第5轮修复：CapacityFactor缺失或非法时不能继续
                var capacityFactor = GetCapacityFactor(demand.MaterialId, operation, resourceId, constraints);
                if (capacityFactor == null || capacityFactor <= 0)
                {
                    return new List<FinalTaskDraft>(); // CapacityFactor缺失/非法，无法计算Duration
                }
                var adjustedDuration = operation.StandardDuration * demand.PlannedProcessQty / capacityFactor.Value;
                var processDuration = TimeSpan.FromMinutes((double)adjustedDuration);

                // P1-02 item1 接线（阶段二）：倒排 Setup 同样走规则查找（v1.2 §2/§5），不再读 RoutingOperation.SetupTime。
                // Setup 影响 candidateStart 反推，有界迭代至解析收敛（≤4轮：以当前 Setup 搜槽 → 槽起点前产品重解析 → 变化则重搜）；
                // 未收敛/无槽 → 该资源不可行（与旧「找不到槽→下一资源」语义一致）。
                // P1-02（StageOverlap）：上游完成转运批量后下游可提前开工，倒排把上游结束时间后延 overlapExtension。
                var overlapExtension = GetOverlapExtension(operation, demand.PlannedProcessQty, processDuration, stageOverlap);

                decimal setupMinutes = 0m;
                TimeWindow? foundSlot = null;
                SetupOptimizer.SetupResolution? convergedResolution = null;   // SetupSource 填充：记录收敛解析结果
                for (int iter = 0; iter < 4; iter++)
                {
                    var totalDuration = processDuration + TimeSpan.FromMinutes((double)setupMinutes);

                    // 计算候选时间窗（包含Setup占用）
                    var candidateEnd = currentEndTime + overlapExtension;
                    var candidateStart = candidateEnd - totalDuration;

                    // P0-05修复：倒排Task不能早于物料可用时间；也不能早于计划期起点
                    if (candidateStart < materialEarliestTime || candidateStart < planningStart)
                    {
                        foundSlot = null;
                        break;
                    }

                    var slot = FindBackwardSlot(
                        candidateStart,
                        candidateEnd,
                        resourceId,
                        constraints,
                        resourceOccupancy,
                        planningStart);

                    if (!slot.HasValue)
                    {
                        foundSlot = null;
                        break;
                    }

                    var resolved = SetupOptimizer.ResolveSetupCore(
                        operation.OperationCode, resourceId,
                        constraints.ProductTimeline.GetPrevMaterial(resourceId, slot.Value.Start),
                        demand.MaterialId, constraints.SetupExactRules, constraints.SetupDefaultRules);

                    if (resolved.SetupMinutes == setupMinutes)
                    {
                        foundSlot = slot;   // 收敛：占用窗与规则解析值一致
                        convergedResolution = resolved;   // SetupSource 填充：收敛值即实际命中类型
                        break;
                    }
                    setupMinutes = resolved.SetupMinutes;   // 未收敛：以新 Setup 重搜
                }

                if (foundSlot.HasValue)
                {
                    // 找到可用槽 → 生成任务
                    // Task的PlannedStartTime是加工开始时间（不含Setup）
                    var setupDuration = TimeSpan.FromMinutes((double)setupMinutes);
                    var taskStart = foundSlot.Value.Start + setupDuration;
                    var taskSetupSource = convergedResolution.HasValue
                        ? SetupOptimizer.SetupOutcomeToSource(convergedResolution.Value.Outcome)
                        : null;
                    scheduledTask = CreateTask(demand, operation, resourceId, taskStart, foundSlot.Value.End, setupMinutes, constraints, taskSetupSource, batchDraftKey);

                    // 更新资源占用：从Setup开始到End结束
                    // 2026-10-08 COW + 有序插入：先脱钩，再按 Start 二分定位插入（维持读端可二分的不变式）
                    AddOccupancyWindow(resourceOccupancy, occupancyPristine, resourceId,
                        new TimeWindow(foundSlot.Value.Start, foundSlot.Value.End), occupancyInsertLog);
                    constraints.ProductTimeline.Place(resourceId, foundSlot.Value.End, demand.MaterialId);

                    // P0-17修复：应用Routing LagTime到前序工序的结束时间约束
                    // currentEndTime应该是加工开始时间（Setup之前）
                    currentEndTime = foundSlot.Value.Start;
                    if (i > 0)
                    {
                        var prevOperation = operations[i - 1];
                        var lagTime = GetLagTime(
                            OperationNodeKey.Of(prevOperation.StageCode, prevOperation.OperationCode),
                            OperationNodeKey.Of(operation.StageCode, operation.OperationCode),
                            routingGraph);
                        currentEndTime = currentEndTime.AddMinutes(-(double)lagTime);
                    }
                    break;
                }
            }

            if (scheduledTask == null)
            {
                return new List<FinalTaskDraft>(); // 排程失败
            }

            tasks.Insert(0, scheduledTask); // 倒序插入
        }

        return tasks;
    }

    /// <summary>
    /// 正排：从物料可用时间往后推
    /// P0-17修复：应用Routing LagTime到工序间时间依赖
    /// 第4轮C2修复：按真实DAG依赖执行，不串行化并行分支
    /// </summary>
    private List<FinalTaskDraft> ScheduleForward(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        RoutingGraph routingGraph,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart,
        DateTime planningEnd,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        string? batchDraftKey = null,
        Dictionary<int, List<TimeWindow>>? occupancyPristine = null,   // 2026-10-08 COW：透传
        List<(int ResourceId, TimeWindow Window)>? occupancyInsertLog = null)   // 2026-10-10 V1_3 F-01：插入身份日志（透传）
    {
        var tasks = new List<FinalTaskDraft>();

        // 获取物料最早可用时间
        // P0-05修复：传入所需数量，根据累计可用量确定启动时间，并验证总量是否足够
        // 块4（任务喂任务）：合并子件完成时间，父件不能早于子件完成
        var materialEarliestStart = GetMaterialEarliestTime(
            demand.AllocationSequence,
            demand.NetOutputQty,
            constraints,
            planningStart,
            dynamicMaterialFloor,
            out bool isMaterialSufficient);

        // P0-05修复：物料总量不足时，标记为业务Unscheduled（不是技术失败）
        if (!isMaterialSufficient)
        {
            return new List<FinalTaskDraft>(); // 物料总量不足
        }

        // 第4轮C2修复：记录每个Operation的实际完成时间，用于DAG依赖计算。
        // 0号位 2026-09-29 裁决 §5.3：键升维为 (StageCode, OperationCode) —— 同码跨 Stage 时
        // 旧单键会让两个 Stage 的同名工序**互相覆盖**（完成时间串台）。
        var operationEndTimes = new Dictionary<OperationNodeKey, DateTime>();

        // 第4轮P7修复：记录每个Operation的阈值启动时间（完成TransferBatchSize数量的时间）
        // 用于Stage overlap：下游工序可在上游达到TransferBatchSize后启动，无需等待全部完成
        var operationThresholdTimes = new Dictionary<OperationNodeKey, DateTime>();

        // 从第一道工序往后推
        for (int i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            var operationKey = OperationNodeKey.Of(operation.StageCode, operation.OperationCode);

            // 第4轮C2修复：计算当前Operation的真实最早开始时间
            // 1. 如果是根工序（无前驱），从物料可用时间开始
            // 2. 如果有前驱，从所有前驱的（结束时间+Lag）中取最大值
            DateTime earliestStart = materialEarliestStart;

            if (routingGraph.Dependencies.TryGetValue(operationKey, out var predecessorEdges))
            {
                // 有前驱：遍历所有前驱边，计算最晚的（前驱结束时间+Lag）
                // 第4轮P7修复：如果前驱配置了TransferBatchSize，使用阈值时间而非完成时间
                foreach (var edge in predecessorEdges)
                {
                    if (operationEndTimes.TryGetValue(edge.From, out var predecessorEnd))
                    {
                        // 检查前驱工序是否配置了TransferBatchSize（P1-02：受 AllowOverlap 门控 + 冻结 TransferBatchQty 回落）
                        DateTime effectiveTime = predecessorEnd;
                        if (stageOverlap.AllowOverlap
                            && routingGraph.Operations.TryGetValue(edge.From, out var predecessorOp)
                            && GetEffectiveTransferBatch(predecessorOp, stageOverlap).HasValue
                            && operationThresholdTimes.TryGetValue(edge.From, out var thresholdTime))
                        {
                            // 有阈值配置且已计算阈值时间，使用阈值时间
                            effectiveTime = thresholdTime;
                        }

                        var candidateStart = effectiveTime.AddMinutes((double)edge.LagTime);
                        if (candidateStart > earliestStart)
                        {
                            earliestStart = candidateStart;
                        }
                    }
                }
            }

            // OperationPlanningMode 分型（0号位 20260922）：非资源工序（UNCONSTRAINED/WAIT_ONLY）
            // 不占资源、保留工艺时间走链 —— 产 ResourceId=NULL 的 Task，不判失败（FINITE_RESOURCE 才 fail-closed）。
            if (IsNonResourceMode(operation.OperationPlanningMode))
            {
                var nonResourceTask = CreateNonResourceTask(demand, operation, earliestStart, backward: false, batchDraftKey);
                tasks.Add(nonResourceTask);
                operationEndTimes[operationKey] = nonResourceTask.PlannedEndTime;
                continue;
            }

            // 查找合格资源（P1-11：软偏好资源优先，Preferred 最前、Fallback 次之）
            var eligibleResources = OrderResourcesByPreference(
                demand,
                GetEligibleResources(demand.MaterialId, operation, constraints),
                constraints,
                operation.OperationCode,
                operation.StageCode);   // P1-02：软偏好仅作用于当前承接工序（身份 = StageCode + OperationCode）
            if (eligibleResources.Count == 0)
            {
                return new List<FinalTaskDraft>(); // 无合格资源（FINITE_RESOURCE fail-closed）
            }

            // 尝试在合格资源上找时间槽
            FinalTaskDraft? scheduledTask = null;
            foreach (var resourceId in eligibleResources)
            {
                // P0-04修复：Duration = StandardDuration × PlannedProcessQty ÷ CapacityFactor
                // 第4轮Setup修复：加上SetupTime占用资源时间轴
                // 第5轮修复：CapacityFactor缺失或非法时不能继续
                var capacityFactor = GetCapacityFactor(demand.MaterialId, operation, resourceId, constraints);
                if (capacityFactor == null || capacityFactor <= 0)
                {
                    return new List<FinalTaskDraft>(); // CapacityFactor缺失/非法，无法计算Duration
                }
                var adjustedDuration = operation.StandardDuration * demand.PlannedProcessQty / capacityFactor.Value;
                var processDuration = TimeSpan.FromMinutes((double)adjustedDuration);

                // P1-02 item1 接线（阶段二）：Setup = 规则查找「当前工序+当前设备+前产品→当前产品」（v1.2 §2/§5），
                // RoutingOperation.SetupTime 不再读取（§1.2/§20.3 废止运行真相）；§12：Setup 从候选槽评价阶段参与，
                // 动态 Setup 与槽位置的相互依赖由 FindSlotWithDynamicSetup 有界迭代解决。
                var found = SetupOptimizer.FindSlotWithDynamicSetup(
                    earliestStart, processDuration, resourceId, operation.OperationCode, demand.MaterialId,
                    constraints.ProductTimeline, constraints.SetupExactRules, constraints.SetupDefaultRules,
                    (f, total) => FindForwardSlot(f, total, resourceId, constraints, resourceOccupancy, planningEnd));

                if (found.HasValue)
                {
                    var (occSlot, setupMinutes, setupResolution) = found.Value;
                    // B.2：Setup 追踪三元组写出（0号位 20260917 回复 §5.1 / §14.3）。
                    // 载体 = 2号位 r13494 落地的 DomainSolveResult.SolveTraceNote；此处只收集，Phase5 统一导出。
                    // 判据：仅写「需说明」的解析结果（ExplanationType 非空 = INITIAL_SETUP_STATE / SETUP_RULE_MISSING_ZERO_FALLBACK /
                    //       DEFAULT_SETUP_FALLBACK）；正常命中（EXACT/DEFAULT/SameProduct）ExplanationType 为 null → 不产 trace，
                    //       避免 10 万 Task 级 trace 体积（与实施包 §19 同源约束）。
                    // ⚠ ReasonCode 位承载的是 ExplanationType（trace 层），**不是**冻结字典 ScheduleExplanationFact.ReasonCode
                    //   （0号位 Q4：决策说明不进 ReasonCode 体系）—— 命名撞域已提请 2号位 改名，未落前在此显式标注。
                    // ⚠ Level 值域归一：SetupOptimizer 填 "INFO"/"WARNING"（全大写），SolveTraceNote.Level 契约为 "Info"/"Warning"/"Error"。
                    // ⚠⚠ 落码前置偏差（2026-09-28 复核，1号位 自记）：1号位 2026-09-24《白天候选配合事项 回执 v1.1》
                    //   §2.3/§2.4 已向 2号位 提请 3 项（`ReasonCode` 是否改名 / `Level` 取值域 / `Key` 语义）并声明
                    //   「三小项一并明确后…同批落两处产出点」。**2号位 尚未答复**，本处属先行落码。
                    //   影响面可控：三项均为字段级 —— 若答复为 §2.3(i) 改名，仅需改 `ReasonCode =` 一行；
                    //   若答复 Level 与 Severity 同域，仅需改 `NormalizeTraceLevel` 映射表；若 Key 语义改为「产出点标识」，仅需改 `Key =`。
                    //   方向（第二层「决策说明」，不进冻结 ReasonCode 字典）已由 0号位 Q4 + 该回执 §2.2 确认，无结构性返工。
                    if (!string.IsNullOrEmpty(setupResolution.ExplanationType))
                    {
                        constraints.TraceNotes.Add(new SolveTraceNote
                        {
                            Key = demand.LogicalDemandKey,
                            ReasonCode = setupResolution.ExplanationType,
                            Message = setupResolution.TraceMessage,
                            Level = NormalizeTraceLevel(setupResolution.TraceLevel)
                        });
                    }
                    var setupDuration = TimeSpan.FromMinutes((double)setupMinutes);

                    // Task的PlannedStartTime是加工开始时间（Setup之后）
                    var taskStart = occSlot.Start + setupDuration;
                    // SetupSource 填充：解析命中类型 → 大写 5 值（5号位 值契约统一 20260921）
                    var taskSetupSource = SetupOptimizer.SetupOutcomeToSource(setupResolution.Outcome);
                    scheduledTask = CreateTask(demand, operation, resourceId, taskStart, occSlot.End, setupMinutes, constraints, taskSetupSource, batchDraftKey);

                    // 资源占用从Setup开始
                    // 2026-10-08 COW + 有序插入（正排写点；天然追加到末尾 ⇒ 退化为 O(1)）
                    AddOccupancyWindow(resourceOccupancy, occupancyPristine, resourceId, occSlot, occupancyInsertLog);
                    constraints.ProductTimeline.Place(resourceId, occSlot.End, demand.MaterialId);

                    // 第4轮C2修复：记录该Operation的实际完成时间，供后续工序使用
                    operationEndTimes[operationKey] = occSlot.End;

                    // 第4轮P7修复：如果配置了TransferBatchSize，计算阈值启动时间（P1-02：AllowOverlap 门控 + 冻结 TransferBatchQty 回落）
                    var transferBatch = GetEffectiveTransferBatch(operation, stageOverlap);
                    if (stageOverlap.AllowOverlap && transferBatch.HasValue && transferBatch.Value > 0 && transferBatch.Value < demand.PlannedProcessQty)
                    {
                        // 阈值时间 = 占用开始 + Setup时间 + (转运批量 / PlannedProcessQty) × 加工时长
                        var thresholdRatio = transferBatch.Value / demand.PlannedProcessQty;
                        var thresholdDuration = setupDuration + TimeSpan.FromMinutes((double)(processDuration.TotalMinutes * (double)thresholdRatio));
                        operationThresholdTimes[operationKey] = occSlot.Start + thresholdDuration;
                    }

                    break;
                }
            }

            if (scheduledTask == null)
            {
                return new List<FinalTaskDraft>(); // 排程失败
            }

            tasks.Add(scheduledTask);
        }

        return tasks;
    }

    /// <summary>
    /// 获取物料最早可用时间
    /// 文档：§四 4.6、§十二 Stage overlap
    /// P0-05修复：支持多段Quantity-Time，根据所需数量确定可用时间，并验证总量是否足够
    /// 块4（任务喂任务，方案A）：合并子件完成时间（dynamicMaterialFloor），
    /// 父件的物料最早可用时间 = max(静态到货时间, 子件真实完成时间)。
    /// </summary>
    private DateTime GetMaterialEarliestTime(
        long allocationSequence,
        decimal requiredQuantity,
        ConstraintContext constraints,
        DateTime planningStart,
        DateTime dynamicMaterialFloor,
        out bool isSufficient)
    {
        isSufficient = true;

        DateTime staticEarliest;

        if (constraints.MaterialAvailability.TryGetValue(allocationSequence, out var segments) && segments.Count > 0)
        {
            // P0-05修复：累计可用数量，找到满足需求数量的最早时间
            decimal accumulated = 0m;
            foreach (var segment in segments.OrderBy(s => s.AvailableTime))
            {
                accumulated += segment.Quantity;
                if (accumulated >= requiredQuantity)
                {
                    // 累计数量满足需求，返回该段时间
                    staticEarliest = segment.AvailableTime;
                    return staticEarliest > dynamicMaterialFloor ? staticEarliest : dynamicMaterialFloor;
                }
            }

            // P0-05修复：所有段累计仍不足需求量，标记不足并返回最后一段时间
            isSufficient = false;
            staticEarliest = segments.Max(s => s.AvailableTime);
            return staticEarliest > dynamicMaterialFloor ? staticEarliest : dynamicMaterialFloor;
        }

        // 无静态物料约束：直接用动态下界（无子件时 dynamicMaterialFloor 为 MinValue，等价于 planningStart）
        staticEarliest = planningStart;
        return staticEarliest > dynamicMaterialFloor ? staticEarliest : dynamicMaterialFloor;
    }

    /// <summary>
    /// 获取工序的合格资源列表（按优先级排序）
    /// </summary>
    private List<int> GetEligibleResources(
        int materialId,
        OperationNode operation,
        ConstraintContext constraints)
    {
        // 第4轮C1修复：索引加入MaterialId
        // 0号位 2026-09-29 裁决 §5.3 落实：键升为强类型 EligibilityLookupKey，**补上 ProductionDepartmentId**
        // （旧键写死 "DEFAULT" 且无部门 ⇒ 两个部门的同名工序资格被合并 ⇒ 跨部门串资源）。
        // ⚠ 契约 OperationResourceEligibility **无 StageCode 字段**，无法再细到 Stage（残留见键类型注释）。
        var key = new EligibilityLookupKey(
            materialId, operation.ProductionDepartmentId, operation.RouteCode, operation.PathId, operation.OperationCode);

        if (constraints.OperationResourceEligibility.TryGetValue(key, out var resources))
        {
            return resources;
        }
        return new List<int>();
    }

    /// <summary>
    /// P1-11：软偏好资源优先——在合法资源集内把偏好资源排最前、FallbackResourceId 次之，
    /// 其余保持原顺序。软偏好不改变合法性（非硬锁），偏好资源不可用时自然回落。
    ///
    /// v1.6 §1号位新增/替换实施要求（`:27`）：新增契约字段 `PreferredResourceCode?`（业务编码）为**软偏好**。
    /// 消费口径（0号位 2026-10-07 裁决 §六 授权落地）：
    ///   · Code 非空且经 <c>ResourceIdsByCode</c> 反查到 ResourceId ⇒ 以该 ResourceId 为软偏好首选；
    ///   · Code 为空 / 反查不到 / 反查到的 ResourceId 不在合法资源集内 ⇒ **回落** `PreferredResourceId`；
    ///   · **不得 Hard Lock**（Q-1 明文）：偏好资源不可用时不失败，按原顺序继续找槽。
    ///
    /// **P1-02（0号位 2026-10-07 (5).md）作用域收窄**：`PreferredResourceCode` 的语义是
    ///   「上一 ACTIVE Task 的资源」= **当前承接工序的连续性偏好**，不是「整条 Route 的设备偏好」。
    ///   原实现把它对每个 Operation 都调用，等于把连续性偏好扩散到后续全部工序。
    ///   现收窄为：**仅作用于 Continuation Slice 的 StartOperation（当前承接工序）**；
    ///   承接工序身份与当前工序不一致时**不施加任何偏好**。
    ///
    /// **P1-02 身份升维（0号位 2026-10-07《未命名的Markdown文件 (7).md》反证 ⑨）**：
    ///   承接工序身份 = **`(StageCode, OperationCode)`**，不是 `OperationCode` 单键。
    ///   依据 0号位 2026-09-29 裁决 §5.3（节点身份 = `(StageCode, OperationCode)`；2号位 实测 117 个物料
    ///   **同码跨 Stage**）：同一 Route 内 `OP10` 可同时出现在 STAGE1 与 STAGE2，仅按 OperationCode 比较
    ///   会把**非承接工序**的同名工序也判为承接工序 ⇒ 软偏好扩散到它（反证 ⑨ 所指缺陷）。
    ///   `demand.StartStageCode` 为空（2号位 未回填）时**不按 Stage 设闸**（只比 OperationCode），零回归。
    ///   （非连续份额 / 未提供 currentOperationCode ⇒ 维持原行为，零回归。）
    ///
    /// **P1-01**：本函数为 Phase2/Phase4 **统一纯函数**，Phase4 不再自带只认 Id 的旧版
    ///   （旧版完全不看 `PreferredResourceCode`，Phase4 局部修复会丢弃 Code 偏好）。
    /// </summary>
    internal static List<int> OrderResourcesByPreference(
        LogicalProductionDemand demand,
        List<int> eligibleResources,
        ConstraintContext constraints,
        string? currentOperationCode = null,
        string? currentStageCode = null)
    {
        // P1-02：连续份额的软偏好只对「当前承接工序」生效，不得扩散到后续整条 Route。
        //   身份 = (StageCode, OperationCode)；StartStageCode 为空时不设 Stage 闸（零回归）。
        if (demand.IsContinuation
            && currentOperationCode is not null
            && (!string.Equals(currentOperationCode, demand.StartOperationCode, StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(demand.StartStageCode)
                    && !string.Equals(currentStageCode, demand.StartStageCode, StringComparison.Ordinal))))
        {
            return eligibleResources;
        }

        // Code 优先，反查不到则回落 Id（Code 为空串不算合法业务编码，与 RouteCode 同口径）
        int? preferred = null;
        if (!string.IsNullOrEmpty(demand.PreferredResourceCode) &&
            constraints.ResourceIdsByCode.TryGetValue(demand.PreferredResourceCode!, out var byCode))
        {
            preferred = byCode;
        }
        preferred ??= demand.PreferredResourceId;

        if (preferred == null && demand.FallbackResourceId == null)
        {
            return eligibleResources;
        }

        var ordered = new List<int>(eligibleResources.Count);

        if (preferred is int preferredId && eligibleResources.Contains(preferredId))
        {
            ordered.Add(preferredId);
        }

        if (demand.FallbackResourceId is int fallback
            && fallback != preferred
            && eligibleResources.Contains(fallback))
        {
            ordered.Add(fallback);
        }

        foreach (var resourceId in eligibleResources)
        {
            if (!ordered.Contains(resourceId))
            {
                ordered.Add(resourceId);
            }
        }

        return ordered;
    }

    /// <summary>
    /// 倒排寻找时间槽
    /// </summary>
    private TimeWindow? FindBackwardSlot(
        DateTime candidateStart,
        DateTime candidateEnd,
        int resourceId,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningStart)
    {
        // 性能计数（§九「SlotSearch 次数」）：倒排找槽。
        SolverDiagnostics.CountSlotSearch();
        long swSlot = SolverDiagnostics.HotspotStart();

        var candidate = new TimeWindow(candidateStart, candidateEnd);

        // 检查日历约束
        if (!IsWithinCalendar(candidate, resourceId, constraints))
        {
            SolverDiagnostics.HotspotEnd(swSlot, SolverDiagnostics.Hotspot.SlotSearch);
            return null;
        }

        // 检查资源占用冲突
        if (HasConflict(candidate, resourceId, resourceOccupancy))
        {
            SolverDiagnostics.HotspotEnd(swSlot, SolverDiagnostics.Hotspot.SlotSearch);
            return null;
        }

        SolverDiagnostics.HotspotEnd(swSlot, SolverDiagnostics.Hotspot.SlotSearch);
        return candidate;
    }

    /// <summary>
    /// 正排寻找时间槽
    /// </summary>
    private TimeWindow? FindForwardSlot(
        DateTime earliestStart,
        TimeSpan duration,
        int resourceId,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        DateTime planningEnd)
    {
        // 获取资源日历
        if (!constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar) || calendar.Count == 0)
        {
            return null;
        }

        // 遍历日历窗口
        // 0号位裁决（2026-09-12）：无末期限制 —— PlanningEnd 不是硬上界，
        // 正排不再被 planningEnd 截断，只受资源日历窗口（calWindow.End）约束。
        // planningEnd 参数保留仅为签名一致，不再作为末期硬边界。
        foreach (var calWindow in calendar.OrderBy(w => w.Start))
        {
            if (calWindow.End <= earliestStart) continue;

            var windowStart = calWindow.Start > earliestStart ? calWindow.Start : earliestStart;
            var windowEnd = calWindow.End;

            if (windowEnd - windowStart < duration) continue;

            // 在窗口内寻找空闲槽
            var slot = FindFirstAvailableSlot(windowStart, duration, resourceId, resourceOccupancy);
            if (slot.HasValue && slot.Value.End <= windowEnd)
            {
                return slot;
            }
        }

        return null;
    }

    /// <summary>
    /// 在窗口内找第一个空闲槽（按资源占用窗线性扫描）。
    ///
    /// 性能口径（原「TODO P15：性能优化（§十九）」已闭合 —— 2026-09-24 改写，勿再按原技术清单重写本方法）：
    ///   端到端目标「10万 Task / 15 分钟」**经实测达成**，故原 TODO 所列五条（Interval Timeline /
    ///   资源时间轴内存索引 / 避免 O(N²) 全 Task 扫描 / Setup 只局部更新 / Candidate 只传播实际变化）
    ///   不再是待办项。
    ///   实测（2026-09-20 性能标定；FORWARD/FULL；200 资源 × 500 Task/资源 = 10万 Task；物料池 500；
    ///   EXACT 规则 5% + DEFAULT 30%；单窗口 90 天日历；seeded 可重放）：
    ///     P2 默认预算(500/50) = 173s；P3 上限(5000/150) = 220s；红线 900s → 余量 ≥ 75%；
    ///     全部 Success 且 0 未排。
    ///   报告：《APS_V1_Setup换型_性能标定报告与预算参数修订提值_1号位致3号位_v1.0_20260920.md》。
    ///   已落地的性能加固：Phase5 CompactGaps / OptimizeSegment 的段内 O(n²) 全表扫描改为 TaskIndex
    ///   三重索引（BySource / ByResource / ContinuationByPi + FreeByPi，FinalDraftId→PI 反查 O(1)）。
    ///   复核触发条件：真实日历接入 / 规模 &gt; 15万 Task / 规则密度 &gt; 30% / 性能红线调整。
    /// </summary>
    private TimeWindow? FindFirstAvailableSlot(
        DateTime windowStart,
        TimeSpan duration,
        int resourceId,
        Dictionary<int, List<TimeWindow>> resourceOccupancy)
    {
        // ── 性能（2026-10-08，2号位 全量测试暴露）：**每次调用**都 `OrderBy(...).ToList()` 是 O(W log W)
        //   且**每次都分配一个 W 元素的新数组**（W = 该资源累计占用窗数）。全量场景下 W ≈ 需求条数
        //   ⇒ 本方法被调用 O(N) 次 ⇒ 合计 O(N² log W) + O(N²) 分配，是「单条成本随 N 上升」的主要来源之一。
        //   占用窗在**正排**下天然按 Start 递增追加（每条新任务落在既有占用之后）⇒ 绝大多数调用时表**已有序**，
        //   此时 `OrderBy` 的结果与**原表逐元素相同**（OrderBy 是稳定排序，非递减序列排序后不变）⇒ 可直接用原表，
        //   零分配、零排序。**只有真的乱序时**才回落到原 `OrderBy(...).ToList()`（行为与改前逐字一致）。
        //   本优化**不假设**任何写入方维持有序 —— 每次调用都自检，故对任意输入都保持原语义（零回归）。
        // 性能计数（§九「SlotSearch 次数」）：正排找槽（本方法是 §7.3 点名的重复排序点）。
        SolverDiagnostics.CountSlotSearch();
        long swSlot = SolverDiagnostics.HotspotStart();

        // ── 2026-10-08 SlotSearch 索引化：**二分跳段**（取代「每次 O(W) 自检有序 + 从头找缝」）──
        //   不变式（见 AddOccupancyWindow）：该列表按 `Start` 升序，且窗互不重叠（⇒ `End` 随 `Start` 非降）。
        //   跳段依据：`End <= windowStart` 的窗对结果**无贡献** ——
        //     · 不可能触发早退：`Start <= End <= windowStart < windowStart + duration`（`duration` 为正）；
        //     · 不推进 `cursor`（`cursor = max(cursor, End)` 仍为 `windowStart`）。
        //   故可整体跳过「`Start <= windowStart`」这段前缀；其中**跨窗**（`End > windowStart`）至多一个，
        //   用一次回退纳入（窗互不重叠时恰好退 0~1 步）。
        var occupied = resourceOccupancy[resourceId];
        int lo0 = 0, hi0 = occupied.Count;
        while (lo0 < hi0)
        {
            var mid = (lo0 + hi0) / 2;
            if (occupied[mid].Start <= windowStart) lo0 = mid + 1; else hi0 = mid;
        }
        int startIdx = lo0;
        while (startIdx > 0 && occupied[startIdx - 1].End > windowStart) startIdx--;

        var cursor = windowStart;

        for (int i = startIdx; i < occupied.Count; i++)
        {
            var occ = occupied[i];
            if (occ.Start >= cursor + duration)
            {
                // 找到间隙
                SolverDiagnostics.HotspotEnd(swSlot, SolverDiagnostics.Hotspot.SlotSearch);
                return new TimeWindow(cursor, cursor + duration);
            }
            cursor = occ.End > cursor ? occ.End : cursor;
        }

        // 最后一个占用槽之后的空间
        SolverDiagnostics.HotspotEnd(swSlot, SolverDiagnostics.Hotspot.SlotSearch);
        return new TimeWindow(cursor, cursor + duration);
    }

    /// <summary>
    /// 检查候选窗口是否在日历内
    /// </summary>
    private bool IsWithinCalendar(
        TimeWindow candidate,
        int resourceId,
        ConstraintContext constraints)
    {
        if (!constraints.ResourceCalendars.TryGetValue(resourceId, out var calendar))
        {
            return false;
        }

        return calendar.Any(c => c.Start <= candidate.Start && c.End >= candidate.End);
    }

    /// <summary>
    /// 检查是否与已占用时间冲突
    /// </summary>
    private bool HasConflict(
        TimeWindow candidate,
        int resourceId,
        Dictionary<int, List<TimeWindow>> resourceOccupancy)
    {
        // ── 2026-10-08 SlotSearch 索引化：二分定区间 + 区内检查（取代全表 `Any`）──
        //   不变式同上（按 `Start` 升序、互不重叠）。与 `candidate` 重叠的窗必须 `Start < candidate.End`
        //   （否则 `Start >= End_c` ⇒ 不重叠）⇒ 只需检查该前缀；其中 `End <= candidate.Start` 者不重叠，
        //   且因 `End` 随 `Start` 非降 ⇒ 该段是**连续前缀**，二分 + 回退即可界定，通常只查 0~1 个窗。
        var occupied = resourceOccupancy[resourceId];
        int hi = occupied.Count, lo = 0;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (occupied[mid].Start < candidate.End) lo = mid + 1; else hi = mid;
        }
        int upper = lo;                       // 前 upper 个窗满足 Start < candidate.End
        int lower = upper;
        while (lower > 0 && occupied[lower - 1].End > candidate.Start) lower--;
        for (int i = lower; i < upper; i++)
        {
            if (Overlaps(candidate, occupied[i])) return true;
        }
        return false;
    }

    /// <summary>
    /// 检查两个时间窗是否重叠
    /// </summary>
    private bool Overlaps(TimeWindow a, TimeWindow b)
    {
        return a.Start < b.End && a.End > b.Start;
    }

    /// <summary>
    /// 创建 FinalTaskDraft
    /// 文档：§四 4.2、§五 5.1、§十六 Firm/Frozen/Execution 继承
    /// Task.Quantity = NetOutputQty（净合格产出）
    /// Task.PlannedProcessQty = 计划加工量（用于资源能力占用计算）
    /// TaskType 继承 Demand 的 Firm/Frozen/Execution 标记
    /// </summary>
    /// <summary>
    /// OperationPlanningMode 非资源工序判定（UNCONSTRAINED / WAIT_ONLY）：
    /// 不占资源、保留工艺时间走链（0号位 20260922 无设备小工序裁决）。
    /// </summary>
    /// <summary>
    /// B.2：TraceLevel 值域归一。
    /// 上游 <see cref="SetupOptimizer.SetupResolution.TraceLevel"/> 按 0号位 §5.1/§5.3 填「全大写」字面量（"INFO"/"WARNING"），
    /// 而 2号位 r13494 的 <see cref="SolveTraceNote.Level"/> 契约值域为「首字母大写」（Info/Warning/Error）。
    /// 不归一即会写出两套值 → 4号位 数据质量查询/2号位 落库按值过滤时漏数。此处为唯一转换点。
    /// 未知值一律回落契约默认 "Info"（Level 为不可空 string，禁止写 null）。
    /// </summary>
    private static string NormalizeTraceLevel(string? level)
        => level switch
        {
            null => "Info",
            "INFO" => "Info",
            "WARNING" => "Warning",
            "ERROR" => "Error",
            _ => level   // 已是契约值域（Info/Warning/Error）或其他自定义值 → 原样透传，不吞信息
        };

    private static bool IsNonResourceMode(string? mode)
        => string.Equals(mode, "UNCONSTRAINED", StringComparison.Ordinal)
        || string.Equals(mode, "WAIT_ONLY", StringComparison.Ordinal);

    /// <summary>
    /// 非资源工序 Task（0号位 20260922 裁决：非资源工序也产出 Task，ResourceId=NULL，不占资源时间轴）。
    /// 时长 = StandardDuration × PlannedProcessQty（1号位 自定：与 FINITE 同源但不除 CapacityFactor —— 非资源无产能系数）。
    /// backward=true 以 anchorTime 为结束倒推；false 以 anchorTime 为开始正推。
    /// </summary>
    private FinalTaskDraft CreateNonResourceTask(
        LogicalProductionDemand demand,
        OperationNode operation,
        DateTime anchorTime,
        bool backward,
        string? batchDraftKey = null)   // P0-01/P0-02：本批归批键（null ⇒ 回落 ExecutionBatchKey(demandKey, 1)）
    {
        var durationMinutes = operation.StandardDuration * demand.PlannedProcessQty;
        var duration = TimeSpan.FromMinutes((double)durationMinutes);
        var start = backward ? anchorTime - duration : anchorTime;
        var end = backward ? anchorTime : anchorTime + duration;

        // AUD-1-002：执行批身份（跨 Stage 工序链）与 **Stage 执行批身份**（Stage 内 MES 批）分开 —— 见 StageExecutionBatchKey 注释。
        var executionBatchDraftKey = batchDraftKey ?? ExecutionBatchKey(demand.LogicalDemandKey);
        var producesStageBatchKey = ProducesStageExecutionBatchKey(demand);

        return new FinalTaskDraft
        {
            FinalDraftId = Guid.NewGuid().ToString(),
            SourceDraftId = demand.LogicalDemandKey,
            MaterialId = demand.MaterialId,
            FactoryId = demand.FactoryId,
            StageCode = operation.StageCode ?? string.Empty,
            OperationCode = operation.OperationCode,
            TaskType = "PRODUCTION",
            ResourceId = null,             // 非资源工序：不占资源（0号位 20260922）
            ResourceCode = string.Empty,
            RouteCode = operation.RouteCode,
            PathId = operation.PathId,
            Quantity = demand.NetOutputQty,
            PlannedProcessQty = demand.PlannedProcessQty,
            UOM = demand.UOM ?? string.Empty,
            PlannedStartTime = start,
            PlannedEndTime = end,
            SetupTime = 0m,                // 非资源无换型
            SetupSource = null,            // 非资源无 Setup 来源
            Priority = demand.DemandSequence,
            IsVirtual = false,
            // 非资源工序 Task 同样属该执行批 ⇒ 与资源 Task 共 Key（v1.6：FinalTask 一律回传）。
            // P0-02：键域为 (需求键, 批序号)；批键由本批入参给定，缺省即 1 号批。
            ContinuationKey = demand.ContinuationKey,
            ExecutionBatchDraftKey = executionBatchDraftKey,
            // AUD-1-002：Stage 内 MES 执行批身份（C 桶给值、A/B 留空）；**不是**上面那个跨 Stage 链键。
            StageExecutionBatchDraftKey = producesStageBatchKey
                ? StageExecutionBatchKey(executionBatchDraftKey, operation.StageCode)
                : null,
            StageExecutionBatchQty = producesStageBatchKey ? demand.NetOutputQty : null
        };
    }

    /// <summary>
    /// 生成执行批键（P0-02，0号位 2026-10-07《未命名的Markdown文件 (5).md》）。
    ///
    /// 【为什么改键域】旧键 <c>EB|{demand}|{route}|{path}</c> 由 **Route/Path 派生** ⇒
    ///   ① 批身份被路由选择**反向决定**（0号位 判词：「把 LogicalDemand 当成了 Execution Batch」）；
    ///   ② 同一 Demand 拆出的 N 个批**会撞成同一个键**（同 Route / 同 Path）⇒
    ///      「同 Batch 共键、不同 Batch 必不同键」**结构性失效**。
    /// 新键只由 **(逻辑需求键, 批序号)** 决定 ⇒ 批身份是**主键**，Route/Path 是批的**属性**
    ///   （每批各自选一条完整 Path，见 P0-03），构造上保证「不同 Batch 即使同 Route/Path 也必不同键」。
    ///
    /// ⚠ 批序号由 `FormExecutionBatches` 产出（1 起、连续、确定性），**不由候选 Path 条数反推**
    ///   （0号位 裁决 §四）。
    /// </summary>
    public static string ExecutionBatchKey(string logicalDemandKey, int batchOrdinal = 1)
        => $"EB|{logicalDemandKey}|{batchOrdinal:D3}";

    /// <summary>
    /// **Stage 执行批归组键**（T-002 / T-005；AUD-1-002，0号位 2026-10-10《APS_V1_2_20261010.md》§3）。
    ///
    /// 【为什么必须与 <see cref="ExecutionBatchKey"/> 分开】
    ///   · `ExecutionBatchDraftKey` = **一条完整 Routing Path 的跨 Stage 工序链**身份
    ///     （一个执行批只允许一条完整 Path ⇒ 链上 N 条 Operation FinalTask 共享同一键）—— 1号位 的排程身份；
    ///   · `StageExecutionBatchDraftKey` = **Stage 内** MES 执行批身份（T-002：「TaskNo 是 **Stage 内** MES
    ///     执行批的 APS 跨版本业务身份」），2号位 据此归组 `TaskNo` / MES 工单；
    ///   · **T-003：一个 TaskNo 绑定一个 `MESWorkOrderNo`，MES 工单不跨 Stage。**
    ///
    /// 旧实现**从未给该字段赋值**（恒 null）⇒ 跨 Stage 的三道工序共用一个 `ExecutionBatchDraftKey`，
    ///   报告据此宣称「三工序共批交 2号位生成**一个** MES 工单」—— **与 T-003 直接冲突**。
    ///
    /// 【取值】`SEB|{ExecutionBatchDraftKey}|{StageCode}`：随执行批身份（不同批必不同键）且按 Stage 分域。
    ///   ⇒ 同 Stage 的 N 条 Operation Task 共享一个 Stage 键（**正例**）；跨 Stage 必得**不同**键（**反例**）。
    /// </summary>
    public static string StageExecutionBatchKey(string executionBatchDraftKey, string? stageCode)
        => $"SEB|{executionBatchDraftKey}|{stageCode ?? string.Empty}";

    /// <summary>
    /// AUD-1-002：本需求是否**产出 Stage 执行批键**。
    ///   · C 桶（非 A/B）⇒ **是** —— B-003：C 桶由 1号位 在 **Stage Execution Batch 层级**作 Batch Decision；
    ///   · A/B（`IsContinuation` / `NoSplitMerge`）⇒ **否** —— 既存 MES 执行批不参与普通拆合批，
    ///     与 3号位 落库口径逐字一致：「C 桶由 1号位 给 Stage 执行批键（**A/B 桶留空**）」
    ///     （`PeggingOrchestrator.cs:666`）。
    /// 桶判据与 `TryMergeOrSchedule` / `DecideExecutionBatchPlan` 的 A/B 闸**同源**（只认 `IsContinuation` / `NoSplitMerge`）。
    /// </summary>
    private static bool ProducesStageExecutionBatchKey(LogicalProductionDemand demand)
        => !demand.IsContinuation && !demand.NoSplitMerge;

    // ─────────────────────────────────────────────────────────────────────────
    // P0-03（0号位 2026-10-07《未命名的Markdown文件 (7).md》§六）：原
    //   `ResolveExecutionBatchKeyForRebuild(LogicalDemandKey, constraints)` **已删除**。
    //
    // 删除理由：它在 `keys.Count > 1` 时取**首批键**，本质是「**通过 LogicalDemandKey 猜批身份**」，
    //   正是 0号位 明文禁止的形态（「不允许再通过LogicalDemandKey猜批身份」）。
    //   它的存在会掩盖多批场景下的错归（Batch-002 的修复结果被写回 Batch-001 的键）。
    //
    // 替代：Phase4 局部修复改为**逐批**展开
    //   （`PhaseFourLocalRepair.ExpandRepairUnits`：本批键 + 本批数量 + 本批已选路径），
    //   由调用方把**本批真实批键**逐批传入 `CreateTask` / `CreateSplitTask`，
    //   求解层不再存在任何「从需求键反推批键」的路径。
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 一个执行批（Execution Batch）的排程草稿（P0-01 / P0-02，0号位 2026-10-07 (5).md）。
    ///
    /// 冻结单位：**执行批是 Routing 择优的业务单位**（「每个 Execution Batch 只允许一条完整 Path」）。
    /// 因此 `BatchDraftKey` 只依赖 (逻辑需求键, 批序号)，与 Route/Path **无关**。
    /// </summary>
    public sealed record ExecutionBatchDraft(
        string BatchDraftKey,            // EB|{逻辑需求键}|{批序号:D3}
        string SourceLogicalDemandKey,
        int Ordinal,                     // 1 起、连续
        decimal NetOutputQty,            // 本批净产出（各批求和 = 需求 NetOutputQty，逐分不丢）
        decimal PlannedProcessQty);      // 本批计划加工量（各批求和 = 需求 PlannedProcessQty）

    /// <summary>
    /// 解析需求对应的 ⑧块 Batch Policy（**P0-01 整改**，0号位 2026-10-07《未命名的Markdown文件 (7).md》§四）。
    ///
    /// **键域 = Material + ProductionDepartment**（冻结 B-001；`TaskSplitRuleConfig` / `BatchPolicyRuleSnapshot` 同口径）。
    /// 解析顺序（**AUD-1-004 整改后只剩「精确命中」一条**）：
    ///   ① `(demand.MaterialId, deptId)` **精确命中** —— deptId 由 `(MaterialId, StartStageCode)` 从
    ///      `DomainSolveRequest.MaterialStageDepartmentContexts` 反查，**与既有「部门锁定」同口径，不另造部门、不跨部门选优**；
    ///   ② 未精确命中 ⇒ `null`（**缺策略**）。⚠ P0-01 后 `null` **不再**意味着「不拆、恒 1 批」：
    ///      C 桶需求一律判 `BATCH_POLICY_MISSING` 并 **Fail Closed**（见 `DecideExecutionBatchPlan`）。
    ///
    /// **禁止**回落到「全局默认批量策略」—— 0号位 (7).md §十一 第 3 条明文：1号位不得自行创造全局默认策略。
    /// **禁止**把 `ProductionDepartmentId == null` 的历史记录当 Material 级默认 —— 见 AUD-1-004 整改注释。
    /// </summary>
    internal static BatchPolicyRuleSnapshot? ResolveExecutionBatchPolicy(
        LogicalProductionDemand demand,
        DomainSolveRequest request,
        ConstraintContext constraints)
    {
        if (constraints.ExecutionBatchPolicies.Count == 0)
        {
            return null;
        }

        int? deptId = null;
        if (!string.IsNullOrEmpty(demand.StartStageCode))
        {
            foreach (var ctx in request.MaterialStageDepartmentContexts)
            {
                if (ctx.MaterialId == demand.MaterialId
                    && string.Equals(ctx.StageCode, demand.StartStageCode, StringComparison.Ordinal))
                {
                    deptId = ctx.ProductionDepartmentId;
                    break;
                }
            }
        }

        if (deptId is int d)
        {
            foreach (var p in constraints.ExecutionBatchPolicies)
            {
                if (p.MaterialId == demand.MaterialId && p.ProductionDepartmentId == d)
                {
                    return p;
                }
            }
        }

        // ── AUD-1-004（0号位 2026-10-10《APS_V1_2_20261010.md》§3，P0/CONFIRMED）修复 ──
        //   原「② `(MaterialId, ProductionDepartmentId == null)` 的 Material 级默认」兜底**已删除**。
        //
        //   理由（B-001 / B-006 / v5.1.10 收口④）：正式生效的 Batch Policy 只按
        //     `MaterialId + **明确** ProductionDepartmentId` **精确匹配**；`ProductionDepartmentId == null`
        //     的行是**历史兼容**记录，**不得**在 Solver 里重新变成「对所有部门生效」的默认策略 ——
        //     那等于 1号位 自造了一条业务上不存在的生效规则（§十一 第 3 条同向：不得自造默认策略）。
        //     投影端（2/3号位）已按 `HasValue` 排除 NULL 部门行；此处再兜底会把它们**复活**。
        //
        //   ⇒ 未精确命中即 `null`（缺策略）⇒ C 桶走 `BATCH_POLICY_MISSING` Fail Closed
        //     （见 `DecideExecutionBatchPlan`），**不静默套用历史默认**；历史 NULL 记录仍由 3号位 治理链追溯。
        return null;
    }

    /// <summary>
    /// V1 合法批域判定（**P0-02 整改**，0号位 (7).md §五）。
    ///
    /// **合法批方案定义**：存在 `n ≥ 1` 与数量切分，使**每一批**数量 ∈ `[Min, Max]`，且各批之和 = `Qty`。
    ///   （`Min` 缺省 0；`Max` 缺省 ∞。）
    ///
    /// 判据（O(1)）：
    ///   · `!AllowSplit` ⇒ 唯一候选 = 单批 ⇒ 合法 ⇔ `Min ≤ Qty ≤ Max`；否则 **冲突**。
    ///     （v5.1.9 §7.3 `TaskSplitRuleConfig.AllowSplit` 逐字：「0且Qty&gt;Max时返回 `BATCH_POLICY_CONFLICT`」。）
    ///   · `AllowSplit` ⇒ 需 `∃n≥1: n·Min ≤ Qty ≤ n·Max`。
    ///       下界 `n ≥ ceil(Qty/Max)`（Max 给定时）；上界 `n ≤ floor(Qty/Min)`（Min&gt;0 时）。
    ///       存在解 ⇔ `ceil(Qty/Max) ≤ floor(Qty/Min)`；V1 取**最小合法 n**（批数最少 ⇒ Setup/在制最少，符合四层目标第④层）。
    ///
    /// **明令禁止的旧结论**（0号位 (7).md §五）：不得再写「Max 优先于 Min」或「Min 在 V1 无作用面」。
    ///   `Qty=10/Min=6/Max=6` ⇒ **无合法切分**（n=1: 10&gt;6；n=2: 12&gt;10）⇒ **冲突**，不得产出 5+5。
    ///
    /// A/B（`IsContinuation` / `NoSplitMerge`）⇒ 恒 1 批（既存执行批，不受普通 C 桶 Batch Decision 约束）。
    /// C 桶**缺有效 Batch Policy ⇒ `BATCH_POLICY_MISSING`**（P0-01，见下）。
    /// </summary>
    private static ExecutionBatchPlan DecideExecutionBatchPlan(
        LogicalProductionDemand demand,
        BatchPolicyRuleSnapshot? policy)
    {
        // A/B 已是**既存执行批**（v1.6 `:26` + B-003：A/B 不参加普通自由拆合批）⇒ 恒 1 批，
        //   不受本裁决的「缺策略 Fail Closed」约束（0号位 2026-10-08 §四末段明文）。
        if (demand.IsContinuation || demand.NoSplitMerge)
        {
            return ExecutionBatchPlan.Legal(1);
        }

        // ── P0-01（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§四 / §九）──
        //   C 桶**缺有效 Batch Policy ⇒ Fail Closed**，判 `BATCH_POLICY_MISSING`。
        //
        //   禁止的旧形态：「缺策略 ⇒ 不拆，恒 1 批」—— 那等于 1号位 在内部自造了一套
        //     「默认策略 = 不拆批」的**隐藏业务规则**，会绕开正式硬业务配置产出未经约束的生产计划。
        //
        //   正式兜底链（0号位 20260928 四级）：① Material+ProductionDepartment 精确 → ② Material 默认
        //     → ③ **正式发布的 Global Batch Default** → ④ 仍无 ⇒ `BATCH_POLICY_MISSING`。
        //   第③级由 2/3号位 在上游**投影成 Solver 可消费的有效 Policy** 后交给 1号位（§九：「不要求1号位
        //   自己维护 Global Default」）；**1号位 不得自行展开该级、更不得把末级 `null` 解释成「单批合法」**。
        if (policy is null)
        {
            return ExecutionBatchPlan.Missing(
                "BATCH_POLICY_MISSING：C 桶需求在 Solver 输入中找不到有效 Batch Policy"
                + "（(Material + ProductionDepartment) 精确命中 与 Material 级默认 均未命中）");
        }

        if (demand.NetOutputQty <= 0m)
        {
            return ExecutionBatchPlan.Legal(1);
        }

        decimal qty = demand.NetOutputQty;
        decimal min = policy.MinExecutionBatchQty ?? 0m;
        decimal? max = policy.MaxExecutionBatchQty;

        if (!policy.AllowSplit)
        {
            if (qty < min || (max is decimal mx0 && qty > mx0))
            {
                return ExecutionBatchPlan.Conflict(
                    $"BATCH_POLICY_CONFLICT：不允许拆批（AllowSplit=false）且 Qty={qty} 不在 [Min={min}, Max={(max?.ToString() ?? "∞")}] 内");
            }
            return ExecutionBatchPlan.Legal(1);
        }

        int nMin = max is decimal mx
            ? ClampToPositiveInt(Math.Ceiling(qty / mx))
            : 1;
        int nMax = min > 0m
            ? ClampToPositiveInt(Math.Floor(qty / min))
            : int.MaxValue;

        if (nMin > nMax)
        {
            return ExecutionBatchPlan.Conflict(
                $"BATCH_POLICY_CONFLICT：不存在合法切分 —— Qty={qty}, Min={min}, Max={(max?.ToString() ?? "∞")}");
        }

        return ExecutionBatchPlan.Legal(nMin);
    }

    /// <summary>把可能溢出/非正的批数折算到 `[1, int.MaxValue]`（防 `decimal→int` 溢出）。</summary>
    private static int ClampToPositiveInt(decimal value)
    {
        if (value <= 1m) return 1;
        if (value >= int.MaxValue) return int.MaxValue;
        return (int)value;
    }

    /// <summary>
    /// 批数裁决结果：区分「合法批方案」/「策略冲突（无合法切分）」/「**缺有效 Batch Policy**」。
    ///
    /// P0-01（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§四/§九）：缺策略是**独立的硬失败类**
    ///   （`BATCH_POLICY_MISSING`），既不得与 `BATCH_POLICY_CONFLICT` 混为一谈，
    ///   更不得回落成「合法单批」—— 那是 1号位 自造的隐藏业务默认。
    /// </summary>
    private readonly record struct ExecutionBatchPlan(bool IsLegal, int Count, string? ConflictReason, bool IsMissingPolicy)
    {
        public static ExecutionBatchPlan Legal(int count) => new(true, count, null, false);
        public static ExecutionBatchPlan Conflict(string reason) => new(false, 0, reason, false);
        public static ExecutionBatchPlan Missing(string reason) => new(false, 0, reason, true);
    }

    /// <summary>
    /// 执行批形成结果（P0-02）：`IsLegal=false` ⇒ 该需求**无合法批方案**，调用方须 fail-closed（不排该需求）。
    ///
    /// P0-01：`IsMissingPolicy=true` 表示失败类别是 **`BATCH_POLICY_MISSING`**（缺有效 Batch Policy），
    ///   否则为 **`BATCH_POLICY_CONFLICT`**（有策略但 Min/Max/AllowSplit 无合法切分）。
    ///   两者同属 **NonRepairable / HardFailure**（0号位 2026-10-08 §五）：Phase4 禁止进入普通 Local Repair。
    /// </summary>
    public sealed record ExecutionBatchFormation(
        bool IsLegal,
        string? ConflictReason,
        IReadOnlyList<ExecutionBatchDraft> Batches,
        bool IsMissingPolicy,
        bool IsPiContractPending = false)   // AUD-1-006：B-010 保守兜底的**前置事实**（PI 权威剩余量）未投影
    {
        public static ExecutionBatchFormation Legal(IReadOnlyList<ExecutionBatchDraft> batches)
            => new(true, null, batches, false);

        public static ExecutionBatchFormation Conflict(string reason)
            => new(false, reason, Array.Empty<ExecutionBatchDraft>(), false);

        /// <summary>P0-01：缺有效 Batch Policy（`BATCH_POLICY_MISSING`）。</summary>
        public static ExecutionBatchFormation Missing(string reason)
            => new(false, reason, Array.Empty<ExecutionBatchDraft>(), true);

        /// <summary>
        /// AUD-1-006 / B-010：C 桶缺策略、**且**符合「唯一明确真实 MTS PI 来源」的保守兜底条件，
        ///   但兜底所需的 **PI 权威剩余量事实未投影进求解输入**（`AUD-1-D04`，归 2号位 装载）
        ///   ⇒ 按 B-010「超出合法可用量须**显式记录未满足**、不得扩大/静默截断 Q」的保守方向，
        ///   **不凭空造批**，维持 Fail Closed 并把原因精确登记为契约待核（而非笼统的「无策略」）。
        /// 仍属 HardFailure（与 `Missing` 同类：Phase4 不得进入普通 Local Repair）。
        /// </summary>
        public static ExecutionBatchFormation PiQuantityContractPending(string reason)
            => new(false, reason, Array.Empty<ExecutionBatchDraft>(), true, IsPiContractPending: true);
    }

    /// <summary>
    /// V1_3 F-02/F-03：**PI 合法量门禁的消费形状**（1号位 消费点）。
    ///
    ///   B-009 合法量 = `min(配置 Max, `**`PI Original`**`, `**`PI Remaining`**`, `**`StageFreeEligibleQty − 同PI同Stage已占量`**`)`。
    ///   其中前两项可由 <see cref="PiRemainingFact"/> 直接得到；**后两项只有上游能给**，由本记录承载：
    ///     · <see cref="StageFreeEligibleQty"/> —— 该 (PI × Stage) 的 **Stage 合法自由可用量**；
    ///     · <see cref="AlreadyCommittedQty"/> —— **同 PI 同 Stage 已承诺 / 已形成批**的累计量（累计口径）。
    ///
    ///   ⚠ 裁决 F-02 明文：**不得以 `PiRemainingFact.PiRemainingQty` 冒充 `StageFreeEligibleQty`**
    ///     —— 前者是 PI 层面的剩余量，与「Stage 合法自由量」是**两个不同的量**，混用会静默放大可用量。
    ///   ⚠ 本记录是 1号位 侧的**消费形状**：**不是**新实体、**不是**新表、**不新增/修改任何 Core 契约字段**
    ///     （字段形态由 Owner 按既有 1↔2 合同收口；装载归 2号位 / 5号位，见 AUD-1-D04）。
    /// </summary>
    public sealed record PiStageQuantityContext(
        string ProductionInstructionNo,
        int MaterialId,
        string StageCode,
        decimal StageFreeEligibleQty,
        decimal AlreadyCommittedQty = 0m);

    /// <summary>
    /// V1_4 NEW-04：**本 Run 内「新增 Stage 执行批」的运行累计账**（1号位 侧消费形状，**非 Core 字段、非表**）。
    ///
    /// 【为什么必须有】
    ///   <see cref="PiStageQuantityContext.AlreadyCommittedQty"/> 是**调用方传入的只读值**，
    ///   门禁对**本轮新形成的批**一无所知 ⇒ 同一 Run 内两条 C 桶需求（30、20）对同一 (PI × Stage)
    ///   先后过闸，各自单独看都合法（Stage 自由余额 40），**累计 50 却越界**（V1_4 NEW-04 判词）。
    ///   ⇒ 需要一份**逐批登记、可隔离、可回滚**的运行累计账。
    ///
    /// 【最小消费机制（不新增表、不新增平台）】
    ///   · **登记**：<see cref="Commit"/> 在批形成**合法且过闸**后按 `(PI, 物料, Stage)` **身份**累加；
    ///   · **消费**：门禁把 <see cref="Committed"/> 并入 `AlreadyCommittedQty`
    ///     （见 `ApplyPiLegalQuantityGate` / `MergeLedgerIntoStageContext`）；
    ///   · **候选试排隔离**：<see cref="Clone"/> —— 试排（`CloneOccupancy` 那一路）用克隆账，**不污染**真实账；
    ///   · **回滚**：<see cref="Mark"/> + <see cref="RollbackTo"/> —— **逆序按日志身份撤销**，
    ///     **不按数量截尾**（与 F-01 同一纪律：登记的是**身份**，不是**计数**）。
    ///
    /// 【跨 Operation 不重复扣账】
    ///   登记粒度 = **需求级批形成**（一次 `FormExecutionBatches` 至多一次 `Commit`），
    ///   与该批展开成 N 道 Operation Task **无关** ⇒ 同一批的 N 条 Task 只扣**一次**。
    ///
    /// ⚠ 权威 Stage 自由余额仍归上游（2号位 装载 / 5号位 事实）；本账只承载**本 Run 内新增批**的增量。
    /// </summary>
    public sealed class PiStageCommitLedger
    {
        private readonly Dictionary<(string Pi, int MaterialId, string Stage), decimal> _committed = new();
        private readonly List<((string Pi, int MaterialId, string Stage) Key, decimal Qty)> _journal = new();

        /// <summary>该 (PI × 物料 × Stage) 在本 Run 内**已形成批**的累计量（无记录 ⇒ 0）。</summary>
        public decimal Committed(string pi, int materialId, string stage)
            => _committed.TryGetValue((pi, materialId, stage), out var v) ? v : 0m;

        /// <summary>已登记身份条目数（= 日志长度；供测试与诊断）。</summary>
        public int JournalLength => _journal.Count;

        /// <summary>按身份登记一批已形成的量（非正量不登记）。</summary>
        public void Commit(string pi, int materialId, string stage, decimal qty)
        {
            if (qty <= 0m) return;

            var key = (pi, materialId, stage);
            _committed[key] = Committed(pi, materialId, stage) + qty;
            _journal.Add((key, qty));
            SolverDiagnostics.CountPiStageLedgerCommit();
        }

        /// <summary>当前日志水位（供候选试排 / 需求未落定时精确回滚）。</summary>
        public int Mark() => _journal.Count;

        /// <summary>
        /// 回滚到水位：**逆序按身份**撤销该水位之后的全部登记（**不截尾、不按数量**）。
        /// 与 <see cref="Mark"/> 成对使用；水位 ≥ 日志长度时为空操作。
        /// </summary>
        public void RollbackTo(int mark)
        {
            if (mark < 0) mark = 0;
            if (mark >= _journal.Count) return;

            for (var i = _journal.Count - 1; i >= mark; i--)
            {
                var (key, qty) = _journal[i];
                var remain = _committed[key] - qty;
                if (remain == 0m) _committed.Remove(key); else _committed[key] = remain;
                _journal.RemoveAt(i);
            }
            SolverDiagnostics.CountPiStageLedgerRollback();
        }

        /// <summary>候选试排隔离：深拷贝一份独立账（试排的登记不污染真实账）。</summary>
        public PiStageCommitLedger Clone()
        {
            var copy = new PiStageCommitLedger();
            copy._journal.AddRange(_journal);
            foreach (var kv in _committed) copy._committed[kv.Key] = kv.Value;
            return copy;
        }
    }

    /// <summary>
    /// V1_3 F-02/F-03：**B-009 统一合法量门禁**（1号位 消费点，批形成前执行）。
    ///
    /// 返回 `null` ⇒ 通过（或本需求**无明确 PI 来源** ⇒ B-009 的 PI 量限**不适用**）。
    /// 返回非 `null` ⇒ 拒绝原因 —— 调用方按既有「显式未满足 / 契约问题」机制 Fail Closed，**不得静默截断 Q**。
    ///
    /// 判定顺序（任一不满足即拒，且**不猜测、不取首条**）：
    ///   ① **PI 权威行必须唯一** —— 0 行 = 事实缺失；≥2 行 = **来源不唯一**（裁决 F-02「来源不唯一」）。
    ///      唯一性键 = `ProductionInstructionNo × MaterialId × **FactoryId**`（V1_4 NEW-03 补充：
    ///      同一 PI 号在不同工厂是**不同的 PI 源**，只按 PI 号 + 物料匹配会把异厂行算成本厂来源
    ///      ⇒ 判定「来源不唯一」或更糟地**误用异厂剩余量**；异厂行**不得**被当作本厂 PI 权威行）。
    ///   ② **Stage 位置必须可信** —— 上下文缺失 / PI 或物料不匹配 / `StageCode` 为空 /
    ///      **`StageCode` 不是本需求当前目标 Stage** ⇒ **位置未知 / 错 Stage**，显式未满足
    ///      （裁决 F-02「位置未知须显式未满足」；V1_4 NEW-03「只检查非空」即缺陷）。
    ///   ③ 合法量 = `min(PI Original, PI Remaining, StageFreeEligibleQty − 已占量)`，
    ///      **当且仅当 `configuredMax` 有值时**再并入 `配置 Max`；
    ///      需求量 **严格大于** 合法量 ⇒ 越限（**边界相等合法**，且**绝不静默截断到合法量**）。
    ///
    /// 【V1_4 NEW-01：`configuredMax` 的**口径**由调用方决定，不得混用】
    ///   `MaxExecutionBatchQty` 是**单批**上限（B-001/B-009 的「单个新增 Stage 执行批」），
    ///   **不是**整条需求的「所有批总量」上限。⇒
    ///     · **单批口径**调用（逐批检查）传 `configuredMax = Policy.MaxExecutionBatchQty`；
    ///     · **需求累计口径**调用（Σ各批）**必须传 `null`** —— 否则 Q=60 / Max=30 的合法 2×30
    ///       会被判成 `PI_LEGAL_QTY_OVER_LIMIT`（把合法方案变成 Conflict/0 批）。
    ///
    /// ⚠ 本方法**不读** `Policy` 的任何其他业务旧列，也不以任何默认值顶替缺失量。
    /// </summary>
    internal static string? EvaluatePiLegalQuantityGate(
        decimal quantity,
        string? productionInstructionNo,
        int materialId,
        int factoryId,
        string? targetStageCode,
        IReadOnlyList<PiRemainingFact>? piFacts,
        PiStageQuantityContext? stageContext,
        decimal? configuredMax)
    {
        // 无明确 PI 来源 ⇒ B-009 的 PI 量限不适用（与 B-010 末句「无明确 PI 来源不适用该兜底」同向）。
        if (string.IsNullOrWhiteSpace(productionInstructionNo)) return null;
        if (quantity <= 0m) return null;

        var piNo = productionInstructionNo!;

        // ── ① 唯一性：禁止取首条（V1_4 NEW-03：唯一性键含工厂，异厂行不得算作本厂 PI 来源）──
        PiRemainingFact? unique = null;
        var matched = 0;
        if (piFacts is not null)
        {
            foreach (var f in piFacts)
            {
                if (string.Equals(f.ProductionInstructionNo, piNo, StringComparison.Ordinal)
                    && f.MaterialId == materialId
                    && f.FactoryId == factoryId)
                {
                    matched++;
                    unique ??= f;
                }
            }
        }

        if (matched == 0)
        {
            return $"PI_LEGAL_QTY_CONTRACT_PENDING：需求 PI={piNo} × 物料 {materialId} × 工厂 {factoryId}"
                 + " 在求解输入中找不到 PI 权威行"
                 + "（PI Original / PI Remaining 不可得；**异厂同号 PI 不作本厂来源**）⇒ 无法判定合法量，显式未满足";
        }

        if (matched > 1)
        {
            return $"PI_LEGAL_QTY_CONTRACT_PENDING：需求 PI={piNo} × 物料 {materialId} × 工厂 {factoryId}"
                 + $" 匹配到 {matched} 行 PI 权威事实"
                 + "⇒ 来源**不唯一**，不得任取其一，显式未满足";
        }

        var pi = unique!;

        // ── ② Stage 位置可信性 ──
        if (stageContext is null)
        {
            return $"PI_LEGAL_QTY_CONTRACT_PENDING：需求 PI={piNo} × 物料 {materialId} 缺 Stage 合法自由量上下文"
                 + "⇒ **位置未知**，不得以 PI Remaining 冒充 Stage 自由量（F-02），显式未满足";
        }

        if (!string.Equals(stageContext.ProductionInstructionNo, piNo, StringComparison.Ordinal)
            || stageContext.MaterialId != materialId)
        {
            return "PI_LEGAL_QTY_CONTRACT_PENDING：Stage 合法自由量上下文与需求不匹配"
                 + $"（上下文 PI={stageContext.ProductionInstructionNo} × 物料 {stageContext.MaterialId}，"
                 + $"需求 PI={piNo} × 物料 {materialId}）⇒ 位置不可信，显式未满足";
        }

        if (string.IsNullOrWhiteSpace(stageContext.StageCode))
        {
            return $"PI_LEGAL_QTY_CONTRACT_PENDING：需求 PI={piNo} 的 Stage 合法自由量**缺 StageCode**"
                 + "⇒ 位置未知，显式未满足";
        }

        // ── ②b V1_4 NEW-03：**必须是本需求当前目标 Stage 的余额**，不是「随便哪个非空 Stage」──
        //   旧实现只判 `StageCode` 非空 ⇒ 当前批属于 STAGE2、却把 STAGE1 的 100 自由量当成可用量
        //   （STAGE2 实际只剩 10）⇒ 数量闸门**被错误放行**。⇒ 目标 Stage 身份**强制一致性校验**。
        //   目标 Stage 来源 = 本需求 `StartStageCode`（批形成发生在 Routing 展开**之前**，故需求级起点
        //   即该批的 Stage 身份；更细的逐工序 Stage 身份属 1↔2 事实身份契约，见回执归口）。
        if (string.IsNullOrWhiteSpace(targetStageCode))
        {
            return $"PI_LEGAL_QTY_CONTRACT_PENDING：需求 PI={piNo} 的目标 Stage 未知"
                 + "（需求 StartStageCode 为空，无法确定本批 Stage 身份）⇒ 位置未知，显式未满足";
        }

        if (!string.Equals(stageContext.StageCode, targetStageCode, StringComparison.Ordinal))
        {
            return $"PI_LEGAL_QTY_CONTRACT_PENDING：Stage 合法自由量上下文**不是本需求目标 Stage**"
                 + $"（上下文 Stage={stageContext.StageCode}，需求目标 Stage={targetStageCode}）"
                 + "⇒ **错 Stage**，位置不可信，显式未满足";
        }

        // ── ③ 合法量 = min(配置 Max, PI Original, PI Remaining, StageFree − 同PI同Stage已占) ──
        var stageFree = stageContext.StageFreeEligibleQty - stageContext.AlreadyCommittedQty;
        if (stageFree < 0m) stageFree = 0m;

        var cap = pi.PiQuantity;
        if (pi.PiRemainingQty < cap) cap = pi.PiRemainingQty;
        if (stageFree < cap) cap = stageFree;
        if (configuredMax.HasValue && configuredMax.Value < cap) cap = configuredMax.Value;

        if (quantity > cap)
        {
            return $"PI_LEGAL_QTY_OVER_LIMIT：需求量 {quantity} 超出合法可用量 {cap}"
                 + $"（= min(PI Original {pi.PiQuantity}, PI Remaining {pi.PiRemainingQty},"
                 + $" Stage 合法自由 {stageFree}［Stage {stageContext.StageCode}：自由 {stageContext.StageFreeEligibleQty}"
                 + $" − 同PI同Stage已占 {stageContext.AlreadyCommittedQty}］"
                 + (configuredMax.HasValue ? $", 配置 Max {configuredMax.Value}" : string.Empty)
                 + "））⇒ 按 B-009/B-010 显式记录未满足，**不静默截断 Q**";
        }

        return null;
    }

    /// <summary>
    /// 执行批形成（P0-01/P0-02）—— 0号位 指定链路的第一环：`Free Slice → Batch Policy → 1..N ExecutionBatchDraft`。
    ///
    /// 硬约束（任何策略都不得推翻）：
    ///   · A/B（`IsContinuation` / `NoSplitMerge`）⇒ **恒 1 批**（v1.6 NoSplitMerge；Phase4 P0_07 同向）。
    ///   · C 桶**缺策略**（`policy == null`）⇒ **`IsLegal=false` / `IsMissingPolicy=true`**
    ///     （`BATCH_POLICY_MISSING`，P0-01）—— **不再**回落成「不拆、恒 1 批」。
    ///
    /// 批数裁决见 <see cref="DecideExecutionBatchPlan"/>（**合法批域**：Min / Max / AllowSplit）。
    ///   **无合法批方案 ⇒ 返回 `IsLegal=false`**（调用方 fail-closed），**绝不产出非法批**。
    ///
    /// 数量切分：各批**逐分不丢**（前 N-1 批向下取整到 4 位小数，末批取余数）——
    ///   保证 `Σ NetOutputQty = 需求 NetOutputQty`、`Σ PlannedProcessQty = 需求 PlannedProcessQty`。
    /// 切分后**逐批复核** `[Min, Max]`（四舍五入可能把边界批推出域）⇒ 越域即判冲突，**不静默放行**；
    ///   **但**批数由更高优先级规则定死时（A/B 恒 1 批 / 缺策略恒 1 批）**不复核** ——
    ///   该批数量不受策略 Min/Max 管辖，据策略判越界会把合法批误杀（A/B 的 `NoSplitMerge` 硬约束优先于策略）。
    /// </summary>
    public static ExecutionBatchFormation FormExecutionBatches(
        LogicalProductionDemand demand,
        BatchPolicyRuleSnapshot? policy,
        IReadOnlyList<PiRemainingFact>? piFacts = null,        // AUD-1-006 / B-010：PI 权威事实（**当前调用方未投影 ⇒ null**，见 AUD-1-D04）
        PiStageQuantityContext? piStageContext = null,          // V1_3 F-02/F-03：Stage 合法自由量上下文（**当前调用方未投影 ⇒ null**）
        PiStageCommitLedger? piStageLedger = null)              // V1_4 NEW-04：本 Run 新增 Stage 批的运行累计账（可选；未传 ⇒ 退化为逐需求独立判定）
    {
        var plan = DecideExecutionBatchPlan(demand, policy);
        if (!plan.IsLegal)
        {
            // ── AUD-1-006（0号位 2026-10-10《APS_V1_2_20261010.md》§3，P0/CONFIRMED）──
            //   B-010 条件化保守兜底：**仅**「唯一明确真实 MTS PI 来源、Stage 合法自由量可靠、
            //   且 Policy **完全无匹配**」的 C 桶，允许按 Q_C 一次性保守组织一个 Stage 执行批候选，
            //   受 PI 量限；**不能普遍一律 Fail Closed**。⇒ 缺策略时先评估该分支，再决定是否 Fail Closed。
            //   ⚠ 「已有配置无效」**不得**冒充「无匹配」（B-010 末句）—— 本分支只在 `IsMissingPolicy`
            //     （= 精确匹配与（已删除的）Material 级默认均未命中）时进入；`IsMissingPolicy=false`
            //     的 `BATCH_POLICY_CONFLICT` 一律走原路径，不适用兜底。
            if (plan.IsMissingPolicy)
            {
                var b010 = TryFormConservativePiBatch(demand, piFacts, piStageContext, piStageLedger);
                if (b010 is not null)
                {
                    return b010;
                }
            }

            // P0-01：缺策略（`BATCH_POLICY_MISSING`）与冲突（`BATCH_POLICY_CONFLICT`）分列，供出口给不同 Reason。
            return plan.IsMissingPolicy
                ? ExecutionBatchFormation.Missing(plan.ConflictReason!)
                : ExecutionBatchFormation.Conflict(plan.ConflictReason!);
        }

        // V1_3 F-03：正常 Policy 路径同样过 B-009 统一合法量门禁（不再只给 B-010 兜底装门禁）。
        return ApplyPiLegalQuantityGate(
            demand, policy, piFacts, piStageContext,
            BuildBatchFormation(demand, policy, plan.Count, validateDomain: true),
            piStageLedger);
    }

    /// <summary>
    /// V1_3 F-03：把 B-009 合法量门禁套到**正常 Policy 路径**产出的批上。
    ///
    /// 【为什么「输入已投影」才强制、「输入未投影」不拦截】
    ///   `piFacts is null` ⇒ B-009 的输入（PI Original / PI Remaining）**整体未投影进求解** ⇒ 门禁**不可评估**。
    ///   此时按 Fail Closed 会把**每一条 PI 来源需求**都拦下 ⇒ 求解器产出 0 Task ——
    ///   这不是任何冻结文档要求的规则，而是**发明**一条新失败模式（0号位 §8「不要求重新冻结业务」）。
    ///   ⇒ 未投影时**不拦截**，但用 <see cref="SolverDiagnostics"/> 计数器**显式登记缺口**（可观测、不静默）。
    ///   反之，**输入一旦投影，门禁即强制**：唯一性 / Stage 位置 / 合法量任一不满足 ⇒ Fail Closed。
    ///   ⚠ 本口径已在回执中如实声明：生产态该门禁**当前不可达**（调用点传 `piFacts: null`），
    ///     缺口归 2号位 装载（AUD-1-D04），1号位 **不宣称** B-009 生产态已达标（不降目标、如实陈述）。
    /// </summary>
    private static ExecutionBatchFormation ApplyPiLegalQuantityGate(
        LogicalProductionDemand demand,
        BatchPolicyRuleSnapshot? policy,
        IReadOnlyList<PiRemainingFact>? piFacts,
        PiStageQuantityContext? stageContext,
        ExecutionBatchFormation formation,
        PiStageCommitLedger? ledger)
    {
        if (!formation.IsLegal) return formation;

        // ── V1_4 NEW-02：**A/B 既存 MES 执行批不参加普通拆合批** ⇒ B-009 的
        //   「**新增** Stage 执行批」数量闸门**不适用** ──
        //   判词（本号位核对：**成立**）：`DecideExecutionBatchPlan` 对 A/B 正确返回 `Legal(1)`（恒 1 批、
        //   不受策略 Min/Max 管辖），但 `FormExecutionBatches` 的正常路径**没有任何 A/B 前置排除**就调用本方法
        //   ⇒ 配置 Max 小于连续量、或 PI/Stage 事实已投影时，会**错误改写既存执行身份与数量**
        //     （把 A/B 的既存批判成 Conflict ⇒ 既存执行批消失 = 比「数量不对」更重的后果）。
        //   ⇒ 与 `DecideExecutionBatchPlan` 同一判据、同一处豁免：`IsContinuation || NoSplitMerge` 直接放行。
        //   ⚠ C 桶仍严格限量（本方法对 C 桶的全部判据不变）。
        if (demand.IsContinuation || demand.NoSplitMerge) return formation;

        if (piFacts is null)
        {
            SolverDiagnostics.CountPiLegalQuantityGateUnevaluated();
            return formation;
        }

        // V1_4 NEW-03：目标 Stage = 本需求 `StartStageCode`（批形成在 Routing 展开之前 ⇒ 需求级起点即批的 Stage 身份）。
        var piNo = demand.ProductionInstructionNo;
        var targetStage = demand.StartStageCode;

        // V1_4 NEW-04：把**本 Run 已形成批**的累计量并入 Stage 已占量（试排隔离由调用方用克隆账保证）。
        var effectiveStage = MergeLedgerIntoStageContext(stageContext, ledger, piNo, demand.MaterialId, targetStage);

        // ── ① **单批口径**：`配置 Max` 只在此口径参与（B-009「**单个**新增 Stage 执行批上限」）──
        //   V1_4 NEW-01 判词（本号位核对：**成立**）：旧实现把 `Σ各批 NetOutputQty` 当成 `quantity`
        //   又传入 `policy.MaxExecutionBatchQty` ⇒ 配置 Max（单批上限）被误用成**整条需求的总量上限**
        //   ⇒ Q=60 / AllowSplit / Max=30 的**合法** 2×30 被判 `PI_LEGAL_QTY_OVER_LIMIT`（合法方案 → Conflict/0 批）。
        foreach (var b in formation.Batches)
        {
            var reasonBatch = EvaluatePiLegalQuantityGate(
                b.NetOutputQty, piNo, demand.MaterialId, demand.FactoryId, targetStage,
                piFacts, effectiveStage, policy?.MaxExecutionBatchQty);

            if (reasonBatch is not null)
            {
                SolverDiagnostics.CountPiLegalQuantityGateBlocked();
                return ExecutionBatchFormation.Conflict(reasonBatch);
            }
        }

        // ── ② **需求累计口径**：`Σ各批` vs PI / Stage 合法自由总量 —— **不含配置 Max**（NEW-01）──
        //   PI Original / PI Remaining / StageFree − 同PI同Stage已占（含本 Run 新增批）是**总量**口径，
        //   必须在 Σ 上核对，否则拆成 N 批即可逐批绕过总量上限。
        decimal total = 0m;
        foreach (var b in formation.Batches)
        {
            total += b.NetOutputQty;
        }

        var reason = EvaluatePiLegalQuantityGate(
            total, piNo, demand.MaterialId, demand.FactoryId, targetStage,
            piFacts, effectiveStage, configuredMax: null);

        if (reason is not null)
        {
            SolverDiagnostics.CountPiLegalQuantityGateBlocked();
            return ExecutionBatchFormation.Conflict(reason);
        }

        // ── 过闸 ⇒ 登记本 Run 新增 Stage 批的累计占用（NEW-04）──
        //   登记粒度 = **需求级批形成**（不是逐 Operation）⇒ 同一批展开成 N 条 Task 只扣**一次**。
        if (ledger is not null && !string.IsNullOrWhiteSpace(piNo) && !string.IsNullOrWhiteSpace(targetStage))
        {
            ledger.Commit(piNo!, demand.MaterialId, targetStage!, total);
        }

        return formation;
    }

    /// <summary>
    /// V1_4 NEW-04：把运行累计账里**本 Run 已形成批**的量并入 Stage 上下文的 `AlreadyCommittedQty`。
    /// 账为空 / 无账 / 无上下文 ⇒ 原样返回（零回归）。
    /// </summary>
    private static PiStageQuantityContext? MergeLedgerIntoStageContext(
        PiStageQuantityContext? stageContext,
        PiStageCommitLedger? ledger,
        string? productionInstructionNo,
        int materialId,
        string? targetStageCode)
    {
        if (stageContext is null || ledger is null) return stageContext;

        var already = ledger.Committed(
            productionInstructionNo ?? string.Empty, materialId, targetStageCode ?? string.Empty);

        return already == 0m
            ? stageContext
            : stageContext with { AlreadyCommittedQty = stageContext.AlreadyCommittedQty + already };
    }

    /// <summary>
    /// AUD-1-006 / B-010：C 桶「缺策略」时的**条件化保守兜底**评估。
    ///
    /// B-010 逐字前置（三条**同时**成立才适用，缺一即不适用该兜底）：
    ///   ① **唯一明确真实 MTS PI 来源** —— `demand.ProductionInstructionNo` 非空
    ///      （B-010 末句：「**无明确 PI 来源不适用该兜底**」）；
    ///   ② **Policy 完全无匹配** —— 由调用点保证（仅 `IsMissingPolicy` 进入）；
    ///   ③ **Stage 合法自由量可靠** —— 需 PI 权威剩余量（`PiRemainingFact.PiRemainingQty`，
    ///      `Core/Dto/PiRemainingFact.cs`：`max(PiQuantity − ReceivedQty, 0)`）与 Stage 自由余额。
    ///
    /// 适用 ⇒ 按 `Q_C = demand.NetOutputQty` 组织**一个** Stage 执行批候选：
    ///   · **不伪造**物料级 Min/Max/Preferred（B-010）；
    ///   · **不额外优化拆合**（B-010）；
    ///   · `Q` **不得扩大、不得静默截断**（B-010）—— 超出 PI 合法可用量必须**显式记录未满足**。
    ///
    /// ⚠ 本 Run 的 ③ **无载体**：`PiRemainingFact` 已存在于 Core，但**未投影进 `DomainSolveRequest`**
    ///   （全仓 grep：`LPS.APS.Scheduling` 零命中；AUD-1-D04 归 2号位 装载）
    ///   ⇒ 1号位 **无法验证**「Stage 合法自由量可靠」这一前置 ⇒ **不得凭空造批**
    ///   （遵 [[lps-no-lowering-targets-redline]]：做不到直说，不把「替代路径」当达标）
    ///   ⇒ 返回 <see cref="ExecutionBatchFormation.PiQuantityContractPending"/>，把原因精确登记为
    ///     **契约待核**（而非笼统的 `BATCH_POLICY_MISSING`），并维持 Fail Closed。
    ///
    /// ── 2026-10-10 V1_3 F-02 整改（0号位 裁决 F-02）──
    ///   旧实现的两处缺陷已修：
    ///     · **取首条**：`foreach … break` 在多行匹配时**静默选定一条** ⇒ 改为**唯一性强制**（≥2 行即拒）；
    ///     · **以 `PiRemainingQty` 冒充 Stage 自由量**：旧实现只卡 `PiRemainingQty`，且注释把
    ///       「Stage 合法自由量」直接等同于 PI 剩余量 ⇒ 改为消费**独立的**
    ///       <see cref="PiStageQuantityContext.StageFreeEligibleQty"/>（**不得混用**），
    ///       并纳入 **PI Original** 与**同 PI 同 Stage 已占量**（B-009 完整四元量限）。
    ///   ⇒ 判定统一收敛到 <see cref="EvaluatePiLegalQuantityGate"/>（B-009 与 B-010 共用同一门禁）。
    ///
    /// 返回 `null` ⇒ **不适用**该兜底（无明确 PI 来源 / 非正数量）⇒ 调用方走原 `BATCH_POLICY_MISSING`。
    /// </summary>
    private static ExecutionBatchFormation? TryFormConservativePiBatch(
        LogicalProductionDemand demand,
        IReadOnlyList<PiRemainingFact>? piFacts,
        PiStageQuantityContext? piStageContext,
        PiStageCommitLedger? ledger)
    {
        // 前置①：唯一明确真实 MTS PI 来源（无 PI 来源 ⇒ 不适用兜底，走原 Fail Closed）
        if (string.IsNullOrWhiteSpace(demand.ProductionInstructionNo))
        {
            return null;
        }

        // 非正数量不构成合法批（与 `DecideExecutionBatchPlan` 同口径），不适用兜底。
        decimal qc = demand.NetOutputQty;
        if (qc <= 0m)
        {
            return null;
        }

        // V1_4 NEW-04：兜底路径同样消费**本 Run 已形成批**的累计占用。
        var effectiveStage = MergeLedgerIntoStageContext(
            piStageContext, ledger, demand.ProductionInstructionNo, demand.MaterialId, demand.StartStageCode);

        // 前置③：**唯一真实 MTS PI 来源 + Stage 合法自由量可靠** —— 收敛到 V1_3 统一门禁判定
        //   （唯一性 / Stage 位置可信 / 合法量 = min(PI Original, PI Remaining, StageFree − 同PI同Stage已占)）。
        //   B-010 兜底**不伪造** Min/Max/Preferred ⇒ `configuredMax: null`（不得把配置上限当兜底上限）。
        var reason = EvaluatePiLegalQuantityGate(
            qc, demand.ProductionInstructionNo, demand.MaterialId, demand.FactoryId, demand.StartStageCode,
            piFacts, effectiveStage, configuredMax: null);

        if (reason is not null)
        {
            return ExecutionBatchFormation.PiQuantityContractPending(
                "B010_PI_QUANTITY_CONTRACT_PENDING：C 桶需求在 Solver 输入中找不到有效 Batch Policy（BATCH_POLICY_MISSING），"
                + "且 B-010 条件化保守兜底所需的前置③「唯一真实 MTS PI 来源 + Stage 合法自由量可靠」未全部成立 —— "
                + reason);
        }

        // 过闸 ⇒ 登记本 Run 新增 Stage 批的累计占用（与正常路径同口径：一次批形成 = 至多一次登记）。
        if (ledger is not null && !string.IsNullOrWhiteSpace(demand.StartStageCode))
        {
            ledger.Commit(demand.ProductionInstructionNo!, demand.MaterialId, demand.StartStageCode, qc);
        }

        // 适用 ⇒ **一个** Stage 执行批候选（Q_C 原值，不伪造 Min/Max/Preferred、不额外优化拆合）。
        return ExecutionBatchFormation.Legal(new[]
        {
            new ExecutionBatchDraft(
                ExecutionBatchKey(demand.LogicalDemandKey, 1),
                demand.LogicalDemandKey,
                Ordinal: 1,
                NetOutputQty: qc,
                PlannedProcessQty: demand.PlannedProcessQty)
        });
    }

    /// <summary>
    /// 按**给定批数**切分并（可选）复核合法域 —— <see cref="FormExecutionBatches"/>（V1 基线，nMin）
    ///   与 P1-01 优化候选共用同一段切分逻辑（逐分不丢 + 边界复核），保证两条路径切分口径一致。
    ///
    /// 切分后逐批复核合法域：**仅在策略真正驱动了拆批**时校验（四舍五入可能把边界批推出域 ⇒ 越域即冲突，不静默放行）。
    ///   排除「批数由更高优先级规则定死」的情形 —— 此时批数量**不受策略 Min/Max 管辖**，
    ///   据策略判其越界会把合法批误杀：
    ///     · A/B（`IsContinuation` / `NoSplitMerge`）⇒ 恒 1 批（B-003：A/B 不做普通自由拆合批）。
    ///   （P0-01 后「缺策略」已不再进入本方法：C 桶缺策略在 `DecideExecutionBatchPlan` 即判硬失败。）
    /// </summary>
    private static ExecutionBatchFormation BuildBatchFormation(
        LogicalProductionDemand demand,
        BatchPolicyRuleSnapshot? policy,
        int count,
        bool validateDomain)
    {
        var batches = new List<ExecutionBatchDraft>(count);
        decimal netAssigned = 0m;
        decimal procAssigned = 0m;

        for (int ordinal = 1; ordinal <= count; ordinal++)
        {
            bool last = ordinal == count;

            decimal batchNet = last
                ? demand.NetOutputQty - netAssigned
                : decimal.Round(demand.NetOutputQty / count, 4, MidpointRounding.ToZero);
            decimal batchProc = last
                ? demand.PlannedProcessQty - procAssigned
                : decimal.Round(demand.PlannedProcessQty / count, 4, MidpointRounding.ToZero);

            netAssigned += batchNet;
            procAssigned += batchProc;

            batches.Add(new ExecutionBatchDraft(
                ExecutionBatchKey(demand.LogicalDemandKey, ordinal),
                demand.LogicalDemandKey,
                ordinal,
                batchNet,
                batchProc));
        }

        if (validateDomain
            && policy is not null
            && !demand.IsContinuation
            && !demand.NoSplitMerge
            && (policy.MinExecutionBatchQty is > 0m || policy.MaxExecutionBatchQty is not null))
        {
            decimal min = policy.MinExecutionBatchQty ?? 0m;
            decimal? max = policy.MaxExecutionBatchQty;
            foreach (var b in batches)
            {
                if (b.NetOutputQty < min || (max is decimal mx && b.NetOutputQty > mx))
                {
                    return ExecutionBatchFormation.Conflict(
                        $"BATCH_POLICY_CONFLICT：切分后批数量越界 —— {b.BatchDraftKey} Qty={b.NetOutputQty} 不在 [Min={min}, Max={(max?.ToString() ?? "∞")}] 内");
                }
            }
        }

        return ExecutionBatchFormation.Legal(batches);
    }

    /// <summary>
    /// 1号位 Solver **技术预算**（B-007 / AUD-1-005，0号位 2026-10-10《APS_V1_2_20261010.md》§3）。
    ///
    /// 为什么单列一个类型：这两个上限是 **Solver 有界搜索的技术预算**，**不是业务拆合批上限**
    ///   （B-007 逐字：「是 1号位 Solver 有界搜索技术预算，**不再按 Material+部门由车间维护**」；
    ///     「经验值 3/8 **不是统一业务上限**」）。它们**不得**再以业务 `BatchPolicyRuleSnapshot` 的
    ///   `MaxOptimizationSplitCount` / `MaxBatchCandidates` 列作为运行单一真相（v5.1.10 收口①：
    ///   该两列投影恒 null、**主链不得消费**，仅追溯）。
    ///
    /// 取源（按 B-007「有效值与运行版本应**单源可追溯**」）：
    ///   ① **正式参数版本载体（若存在）**：⑤ `SolverStrategyBlock.Split.MaxOptimizationSplitCount`
    ///      （`FrozenStrategySnapshot.cs` 清单 31；由 3号位 冻结、2号位 在 Run 冻结上下文装载）；
    ///   ② 无正式载体可用的那一项 ⇒ **1号位 有界、可追溯的版本化安全默认**（<see cref="VersionedSafeDefault"/>）。
    ///
    /// ⚠ `MaxBatchCandidates` **当前无正式参数版本载体**（`SplitParams` 只有 `MaxOptimizationSplitCount`；
    ///   `CandidateGuardrailBlock.SplitAlternatives` 语义为「拆分备选数」、已被 Phase4 消费，**不得挪用**）
    ///   ⇒ 走 ②，并登记为待 2/3号位 提供正式载体的契约项。
    /// ⚠ **硬 Max 强制拆出的基线批不受本预算限制**（B-007：「不能让预算限制硬 Max 强制拆批」）。
    /// </summary>
    public readonly record struct SolverBatchBudget(
        int MaxOptimizationSplitCount,
        int MaxBatchCandidates,
        string Source)
    {
        /// <summary>预算版本号（B-007：运行版本应单源可追溯）。</summary>
        public const string BudgetVersion = "SolverBatchBudget/v1";

        /// <summary>版本化安全默认 —— 优化性拆分批数上限（有界、可追溯；**非**业务上限）。</summary>
        public const int DefaultMaxOptimizationSplitCount = 3;

        /// <summary>版本化安全默认 —— 单问题最多评估候选数（含基线）。</summary>
        public const int DefaultMaxBatchCandidates = 8;

        /// <summary>无正式载体时的版本化安全默认（B-007 ②）。</summary>
        public static SolverBatchBudget VersionedSafeDefault { get; } = new(
            DefaultMaxOptimizationSplitCount,
            DefaultMaxBatchCandidates,
            $"{BudgetVersion}:default");

        /// <summary>来源自正式参数版本载体（⑤ `SolverStrategy.Split`）。</summary>
        public static SolverBatchBudget FromSplitParams(int maxOptimizationSplitCount) => new(
            maxOptimizationSplitCount < 0 ? DefaultMaxOptimizationSplitCount : maxOptimizationSplitCount,
            DefaultMaxBatchCandidates,
            $"{BudgetVersion}:SplitParams");
    }

    /// <summary>
    /// AUD-1-005：解析本 Run 的 <see cref="SolverBatchBudget"/>。
    ///   ① ⑤ `SolverStrategy.Split.MaxOptimizationSplitCount`（正式参数版本载体，若存在）⇒ 用之；
    ///   ② 否则 ⇒ <see cref="SolverBatchBudget.VersionedSafeDefault"/>。
    /// **绝不**读业务 `BatchPolicyRuleSnapshot` 的两列。
    /// </summary>
    internal static SolverBatchBudget ResolveSolverBatchBudget(DomainSolveRequest? request)
    {
        var split = request?.StrategySnapshot?.SolverStrategy?.Split;
        var budget = split is not null
            ? SolverBatchBudget.FromSplitParams(split.MaxOptimizationSplitCount)
            : SolverBatchBudget.VersionedSafeDefault;

        // ── V1_3 F-04：把**实际生效**的技术预算登记进 Run 快照（版本 / 取源 / 两个生效值）──
        //   0号位 裁决 F-04：「记录 Solver 有效技术预算的 Run 快照 / 追踪证据；检验输入变化、默认和
        //   业务旧列变化互不串联。」⇒ 本处是**唯一**解析点，写一次即代表本 Run 全量生效值。
        //   ⚠ 只登记本对象（纯 `SolverDiagnostics`，非契约 DTO）⇒ 不新增表、不改契约、不要求重新冻结业务。
        //   ⚠ 业务旧列（`BatchPolicyRuleSnapshot.MaxOptimizationSplitCount` / `MaxBatchCandidates`）
        //     **不在**本路径上 ⇒ 改动它们不会改变本快照（B-007 单源可追溯，见 F-04 反证测试）。
        // ── V1_4 NEW-06：**无条件**写进程级可检索快照（未开 `BeginScope()` 的生产 Run 也可追溯）──
        //   复审 NEW-06 判词（本号位核对：**成立**）：V1_3 只写 `SolverDiagnostics.Current`（`AsyncLocal`），
        //   而正常生产 Run（`SolveAsync` 未自动开 scope）**没有任何记录** ⇒ 预算诊断**不是**生产 Run 正式快照。
        //   ⇒ 除 scope 计数外，**始终**写带 Run 版本源三元组
        //     （`ScheduleRunId` / `StrategyProfileVersionId` / `ParameterSetVersionId`）的进程级快照。
        //   ⚠ 持久化落盘（`ScheduleRun` 表）仍归 2号位；1号位 交付的是**进程内可检索的正式快照**。
        SolverDiagnostics.RecordSolverBatchBudget(
            request?.ScheduleRunId,
            request?.StrategySnapshot?.StrategyProfileVersionId,
            request?.StrategySnapshot?.ParameterSetVersionId,
            SolverBatchBudget.BudgetVersion,
            budget.Source,
            budget.MaxOptimizationSplitCount,
            budget.MaxBatchCandidates);

        return budget;
    }

    /// <summary>
    /// P1-01（0号位 2026-10-07《未命名的Markdown文件 (7).md》§八）：**有界优化候选**批方案枚举。
    ///
    /// 候选（**结构上有界，≤5**，不新增任何默认值）：
    ///   · **首元素恒为 V1 基线**（`nMin` = 最小合法批数）—— 保证「无改善时基线胜出」，既有行为零回归；
    ///   · 合法**不拆**（1 批）；
    ///   · 合法 **2 批** / 合法 **3 批**；
    ///   · `PreferredBatchQty` **附近切分**（`nPref = round(Qty / PreferredBatchQty)`，越界即丢弃）。
    ///
    /// 两个上限**只收不放**，且**来源已按 AUD-1-005 整改**（0号位 2026-10-10《APS_V1_2_20261010.md》§3）：
    ///   · `MaxOptimizationSplitCount`：**仅限制优化性拆分**（批数 &gt; 基线者），不限制硬 Max 强制拆出的基线批数
    ///     （与 DTO 注释逐字一致：「不限制硬 Max 强制拆分」）；
    ///   · `MaxBatchCandidates`：单问题最多评估候选数（**含基线**）。
    ///   ⚠ 二者**一律来自入参 `budget`**（`SolverBatchBudget`），**不再从业务 `BatchPolicyRuleSnapshot` 读取**
    ///     —— 后者两列自 v5.1.10 收口① 起恒 null 且**主链不得消费**（B-007）。
    ///
    /// 每个优化候选都要过 <see cref="BuildBatchFormation"/> 的**合法域复核**；越域者**丢弃该候选**
    ///   （不得因一个优化候选越界就把整个需求判冲突 —— 基线不受影响）。
    ///
    /// 不展开的条件（候选恒 = 1，直接返回基线）：
    ///   · A/B（`IsContinuation` / `NoSplitMerge`）/ 非正数量 / `!AllowSplit`
    ///     （`policy == null` 的 C 桶需求 P0-01 后已在上游判硬失败，到不了这里）；
    ///   · 唯一合法批数（`nMin == nMax`）。
    /// </summary>
    internal static List<ExecutionBatchFormation> EnumerateLegalBatchPlanCandidates(
        LogicalProductionDemand demand,
        BatchPolicyRuleSnapshot? policy,
        ExecutionBatchFormation baseline,
        SolverBatchBudget? budget = null)   // AUD-1-005：技术预算**入参**（不再从业务 policy 读）；缺省 = 版本化安全默认
    {
        var list = new List<ExecutionBatchFormation> { baseline };

        if (policy is null || demand.IsContinuation || demand.NoSplitMerge
            || demand.NetOutputQty <= 0m || !policy.AllowSplit)
        {
            return list;
        }

        decimal qty = demand.NetOutputQty;
        decimal min = policy.MinExecutionBatchQty ?? 0m;
        decimal? max = policy.MaxExecutionBatchQty;

        int nMin = max is decimal mx ? ClampToPositiveInt(Math.Ceiling(qty / mx)) : 1;
        int nMax = min > 0m ? ClampToPositiveInt(Math.Floor(qty / min)) : int.MaxValue;
        if (nMin >= nMax)
        {
            return list;   // 唯一合法批数 ⇒ 无优化空间
        }

        int baselineCount = baseline.Batches.Count;

        var counts = new List<int>();
        void TryAdd(int c)
        {
            if (c < nMin || c > nMax || counts.Contains(c))
            {
                return;
            }
            counts.Add(c);
        }

        TryAdd(1);   // 合法不拆
        TryAdd(2);   // 合法 2 批
        TryAdd(3);   // 合法 3 批
        if (policy.PreferredBatchQty is decimal pref && pref > 0m)
        {
            TryAdd(ClampToPositiveInt(Math.Round(qty / pref, MidpointRounding.AwayFromZero)));   // Preferred 附近
        }

        // ── 技术预算来源（AUD-1-005 整改）：**入参 `budget`**，绝不读业务 `policy` 的两列 ──
        //   `policy.MaxOptimizationSplitCount` / `policy.MaxBatchCandidates` 自 v5.1.10 收口①（2026-10-09 生效）
        //   起投影恒 null 且**主链不得消费**（B-007）；旧写法把它们当运行预算 = 让历史业务列重新变成生效默认。
        var solverBudget = budget ?? SolverBatchBudget.VersionedSafeDefault;

        // 上限①：优化性拆分（批数 > 基线）受预算限制；**基线不受限**（硬 Max 强制拆分不得被技术预算压制）。
        counts.RemoveAll(c => c > baselineCount && c > solverBudget.MaxOptimizationSplitCount);

        // 上限②：候选总数受预算限制（基线恒保留 ⇒ 可容纳的优化候选 = cap - 1）。
        if (solverBudget.MaxBatchCandidates > 0)
        {
            int allowed = Math.Max(0, solverBudget.MaxBatchCandidates - 1);
            if (counts.Count > allowed)
            {
                counts = counts.Take(allowed).ToList();
            }
        }

        foreach (var c in counts)
        {
            if (c == baselineCount)
            {
                continue;
            }

            var formation = BuildBatchFormation(demand, policy, c, validateDomain: true);
            if (formation.IsLegal)
            {
                list.Add(formation);
            }
        }

        return list;
    }

    /// <summary>
    /// P0-01/P0-03：按执行批数量克隆需求（只改数量，其余字段**全量逐字拷贝**，不得漏项）。
    ///
    /// 只在 `batches.Count &gt; 1` 时调用；`count == 1` 一律直接用原 `LogicalProductionDemand` 实例
    /// （保证单批路径与旧版**逐字一致**，零回归）。
    /// </summary>
    internal static LogicalProductionDemand CloneDemandWithBatchQty(
        LogicalProductionDemand demand,
        decimal netOutputQty,
        decimal plannedProcessQty)
        => new()
        {
            LogicalDemandKey = demand.LogicalDemandKey,
            PlanVersionId = demand.PlanVersionId,
            DomainKey = demand.DomainKey,
            AllocationSequence = demand.AllocationSequence,
            DemandKey = demand.DemandKey,
            OrderId = demand.OrderId,
            MaterialId = demand.MaterialId,
            FactoryId = demand.FactoryId,
            StartStageCode = demand.StartStageCode,
            RequiredStageCode = demand.RequiredStageCode,
            StartOperationCode = demand.StartOperationCode,
            NetOutputQty = netOutputQty,
            PlannedProcessQty = plannedProcessQty,
            UOM = demand.UOM,
            RequiredAvailableTime = demand.RequiredAvailableTime,
            DemandSequence = demand.DemandSequence,
            ProductionInstructionNo = demand.ProductionInstructionNo,
            IsUnlocated = demand.IsUnlocated,
            PreferredResourceId = demand.PreferredResourceId,
            FallbackResourceId = demand.FallbackResourceId,
            IsContinuation = demand.IsContinuation,
            ContinuationKey = demand.ContinuationKey,
            RouteCode = demand.RouteCode,
            PathId = demand.PathId,
            NoSplitMerge = demand.NoSplitMerge,
            PreferredResourceCode = demand.PreferredResourceCode
        };

    private FinalTaskDraft CreateTask(
        LogicalProductionDemand demand,
        OperationNode operation,
        int? resourceId,          // 非资源工序（UNCONSTRAINED/WAIT_ONLY）传 null → Task.ResourceId=NULL，不占资源（0号位 2026-09-22 裁决）
        DateTime start,
        DateTime end,
        decimal setupMinutes,     // item1 接线（阶段二）：规则解析出的换型分钟（调用方经 FindSlotWithDynamicSetup/收敛迭代取得）
        ConstraintContext constraints,
        string? setupSource = null,  // SetupSource 填充（v1.2/5号位 值契约）：SetupOutcome → 大写 5 值
        string? batchDraftKey = null)   // P0-01/P0-02：本批归批键（null ⇒ 回落 ExecutionBatchKey(demandKey, 1)）
    {
        // P0-16修复：V1新生成的都是生产Task，统一使用PRODUCTION
        // UNLOCATED、无PI等作为独立标识/来源事实，不增加新TaskType
        string taskType = "PRODUCTION";

        // P0-04修复：补齐FinalTaskDraft必需字段，Duration已使用 StandardDuration × PlannedProcessQty ÷ CapacityFactor

        // AUD-1-002：执行批身份（跨 Stage 工序链）与 **Stage 执行批身份**（Stage 内 MES 批）分开 —— 见 StageExecutionBatchKey 注释。
        var executionBatchDraftKey = batchDraftKey ?? ExecutionBatchKey(demand.LogicalDemandKey);
        var producesStageBatchKey = ProducesStageExecutionBatchKey(demand);

        return new FinalTaskDraft
        {
            FinalDraftId = Guid.NewGuid().ToString(),
            SourceDraftId = demand.LogicalDemandKey,
            MaterialId = demand.MaterialId,
            FactoryId = demand.FactoryId,
            StageCode = operation.StageCode ?? string.Empty,
            OperationCode = operation.OperationCode,
            TaskType = taskType,
            ResourceId = resourceId,
            ResourceCode = GetResourceCode(resourceId, constraints),
            RouteCode = operation.RouteCode,
            PathId = operation.PathId,
            Quantity = demand.NetOutputQty,
            PlannedProcessQty = demand.PlannedProcessQty,
            UOM = demand.UOM ?? string.Empty,
            PlannedStartTime = start,
            PlannedEndTime = end,
            SetupTime = setupMinutes,   // item1 接线：规则值（原 operation.SetupTime 已废止，v1.2 §1.2）
            SetupSource = setupSource,  // SetupSource 填充：SetupOutcome → 大写 5 值（2号位 原样落库）
            Priority = demand.DemandSequence,
            IsVirtual = false,
            // v1.6 §新增/替换实施要求：FinalTask 必须原样回传 ContinuationKey + 回传真实 RouteCode/PathId。
            // ContinuationKey：1号位 **不生成、不解析**，从源 Demand 逐字拷贝（null 表示无该身份）。
            ContinuationKey = demand.ContinuationKey,
            // ExecutionBatchDraftKey：同一执行批下多 Operation 共 Key。
            // P0-02：键域为 (需求键, 批序号)，**不再由 Route/Path 派生**；批键由本批入参给定，缺省即 1 号批。
            ExecutionBatchDraftKey = executionBatchDraftKey,
            // AUD-1-002：Stage 内 MES 执行批身份（C 桶给值、A/B 留空）；**不是**上面那个跨 Stage 链键。
            //   T-003：MES 工单不跨 Stage ⇒ 跨 Stage 的三道工序必得**三个不同** Stage 键。
            StageExecutionBatchDraftKey = producesStageBatchKey
                ? StageExecutionBatchKey(executionBatchDraftKey, operation.StageCode)
                : null,
            StageExecutionBatchQty = producesStageBatchKey ? demand.NetOutputQty : null
        };
    }

    /// <summary>
    /// P1-08修复：资源编码回填（ResourceId → ResourceCode）。
    /// 查不到时返回空串，不抛异常（资源定义缺省时 FinalTaskDraft.ResourceCode 留空，2号位落库兜底）。
    /// resourceId 为 null（非资源工序 Task）时同样返回空串——非资源 Task 无资源编码。
    /// </summary>
    private static string GetResourceCode(int? resourceId, ConstraintContext constraints)
        => resourceId is int id && constraints.ResourceCodes.TryGetValue(id, out var code)
            ? code
            : string.Empty;

    /// <summary>
    /// P0-04修复：获取资源产能系数
    /// 第4轮C1修复：索引加入MaterialId
    /// </summary>
    private decimal? GetCapacityFactor(int materialId, OperationNode operation, int resourceId, ConstraintContext constraints)
    {
        // 0号位 2026-09-29 裁决 §5.3 落实：与 GetEligibleResources 同步升维（键补 ProductionDepartmentId）
        var key = new EligibilityLookupKey(
            materialId, operation.ProductionDepartmentId, operation.RouteCode, operation.PathId, operation.OperationCode);
        if (constraints.ResourceCapacityFactors.TryGetValue(key, out var resourceFactors))
        {
            if (resourceFactors.TryGetValue(resourceId, out var capacityFactor))
            {
                return capacityFactor;
            }
        }
        // 第5轮修复：CapacityFactor查不到时返回null，不静默使用1.0
        return null;
    }

    /// <summary>
    /// 第4轮Merge修复：尝试合并到已有Task，或新排程
    /// 文档§十 10.3：不同Demand可以合并成一个FinalTask，只要Material/Operation/工艺相容、Resource能力允许、交期不被破坏、AllocationTaskShare完整保留
    /// </summary>
    private List<FinalTaskDraft> TryMergeOrSchedule(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        RoutingGraph routingGraph,
        string direction,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        List<FinalTaskDraft> scheduledTasks,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> allocationTaskShare,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        DateTime planningStart,
        DateTime planningEnd,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        out FinalTaskDraft? mergedIntoTask,   // 2026-10-08 第②刀：真替换了既有 Task 时才非 null
        string? batchDraftKey = null,   // P0-01/P0-02：本批归批键（转交 ScheduleDemandOperations）
        bool requireIdentityPreservingMerge = false,   // P1-01：只合并到「未归属执行批」的目标 Task
        Dictionary<int, List<TimeWindow>>? occupancyPristine = null,   // 2026-10-08 COW：透传
        DomainSolveRequest? request = null)   // OWN-P0-02：合批数量上界（Batch Policy Max）判定所需
    {
        // P0-07：连续份额不可被普通 Merge 破坏逐工单身份，直接独立排程，不尝试合并。
        // v1.6 `:26` + 0号位 2026-10-07 裁决 `:246`「`NoSplitMerge` 及固定 Route/Path 应**显式落实**」：
        //   A/B 语义含 `NoSplitMerge=true`（不拆不合）⇒ 此处显式读该冻结字段，
        //   不再只靠 `IsContinuation` 间接覆盖（两者当前同源，但契约字段须显式落实）。
        if (demand.IsContinuation || demand.NoSplitMerge)
        {
            mergedIntoTask = null;   // 连续份额不参与 Merge（P0-07）
            return ScheduleDemandOperations(
                demand, operations, routingGraph, direction, constraints,
                resourceOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap, batchDraftKey, occupancyPristine);
        }

        // ── OWN-P0-02（0号位 2026-10-10《APS_V1_1_20261010.md》§三）：**多 Operation Stage 执行批**合批 ──
        //   复审判词（本号位核对：**成立**）：
        //     · `FindMergeableTasks` 首行 `if (operations.Count != 1) return candidates;` ⇒
        //       「正常Stage多Operation工艺**无法参与完整合批**」；
        //     · 「先形成带 `ExecutionBatchDraftKey` 的 Task，再由 `requireIdentityPreserving` 拒绝已归批 Task
        //       ⇒ 当前常规多批 C 需求**缺真实合批路径**」。
        //   冻结模型（同文 §二.4）：C 桶可在合法条件下**合批**；「同一 Stage 执行批承载 N 个 Operation Task，
        //     多个需求份额各自可追溯」；「若 30 件+20 件合批方案胜出，形成**一个 50 件 Stage 执行批**
        //     及该 Path 上的 N 条 Operation 级 FinalTask…需求 A 的 30 件与 B 的 20 件由**份额账本**追溯」。
        //
        //   实现（= 复审核定的制造/APS 计算顺序「先形成合法候选组合 → 再联合择优 → 最后落定唯一 Stage 执行批身份」）：
        //     ① 找**已落定的完整 Stage 执行批**（同一批键、工序集合与候选 Path 完全一致、同物料/工厂/真实 Path/起点、
        //        未锁定、非 A/B 连续份额、且属**另一条需求**）—— 见 `FindMergeableStageBatches`；
        //     ② **先形成两个候选**：无合批目标 ⇒ 唯一候选 = 独立排程（**一次**运行，与整改前逐字一致）；
        //        有合批目标 ⇒ 「独立排 / 合并排」两候选各在隔离副本上试排；
        //     ③ **联合择优**（复审 §三 第 3 步「再对每个候选联合择优」）：按冻结四层目标
        //        （可行 → 履约 → 交期(受 Direction 控制) → Setup）比较两候选，胜者在**真实上下文**上重跑落定；
        //        完全判平取独立排程 ⇒ 合批必须有可度量的改善才被采用（零回归）。见 `PreferStageMerge`。
        //     ④ 合批候选的做法 = **移出**目标批（Task / 资源占用窗 / 份额 / 产品时间线登记），
        //        再以**锚点需求身份 + 两需求数量之和**重跑 `ScheduleDemandOperations`（⇒ 产出该 Path 上**完整
        //        Operation 链**、同一批键、真实 RouteCode/PathId、真实 Calendar/Direction/Setup）；
        //     ⑤ 交期保护（P0-04 的多工序版）：合批不得把**任一**参与需求的交付推过其有效交期（M-02 反向用例）；
        //        失败 ⇒ 原样恢复目标批（不产生任何半成品状态）。
        //   ⚠ 目标必须是**另一条需求**（`LogicalDemandKey` 不同）：同需求的不同执行批是**拆批**的两半，
        //     把两半再合回会静默撤销拆批、并可能突破该需求 Batch Policy 的 Max ⇒ 不在本次开放范围（如实登记）。
        //   ⚠ 载体用**当前正式生效**的 `ExecutionBatchDraftKey`（1号位 生成、Phase4/Phase5 已消费）；
        //     **AUD-1-002 整改后**：`StageExecutionBatchDraftKey` **已由本号位产出**（`CreateTask` /
        //       `CreateNonResourceTask` / 本方法单工序 Merge 三处，C 桶给值、A/B 留空），
        //       与 `ExecutionBatchDraftKey` **不是同一字段** —— 前者 Stage 内 MES 批身份、后者跨 Stage 链身份。
        //       仍**不新建批次平台、不新增 Core 字段**（复用 2026-10-08 已存在的 Core 载体）。
        if (operations.Count > 1)
        {
            var stageBatches = FindMergeableStageBatches(
                demand, operations, scheduledTasks, constraints, demandByKey, request);

            // ── 无合法合批目标 ⇒ 唯一候选 = 独立排程（**一次**运行；与 OWN-P0-02 之前逐字一致）──
            //   这是常态路径：多工序 C 需求在开 Merge 时不因此多跑一次排程。
            if (stageBatches.Count == 0)
            {
                mergedIntoTask = null;
                return ScheduleDemandOperations(
                    demand, operations, routingGraph, direction, constraints,
                    resourceOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
                    batchDraftKey, occupancyPristine);
            }

            // ── OWN-P0-02（复审 §三 第 2/3 步）：**先形成「独立排 / 合并排」两个候选，再联合择优** ──
            //   复审要求的顺序是「C桶先形成合法 Stage 合批/独立排程候选，再**联合比较**完整 Routing Path、
            //   Resource、Calendar、Direction、Setup、需求交付，选中后形成统一 Stage 执行批」。
            //   ⇒ 合批**不得**因为「合法就先拿下」而绕过择优：一个合法但更差的合批（更晚完成、且不多省 Setup）
            //     会在本层被独立排程淘汰。
            //   做法与 `SelectBestBatchPlan` 同口径：两个候选各在**隔离副本**上试排 → 按冻结四层目标比较 →
            //   胜者在**真实上下文**上重跑落定（排程确定性 ⇒ 与试排同结果）。试排的 TraceNotes 按水位回滚。
            var realTimeline = constraints.ProductTimeline;
            var traceNotesWatermark = constraints.TraceNotes.Count;

            // 候选 A：独立排程（与上方回落路径调用**逐字一致**的入参）。
            constraints.ProductTimeline = realTimeline.Clone();
            var sepOccupancy = CloneOccupancy(resourceOccupancy, out var sepOccupancyPristine);
            var sepTasks = new List<FinalTaskDraft>(scheduledTasks);
            var sepShares = CloneShares(allocationTaskShare);
            var separateProduced = ScheduleDemandOperations(
                demand, operations, routingGraph, direction, constraints,
                sepOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
                batchDraftKey, sepOccupancyPristine);
            var separateEnd = separateProduced.Count == 0
                ? DateTime.MinValue
                : separateProduced.Max(t => t.PlannedEndTime);
            var separateSetup = separateProduced.Sum(t => t.SetupTime);

            // 候选 B：Stage 合批（在**同一初始上下文**的另一套隔离副本上）。
            // ── AUD-1-R01（0号位 2026-10-10《APS_V1_2_20261010.md》§4，RISK_UNVERIFIED）修复：**逐锚点试排** ──
            //   复审判词（本号位核对：**成立**）：旧实现只试 `stageBatches[0]`（`scheduledTasks` 中出现最早的
            //   那个合法合批目标）⇒ 存在**多个合法锚点**（或替代 Path 提供多个同形目标）时，可能漏掉业务更优方案。
            //   修法 = **每个合法锚点各形成一份合批候选**（一锚点一候选，**不枚举锚点组合** —— 与复审
            //   「不自动要求穷举全部组合」一致），全部候选与「独立排程」在**同一冻结四层目标**下逐个比较，
            //   取最优者落定。比较口径与 `SelectBestBatchPlan` / `PreferStageMerge` **同源**，不新增加权目标函数。
            //   每个候选都在**同一初始上下文**上试排：`ProductTimeline` 重新 `Clone()`、`TraceNotes` 回退到水位。
            var bestAnchorIndex = -1;                 // -1 = 独立排程胜出（= 该分支整改前的回落行为）
            FinalTaskDraft? bestMergeRepresentative = null;
            var bestMergeSetup = 0m;

            for (var anchorIndex = 0; anchorIndex < stageBatches.Count; anchorIndex++)
            {
                constraints.ProductTimeline = realTimeline.Clone();
                if (constraints.TraceNotes.Count > traceNotesWatermark)
                {
                    constraints.TraceNotes.RemoveRange(
                        traceNotesWatermark, constraints.TraceNotes.Count - traceNotesWatermark);
                }

                var mrgOccupancy = CloneOccupancy(resourceOccupancy, out var mrgOccupancyPristine);
                var mrgTasks = new List<FinalTaskDraft>(scheduledTasks);
                var mrgShares = CloneShares(allocationTaskShare);
                var anchorRepresentative = TryMergeDemandIntoStageBatch(
                    demand, operations, routingGraph, direction, stageBatches[anchorIndex], constraints,
                    mrgOccupancy, mrgTasks, mrgShares, demandByKey,
                    planningStart, planningEnd, dynamicMaterialFloor, stageOverlap, mrgOccupancyPristine,
                    out var anchorCombinedTasks);
                var anchorSetup = anchorCombinedTasks.Sum(t => t.SetupTime);

                // 当前擂主：首轮 = 独立排程候选；其后 = 已胜出的合批候选（同一冻结四层目标下两两比较）。
                var incumbentFeasible = bestAnchorIndex >= 0
                    ? bestMergeRepresentative is not null
                    : separateProduced.Count > 0;
                var incumbentEnd = bestAnchorIndex >= 0
                    ? bestMergeRepresentative?.PlannedEndTime ?? DateTime.MinValue
                    : separateEnd;
                var incumbentSetup = bestAnchorIndex >= 0 ? bestMergeSetup : separateSetup;

                if (PreferStageMerge(
                        separateFeasible: incumbentFeasible,
                        separateEnd: incumbentEnd,
                        separateSetup: incumbentSetup,
                        mergeFeasible: anchorRepresentative is not null,
                        mergeEnd: anchorRepresentative?.PlannedEndTime ?? DateTime.MinValue,
                        mergeSetup: anchorSetup,
                        demand, constraints, direction))
                {
                    bestAnchorIndex = anchorIndex;
                    bestMergeRepresentative = anchorRepresentative;
                    bestMergeSetup = anchorSetup;
                }
            }

            // 试排回滚：还原真实时间线 + 按水位清掉**全部**试排的 Setup 追踪（落定重跑会重写恰好一份）。
            constraints.ProductTimeline = realTimeline;
            if (constraints.TraceNotes.Count > traceNotesWatermark)
            {
                constraints.TraceNotes.RemoveRange(
                    traceNotesWatermark, constraints.TraceNotes.Count - traceNotesWatermark);
            }

            // 落定：胜者在**真实上下文**上重跑（独立排程与试排同一调用；合批走真实表 + 自身快照）。
            if (bestAnchorIndex >= 0)
            {
                var landed = TryMergeDemandIntoStageBatch(
                    demand, operations, routingGraph, direction, stageBatches[bestAnchorIndex], constraints,
                    resourceOccupancy, scheduledTasks, allocationTaskShare, demandByKey,
                    planningStart, planningEnd, dynamicMaterialFloor, stageOverlap, occupancyPristine,
                    out _);

                if (landed is not null)
                {
                    mergedIntoTask = landed;
                    return new List<FinalTaskDraft>();   // 已并入 Stage 执行批：不产新批的独立 Task
                }

                // 理论不可达（试排已成功且初始上下文相同）⇒ 保守回落独立排程，不静默丢需求。
            }

            mergedIntoTask = null;
            return ScheduleDemandOperations(
                demand, operations, routingGraph, direction, constraints,
                resourceOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
                batchDraftKey, occupancyPristine);
        }

        // 检测是否可以合并到已有Task
        var candidateTasks = FindMergeableTasks(
            demand, operations, scheduledTasks, constraints, demandByKey, requireIdentityPreservingMerge);

        if (candidateTasks.Count > 0)
        {
            // 尝试将Demand合并到第一个候选Task
            var targetTask = candidateTasks[0];

            // 检查合并后是否破坏交期：合并后Duration增加，End时间延后
            var mergedTask = TryMergeDemandIntoTask(demand, targetTask, routingGraph, constraints, resourceOccupancy, allocationTaskShare, planningEnd, demandByKey, batchDraftKey, occupancyPristine);
            if (mergedTask != null)
            {
                // 合并成功：替换scheduledTasks中的旧Task
                var index = scheduledTasks.FindIndex(t => t.FinalDraftId == targetTask.FinalDraftId);
                if (index >= 0)
                {
                    var endChanged = mergedTask.PlannedEndTime != targetTask.PlannedEndTime;
                    scheduledTasks[index] = mergedTask;
                    // 与旧判据**逐字等价**：旧实现用「试排前后 PlannedEndTime 是否变化」推断合并，
                    // 故此处也只在**结束时间真的变了**时才上报 —— 收紧语义增量，保证零回归。
                    mergedIntoTask = endChanged ? mergedTask : null;
                }
                else
                {
                    mergedIntoTask = null;   // 目标不在任务表（理论不可达，保守按未合并处理）
                }
                // 不生成新Task
                return new List<FinalTaskDraft>();
            }
        }

        // 无法合并：正常排程
        mergedIntoTask = null;
        return ScheduleDemandOperations(
            demand,
            operations,
            routingGraph,
            direction,
            constraints,
            resourceOccupancy,
            planningStart,
            planningEnd,
            dynamicMaterialFloor,
            stageOverlap,
            batchDraftKey,
            occupancyPristine);
    }

    /// <summary>
    /// OWN-P0-02（复审 §三 第 3 步「再对每个候选**联合择优**」）：在「独立排程」与「Stage 合批」两个候选之间裁决。
    ///
    /// 比较顺序与 <see cref="SelectBestRoutingCandidate"/> / <see cref="CompareBatchPlans"/> **逐层同源**
    /// （冻结四层目标，不新增加权目标函数）：
    ///   ① 可行优于不可行（一方排不下 ⇒ 另一方胜）；
    ///   ② 履约：延期短者优先（延期 = 相对**有效交期**的真实分钟数，延期是正式排程结果）；
    ///   ③ 交期：两方均按期且完成时间不同 ⇒ 受 **Direction** 控制 ——
    ///        FORWARD / MIXED → 更早可行完成优先；BACKWARD → 更靠近 Due（完成更晚者）优先；
    ///   ④ 次级：Setup 总量小者优先（合批省换型的业务收益在此层体现）；
    ///   ⑤ 完全判平 ⇒ 取**独立排程**（= OWN-P0-02 之前的既有行为 ⇒ 零回归；合批必须有可度量的改善才被采用）。
    ///
    /// ⚠ 两个候选同属一条需求、同一 Routing 候选、同一 Direction ⇒ 第 ③ 层不存在「两方方向不同不可比」的情形
    ///   （该判平条件只出现在跨 Routing 候选比较里）。
    ///
    /// AUD-1-R01（2026-10-10）复用：本方法对入参只做「A 是否劣于 B」的**两两比较**，与 A/B 各自是谁无关
    ///   ⇒ 同一套冻结四层目标被直接复用于「**当前擂主 vs 下一个合法锚点候选**」的逐个擂台比较
    ///   （首轮擂主 = 独立排程候选）。判平（⑤）取「不换擂主」= 保守，语义与「判平取独立排程」一致。
    /// </summary>
    private static bool PreferStageMerge(
        bool separateFeasible,
        DateTime separateEnd,
        decimal separateSetup,
        bool mergeFeasible,
        DateTime mergeEnd,
        decimal mergeSetup,
        LogicalProductionDemand demand,
        ConstraintContext constraints,
        string direction)
    {
        // ① 可行优于不可行
        if (separateFeasible != mergeFeasible)
        {
            return mergeFeasible;
        }

        var due = constraints.EffectiveDue(demand);

        // ② 履约：延期短者优先
        var delaySeparate = DelayMinutes(separateEnd, due);
        var delayMerge = DelayMinutes(mergeEnd, due);
        if (delaySeparate != delayMerge)
        {
            return delayMerge < delaySeparate;
        }

        // ③ 交期：均按期 ⇒ 受 Direction 控制
        if (delaySeparate == 0 && separateEnd != mergeEnd)
        {
            if (string.Equals(direction, SchedulingDirectionResolver.Backward, StringComparison.Ordinal))
            {
                return mergeEnd > separateEnd;   // 倒排：不延期前提下更靠近 Due
            }

            return mergeEnd < separateEnd;       // FORWARD / MIXED：更早可行完成优先
        }

        // ④ 次级：Setup 总量小者优先
        if (separateSetup != mergeSetup)
        {
            return mergeSetup < separateSetup;
        }

        // ⑤ 判平 ⇒ 保守取独立排程
        return false;
    }

    /// <summary>
    /// 第4轮Merge修复：查找可合并的已有Task
    /// 条件：Material/Operation/工艺相容、Resource相同、时间窗口邻近
    /// 第5轮修复：多工序Demand必须检查完整Routing兼容性
    /// </summary>
    private List<FinalTaskDraft> FindMergeableTasks(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        List<FinalTaskDraft> scheduledTasks,
        ConstraintContext constraints,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        bool requireIdentityPreserving = false)
    {
        var candidates = new List<FinalTaskDraft>();

        // 第5轮修复：多工序场景不能Merge到单一Task
        // Merge只适用于单工序Demand，多工序必须独立排程保证DAG完整性
        if (operations.Count != 1)
        {
            return candidates; // 多工序不支持Merge
        }

        var targetOp = operations[0];

        // 遍历已排程的Task，找同Material、同Operation的Task
        foreach (var task in scheduledTasks)
        {
            // ── P1-01（0号位 2026-10-08 §八）：多批需求下 Merge 必须**保持执行批身份** ──
            //   目标 Task 若已归属某个执行批，合并后只能沿用**目标**批键 ⇒ 本批身份丢失
            //   ⇒ 只接受「尚未归属任何执行批」的目标（合并后采用本批键，见 TryMergeDemandIntoTask）。
            if (requireIdentityPreserving && task.ExecutionBatchDraftKey is not null)
            {
                continue;
            }

            // ── P0-01（0号位 2026-10-09《APS_V1_2_20261009.md》§三）：**既成事实锁定 Task 不得作为 Merge 目标** ──
            //   锁定 Task（Execution / Firm / Frozen / Manual）是**原地继承的硬锚点**，其 PlannedEndTime
            //   与数量不得被任何候选合批延长 / 改写。此前本方法只按 Material/Stage/Operation/Route/Path/身份
            //   筛选，**不检查目标是否锁定** ⇒ 候选评分、路径择优乃至最终域失败都可能被污染。
            //   虽然 Phase5 事后核查锁定时间，但**不能把「事后发现」当作「允许进入候选」的理由**（复审判词）。
            //   判据 = (SourceDraftId, StageCode, OperationCode) 命中 `LockedTasks`（P0-02：键域含 StageCode）。
            if (constraints.LockedTasks.ContainsKey(
                    (task.SourceDraftId, task.StageCode ?? string.Empty, task.OperationCode ?? string.Empty)))
            {
                continue;
            }

            // Material必须相同
            if (task.MaterialId != demand.MaterialId) continue;

            // FactoryId必须相同
            if (task.FactoryId != demand.FactoryId) continue;

            // Stage和Operation必须完全匹配
            if (task.StageCode != (targetOp.StageCode ?? string.Empty)) continue;
            if (task.OperationCode != targetOp.OperationCode) continue;

            // ── P0-06（0号位 2026-10-07 (5).md）：**跨 Path 不得 Merge** ──
            //   评估 Routing A 时若把既有 Routing B 的 Task 当成合并目标，会保留 target 的
            //   (RouteCode, PathId, ExecutionBatchDraftKey) ⇒ 同一 Execution Batch 里混入另一条 Path 的
            //   节点与资源，违反「每个 Execution Batch 只允许一条完整 Path」。
            //   两条 Path 存在**同名工序码**时（OP10 在 A、B 各一份）本检查是唯一拦截点。
            if (!string.Equals(task.RouteCode ?? string.Empty, targetOp.RouteCode ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }
            if (task.PathId != targetOp.PathId)
            {
                continue;
            }

            // S29：执行起点一致性校验——不同执行起点不得合批，避免已完成工序被重新排产
            // 反查候选Task的来源Demand，比较 StartStageCode / StartOperationCode
            if (!demandByKey.TryGetValue(task.SourceDraftId, out var sourceDemand))
            {
                continue; // 无法追溯来源 demand，保守不 merge
            }
            // P0-07：连续份额任务不能被普通 Merge 吸收，自由份额也不得并入连续份额任务（保逐工单身份）。
            // NoSplitMerge（不拆不合）显式落实：任一侧带该标记都不得合批。
            if (sourceDemand.IsContinuation || demand.IsContinuation
                || sourceDemand.NoSplitMerge || demand.NoSplitMerge)
            {
                continue;
            }
            if (!string.Equals(demand.StartStageCode, sourceDemand.StartStageCode, StringComparison.Ordinal))
            {
                continue; // 不同 StartStage，不 merge
            }
            if (!string.Equals(demand.StartOperationCode, sourceDemand.StartOperationCode, StringComparison.Ordinal))
            {
                continue; // 不同 StartOperation，不 merge
            }

            candidates.Add(task);
        }

        return candidates;
    }

    /// <summary>
    /// OWN-P0-02（0号位 2026-10-10《APS_V1_1_20261010.md》§三）：查找可合并的**完整 Stage 执行批**。
    ///
    /// 与 <see cref="FindMergeableTasks"/>（单 Operation 目标 Task）**并列**，不替代它：
    ///   · `operations.Count == 1` ⇒ 走既有单工序 Merge（零回归）；
    ///   · `operations.Count &gt; 1` ⇒ 走本方法（多 Operation Stage 执行批）。
    ///
    /// 合批目标 = **同一个 `ExecutionBatchDraftKey` 下、工序集合与候选 Path 完全一致的完整任务链**。
    /// 筛选口径（与单工序 Merge 逐条对齐，仅把「单个 Task」升维为「整批」）：
    ///   ① 工序集合**完全一致**（数量 + 身份 `(StageCode, OperationCode)`）—— 保证「同 Stage 执行批」语义；
    ///   ② 同 `MaterialId` / `FactoryId`；
    ///   ③ 每个 Task 的 `(RouteCode, PathId)` 必须等于候选 Path 上该工序的真实身份 ⇒ **同一执行批只用一条完整 Path**，
    ///      跨 Path（含两条 Path 同名工序码）在此唯一拦截；
    ///   ④ 批内任一 Task 命中 `LockedTasks` ⇒ 整批排除（既成事实锚点不得被合批改写，M-04）；
    ///   ⑤ 源需求可追溯，且**不是** A/B 连续份额 / `NoSplitMerge`（不拆不合），**且与本需求不是同一条需求**（见上）；
    ///   ⑥ `StartStageCode` / `StartOperationCode` 一致（执行起点兼容）；
    ///   ⑦ 批内数量一致（构造不变式），且「两需求合批后数量」不突破**锚点需求**的 Batch Policy `Max`（M-01 前提之一）。
    /// </summary>
    private List<List<FinalTaskDraft>> FindMergeableStageBatches(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        List<FinalTaskDraft> scheduledTasks,
        ConstraintContext constraints,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        DomainSolveRequest? request)
    {
        var candidates = new List<List<FinalTaskDraft>>();

        // 候选 Path 的工序身份 → 该工序真实路径身份（供逐 Task 校验 Route/Path）。
        var opByKey = new Dictionary<OperationNodeKey, OperationNode>();
        foreach (var op in operations)
        {
            opByKey[OperationNodeKey.Of(op.StageCode, op.OperationCode)] = op;
        }

        // 按执行批键分区：批键为 null 的 Task 不构成执行批 ⇒ 不能作为 Stage 合批目标。
        var groups = new Dictionary<string, List<FinalTaskDraft>>(StringComparer.Ordinal);
        foreach (var task in scheduledTasks)
        {
            if (task.ExecutionBatchDraftKey is not string key) continue;
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<FinalTaskDraft>();
                groups[key] = list;
            }
            list.Add(task);
        }

        foreach (var group in groups.Values)
        {
            // ① 工序集合完全一致（数量 + 身份）。
            if (group.Count != operations.Count) continue;
            var groupOps = new HashSet<OperationNodeKey>();
            foreach (var t in group)
            {
                groupOps.Add(OperationNodeKey.Of(t.StageCode, t.OperationCode));
            }
            if (!groupOps.SetEquals(opByKey.Keys)) continue;

            var anchorTask = group[0];
            var sourceKey = anchorTask.SourceDraftId;

            // ② 同物料 / 同工厂。
            if (anchorTask.MaterialId != demand.MaterialId) continue;
            if (anchorTask.FactoryId != demand.FactoryId) continue;

            var matched = true;
            foreach (var t in group)
            {
                // 批内来源必须一致（一条执行批 = 一条需求的份额组合）。
                if (!string.Equals(t.SourceDraftId, sourceKey, StringComparison.Ordinal)) { matched = false; break; }

                // ⑦ 批内数量一致（构造不变式；不一致 ⇒ 不是可合批的完整批）。
                //
                // ── AUD-1-R02（0号位 2026-10-10《APS_V1_2_20261010.md》§4，RISK_UNVERIFIED）**核对结论：V1 不过度拒绝** ──
                //   复审疑点：「Stage 内不同 Operation 的 `PlannedProcessQty` 若真实不同，本处要求批内完全相等，
                //     可能错拒合法批」（实施包 v1.7 `:44`：「各 Operation 的实际剩余/预计加工量可因报工、良率等不同」）。
                //
                //   实测/源码核对（两条）：
                //     ① **1号位 产出的同批 Task 逐工序恒等** —— `CreateTask` / `CreateNonResourceTask` 一律取
                //        `PlannedProcessQty = demand.PlannedProcessQty`、`Quantity = demand.NetOutputQty`；
                //        `TryMergeDemandIntoStageBatch` 重跑完整链 ⇒ 同样均匀。⇒ 由 `FormExecutionBatches` 形成的
                //        批**构造上均匀**，本判据**不可能**拒绝它（对 V1 自身数据是恒真守卫）。
                //     ② 唯一可造成批内不均的路径 = **单工序 Merge 并入已归批目标**（`TryMergeDemandIntoTask`
                //        改写 `Quantity`/`PlannedProcessQty`；`requireIdentityPreserving = multiBatch` ⇒ 单批需求
                //        可命中）。此时本判据把该批**保守排除**为合批目标 —— **不丢需求**（该需求仍独立排程），
                //        也**不改写**既有逐工序量。⇒ 不是「错拒」，是「拒绝改写无法表达的逐工序差异」。
                //
                //   ⇒ **本轮不放宽**：放宽会把既有逐工序量**静默同质化**（重跑只带一个 `PlannedProcessQty`），
                //     比保守不合批更坏。逐工序量要真实保留，需**逐工序数量载体**（1↔2 契约缺口，与 AUD-1-D04 同族）。
                //     已登记为契约项，**不降目标**（不把「放宽后能合批」当作达标）。
                //
                // ── 2026-10-10 V1_3 F-05 更正（0号位《APS_V1_3_20261010.md》§3）──
                //   复审判词：「保留真实逐工序量而不是凭本地等量构造推定本业务可验收。」
                //   ⚠ **上段①不得被读作「本业务可验收」的证据**：
                //     「1号位 产出的批构造上均匀 ⇒ 本判据对 V1 自身数据恒真」是**关于我们本地产物的陈述**，
                //     **不是**「实施包 v1.7 :44『各 Operation 的实际剩余/预计加工量可因报工、良率等不同』
                //     这一业务事实已被满足」的证明。把前者当后者 = 以**本地等量构造**推定**业务可验收**（F-05 所指）。
                //   ⚠ 真实逐工序量在本设计下**无处承载**：`ExecutionBatchDraft` 只有**一个**
                //     `PlannedProcessQty`（需求级），逐工序差异**没有载体** ⇒ 一旦上游喂入真实差异量，
                //     本判据会把**合法批**保守排除 —— 这是**能力缺口**，不是「已通过」。
                //   ⇒ 冻结要求**继续有效**（不降目标、不注销）：逐工序真实量的载体归 **1↔2 契约**
                //     （2号位 装载 / 0号位 若需改契约则改契约），1号位 侧**不宣称**本项业务已达标。
                if (t.Quantity != anchorTask.Quantity || t.PlannedProcessQty != anchorTask.PlannedProcessQty)
                {
                    matched = false; break;
                }

                var opKey = OperationNodeKey.Of(t.StageCode, t.OperationCode);
                if (!opByKey.TryGetValue(opKey, out var op)) { matched = false; break; }

                // ③ 真实完整 Path 一致（RouteCode + PathId）。
                if (!string.Equals(t.RouteCode ?? string.Empty, op.RouteCode ?? string.Empty, StringComparison.Ordinal))
                {
                    matched = false; break;
                }
                if (t.PathId != op.PathId) { matched = false; break; }

                // ④ 锁定 Task（Execution / Firm / Frozen / Manual）⇒ 整批排除。
                if (constraints.LockedTasks.ContainsKey(
                        (t.SourceDraftId, t.StageCode ?? string.Empty, t.OperationCode ?? string.Empty)))
                {
                    matched = false; break;
                }
            }
            if (!matched) continue;

            // ⑤ 源需求可追溯 + 非 A/B 连续份额 + **另一条需求**。
            if (!demandByKey.TryGetValue(sourceKey, out var anchorDemand)) continue;
            if (anchorDemand.IsContinuation || anchorDemand.NoSplitMerge) continue;
            if (string.Equals(anchorDemand.LogicalDemandKey, demand.LogicalDemandKey, StringComparison.Ordinal)) continue;

            // ⑥ 执行起点兼容。
            if (!string.Equals(demand.StartStageCode, anchorDemand.StartStageCode, StringComparison.Ordinal)) continue;
            if (!string.Equals(demand.StartOperationCode, anchorDemand.StartOperationCode, StringComparison.Ordinal)) continue;

            // ⑦ 合批后数量不得突破锚点需求 Batch Policy 的 Max（Min 由两需求各自的既有合法性覆盖）。
            var combinedQty = anchorTask.Quantity + demand.NetOutputQty;
            if (request is not null)
            {
                var anchorPolicy = ResolveExecutionBatchPolicy(anchorDemand, request, constraints);
                if (anchorPolicy?.MaxExecutionBatchQty is decimal maxQty && combinedQty > maxQty) continue;
            }

            candidates.Add(group);
        }

        return candidates;
    }

    /// <summary>
    /// 第4轮Merge修复：尝试将Demand合并到已有Task
    /// 检查Resource能力是否允许、交期是否被破坏
    /// 返回合并后的新Task，失败时返回null
    /// </summary>
    private FinalTaskDraft? TryMergeDemandIntoTask(
        LogicalProductionDemand demand,
        FinalTaskDraft targetTask,
        RoutingGraph routingGraph,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> allocationTaskShare,
        DateTime planningEnd,
        Dictionary<string, LogicalProductionDemand>? demandByKey = null,
        string? batchDraftKey = null,   // P1-01：目标 Task 未归批时，合并结果**采用本批键**（批身份不丢）
        Dictionary<int, List<TimeWindow>>? occupancyPristine = null)   // 2026-10-08 COW：写占用表前脱钩
    {
        // 计算合并后的总数量
        var mergedQty = targetTask.PlannedProcessQty + demand.PlannedProcessQty;

        // 获取Operation信息
        // 0号位 2026-09-29 裁决 §5.3：节点身份 = (StageCode, OperationCode)。
        // FinalTaskDraft 侧两字段均已在建 Task 时落值（:1352/:1393 `StageCode = operation.StageCode ?? string.Empty`），
        // 与 OperationNodeKey.Of 的 null→Empty 归一化一致，可安全回查。
        if (!routingGraph.Operations.TryGetValue(
                OperationNodeKey.Of(targetTask.StageCode, targetTask.OperationCode), out var operation))
        {
            return null; // Operation不存在，无法合并
        }

        // 非资源工序 Task（OperationPlanningMode=UNCONSTRAINED/WAIT_ONLY，ResourceId=null，0号位 2026-09-22 裁决）
        // 不占资源、无产能系数可查，无法参与「基于资源占用窗」的合并 → 直接放弃合并，语义上等价于原来就匹配不到资源窗。
        if (targetTask.ResourceId is not int targetResourceId)
        {
            return null;
        }

        // 计算合并后的Duration
        // 第5轮修复：CapacityFactor缺失或非法时不能继续
        var capacityFactor = GetCapacityFactor(demand.MaterialId, operation, targetResourceId, constraints);
        if (capacityFactor == null || capacityFactor <= 0)
        {
            return null; // CapacityFactor缺失/非法，无法计算合并后Duration
        }
        var mergedDuration = operation.StandardDuration * mergedQty / capacityFactor.Value;
        var newDuration = TimeSpan.FromMinutes((double)mergedDuration);

        // 计算新的结束时间
        var newEndTime = targetTask.PlannedStartTime + newDuration;

        // ── OWN-P0-01（0号位 2026-10-10《APS_V1_1_20261010.md》§三）：**取消 `planningEnd` 作为合批硬上界** ──
        //   复审判词（本号位核对：**成立**）：滚动 90 天是**需求进入本轮求解的范围**，
        //   **不是资源时间终点**（0号位 2026-09-12 裁决；同一 Solver 的 `FindForwardSlot` 已按此实现 ——
        //   `:2430-2432`「PlanningEnd 不是硬上界，正排只受资源日历窗约束」）。
        //   旧实现此处 `if (newEndTime > planningEnd) return null;` 会在**合批可行性判断**阶段，
        //   拒绝一个在真实维护日历上合法、只是落在 90 天之后的合批 ⇒ 与同一 Solver 的时间口径直接冲突
        //   （复审原话：「不是优化偏好，而是错误的可行性判断」）。
        //   现改为：用**真实资源日历窗**判定延长后的占用是否仍有合法承载。
        //   · 占用起点含 Setup（与 `AddOccupancyWindow` / `FindForwardSlot` 同口径）；
        //   · 语义与 `FindForwardSlot` 一致：**单个 Task 必须整体落在某一个日历窗内**（不得跨窗跨越非工作时间）；
        //   · 资源占用冲突由下方既有检查（`:3478-3495`）负责；
        //   · 交期只作**择优目标**（见下方 P0-04 段），**不作硬截止** —— 延期是正式排程结果。
        //   `planningEnd` 参数保留仅为签名一致（与 `FindForwardSlot` 同一约定），不再作为末期硬边界。
        var mergedOccStart = targetTask.PlannedStartTime - TimeSpan.FromMinutes((double)targetTask.SetupTime);
        if (!IsWithinCalendar(new TimeWindow(mergedOccStart, newEndTime), targetResourceId, constraints))
        {
            return null; // 延长后超出**真实资源日历窗**（无合法承载），无法合并
        }

        // ── P0-04（0号位 2026-10-07 (5).md）：Merge 必须校验**交期不被破坏** ──
        //   旧实现只查 PlanningEnd + 资源后续冲突，**未查新 Demand 的有效交期，也未查被合并 Task
        //   已有份额的交期是否因延长而恶化**。合批会延长 Task 的 PlannedEndTime ⇒ 必须验证：
        //   ① 本 Demand：newEndTime 不得越过 `RequiredAvailableTime`（有效交期）；
        //   ② target 已服务的**全部既有份额**（血缘在 allocationTaskShare，另加 SourceDraftId 兜底）：
        //      目标 Task 原完成时间已合规，但延长后若越过任一既有份额的交期 ⇒ 该份额被推违约 ⇒ 放弃合并。
        if (demand.RequiredAvailableTime != DateTime.MaxValue && newEndTime > demand.RequiredAvailableTime)
        {
            return null;
        }

        if (demandByKey is not null)
        {
            var targetDue = DateTime.MaxValue;
            void WidenDue(string demandKey)
            {
                if (demandByKey.TryGetValue(demandKey, out var d) && d.RequiredAvailableTime < targetDue)
                {
                    targetDue = d.RequiredAvailableTime;
                }
            }

            WidenDue(targetTask.SourceDraftId);
            if (allocationTaskShare.TryGetValue(targetTask.FinalDraftId, out var existingShares))
            {
                foreach (var (shareDemandKey, _) in existingShares)
                {
                    WidenDue(shareDemandKey);
                }
            }

            if (targetDue != DateTime.MaxValue && newEndTime > targetDue)
            {
                return null; // 延长后使既有份额交期恶化，无法合并
            }
        }

        // 第5轮修复：检查延长Task后是否与同资源的后续Task冲突
        // item1 接线（阶段二）顺手修复：自身占用窗起点 = PlannedStartTime - SetupTime。
        // 原比较用 PlannedStartTime 直接比 window.Start，漏了 Setup 偏移——规则 Setup>0 时自身窗
        // 永远匹配不上、被当成"其它Task冲突"，Merge 必被误拒。
        var targetOccStart = targetTask.PlannedStartTime - TimeSpan.FromMinutes((double)targetTask.SetupTime);
        if (resourceOccupancy.ContainsKey(targetResourceId))
        {
            var occupancies = resourceOccupancy[targetResourceId];
            foreach (var window in occupancies)
            {
                // 跳过当前Task自己的占用窗口
                if (window.Start == targetOccStart && window.End == targetTask.PlannedEndTime)
                {
                    continue;
                }

                // 检查延长后的结束时间是否侵入其他占用窗口
                if (newEndTime > window.Start && targetTask.PlannedStartTime < window.End)
                {
                    return null; // 延长后与资源上其他Task冲突，无法合并
                }
            }
        }

        // 创建合并后的新Task（因为FinalTaskDraft属性是init-only，不能修改已有对象）
        // AUD-1-002：执行批身份（跨 Stage 工序链）与 **Stage 执行批身份**（Stage 内 MES 批）分开 —— 见 StageExecutionBatchKey 注释。
        //   本条路径只处理**同一 Stage、同一 Operation** 的 Task 合并（`FindMergeableTasks` 按 StageCode+OperationCode 匹配），
        //   故 Stage 身份沿用目标即可；目标无 Stage 键（历史/未归组）时按合并后执行批键 + 本 Stage 派生。
        var mergedExecutionBatchKey = targetTask.ExecutionBatchDraftKey ?? batchDraftKey;
        var mergedStageBatchKey = targetTask.StageExecutionBatchDraftKey
            ?? (mergedExecutionBatchKey is not null && ProducesStageExecutionBatchKey(demand)
                ? StageExecutionBatchKey(mergedExecutionBatchKey, targetTask.StageCode)
                : null);

        var mergedTask = new FinalTaskDraft
        {
            FinalDraftId = targetTask.FinalDraftId, // 保持相同的DraftId
            SourceDraftId = targetTask.SourceDraftId,
            MaterialId = targetTask.MaterialId,
            FactoryId = targetTask.FactoryId,
            StageCode = targetTask.StageCode,
            OperationCode = targetTask.OperationCode,
            TaskType = targetTask.TaskType,
            ResourceId = targetTask.ResourceId,
            ResourceCode = targetTask.ResourceCode,
            RouteCode = targetTask.RouteCode,
            PathId = targetTask.PathId,
            Quantity = targetTask.Quantity + demand.NetOutputQty, // 合并数量
            PlannedProcessQty = mergedQty, // 合并加工数量
            UOM = targetTask.UOM,
            PlannedStartTime = targetTask.PlannedStartTime,
            PlannedEndTime = newEndTime, // 新的结束时间
            SetupTime = targetTask.SetupTime,
            SetupSource = targetTask.SetupSource,   // SetupSource 填充：合并保留 target 来源（2号位 原样落库）
            Priority = targetTask.Priority,
            IsVirtual = targetTask.IsVirtual,
            // 合批：保留 target 批身份（同 Key ⇒ 同 Path 不变量天然成立 —— RouteCode/PathId 亦取自 target）。
            // 被吸收 Demand 的血缘不丢：见下方 allocationTaskShare 追加记录。
            // Merge 仅适用于非连续份额（FindMergeableTasks 已排除 IsContinuation）⇒ ContinuationKey 恒 null。
            ContinuationKey = targetTask.ContinuationKey,
            // P1-01：目标未归批 ⇒ 合并结果采用**本批键**（执行批身份不丢）；目标已归批 ⇒ 沿用目标键
            //   （`FindMergeableTasks` 在 requireIdentityPreserving 下已把「已归批目标」挡掉）。
            ExecutionBatchDraftKey = mergedExecutionBatchKey,
            // AUD-1-002：Stage 内 MES 执行批身份（与上面的跨 Stage 链键**不是同一字段**）。
            StageExecutionBatchDraftKey = mergedStageBatchKey,
            StageExecutionBatchQty = mergedStageBatchKey is null
                ? null
                : targetTask.Quantity + demand.NetOutputQty
        };

        // 找到targetTask在scheduledTasks中的索引，替换为mergedTask
        // 注意：这里需要外部传入scheduledTasks的引用并支持修改
        // 简化处理：直接修改字段（需要调整方法签名）

        // 第6轮Merge修复：记录AllocationTaskShare时保留原Task的历史Demand
        // 原Task可能已经服务于其他Demand，不能覆盖
        if (!allocationTaskShare.ContainsKey(mergedTask.FinalDraftId))
        {
            allocationTaskShare[mergedTask.FinalDraftId] = new List<(string, decimal)>();
        }
        // 追加当前Demand的份额（原Task的份额已在之前记录）
        allocationTaskShare[mergedTask.FinalDraftId].Add((demand.LogicalDemandKey, demand.NetOutputQty));

        // 第6轮Merge修复：更新资源占用，包含Setup时间
        if (resourceOccupancy.ContainsKey(targetResourceId))
        {
            // 移除旧的时间窗（item1 接线：按含 Setup 的占用起点匹配）
            var oldWindows = resourceOccupancy[targetResourceId]
                .Where(w => w.Start == targetOccStart && w.End == targetTask.PlannedEndTime)
                .ToList();

            foreach (var oldWindow in oldWindows)
            {
                // 2026-10-08 COW：Merge 会改写目标资源的占用表 ⇒ 先脱钩再改
                EnsureOwned(resourceOccupancy, occupancyPristine, targetResourceId);
                resourceOccupancy[targetResourceId].Remove(oldWindow);
            }

            // 添加新的时间窗：Setup时间也占用资源
            var setupDuration = TimeSpan.FromMinutes((double)mergedTask.SetupTime);
            var resourceStart = mergedTask.PlannedStartTime - setupDuration;
            AddOccupancyWindow(resourceOccupancy, occupancyPristine, targetResourceId,
                new TimeWindow(resourceStart, newEndTime));
        }

        // item1 接线（阶段二）：产品时间线同步——Merge 是同物料合并（v1.2 §六 同产品语义，Setup 继承不变），
        // 仅占用末端延长：移除旧末端、登记新末端。
        constraints.ProductTimeline.Remove(targetResourceId, targetTask.PlannedEndTime, targetTask.MaterialId);
        constraints.ProductTimeline.Place(targetResourceId, newEndTime, targetTask.MaterialId);

        return mergedTask; // 合并成功，返回合并后的Task
    }

    /// <summary>
    /// OWN-P0-02（0号位 2026-10-10《APS_V1_1_20261010.md》§三）：把本需求并入一个**已落定的完整 Stage 执行批**。
    ///
    /// 做法 = **移出目标批 → 以「锚点需求身份 + 两需求数量之和」重跑完整排程 → 成功即落定，失败即原样恢复**。
    /// 为什么不是「就地延长目标批各工序」：复审 §三 要求的顺序是「C桶先形成合法 Stage 合批/独立排程候选，
    ///   再**联合比较**完整 Routing Path、Resource、Calendar、Direction、Setup、需求交付，选中后形成统一
    ///   Stage 执行批与**完整 Operation Task 链**」——只有重跑完整排程才能让合批候选与独立候选在同一套
    ///   硬约束（DAG 依赖、日历窗、Setup 规则、方向）下被真实评价；逐工序延长无法处理上游延长挤压下游。
    ///
    /// 锚点侧保留的**全部身份**：批键（`ExecutionBatchDraftKey`）、`SourceDraftId`、真实 `RouteCode/PathId`、
    ///   `ContinuationKey`；被吸收需求的份额进入 `allocationTaskShare`（Phase5 `mergeLineage` 消费），
    ///   ⇒ 「同一 Stage 执行批承载 N 个 Operation Task，多个需求份额各自可追溯」。
    ///
    /// ⚠ 合批**不得**为省 Setup 延误更紧急一方（M-02）：落定前按**两需求各自有效交期**双重把关（P0-04 口径的多工序版）。
    /// </summary>
    /// <returns>合批后的代表 Task（完成时间最晚者，供候选比较取真实完成时间）；失败返回 null（已恢复目标批）。</returns>
    private FinalTaskDraft? TryMergeDemandIntoStageBatch(
        LogicalProductionDemand demand,
        List<OperationNode> operations,
        RoutingGraph routingGraph,
        string direction,
        List<FinalTaskDraft> targetBatch,
        ConstraintContext constraints,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        List<FinalTaskDraft> scheduledTasks,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> allocationTaskShare,
        Dictionary<string, LogicalProductionDemand> demandByKey,
        DateTime planningStart,
        DateTime planningEnd,
        DateTime dynamicMaterialFloor,
        StageOverlapParams stageOverlap,
        Dictionary<int, List<TimeWindow>>? occupancyPristine,
        out List<FinalTaskDraft> combinedTasks)   // OWN-P0-02：合批产出的**完整 Operation 链**（供候选比较取 Setup 总量）
    {
        combinedTasks = new List<FinalTaskDraft>();

        if (targetBatch.Count == 0) return null;
        var targetKey = targetBatch[0].ExecutionBatchDraftKey;
        if (targetKey is null) return null;   // 无批键 ⇒ 不构成执行批（FindMergeableStageBatches 已挡，防御）

        var anchorKey = targetBatch[0].SourceDraftId;
        if (!demandByKey.TryGetValue(anchorKey, out var anchorDemand)) return null;

        // 合批数量：锚点批**当前累计**数量（可能已含此前合批的份额）+ 本需求本批数量。
        var combinedNet = targetBatch[0].Quantity + demand.NetOutputQty;
        var combinedProc = targetBatch[0].PlannedProcessQty + demand.PlannedProcessQty;

        // 合并需求 = 锚点需求身份 + 两需求数量之和（其余字段逐字拷贝；`RequiredAvailableTime` 保留锚点值，
        //   非锚点一侧的交期保护由下方双重把关负责）。
        var combinedDemand = CloneDemandWithBatchQty(anchorDemand, combinedNet, combinedProc);

        // ── 快照（用于失败回滚；试排阶段作用于克隆表，落定阶段作用于真实表，两处语义一致）──
        var ids = new HashSet<string>(targetBatch.Select(t => t.FinalDraftId), StringComparer.Ordinal);
        var removedTasks = new List<(int Index, FinalTaskDraft Task)>();
        var removedWindows = new List<(int ResourceId, TimeWindow Window)>();
        var removedTimeline = new List<(int ResourceId, DateTime End, int MaterialId)>();
        var removedShares = new List<(string FinalDraftId, List<(string DemandKey, decimal ShareQty)> Shares)>();

        // 1) Task（自高位起移除；`removedTasks` 事后按原索引升序回插）
        for (int i = scheduledTasks.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(scheduledTasks[i].FinalDraftId)) continue;
            removedTasks.Add((i, scheduledTasks[i]));
            scheduledTasks.RemoveAt(i);
        }
        removedTasks.Reverse();

        // 2) 资源占用窗 / 3) 产品时间线登记 / 4) 份额血缘
        foreach (var (_, t) in removedTasks)
        {
            if (t.ResourceId is int rid)
            {
                var occStart = t.PlannedStartTime - TimeSpan.FromMinutes((double)t.SetupTime);
                var window = new TimeWindow(occStart, t.PlannedEndTime);

                EnsureOwned(resourceOccupancy, occupancyPristine, rid);
                if (resourceOccupancy.TryGetValue(rid, out var windows))
                {
                    var idx = windows.FindIndex(w => w.Start == window.Start && w.End == window.End);
                    if (idx >= 0)
                    {
                        removedWindows.Add((rid, windows[idx]));
                        windows.RemoveAt(idx);
                    }
                }

                removedTimeline.Add((rid, t.PlannedEndTime, t.MaterialId));
                constraints.ProductTimeline.Remove(rid, t.PlannedEndTime, t.MaterialId);
            }

            if (allocationTaskShare.TryGetValue(t.FinalDraftId, out var shares))
            {
                removedShares.Add((t.FinalDraftId, shares));
                allocationTaskShare.Remove(t.FinalDraftId);
            }
        }

        // ── V1_3 F-01（0号位 2026-10-10《APS_V1_3_20261010.md》§3，REWORK/BLOCKED）修复：**按插入身份回滚** ──
        //   上轮（AUD-1-003）已建「失败试排全状态回滚」，但**回滚方式本身是错的**（本轮 F-01）：
        //     旧实现记「每资源当前窗数」，失败时 `RemoveRange(count, Count-count)` **截尾**。
        //     而占用表是**有序插入**（`AddOccupancyWindow` 二分定位）—— 倒排 / 合批落定会把新窗插进
        //     列表**中间**；此时截尾删掉的是**列表尾部的旧合法窗**，试排新增的中间窗反而留下 ⇒
        //     既产生幽灵占用（INV-CAL-001 依旧未消），又**丢失既有合法占用**（新缺陷，后果更重）。
        //   修法 = **跟踪实际插入窗身份**（裁决 F-01 整改方向原话：「以快照精确还原或跟踪实际插入窗
        //     身份删除，禁止继续只按数量截尾；保持时间排序与 COW 语义。」）：
        //     · `occupancyInsertLog` 由 `AddOccupancyWindow` 逐次登记 `(资源, 窗)` **身份**；
        //     · 失败 / 违约时 `RollbackInsertedOccupancyWindows` **逆序按值身份**精确移除；
        //     · 资源键清理依据**试排前的键集**（`CaptureOccupancyResourceIds`），不再依赖窗数。
        //   ⚠ 不把「COW 通常隔离试排」当作允许真实落定失败时残留状态的理由：真实落定路径
        //     （`TryMergeDemandIntoStageBatch` 由 3611 行以**真实表**调用）非克隆，**必须**按身份回滚；
        //     试排克隆表上回滚是无害的（克隆随即丢弃），保持同一代码路径以便联验。
        var occupancyBaselineResourceIds = CaptureOccupancyResourceIds(resourceOccupancy);
        var occupancyInsertLog = new List<(int ResourceId, TimeWindow Window)>();
        var timelineBeforeRerun = constraints.ProductTimeline.Clone();

        // ── 重跑完整排程：同一 Path、同一批键、完整 Operation 链 ──
        combinedTasks = ScheduleDemandOperations(
            combinedDemand, operations, routingGraph, direction, constraints,
            resourceOccupancy, planningStart, planningEnd, dynamicMaterialFloor, stageOverlap,
            targetKey, occupancyPristine, occupancyInsertLog);

        // 失败 ⇒ 原样恢复（不留下任何半成品状态），回落独立排程。
        if (combinedTasks.Count == 0)
        {
            // V1_3 F-01：按插入身份精确撤销试排写入（含**中间插入**），再恢复被移出的原批。
            SolverDiagnostics.CountOccupancyRollbackUnmatched(
                RollbackInsertedOccupancyWindows(
                    resourceOccupancy, occupancyBaselineResourceIds, occupancyInsertLog));
            constraints.ProductTimeline = timelineBeforeRerun;
            RestoreStageBatchSnapshot(
                scheduledTasks, removedTasks, removedWindows, removedTimeline, removedShares,
                resourceOccupancy, occupancyPristine, constraints, allocationTaskShare);
            return null;
        }

        // ── 交期保护（P0-04 的多工序版 / M-02）：合批不得把**任一**参与需求推过其有效交期 ──
        //   合并会延长批次占用 ⇒ 若把更紧急一方推违约，则合批方案**不可选**（不得为省 Setup 延误高优先需求）。
        var combinedEnd = DateTime.MinValue;
        foreach (var t in combinedTasks)
        {
            if (t.PlannedEndTime > combinedEnd) combinedEnd = t.PlannedEndTime;
        }

        var dueAnchor = constraints.EffectiveDue(anchorDemand);
        var dueCurrent = constraints.EffectiveDue(demand);
        var worsensAnchor = dueAnchor != DateTime.MaxValue && combinedEnd > dueAnchor;
        var worsensCurrent = dueCurrent != DateTime.MaxValue && combinedEnd > dueCurrent;
        if (worsensAnchor || worsensCurrent)
        {
            // V1_3 F-01：本路径试排**已成功**（`combinedTasks` 非空）⇒ 其占用/时间线写入必须按身份整体回滚。
            SolverDiagnostics.CountOccupancyRollbackUnmatched(
                RollbackInsertedOccupancyWindows(
                    resourceOccupancy, occupancyBaselineResourceIds, occupancyInsertLog));
            constraints.ProductTimeline = timelineBeforeRerun;
            RestoreStageBatchSnapshot(
                scheduledTasks, removedTasks, removedWindows, removedTimeline, removedShares,
                resourceOccupancy, occupancyPristine, constraints, allocationTaskShare);
            combinedTasks = new List<FinalTaskDraft>();   // 未落定 ⇒ 不得把产出当作合批结果外传
            return null;
        }

        // ── 落定 ──
        scheduledTasks.AddRange(combinedTasks);

        // 份额账本：合批后**本批每条 Operation Task** 都承载同一份需求构成（含此前已并入的份额，不覆盖）。
        //   Phase5 `GetTaskDemandComposition` 据此展开 `mergeLineage`，锚点残余 = `Quantity - Σ已登记份额`。
        //   ⚠ 必须**逐 Task**登记，不能只登记末端 Task：Phase5 `ValidateHardResult` 的闭合校验是
        //     **逐 (需求 × 工序)** 的（`compositionByDemandOp`）—— 若上游 Task 无血缘，其构成会退化成
        //     「锚点需求 × 整批数量 50」，而该需求的声明量只有 30 ⇒ 误报「数量未闭合」。
        //     （实际落到 AllocationTaskShare 的仍只有**末端** Task —— `GenerateAllocationShares` 用
        //       `downstreamTasks` 过滤，与 `mergeLineage` 的登记范围无关。）
        // ── AUD-1-001（0号位 2026-10-10《APS_V1_2_20261010.md》§3，P0/CONFIRMED）修复 ──
        //   **按需求取「一份」规范构成，不得按 Operation 累加物理份额。**
        //
        //   缺陷：被移出批的 N 条 Operation Task **各自**登记同一份需求构成（这是 Phase5 逐 (需求 × 工序)
        //     闭合所必需的，见上方注释）；但旧写法把 `removedShares` **逐条** AddRange 进新批份额，
        //     同一份份额于是被算了 N 遍。静态反例（裁决 §3 原文场景）：
        //       A30+B20 先合成 50 件批（3 工序，每 Task 各持 [A30,B20]）⇒ 再合 C10 时
        //       removedShares = [(t1,[A30,B20]),(t2,[A30,B20]),(t3,[A30,B20])]
        //       ⇒ 旧写法得 B20×3 + C10 = 70，而新批 Task 数量只有 60
        //       ⇒ Phase5 `ValidateHardResult` 的逐 (需求×工序) 闭合校验必然失真/误报
        //         （B 份额膨胀、A 锚点份额被淹没）。
        //
        //   正解：按 `DemandKey` **去重**取一份规范构成（各 Operation 副本同值，首见即取 —— 同一份份额
        //     在同一执行批内**只可按需求真实归属计一次**，B-003 / INV-QTY-001 / INV-OUT-001），
        //     再映射到新批**每条** Task（保证 Phase5 逐工序闭合仍可展开）。
        var shareList = new List<(string DemandKey, decimal ShareQty)>();
        var seenShareDemandKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, shares) in removedShares)
        {
            foreach (var (demandKey, shareQty) in shares)
            {
                if (seenShareDemandKeys.Add(demandKey))
                {
                    shareList.Add((demandKey, shareQty));
                }
            }
        }

        // 本需求自身份额：同键不得重复累加（`FindMergeableStageBatches` 已保证目标批属**另一条需求**，
        //   此处仅为防御性合并 —— 真同键时按数量相加，绝不静默丢份额）。
        var currentShareIdx = shareList.FindIndex(
            s => string.Equals(s.DemandKey, demand.LogicalDemandKey, StringComparison.Ordinal));
        if (currentShareIdx >= 0)
        {
            shareList[currentShareIdx] =
                (demand.LogicalDemandKey, shareList[currentShareIdx].ShareQty + demand.NetOutputQty);
        }
        else
        {
            shareList.Add((demand.LogicalDemandKey, demand.NetOutputQty));
        }

        foreach (var combinedTask in combinedTasks)
        {
            allocationTaskShare[combinedTask.FinalDraftId] =
                new List<(string DemandKey, decimal ShareQty)>(shareList);
        }

        // 候选比较用代表 Task = 完成最晚者（真实完成时间，取代「并入既有 Task 无完成时间」的占位）。
        FinalTaskDraft representative = combinedTasks[0];
        foreach (var t in combinedTasks)
        {
            if (t.PlannedEndTime > representative.PlannedEndTime) representative = t;
        }
        return representative;
    }

    /// <summary>
    /// OWN-P0-02：恢复 <see cref="TryMergeDemandIntoStageBatch"/> 移出的目标 Stage 执行批（失败回滚）。
    /// 恢复顺序与移除顺序相反：份额 → 产品时间线 → 资源占用窗 → Task（按原索引升序回插，位置与原状一致）。
    /// </summary>
    private static void RestoreStageBatchSnapshot(
        List<FinalTaskDraft> scheduledTasks,
        List<(int Index, FinalTaskDraft Task)> removedTasks,
        List<(int ResourceId, TimeWindow Window)> removedWindows,
        List<(int ResourceId, DateTime End, int MaterialId)> removedTimeline,
        List<(string FinalDraftId, List<(string DemandKey, decimal ShareQty)> Shares)> removedShares,
        Dictionary<int, List<TimeWindow>> resourceOccupancy,
        Dictionary<int, List<TimeWindow>>? occupancyPristine,
        ConstraintContext constraints,
        Dictionary<string, List<(string DemandKey, decimal ShareQty)>> allocationTaskShare)
    {
        foreach (var (finalDraftId, shares) in removedShares)
        {
            allocationTaskShare[finalDraftId] = shares;
        }

        foreach (var (resourceId, end, materialId) in removedTimeline)
        {
            constraints.ProductTimeline.Place(resourceId, end, materialId);
        }

        foreach (var (resourceId, window) in removedWindows)
        {
            AddOccupancyWindow(resourceOccupancy, occupancyPristine, resourceId, window);
        }

        foreach (var (index, task) in removedTasks)
        {
            scheduledTasks.Insert(index < scheduledTasks.Count ? index : scheduledTasks.Count, task);
        }
    }

    /// <summary>
    /// V1_3 F-01：占用表**基线资源键集** —— 试排前记录已存在的资源键，供回滚判定「哪些键是试排新建的」。
    /// ⚠ 旧实现（按「每资源窗数」记水位、回滚时截尾）**已被 0号位 裁决判为缺陷（F-01）**：
    /// 有序插入会把新窗插到列表**中间**，按数量截尾会删掉**旧的合法窗**、留下**新窗幽灵**。
    /// 现改为**按插入身份**回滚（见 <see cref="RollbackInsertedOccupancyWindows"/>），本方法只负责键集。
    /// </summary>
    internal static HashSet<int> CaptureOccupancyResourceIds(
        Dictionary<int, List<TimeWindow>> occupancy)
    {
        var ids = new HashSet<int>();
        foreach (var resourceId in occupancy.Keys)
        {
            ids.Add(resourceId);
        }
        return ids;
    }

    /// <summary>
    /// V1_3 F-01：按**插入身份**精确回滚试排期间写入的占用窗。0号位 裁决 F-01 整改方向：
    /// 「以快照精确还原或**跟踪实际插入窗身份删除**，禁止继续只按数量截尾；保持时间排序与 COW 语义。」
    ///   · 逆序处理 <paramref name="insertLog"/>，每条按其 `(资源, 窗)` **值身份**在有序表中二分定位并移除；
    ///     <see cref="TimeWindow"/> 是值结构，且同一资源上的窗**互不重叠** ⇒ 值身份即唯一身份，移除无歧义。
    ///   · 逆序是必要的：同资源多次插入时后插的窗可能排在先插的窗之前，逆序保证每步都在「当前表」中命中。
    ///   · 条目找不到 ⇒ 说明该窗并非本次插入（不应发生）⇒ **不静默跳过**：计入返回值供调用方观测，
    ///     **绝不退化为「删别的窗来凑数」**。
    ///   · 收尾：资源键**不在** <paramref name="baselineResourceIds"/> 中且已清空 ⇒ 整键移除（回到「该资源无占用」初态）；
    ///     在基线中 ⇒ 保留空列表（调用方随后由 <see cref="RestoreStageBatchSnapshot"/> 把原批窗按原值加回）。
    /// 有序不变式：全程只做「有序表内定位 + 移除」，不破坏 `Start` 升序。
    /// </summary>
    /// <returns>**未命中**（日志中的窗在表中不存在）的条目数 —— 正常恒为 0；非 0 说明有代码绕过本日志写占用表。</returns>
    internal static int RollbackInsertedOccupancyWindows(
        Dictionary<int, List<TimeWindow>> occupancy,
        HashSet<int> baselineResourceIds,
        List<(int ResourceId, TimeWindow Window)> insertLog)
    {
        var unmatched = 0;
        for (var i = insertLog.Count - 1; i >= 0; i--)
        {
            var (resourceId, window) = insertLog[i];
            if (!occupancy.TryGetValue(resourceId, out var windows))
            {
                unmatched++;
                continue;
            }

            var index = IndexOfWindow(windows, window);
            if (index < 0)
            {
                unmatched++;
                continue;
            }

            windows.RemoveAt(index);
        }

        foreach (var resourceId in occupancy.Keys.ToList())
        {
            if (occupancy[resourceId].Count == 0 && !baselineResourceIds.Contains(resourceId))
            {
                occupancy.Remove(resourceId);
            }
        }

        return unmatched;
    }

    /// <summary>
    /// 有序窗表中的**值身份**定位（`(Start, End)` 精确相等）；未命中返回 -1。二分，O(log W)。
    /// ⚠ 下界条件必须是**严格**字典序 `<`（`w &lt; window` 才右移）。
    ///   若写成 `&lt;=`（= 与 <see cref="AddOccupancyWindow"/> 的**插入**下界同形），遇到**相等**窗还会继续右移
    ///   ⇒ 正好**越过**目标 ⇒ 永远返回 -1 ⇒ 回滚全部「未命中」而**一条也不撤销**
    ///   （实测：V1_3 F-01 判别性单测 5 例中 4 例因此红，`unmatched == 日志条数`）。
    ///   插入端用 `&lt;=` 是对的（等值窗插在其后、保持稳定），**查找端必须用 `&lt;`** —— 两端条件形状相似但语义相反。
    /// </summary>
    private static int IndexOfWindow(List<TimeWindow> windows, TimeWindow window)
    {
        int lo = 0, hi = windows.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            var w = windows[mid];
            if (w.Start < window.Start || (w.Start == window.Start && w.End < window.End)) lo = mid + 1;
            else hi = mid;
        }
        return lo < windows.Count && windows[lo].Start == window.Start && windows[lo].End == window.End
            ? lo
            : -1;
    }

    /// <summary>
    /// P0-08：同 PI 连续份额先于自由份额的稳定排序。
    /// 仅在「同 PI 组内」把 IsContinuation=true 的 demand 移到 IsContinuation=false 之前；
    /// 跨 PI 的相对顺序按「该 PI 首次出现的位置」保持，不破坏跨物料父子拓扑序
    ///（同一 PI 的连续/自由份额属于同一物料、同一拓扑层）。
    /// 无 PI（ProductionInstructionNo 为空）的 demand 不参与分组，保持原序。
    /// </summary>
    private static List<LogicalProductionDemand> OrderContinuityFirst(List<LogicalProductionDemand> demands)
    {
        // 无连续份额时直接返回原列表，避免无意义的排序开销与顺序扰动。
        if (demands.Count == 0 || !demands.Any(d => d.IsContinuation))
        {
            return demands;
        }

        var firstIndexByPI = new Dictionary<string, int>();
        for (var i = 0; i < demands.Count; i++)
        {
            var pi = demands[i].ProductionInstructionNo;
            if (string.IsNullOrEmpty(pi)) continue; // 无 PI 不分组
            if (!firstIndexByPI.ContainsKey(pi))
            {
                firstIndexByPI[pi] = i;
            }
        }

        return demands
            .Select((d, i) => new
            {
                Demand = d,
                Index = i,
                // 有 PI：用该 PI 首次出现的位置作为组主键；无 PI：用自身下标，保持原序不合并。
                GroupRank = string.IsNullOrEmpty(d.ProductionInstructionNo) ? i : firstIndexByPI[d.ProductionInstructionNo!]
            })
            .OrderBy(x => x.GroupRank)
            .ThenBy(x => x.Demand.IsContinuation ? 0 : 1)
            .ThenBy(x => x.Index)
            .Select(x => x.Demand)
            .ToList();
    }

    /// <summary>
    /// P0-17修复：获取工序间的Lag时间（分钟）
    /// 应用Routing LagTime到工序间时间依赖
    /// 第4轮审核修正：Dependencies按ToOperationCode存储，应查toOperationCode
    /// 0号位 2026-09-29 裁决 §5.3：节点身份升维为 (StageCode, OperationCode)，
    ///   出入参由 string operationCode 改为 OperationNodeKey，避免同码跨 Stage 时取到错误边的 LagTime。
    /// </summary>
    private decimal GetLagTime(OperationNodeKey fromNode, OperationNodeKey toNode, RoutingGraph routingGraph)
    {
        // Dependencies结构：Key=To节点, Value=该To的所有前驱边
        // 应查找toNode的前驱边列表，找到From匹配的边
        if (routingGraph.Dependencies.TryGetValue(toNode, out var edges))
        {
            // 找到从fromNode来的边
            var edge = edges.FirstOrDefault(e => e.From == fromNode);
            if (edge != null)
            {
                return edge.LagTime;
            }
        }
        return 0m; // 默认无延迟
    }
}

/// <summary>
/// 初始排程结果（Phase 2 输出）
/// </summary>
internal class InitialScheduleResult
{
    public List<FinalTaskDraft> ScheduledTasks { get; set; } = new();
    public List<string> UnscheduledDemandKeys { get; set; } = new();

    /// <summary>
    /// P0-04修复：Merge M:N 份额追溯（Demand → Task 的份额血缘），
    /// 由 Phase2 产出并传递给 Phase5，避免合并批次的份额血缘丢失。
    /// Key = 合并后 Task 的 FinalDraftId，Value = 该 Task 承载的各 Demand 份额。
    /// </summary>
    public Dictionary<string, List<(string DemandKey, decimal ShareQty)>> AllocationTaskShare { get; set; } = new();

    /// <summary>
    /// P0-02 整改（0号位 (7).md §五）：本 Run 中因 ⑧块 Batch Policy **无合法批方案**而 fail-closed 的需求
    /// （`Min/Max/AllowSplit` 冲突，如 `Qty=10/Min=6/Max=6`）。与 `UnscheduledDemandKeys` 同步登记，
    /// 单独成列以区别于其它 Unscheduled 原因，供诊断与出口使用。
    /// </summary>
    public List<string> BatchPolicyConflicts { get; set; } = new();

    /// <summary>
    /// P0-01 整改（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§四）：
    /// 本 Run 中因 **C 桶缺有效 Batch Policy**（`BATCH_POLICY_MISSING`）而 fail-closed 的需求。
    /// 与 <see cref="BatchPolicyConflicts"/> 分列 —— 两者原因类别不同，出口 Reason 亦不同。
    /// </summary>
    public List<string> BatchPolicyMissingDemandKeys { get; set; } = new();

    /// <summary>
    /// **需求键 → 硬失败原因**（`BATCH_POLICY_MISSING` / `BATCH_POLICY_CONFLICT`）。
    ///
    /// P0-02（0号位 2026-10-08 §五）：Solver 内部必须明确区分「**可修复未排**」与「**硬业务失败**」。
    ///   本表就是硬失败登记：Phase4 读到即**跳过普通 Local Repair**（禁止绕过 Min/Max 硬约束），
    ///   Phase5 读到即以本表值作为出口 `UnscheduledTaskResult.Reason`。
    /// </summary>
    public Dictionary<string, string> BatchPolicyHardFailures { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// **未落定执行批身份**（P0-03，0号位 2026-10-08 §六 / §十一-2）。
    ///
    /// 语义：Phase2 逐批落定时，首个失败批**及其后所有未试批**在此登记
    ///   （已落定的前序批**不登记** ⇒ Phase4 不得重新展开它们）。
    /// Phase4 `ExpandRepairUnits` 只按本表展开修复单元 ⇒ 需求级成功条件从
    ///   「任意一批修出」收紧为「**所有未落定批全部修出**」（§十一-2）。
    /// </summary>
    public List<FailedExecutionBatch> FailedExecutionBatches { get; set; } = new();

    /// <summary>
    /// P0-03+P0-15修复：技术失败标记（Routing非法、数据结构错误等）
    /// </summary>
    public bool TechnicalFailure { get; set; } = false;
    public string? TechnicalFailureReason { get; set; }

    /// <summary>某需求是否属**硬业务失败**（Batch Policy 缺失 / 冲突）—— Phase4 据此禁止普通修复。</summary>
    public bool IsBatchPolicyHardFailure(string logicalDemandKey)
        => BatchPolicyHardFailures.ContainsKey(logicalDemandKey);
}

/// <summary>
/// **未落定执行批**（P0-03，0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§六）。
///
/// 0号位 指定字段：`LogicalDemandKey` / `ExecutionBatchDraftKey` / `BatchQty` / `RouteCode` / `PathId`。
///   `RouteCode` / `PathId` 在「本批已选出路径但落定失败」时有值；「尚未被尝试的批」为 null
///   （Phase4 修复时按 `TryGetBatchRoutingGraph` 既有解析顺序取图，不在此处猜）。
/// Scheduling 内部类型，**非 1↔2 契约面**（不动 Core）。
/// </summary>
internal sealed record FailedExecutionBatch(
    string LogicalDemandKey,
    string ExecutionBatchDraftKey,
    int Ordinal,
    decimal BatchQty,
    decimal BatchPlannedProcessQty,
    string? RouteCode,
    long? PathId);
