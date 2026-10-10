using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using LPS.APS.Application.Models;
using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Rules;
using LPS.APS.BusinessRules.Loaders;
using LPS.APS.BusinessRules.Models;
using LPS.APS.BusinessRules.Repositories;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;
using ApsTask = LPS.APS.Core.Entities.APS.Task;

namespace LPS.APS.Application.Services;

/// <summary>
/// Pegging 编排器（2号位职责）
/// </summary>
public class PeggingOrchestrator : IPeggingOrchestrator
{
    private readonly IDemandSupplyHardLockRepository _lockRepo;
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<PeggingOrchestrator> _logger;
    private readonly IFiniteCapacityScheduler _scheduler;
    private readonly IDemandPriorityExecutor _demandPriorityExecutor;
    private readonly IDemandPriorityConfigProvider _demandPriorityConfigProvider;
    private readonly IFrozenStrategySnapshotProvider _frozenStrategySnapshotProvider;
    private readonly ITimedSupplyFactLoader _timedSupplyFactLoader;
    private readonly IProcurementManualEtaRepository _procurementManualEtaRepo;
    private readonly IProductionInstructionPositionCalculator _piPositionCalculator;
    private readonly IProductionInstructionPositionSnapshotRepository _piPositionSnapshotRepo;

    public PeggingOrchestrator(
        IDemandSupplyHardLockRepository lockRepo,
        DatabaseConnectionManager connectionManager,
        ILogger<PeggingOrchestrator> logger,
        IFiniteCapacityScheduler scheduler,
        IDemandPriorityExecutor demandPriorityExecutor,
        IDemandPriorityConfigProvider demandPriorityConfigProvider,
        IFrozenStrategySnapshotProvider frozenStrategySnapshotProvider,
        ITimedSupplyFactLoader timedSupplyFactLoader,
        IProcurementManualEtaRepository procurementManualEtaRepo,
        IProductionInstructionPositionCalculator piPositionCalculator,
        IProductionInstructionPositionSnapshotRepository piPositionSnapshotRepo)
    {
        _lockRepo           = lockRepo           ?? throw new ArgumentNullException(nameof(lockRepo));
        _connectionManager  = connectionManager  ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger             = logger             ?? throw new ArgumentNullException(nameof(logger));
        _scheduler          = scheduler          ?? throw new ArgumentNullException(nameof(scheduler));
        _demandPriorityExecutor = demandPriorityExecutor ?? throw new ArgumentNullException(nameof(demandPriorityExecutor));
        _demandPriorityConfigProvider = demandPriorityConfigProvider ?? throw new ArgumentNullException(nameof(demandPriorityConfigProvider));
        _frozenStrategySnapshotProvider = frozenStrategySnapshotProvider ?? throw new ArgumentNullException(nameof(frozenStrategySnapshotProvider));
        _timedSupplyFactLoader = timedSupplyFactLoader ?? throw new ArgumentNullException(nameof(timedSupplyFactLoader));
        _procurementManualEtaRepo = procurementManualEtaRepo ?? throw new ArgumentNullException(nameof(procurementManualEtaRepo));
        _piPositionCalculator = piPositionCalculator ?? throw new ArgumentNullException(nameof(piPositionCalculator));
        _piPositionSnapshotRepo = piPositionSnapshotRepo ?? throw new ArgumentNullException(nameof(piPositionSnapshotRepo));
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<PeggingOrchestrationResult> ExecutePeggingWorkflowAsync(
        PeggingExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new PeggingOrchestrationResult
        {
            PlanVersionId = request.PlanVersionId,
            OrderId       = request.OrderIds.FirstOrDefault()
        };

        _logger.LogInformation(
            "[Pegging] 开始: PlanVersionId={PlanVersionId}, 订单数={OrderCount}",
            request.PlanVersionId, request.OrderIds.Count);

        try
        {
            var bomSnapshot = await LoadBomSnapshotAsync(request.PlanVersionId, cancellationToken);
            _logger.LogInformation(
                "[Pegging] BOM 快照加载完成: PlanVersionId={PlanVersionId}, 边数={EdgeCount}",
                request.PlanVersionId, bomSnapshot.EdgeCount);

            // ── 冻结策略快照（3号位）：Run 启动按已冻结 VersionId 装载一次，供 Supply 多键排序使用 ──
            // 缺失版本号即运行上下文不完整（与 BuildDemandSequenceMapAsync 同口径），禁止静默回退。
            var strategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId;
            if (!strategyProfileVersionId.HasValue || strategyProfileVersionId.Value <= 0)
            {
                throw new InvalidOperationException(
                    "Supply 排序策略上下文不完整：SchedulingContext.StrategyProfileVersionId 为空。正式运行必须有冻结策略版本，禁止静默回退。");
            }
            var frozenSnapshot = await _frozenStrategySnapshotProvider
                .GetFrozenStrategySnapshotAsync(strategyProfileVersionId.Value, cancellationToken);

            // ── ⑦ Setup 换型规则（重构方案 S-5）：由 FrozenStrategySnapshotProvider 装配第⑦块 ──
            // （原物理表装载 LoadSetupTransitionRulesAsync 已移除；红线：Solver 不得运行中逐 Task 查 3号位规则库）
            var (supplyPool, continuityFacts) = await LoadSupplyPoolAsync(request, frozenSnapshot, bomSnapshot, cancellationToken);
            _logger.LogInformation(
                "[Pegging] 供给池装载完成: PlanVersionId={PlanVersionId}, 条目={EntryCount}",
                request.PlanVersionId, supplyPool.TotalEntries);

            // ── 步骤3：PeggingLoop BOM 遍历 + 供给扣减 ──
            var voucher = await ExecutePeggingLoopAsync(request, bomSnapshot, supplyPool, cancellationToken);
            result.Voucher = voucher;

            // ── 结果红线校验（PM 口径，前 4 项；在 Solver 与落库之前拦截）──
            var redLineErrors = ValidatePeggingResult(supplyPool, voucher);
            if (redLineErrors.Count > 0)
            {
                foreach (var e in redLineErrors)
                    _logger.LogError("[Pegging] 结果红线校验失败: {Error}", e);
                result.IsSuccess   = false;
                result.ErrorMessage = string.Join("; ", redLineErrors);
                return result;
            }

            var (solveRequest, overCommitEvents, peggingMs, _) =
                await BuildSolveRequestAsync(
                    request, voucher, frozenSnapshot, continuityFacts, bomSnapshot, sw, cancellationToken);

            var solverSw = System.Diagnostics.Stopwatch.StartNew();
            var solveResult = await _scheduler.SolveAsync(solveRequest, cancellationToken);
            solverSw.Stop();
            Console.WriteLine($"[PeggingOrchestrator] IFiniteCapacityScheduler.SolveAsync完成: FinalTasks={solveResult.FinalTasks?.Count ?? 0}, Success={solveResult.Success}");

            // ── 求解结果可观测性（2026-10-08 新增，纯读、不改行为）────────────────────────
            // 背景：2026-10-08 真实域跑批出现「22,135 条需求进求解器 → 722s → FinalTask=0 → Success=true」的
            //   **静默空跑**。原实现只打 `FinalTasks.Count` + `Success` ⇒ 求解器**为什么**一条不产
            //   （`UnscheduledTasks.Reason` / `ExplanationFacts.ReasonCode` / `Summary.UnscheduledCount`）
            //   全部不可见，只能回 1号位 源码里猜丢弃点。
            //   与 2026-09-29「优雅失败 ErrorMessage 恒 null」同族：**失败现场必须能从日志自证**。
            //   `DomainSolveResult` 的这三个集合是 1号位 已出口的字段，2号位 只是原样打印，不解释、不推断。
            var unscheduledByReason = solveResult.UnscheduledTasks
                .GroupBy(t => string.IsNullOrEmpty(t.Reason) ? "(空原因)" : t.Reason, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Count()}×{g.Key}");
            var explanationByReason = solveResult.ExplanationFacts
                .Where(f => !string.IsNullOrEmpty(f.ReasonCode))
                .GroupBy(f => f.ReasonCode, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Count()}×{g.Key}");

            // 求解过程追溯（B.2）：`DomainSolveResult.SolveTraceNotes` 是 1号位 已出口的字段，
            //   此前 2号位 全仓零消费（`LPS.APS.Application`/`LPS.APS.Engine` grep 无引用）⇒ **静默丢弃**。
            //   该集合是「非排程结果」的求解过程上下文（如 SCOPE_REFERENCE_MISSING / TARGET_MISSED /
            //   SETUP_RESOLUTION），正是排不下时最需要的证据。此处按 Level+ReasonCode 聚合打印，不落库。
            var traceByCode = solveResult.SolveTraceNotes
                .GroupBy(n => $"{n.Level}:{n.ReasonCode}", StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Count()}×{g.Key}");

            _logger.LogInformation(
                "[Pegging] 求解结果明细: Success={Success}, IsRoughCut={Rough}, Error={Error}, "
                + "Summary(Total={Total}, Scheduled={Scheduled}, Unscheduled={Unscheduled}, Issues={Issues}, ElapsedMs={Elapsed}) "
                + "| UnscheduledTasks 原因 Top5: {UnschedReasons} "
                + "| ExplanationFacts ReasonCode Top5: {ExplReasons} "
                + "| SolveTraceNotes 共 {TraceCount} 条，Level:ReasonCode Top5: {TraceReasons}",
                solveResult.Success, solveResult.IsRoughCut, solveResult.ErrorMessage ?? "(null)",
                solveResult.Summary.TotalDrafts, solveResult.Summary.ScheduledCount,
                solveResult.Summary.UnscheduledCount, solveResult.Summary.IssueCount, solveResult.Summary.ElapsedMs,
                unscheduledByReason.Any() ? string.Join(" | ", unscheduledByReason) : "(空)",
                explanationByReason.Any() ? string.Join(" | ", explanationByReason) : "(空)",
                solveResult.SolveTraceNotes.Count,
                traceByCode.Any() ? string.Join(" | ", traceByCode) : "(空)");

            var persistSw = System.Diagnostics.Stopwatch.StartNew();
            (result.GeneratedTasks, result.PhysicalPeggingCount, result.SupplyAllocationCount) =
                await PersistDomainAndPeggingInTransactionAsync(
                    request.PlanVersionId, voucher, solveResult, overCommitEvents, cancellationToken);
            persistSw.Stop();
            _logger.LogInformation(
                "[Pegging] 统一事务落库: Task={Tasks}, Pegging={Pegging}, SupplyAllocation={Alloc}",
                result.GeneratedTasks.Count, result.PhysicalPeggingCount, result.SupplyAllocationCount);

            sw.Stop();
            // 【2026-10-08 修复】原为 `result.IsSuccess = true;` **无条件置真** ⇒ 求解器返回
            //   `Success=false`（1号位 语义 = **技术失败**：Routing 非法 / 数量闭合错误 / 硬资源约束破坏）
            //   时，本编排仍报成功；上层 `ExecuteDomainAsync` 的 `isSuccess = peggingFailed.Count == 0`
            //   因此恒真 ⇒ **PV 被标 `Computed`、ETL 记「失败:0」、库里查不到任何失败痕迹**。
            //   2026-10-08 实测即栽在此：PV2 22,135 条需求、求解 722s、FinalTask=0，而 ETL 写的是「失败:0」，
            //   只能回 1号位 源码里猜丢弃点（`ErrorMessage` 也一并被丢）。
            //   现口径：**Pegging 编排成功 且 求解器未报技术失败** ⇒ IsSuccess=true。
            //   ⚠️ 不改变业务语义：1号位 明示「产能不足 / 排不下」属**业务结果**，走 `Success=true`
            //      + `UnscheduledTasks` 出口，**不会**在此被判失败；此处只兜技术失败。
            result.IsSuccess = solveResult.Success;
            result.ErrorMessage = solveResult.Success
                ? null
                : string.IsNullOrWhiteSpace(solveResult.ErrorMessage)
                    ? "求解器返回 Success=false（技术失败）但未提供 ErrorMessage"
                    : $"求解器技术失败：{solveResult.ErrorMessage}";
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;
            result.PeggingMs      = peggingMs;
            result.SolverMs       = solverSw.ElapsedMilliseconds;
            result.PersistMs      = persistSw.ElapsedMilliseconds;

            _logger.LogInformation(
                "[Pegging] 完成: PlanVersionId={PlanVersionId}, Task={Tasks}, 分配={Alloc}, 耗时={Ms}ms",
                request.PlanVersionId, result.GeneratedTasks.Count,
                result.SupplyAllocationCount, result.ExecutionTimeMs);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[Pegging] 编排异常: PlanVersionId={PlanVersionId}", request.PlanVersionId);
            Console.WriteLine($"[PeggingOrchestrator] 捕获异常: {ex.Message}");
            Console.WriteLine($"[PeggingOrchestrator] 异常堆栈: {ex.StackTrace}");
            result.IsSuccess      = false;
            result.ErrorMessage   = ex.Message;
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;
            return result;
        }
    }
    ///
    /// <inheritdoc />
    /// <summary>
    /// 组装交给 1号位 的 <see cref="DomainSolveRequest"/>（**Solver 之前的全部装载**，2026-10-09 抽出）：
    /// 连续性分桶（Continuation/Free + E&gt;Q 超量登记）→ Routing 三件套（按 (MaterialId,StageCode) 过滤，不混载）
    /// → 部门归属上下文 → StartStageCode/RequiredStageCode 填值 → 无工序阶段提前期 → 有效 Stage 链
    /// → 候选上下文/RunScope → 求解参数投影。
    ///
    /// 抽出的目的：给「装载给 1号位」一个**可独立调用的验收入口**
    /// （<see cref="RunPeggingOnlyAsync"/>），使整块载荷能被逐项核对，而不必调用 Solver 或落库。
    /// 与 <c>ExecutePeggingWorkflowAsync</c> 共用同一段代码，**杜绝验收路径与生产路径分叉**。
    /// </summary>
    private async System.Threading.Tasks.Task<(
        DomainSolveRequest SolveRequest,
        List<ContinuityOverCommitEvent> OverCommits,
        long PeggingMs,
        IReadOnlyList<LogicalProductionDemand> PreBucketingDemands)>
        BuildSolveRequestAsync(
            PeggingExecutionRequest request,
            PeggingResultVoucher voucher,
            FrozenStrategySnapshot frozenSnapshot,
            ContinuityFacts continuityFacts,
            BomSnapshot bomSnapshot,
            Stopwatch sw,
            CancellationToken cancellationToken)
    {
        // ── 跨版本连续性分桶（PM 0914 红线）：先形成的 Q 按 IN_PROGRESS 既存执行切 Continuation/Free ──
        // Q 不因 WIP 供给减小；被覆盖份额以 Continuation Slice（IsContinuation=true）＋自由份额 Free Slice
        // 一起进 1号位（1号位必须看到 Continuation E + Free (Q−E)，不得只看到 Free）。E 来源 = StageProgressSnapshot.RemainingQty。
        // 分桶**前**的源需求快照：分桶是「一进多出」，守恒性（Σ片Q == 源Q）只能拿它做基准核验，
        // 探针靠它验「分桶只切不增」（`RunPeggingOnlyAsync` 出口带回；生产路径不使用、零行为影响）。
        var preBucketingDemands = voucher.LogicalProductionDemands;
        var (bucketedDemands, overCommitEvents) = ApplyContinuityBucketing(
            voucher.LogicalProductionDemands, continuityFacts);
        voucher.LogicalProductionDemands = bucketedDemands;

        // v5.1.2架构整改：不再预先生成TaskDrafts，改为传递LogicalProductionDemands给1号位
        // 1号位基于LogicalProductionDemands生成FinalTasks（含拆批/合批决策）
        _logger.LogInformation("[Pegging] 准备传递LogicalProductionDemands给Solver: {Count} 个",
            voucher.LogicalProductionDemands.Count);

        // ── 装载 Routing 三件套 + 部门归属上下文（PM 裁定：最小 B）──
        // 【2026-09-28 改造】按 PM《BOM取用_Pegging_Stage_Routing完整链路说明》§二十一/§二十二：
        //   Stage 的存在性与顺序由 `APS_BOM_STAGE_PATH_RAW`（本次 BOM 上下文）决定，**不得从 Routing 反推**；
        //   Routing 只回答「该 Stage 内部有哪些小工序」⇒ 须按 (MaterialId, StageCode) 过滤装载。
        //   原实现 `WHERE MaterialId IN @Ids` 会把该物料所有 Stage 的工序全捞进来（PM 指出的「混载」）。
        var demandMaterialIds = voucher.LogicalProductionDemands
            .Select(d => d.MaterialId)
            .Distinct()
            .ToList();

        // 本次 BOM 上下文下的有效 Stage：MaterialId → 该物料本次需经过的 StageCode 集合（ROOT ∪ EDGE）
        var effectiveStages = await LoadEffectiveStageKeysAsync(request.PlanVersionId, demandMaterialIds, cancellationToken);

        var (routingOperations, routingDependencies, operationResourceEligibility) =
            await LoadRoutingContextAsync(demandMaterialIds, effectiveStages.ByMaterial, cancellationToken);

        var materialStageDeptContexts =
            await LoadMaterialStageDeptContextAsync(demandMaterialIds, cancellationToken);

        // ③ StartStageCode 填值（2026-09-11，5号位 O3 回复划归 2号位）：Routing 图「无入边源结点」的
        // 大工艺阶段码 → LogicalProductionDemand.StartStageCode（原 BuildLogicalProductionDemand 写空）。
        FillStartStageCodes(voucher, routingOperations, routingDependencies);

        // ③c 连续份额路径身份回填（2026-10-09，PM回复1009 §七 第5行 / v1.6 §1）：
        //   「A/B 输入必须带**真实固定** RouteCode / PathId / StartOperation」，1号位
        //   `PhaseTwoInitialScheduler.cs:310-325` 对 `IsContinuation` 需求做**硬 Fail Closed**
        //   （缺任一 ⇒ 记 Unscheduled、**不产任何 Task**）。原实现**从不给需求填这两个字段** ⇒
        //   PV2 实测 284/284 片缺 RouteCode/PathId ⇒ A/B 数量在 1号位 侧凭空消失（静默丢单）。
        //   取值源 = 本次已归一化、即将随 `DomainSolveRequest` 一起交给 1号位的那份路由上下文
        //   （`NormalizeToSingleRoute` 后 V1 恒为 DEFAULT / 1）——**不是猜路径**：
        //   1号位 的 `RoutingGraph` 字典就是按这份载荷建的，给别的值反而查不到图。
        //   物料在本次载荷里存在**多条**不同 (RouteCode, PathId) 时不猜，留 null 并告警（保持 Fail Closed）。
        FillContinuationRouteIdentities(voucher, routingOperations, _logger);

        // ④ 数量单位回填（2026-10-09，P1-08 方案a 取值源修正）：需求**自身物料**的 Material.UOM
        //   → LogicalProductionDemand.UOM。原实现从不在 BuildLogicalProductionDemand 里赋值 ⇒ PV2 实测
        //   96,627/96,627 条需求 UOM=null ⇒ 1号位各处 `demand.UOM ?? string.Empty` ⇒ **每个 Task 的 UOM 恒为空串**。
        await FillDemandUomAsync(voucher, cancellationToken);

        // ③b 供给阈值 Stage 填值（PM《BOM取用…完整链路说明》§八，2026-09-28）：BOM 边
        //   `ChildRequiredStageCode`（主源，已随 bomSnapshot 按本批上下文装载）→ 兜底 StagePath `IsSupplyThreshold=1`（按本批）
        //   → LogicalProductionDemand.RequiredStageCode。
        //   语义：「子件做到该 Stage 以后才成为父件可使用的 Supply」⇒ 该需求做到此阶段即为终点。
        await FillRequiredStageCodesAsync(voucher, bomSnapshot, request.PlanVersionId, cancellationToken);

        // ── 无 Routing 阶段的提前期（PM《无Routing Stage统一处理建议》§九：2号位 装载 → 1号位 消费）──
        // StagePath 决定阶段存在性；Routing 不存在 ≠ Stage 不存在 ⇒ 对「有效 Stage 但零工序」的
        // (物料, 阶段) 解析 StageLeadTimeParam 提前期，交由 1号位 保留该阶段的时间与前后依赖。
        //   部门码复用上一行已整批装好的 `materialStageDeptContexts`（其物料集 = 本 Domain 需求物料，
        //   是 gapPairs 物料的超集）——**不在循环内逐对重查**（原为 N+1，见方法内注释）。
        var stageLeadTimes = await LoadStageLeadTimesAsync(
            effectiveStages.ByMaterial, routingOperations, voucher.LogicalProductionDemands,
            materialStageDeptContexts);

        if (demandMaterialIds.Count > 0 && routingOperations.Count == 0)
        {
            _logger.LogWarning(
                "[Pegging] Routing 三件套为空（需求物料数={MaterialCount}），1号位将把所有新增生产需求判定为 Unscheduled；请确认 routing-sync（00:25）已灌入数据",
                demandMaterialIds.Count);
        }

        // ── 求解参数：由冻结 SolverStrategy 块投影，不再硬编码（Q9 修复，2026-09-04）──
        // Mode → Direction 1:1（固定映射，见 SolverStrategyModeMap）；AllowSplit 由 Split.MaxOptimizationSplitCount>1 推；
        // AllowMerge 由 3号位 补源字段直接投影（2026-09-04 回执认可口径，零行为变化）。
        // P1-02 B 组：ImpactedTaskWarningPercent/MaxPropagationRounds/SplitAlternatives/MinBatchQty 直接投影
        // （3号位 已冻结；字段先行，1号位 换读后逐项激活，零行为变化）。
        var solverStrategy = frozenSnapshot.SolverStrategy;
        var schedulingDirection = SolverStrategyModeMap.ToDirection(solverStrategy.Mode);

        // P0-04：CandidateContext（FULL 为 null）。Base 锚点 = request.BasePlanVersionId（3号位冻结），
        // ChangeSeed/ExternalDomainResourceBlocks 由 BuildCandidateContextAsync 计算/透传。
        var candidateContext = await BuildCandidateContextAsync(request, voucher);

        // ScopeJsonV2 → RunScope 投影（M2；null = FULL 零改动）
        var runScope = await BuildRunScopeAsync(request, voucher, candidateContext?.BasePlanVersionId, cancellationToken);

        var solveRequest = new DomainSolveRequest
        {
            ScheduleRunId = request.SchedulingContext?.ScheduleRunId,
            PlanVersionId = request.PlanVersionId,
            DomainKey     = request.DomainKey,
            DataCutoffTime = request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt,
            // P0-03：PlanningStart/End 是有限产能求解窗口 = PlanVersion 的 90 天边界（PlanHorizonStart/End），
            // 与「FrozenWindowEnd(now+2h 滑动冻结窗)」解耦；冻结窗是约束（P0-05），不再充当 Solver Horizon。
            PlanningStart = request.SchedulingContext != null && request.SchedulingContext.PlanHorizonStart != default
                ? request.SchedulingContext.PlanHorizonStart
                : (request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt),
            PlanningEnd = request.SchedulingContext != null && request.SchedulingContext.PlanHorizonEnd != default
                ? request.SchedulingContext.PlanHorizonEnd
                : DateTime.Now.AddDays(90),

            LogicalProductionDemands = voucher.LogicalProductionDemands,
            MaterialRequirementLinks = voucher.MaterialRequirementLinks,
            AllocationLineage = BuildAllocationLineage(voucher),

            RoutingOperations = routingOperations,
            RoutingDependencies = routingDependencies,
            OperationResourceEligibility = operationResourceEligibility,
            MaterialStageDepartmentContexts = materialStageDeptContexts,
            StageLeadTimes                 = stageLeadTimes,
            // Stage 顺序事实（PM §七 权威 = StageSeq；1号位 2026-09-28 回执 §六 第 2 项同意先落）。
            // 每个物料一条完整有序链、每步带 StageSeq 数值 + ProductionDepartmentId
            // —— 即 PM《…接口裁决回复》2026-09-28 §三 命名的 `EffectiveStagePath` 业务事实
            //（MaterialId + StageCode + StageSeq + ProductionDepartmentId 四条一组）。
            StageSequenceChains            = BuildEffectiveStagePaths(effectiveStages.OrderedChains, materialStageDeptContexts),

            MaterialConstraints = BuildMaterialConstraints(voucher),

            Resources     = BuildResourceDefinitions(request.SchedulingContext),
            CalendarSlots = BuildResourceCalendarSlots(request.SchedulingContext),
            ResourceEligibility = Array.Empty<ResourceEligibilityDefinition>(),
            // D8/R17/T18（同TaskNo自阻挡）：本域上一版本 ACTIVE 旧块不得作为「外部 ResourceBlock」挡自己，
            // 应转成 ExecutionConstraint 锚点（TaskKey 识别同一 Task）。当前锚点机制未建（PreferredResourceId
            // 软偏好=可选先不做），故预留为空；本域旧块既不进外部块（见 SchedulingOrchestrator 的 D8 守卫）、
            // 也不硬锁资源——D8 must-not 天然成立。
            ExecutionConstraints = Array.Empty<ExecutionConstraint>(),

            StrategySnapshot = new SolverStrategySnapshot
            {
                StrategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId,
                ParameterSetVersionId = frozenSnapshot.ParameterSetVersionId,
                // P1-02：⑤⑥ 全字段整块透传（强类型零漂移）——1号位从整块读，不再等 2号位 逐批平铺/读投影子集硬编码。
                SolverStrategy     = frozenSnapshot.SolverStrategy,
                CandidateGuardrail = frozenSnapshot.CandidateGuardrail,
                // ⑦ 换型规则：2号位装载投影后按 Domain（Dept+Stage）裁剪，只传本域涉及规则（§19.3「只加载本 Domain」）。
                SetupTransitionRules = SetupTransitionRuleProjector.CropToDomain(frozenSnapshot.SetupTransitionRules, materialStageDeptContexts),
                // ⑧ 批量策略（05 契约 §2.3 第 7 类）：**整块全量透传，不裁剪**。
                //   粒度 = Material + ProductionDepartmentId（可空 = Material 级默认）。
                //   与 ①②③④ 同向（装载不裁剪、匹配期决定）——裁剪会把「Material 级默认行」(Dept=NULL)
                //   一并丢掉，1号位 收到空策略即按「每需求恒 1 批」处理，而 §十一 禁止 1号位 自造默认。
                //   交由 1号位 PhaseOneConstraintBuilder 按本域 Material/Dept 自行匹配。
                BatchPolicies = frozenSnapshot.BatchPolicy,
                Parameters = new FiniteCapacityParameters
                {
                    AllowSplit = solverStrategy.Split.MaxOptimizationSplitCount > 1,
                    AllowMerge = solverStrategy.AllowMerge,
                    SchedulingDirection = schedulingDirection,

                    // P1-02 B 组：⑤⑥ 已冻结字段直接投影（零行为变化；1号位 消费点后置）
                    ImpactedTaskWarningPercent = frozenSnapshot.CandidateGuardrail.ImpactedTaskWarningPercent,
                    MaxPropagationRounds = frozenSnapshot.CandidateGuardrail.MaxPropagationRounds,
                    SplitAlternatives = frozenSnapshot.CandidateGuardrail.SplitAlternatives,
                    MinBatchQty = solverStrategy.Split.MinBatchQty
                }
            },

            CandidateContext = candidateContext,

            RunScope = runScope,

            // FULL §9：前序 Domain 成功后的共享 Resource 占用块 → 1号位 作为不可用时间窗
            UpstreamDomainResourceBlocks = request.UpstreamResourceBlocks ?? Array.Empty<ResourceBlock>()
        };
        // 阶段3前：Pegging 阶段耗时（供给装载 + BOM 遍历扣减 + Routing 装载 + 请求构建）
        var peggingMs = sw.ElapsedMilliseconds;
        return (solveRequest, overCommitEvents, peggingMs, preBucketingDemands);
    }

    /// <summary>
    /// **装载给 1号位 的验收入口（不调 Solver、不落库）**：跑完 Pegging 全链 + 组出
    /// <see cref="DomainSolveRequest"/> 后即返回，供探针逐项核对载荷（需求数量闭合、PI 归属、
    /// 分桶守恒/E≤Q、Routing 依赖闭合、部门上下文覆盖、Stage 链有序等）。
    ///
    /// ⚠️ 与生产路径 <c>ExecutePeggingWorkflowAsync</c> 的唯一差异 = **到 solveRequest 为止就返回**：
    /// 同一 <c>BuildSolveRequestAsync</c>、同一 <c>ExecutePeggingLoopAsync</c>、同一供给池装载。
    /// <c>persistPositionSnapshot:false</c> —— 探针不覆盖夜间 run 的 PI Position 快照。
    /// </summary>
    internal async System.Threading.Tasks.Task<PeggingPayload> RunPeggingOnlyAsync(
        PeggingExecutionRequest request,
        CancellationToken ct = default,
        bool scopeBomToOrders = false)
    {
        var sw = Stopwatch.StartNew();

        var strategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId;
        if (!strategyProfileVersionId.HasValue || strategyProfileVersionId.Value <= 0)
            throw new InvalidOperationException(
                "装载验收策略上下文不完整：SchedulingContext.StrategyProfileVersionId 为空。正式运行必须有冻结策略版本，禁止静默回退。");
        var frozenSnapshot = await _frozenStrategySnapshotProvider
            .GetFrozenStrategySnapshotAsync(strategyProfileVersionId.Value, ct);

        // 分段计时（2026-10-09）：小样本定向跑只缩到 160s（全量 ~200s），说明大头**不在订单/BOM 侧**。
        // 没有分段耗时就是黑盒，遂在此埋点。四处即可定位：BOM 快照 / 供给池(含 PI 事实) / Pegging 回路 / 请求构建(含分桶)。
        var t0 = sw.ElapsedMilliseconds;
        // `scopeBomToOrders=true` 时把 BOM 宇宙收窄到 request.OrderIds（**仅小样本定向跑用**，见方法注释）。
        var bomSnapshot = await LoadBomSnapshotAsync(
            request.PlanVersionId, ct, scopeBomToOrders ? request.OrderIds : null);
        var t1 = sw.ElapsedMilliseconds;
        var (supplyPool, continuityFacts) = await LoadSupplyPoolAsync(
            request, frozenSnapshot, bomSnapshot, ct, persistPositionSnapshot: false);
        var t2 = sw.ElapsedMilliseconds;

        var voucher = await ExecutePeggingLoopAsync(request, bomSnapshot, supplyPool, ct);
        var t3 = sw.ElapsedMilliseconds;
        var redLineErrors = ValidatePeggingResult(supplyPool, voucher);

        var (solveRequest, overCommits, peggingMs, preBucketing) = await BuildSolveRequestAsync(
            request, voucher, frozenSnapshot, continuityFacts, bomSnapshot, sw, ct);
        var t4 = sw.ElapsedMilliseconds;

        _logger.LogInformation(
            "[Pegging][阶段耗时] BOM快照={Bom}ms 供给池(含PI事实)={Supply}ms Pegging回路={Loop}ms 请求构建(含分桶)={Build}ms 合计={Total}ms 订单={Orders}",
            t1 - t0, t2 - t1, t3 - t2, t4 - t3, t4 - t0, request.OrderIds?.Count ?? 0);

        return new PeggingPayload(
            request.PlanVersionId, voucher, solveRequest, overCommits, redLineErrors,
            peggingMs, sw.ElapsedMilliseconds, preBucketing, continuityFacts.PiPositions);
    }

    /// <summary>装载给 1号位 的完整产出（Solver 之前的全部数据）——验收探针读取用。</summary>
    internal sealed record PeggingPayload(
        int PlanVersionId,
        PeggingResultVoucher Voucher,
        DomainSolveRequest SolveRequest,
        IReadOnlyList<ContinuityOverCommitEvent> OverCommits,
        IReadOnlyList<string> RedLineErrors,
        long PeggingMs,
        long TotalMs,
        // 分桶**前**的源需求（分桶守恒核验基准）
        IReadOnlyList<LogicalProductionDemand> PreBucketingDemands,
        // PI Position 事实（含 ExistingExecutionContexts；E 重复计核验基准）
        IReadOnlyDictionary<string, ProductionInstructionPositionResult> PiPositions);

    public async System.Threading.Tasks.Task<IEnumerable<PeggingOrchestrationResult>> ExecuteBatchPeggingWorkflowAsync(
        PeggingExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        // P0-01：撤销「20单=独立 Pegging/Solver/事务」的业务切批（原 const int batchSize=20 + OrderIds.Chunk）。
        // 冻结口径：一个 Domain = 一个数量真相边界 / 一次 DemandPriority / 一条 AllocationSequence /
        // 一次有限产能 Solver / 单Domain事务。LoadSupplyPoolAsync 本就按 DataCutoffTime 装载整域供给池（不分单），
        // 20单切批反而导致四个直接错误（见 2号位代码审核报告 P0-01）：
        //   A. 供给被多批重复消费（每批重载原始数量、不回扣上一批已落分配）；
        //   B. DemandPriority 只在批次内部成立，全 Domain 高优先级订单无法跨批争抢同一供给；
        //   C. 同一 Domain 被多次独立调用 Solver，FinalTask 无法在同一有限产能问题中竞争/合批/拆批；
        //   D. AllocationSequence 每批从 1 重复起始，撞 UNIQUE(PlanVersionId, AllocationSequence)。
        // 最小整改（PM 方向）：保留内存结构，整域单次调用；方法名保留 Batch 仅为 IPeggingOrchestrator 接口
        // 与 SchedulingOrchestrator 的聚合口径（r => 单结果集）兼容。
        return new[] { await ExecutePeggingWorkflowAsync(request, cancellationToken) };
    }

    /// <summary>
    /// 统一事务：DELETE 占位 Task → INSERT Task → INSERT Pegging 血缘 → INSERT AllocationLedger。
    /// 四步在同一 SqlTransaction 内，任一失败全部回滚。
    /// </summary>
    private async System.Threading.Tasks.Task<(List<ApsTask> tasks, int peggingCount, int supplyAllocationCount)>
        PersistDomainAndPeggingInTransactionAsync(
            int planVersionId,
            PeggingResultVoucher voucher,
            DomainSolveResult solveResult,
            IReadOnlyList<ContinuityOverCommitEvent> overCommits,
            CancellationToken ct)
    {
        return await _connectionManager.ExecuteInTransactionAsync<(List<ApsTask>, int, int)>(
            async (conn, tx) =>
            {
                Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] 开始事务，FinalTasks数量={solveResult.FinalTasks.Count}");
                var now    = DateTime.Now;
                var tasks  = new List<ApsTask>(solveResult.FinalTasks.Count);

                var finalDraftToTaskId = new Dictionary<string, long>(
                    solveResult.FinalTasks.Count, StringComparer.Ordinal);

                // W3：Task 落库必须携带 MTS_InstructionNo（连续份额尤须，供 MES 绑原工单识别）。
                // FinalTaskDraft.SourceDraftId = 源 LogicalDemandKey（1号位 PhaseTwoInitialScheduler L1135）→ 反查 LPD.ProductionInstructionNo。
                // Continuation 保持源 key、Free 用源 key+"/FREE"——两者都在 voucher.LogicalProductionDemands 里带了同一 PI，均能命中。
                var piByDemandKey = voucher.LogicalProductionDemands
                    .Where(d => !string.IsNullOrEmpty(d.ProductionInstructionNo))
                    .GroupBy(d => d.LogicalDemandKey, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().ProductionInstructionNo!);

                // G4：反查「旧 TaskNo ↔ MESWorkOrderNo」映射（TaskNo 继承，冻结 §12.10.1 / T10）。
                // 映射落点 = 下发中间表 [TaskDispatch]，不是 [Task]（MES 侧工单身份不进核心 Task 模型，0号位 2026-09-17 P2/P3/P4 裁决）。
                // 只对连续份额有 MES 工单号；显式剔除当前 SourcePlanVersionId，命中均为历史版本的 TaskDispatch 下发绑定。
                // 键 = MES 工单号（一个 TaskNo ↔ 一个 MESWorkOrderNo，§7.1），取该工单最新下发记录的旧 TaskNo。
                var continuationWorkOrders = solveResult.FinalTasks
                    .Select(f => ExtractMesWorkOrderNo(f.SourceDraftId))
                    .Where(w => w is not null)
                    .Select(w => w!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var oldTaskNoByWorkOrder = new Dictionary<string, string>(StringComparer.Ordinal);
                if (continuationWorkOrders.Count > 0)
                {
                    // 分片：`continuationWorkOrders` = 本域 **全部** FinalTasks 的 MES 工单号去重（全量跑可达数千，
                    // 远超 2100）⇒ 裸 `IN @Wos` 会把「TaskNo 继承」这一步整个打断。lambda 内闭包 `tx`，同一事务不变。
                    var bindRows = await QueryChunkedInAsync(continuationWorkOrders, chunk =>
                        conn.QueryAsync<TaskNoBindingRow>(
                            @"SELECT d.MESWorkOrderNo, d.TaskNo
                          FROM [TaskDispatch] d
                          JOIN (
                              SELECT MESWorkOrderNo, MAX(Id) AS MaxId
                              FROM [TaskDispatch]
                              WHERE MESWorkOrderNo IN @Wos AND SourcePlanVersionId <> @CurrentPlanVersionId
                              GROUP BY MESWorkOrderNo
                          ) m ON m.MaxId = d.Id",
                            new { Wos = chunk, CurrentPlanVersionId = planVersionId },
                            transaction: tx));

                    foreach (var r in bindRows)
                        oldTaskNoByWorkOrder[r.MESWorkOrderNo] = r.TaskNo;
                }

                // B/C 桶「同批共号」登记表（T-002）：批身份 → 本 Run 内已分配的新 TaskNo。
                // 作用域 = 本 PlanVersion 落库事务（跨域循环不共享，故声明在此 foreach 之前）。
                var taskNoByBatch = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var final in solveResult.FinalTasks)
                {
                    ct.ThrowIfCancellationRequested();

                    piByDemandKey.TryGetValue(final.SourceDraftId, out var mtsInstructionNo);

                    // G4：连续份额从 ContinuationKey 反解 MES 工单号；TaskNo 三分支（T-002/T-003/T-004）
                    var mesWorkOrderNo = ExtractMesWorkOrderNo(final.SourceDraftId);
                    var taskNo = ResolveTaskNo(planVersionId, final, mesWorkOrderNo, oldTaskNoByWorkOrder, taskNoByBatch);

                    // S3c：人工槽合成 ResourceId 拆回——合成键（≥ ManualSlotResourceOffset）→ Task.ResourceId=NULL + Task.ManualSlotId=原槽号；
                    // 设备真实 ResourceId 原样落 ResourceId（P0-06 不丢 1号位 时间资源真相）。
                    int? persistResourceId;
                    int? persistManualSlotId;
                    if (final.ResourceId.HasValue && SolverResourceProjection.IsManualSlotId(final.ResourceId.Value))
                    {
                        persistResourceId = null;
                        persistManualSlotId = SolverResourceProjection.ToManualSlotId(final.ResourceId.Value);
                    }
                    else
                    {
                        persistResourceId = final.ResourceId;
                        persistManualSlotId = null;
                    }

                    // 真实路径身份落库（红线 Q1 / RT-001 / DB-002）：1号位 FinalTaskDraft 原样透传，**不回填 DEFAULT/1**。
                    //   ⚠ `FinalTaskDraft.PathId` 是 `long?`，而 Task.PathId 是 `INT`（DDL v5.1.8.3）⇒ 越界即置 NULL，不静默截断。
                    //   ⚠ 当前值仍会是 DEFAULT/1 —— 因为 1号位 侧 `StageTimingNodeBuilder.cs:295` 尚硬编码
                    //     RouteCode=="DEFAULT"，2号位 不敢先停归一化（见台账 §T-1008l）。待 1号位 整改后本列自动变真值。
                    int? persistPathId =
                        final.PathId is { } pid && pid >= int.MinValue && pid <= int.MaxValue ? (int)pid : null;

                    // ProductionDepartmentId：DB-002 要求落真实部门，但 1↔2 契约 `FinalTaskDraft` 尚无此字段
                    //   （1号位 `OperationNode.ProductionDepartmentId` 有真值，未随 FinalTask 带出）。
                    //   按 CLAUDE.md「1↔2 契约字段只按 1号位 点名扩」⇒ 此处先落 NULL 并挂账，不自行反推。
                    int? persistDepartmentId = null;

                    var ids = await conn.QueryAsync<long>(
                        @"INSERT INTO [Task] (
                              PlanVersionId, TaskNo, OrderId, MaterialId,
                              OperationSeq, OperationCode,
                              Quantity, PlannedProcessQty, UOM, PlannedStartTime, PlannedEndTime, Duration,
                              ResourceId, ManualSlotId,
                              Status, IsLocked, IsCriticalPath, TaskType,
                              MTS_InstructionNo, SetupSource, StageCode,
                              MESWorkOrderNo, ProductionDepartmentId, RouteCode, PathId,
                              StageExecutionBatchDraftKey, StageExecutionBatchQty,
                              CreatedAt, UpdatedAt
                          )
                          OUTPUT INSERTED.Id
                          VALUES (
                              @PlanVersionId, @TaskNo, @OrderId, @MaterialId,
                              @OperationSeq, @OperationCode,
                              @Quantity, @PlannedProcessQty, @UOM, @PlannedStartTime, @PlannedEndTime, @Duration,
                              @ResourceId, @ManualSlotId,
                              @Status, @IsLocked, @IsCriticalPath, @TaskType,
                              @MTS_InstructionNo, @SetupSource, @StageCode,
                              @MESWorkOrderNo, @ProductionDepartmentId, @RouteCode, @PathId,
                              @StageExecutionBatchDraftKey, @StageExecutionBatchQty,
                              @CreatedAt, @UpdatedAt
                          )",
                        new
                        {
                            PlanVersionId    = planVersionId,
                            TaskNo           = taskNo,
                            OrderId          = voucher.OrderId,
                            MaterialId       = final.MaterialId,
                            OperationSeq     = final.OperationSeq,
                            OperationCode    = final.OperationCode,
                            Quantity         = final.Quantity,
                            // P0-06：1号位 FinalTask 的 PlannedProcessQty 原样落库（DB 列 v5.1.2 已加）
                            PlannedProcessQty = final.PlannedProcessQty,
                            UOM              = final.UOM,
                            PlannedStartTime = final.PlannedStartTime,
                            PlannedEndTime   = final.PlannedEndTime,
                            // Q8：Duration 补齐回写（表有列；单位=分钟，PlannedEnd-Start 计划区间跨度）
                            Duration         = (decimal)Math.Round((final.PlannedEndTime - final.PlannedStartTime).TotalMinutes, 4),
                            // S3c：人工槽合成键拆回 ResourceId=NULL + ManualSlotId；设备 Resource 原样落库（P0-06 不丢时间资源真相）
                            ResourceId       = persistResourceId,
                            ManualSlotId     = persistManualSlotId,
                            Status           = "PLANNED",
                            IsLocked         = false,
                            IsCriticalPath   = false,
                            TaskType         = final.TaskType,
                            // W3：连续份额 / 自由份额 Task 回填 MTS_InstructionNo（无 PI 需求 = null，不得下发 MES）
                            MTS_InstructionNo = mtsInstructionNo,
                            // SetupSource 增列（5号位 动作单 2026-09-21）：1号位 SetupOutcome 原样落库，2号位 不反推
                            SetupSource      = final.SetupSource,
                            // StageCode 增列（2026-09-21）：FinalTaskDraft.StageCode（=RoutingOperation.StageCode 大工艺）原样落库，
                            // 供 TaskDispatch 下发时区分「哪个 Stage 的指示」；空串落 NULL（与 MTS_InstructionNo 同口径）。
                            StageCode        = string.IsNullOrWhiteSpace(final.StageCode) ? null : final.StageCode,
                            // ── v5.1.8.3 新增列：真实身份 + MES 执行批（T-002/T-003 / DB-002 / 红线 Q1）──
                            MESWorkOrderNo   = mesWorkOrderNo,
                            ProductionDepartmentId = persistDepartmentId,
                            // 空串落 NULL（与 StageCode 同口径）；**不得**把 DEFAULT 当占位真值写进去
                            RouteCode        = string.IsNullOrWhiteSpace(final.RouteCode) ? null : final.RouteCode,
                            PathId           = persistPathId,
                            // C 桶由 1号位 给 Stage 执行批键（A/B 桶留空，见 09-29 加列脚本口径）；当前 1号位 尚未产出 ⇒ 恒 NULL
                            StageExecutionBatchDraftKey = string.IsNullOrWhiteSpace(final.StageExecutionBatchDraftKey) ? null : final.StageExecutionBatchDraftKey,
                            StageExecutionBatchQty      = final.StageExecutionBatchQty,
                            CreatedAt        = now,
                            UpdatedAt        = now
                        },
                        transaction: tx);

                    var taskId = ids.Single();
                    Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] Task插入成功: TaskId={taskId}, TaskNo={taskNo}");
                    finalDraftToTaskId[final.FinalDraftId] = taskId;

                    tasks.Add(new ApsTask
                    {
                        Id               = taskId,
                        PlanVersionId    = planVersionId,
                        TaskNo           = taskNo,
                        OrderId          = voucher.OrderId,
                        MaterialId       = final.MaterialId,
                        OperationSeq     = final.OperationSeq,
                        OperationCode    = final.OperationCode,
                        ResourceId       = persistResourceId,   // S3c：拆键后（人工槽→NULL）；设备保留 1号位 实际 Resource（供跨域占用块提取）
                        ManualSlotId     = persistManualSlotId,
                        // 真实路径身份（红线 Q1）：与上方 INSERT 同源逐字一致，**不再硬编码 DEFAULT/1**
                        RouteCode        = string.IsNullOrWhiteSpace(final.RouteCode) ? null : final.RouteCode,
                        PathId           = persistPathId,
                        MESWorkOrderNo   = mesWorkOrderNo,
                        ProductionDepartmentId = persistDepartmentId,
                        StageExecutionBatchDraftKey = string.IsNullOrWhiteSpace(final.StageExecutionBatchDraftKey) ? null : final.StageExecutionBatchDraftKey,
                        StageExecutionBatchQty      = final.StageExecutionBatchQty,
                        Quantity         = final.Quantity,
                        UOM              = final.UOM,
                        PlannedStartTime = final.PlannedStartTime,
                        PlannedEndTime   = final.PlannedEndTime,
                        Duration         = (decimal)Math.Round((final.PlannedEndTime - final.PlannedStartTime).TotalMinutes, 4),
                        Status           = "PLANNED",
                        IsLocked         = false,
                        IsCriticalPath   = false,
                        TaskType         = final.TaskType,
                        MTS_InstructionNo = mtsInstructionNo,
                        SetupSource      = final.SetupSource,
                        StageCode        = string.IsNullOrWhiteSpace(final.StageCode) ? null : final.StageCode,
                        CreatedAt        = now,
                        UpdatedAt        = now
                    });
                }

                // 3. INSERT Pegging 血缘（C1修复：使用 solveResult.PhysicalPeggingDrafts，键为 FinalDraftId）
                var peggingRows = solveResult.PhysicalPeggingDrafts
                    .Where(ppd =>
                        finalDraftToTaskId.ContainsKey(ppd.UpstreamFinalDraftId) &&
                        finalDraftToTaskId.ContainsKey(ppd.DownstreamFinalDraftId))
                    .Select(ppd => new
                    {
                        PlanVersionId        = planVersionId,
                        UpstreamTaskId       = finalDraftToTaskId[ppd.UpstreamFinalDraftId],
                        DownstreamTaskId     = finalDraftToTaskId[ppd.DownstreamFinalDraftId],
                        UpstreamMaterialId   = ppd.UpstreamMaterialId,
                        DownstreamMaterialId = ppd.DownstreamMaterialId,
                        Quantity             = ppd.Quantity,
                        UOM                  = ppd.UOM,
                        PeggingType          = "TASK_TO_TASK",
                        AllocatedQuantity    = ppd.Quantity,
                        InheritedPriority    = ppd.InheritedPriority,
                        AllocationReason     = (string?)null
                    })
                    .ToList();

                if (peggingRows.Count > 0)
                {
                    await conn.ExecuteAsync(
                        @"INSERT INTO [Pegging] (
                              PlanVersionId,
                              UpstreamTaskId, DownstreamTaskId,
                              UpstreamMaterialId, DownstreamMaterialId,
                              Quantity, UOM, PeggingType,
                              LeadTimeDays, IsCrossDomain,
                              AllocatedQuantity, InheritedPriority, AllocationReason,
                              CreatedAt
                          )
                          VALUES (
                              @PlanVersionId,
                              @UpstreamTaskId, @DownstreamTaskId,
                              @UpstreamMaterialId, @DownstreamMaterialId,
                              @Quantity, @UOM, @PeggingType,
                              0, 0,
                              @AllocatedQuantity, @InheritedPriority, @AllocationReason,
                              GETDATE()
                          )",
                        peggingRows,
                        transaction: tx);
                }

                // B5. INSERT AllocationTaskShare (v5.1.2冻结设计：轻量中间表，支持批次拆分多对多)
                // PM《回复0928-2》§三/§六 裁决：防重复不是「显示问题」而是 **Candidate 正确性的前置条件**
                // （本文件 ComputeChangeSeedKeysAsync 以 `SUM(ShareQty) BY DemandKey` 为变化基线）。
                // ⇒ Inv1~Inv4 纳入正式运行校验，违例 **fail-closed**（不落库），不得静默跳过。
                ValidateAllocationShareInvariants(solveResult, voucher, finalDraftToTaskId);

                var seqToShareId = new Dictionary<long, long>();
                if (solveResult.AllocationShares.Count > 0)
                {
                    foreach (var share in solveResult.AllocationShares)
                    {
                        if (!finalDraftToTaskId.TryGetValue(share.FinalDraftId, out var taskId))
                            continue;

                        var allocation = voucher.SupplyAllocations
                            .FirstOrDefault(a => a.AllocationSequence == share.AllocationSequence);

                        if (allocation == null)
                        {
                            _logger.LogWarning(
                                "[PeggingPersist] AllocationSequence={Seq} 未找到对应的SupplyAllocation",
                                share.AllocationSequence);
                            continue;
                        }

                        // P0-06：TaskShare 真实 Demand 归属来自该 AllocationSequence 对应的 Allocation 自身，
                        // 不再用批次第一单 voucher.OrderId；RootOrderId 从 DemandKey 反解真实 OrderId。
                        var rootOrderId = ExtractOrderIdFromDemandKey(allocation.DemandKey);

                        var shareId = await conn.ExecuteScalarAsync<long>(
                            @"INSERT INTO AllocationTaskShare (
                                  PlanVersionId, AllocationSequence, DemandType, DemandKey,
                                  RootOrderId, TaskId, ShareQty, CreatedAt
                              ) OUTPUT INSERTED.Id
                              VALUES (
                                  @PlanVersionId, @AllocationSequence, @DemandType, @DemandKey,
                                  @RootOrderId, @TaskId, @ShareQty, @CreatedAt
                              )",
                            new
                            {
                                PlanVersionId = planVersionId,
                                AllocationSequence = share.AllocationSequence,
                                DemandType = "ORDER",
                                DemandKey = allocation.DemandKey,
                                RootOrderId = rootOrderId,
                                TaskId = taskId,
                                ShareQty = share.ComponentQty,
                                CreatedAt = now
                            },
                            transaction: tx);
                        seqToShareId[share.AllocationSequence] = shareId;
                    }
                }

                // B6. INSERT PeggingSupplyAllocation (仅对非Task供给)
                // v5.1.2: 直接使用allocation.AllocationSequence（在供需扣减时已生成）
                var nonTaskAllocations = voucher.SupplyAllocations
                    .Where(a => a.SourceType != Core.Enum.SupplySourceType.NEW_REQUIREMENT)
                    .ToList();

                Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] 准备INSERT PeggingSupplyAllocation，nonTaskAllocations={nonTaskAllocations.Count}");

                // 查询ScheduleRunId（B6和B7都需要）
                var scheduleRunId = await conn.ExecuteScalarAsync<int?>(
                    "SELECT SourceScheduleRunId FROM PlanVersion WHERE Id = @PlanVersionId",
                    new { PlanVersionId = planVersionId },
                    transaction: tx) ?? 0;

                if (nonTaskAllocations.Count > 0)
                {
                    var supplyMaterialIds = nonTaskAllocations.Select(a => a.SupplyMaterialId).Distinct().ToList();
                    var materialMap = new Dictionary<int, string>();
                    for (var i = 0; i < supplyMaterialIds.Count; i += SqlServerInParameterLimit)
                    {
                        var chunk = supplyMaterialIds.Skip(i).Take(SqlServerInParameterLimit).ToList();
                        var batch = await conn.QueryAsync(
                            "SELECT Id, MaterialCode FROM Material WHERE Id IN @Ids",
                            new { Ids = chunk },
                            transaction: tx);
                        foreach (var r in batch)
                            materialMap[(int)r.Id] = (string)r.MaterialCode;
                    }

                    // PM《Stage、生产部门、Routing、Dependency、StageLeadTimeParam 接口裁决回复》§十一（2026-09-28）：
                    //   `RequiredStageCode` **两处保留** —— ① 运行输入 `LogicalProductionDemand.RequiredStageCode`（Solver 用，已回填）
                    //   ② Pegging 结果 `PeggingSupplyAllocation.NextRequiredStageCode`（追溯「为什么这个 Supply 可以承接这个需求」）。
                    //
                    // 【2026-09-29 修正】取值改为**按承接行自身物料直查**（主），`DemandKey` 关联降为兜底。
                    //   原实现只走 DemandKey ⇒ 经 `LogicalProductionDemand` 中转，而 LPD **只为「有生产缺口」的需求生成**
                    //   ⇒ 纯供给承接（库存/在途直接接走）的行取不到值。实测 PlanVersionId=540：**1,205 行只填出 40 行**，
                    //   而 BOM 侧有 30,652 个物料带阈值 ⇒ 缺的是取值路径，不是数据。
                    //   `SupplyAllocationItem` 无需求物料字段，但 `ValidateEligibility` 红线1 保证供需同物料
                    //   ⇒ 落库行的 `MaterialId`（= supply.MaterialId）即需求物料，可直接命中物料级映射。
                    var requiredStageByDemandKey = voucher.LogicalProductionDemands
                        .Where(d => !string.IsNullOrEmpty(d.RequiredStageCode))
                        .GroupBy(d => d.DemandKey, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.First().RequiredStageCode!, StringComparer.Ordinal);

                    var supplyRows = nonTaskAllocations
                        .Select(a =>
                        {
                            materialMap.TryGetValue(a.SupplyMaterialId, out var materialCode);
                            return new
                            {
                                PlanVersionId          = planVersionId,
                                ScheduleRunId          = scheduleRunId,
                                AllocationSequence     = a.AllocationSequence,
                                RootOrderId            = ExtractOrderIdFromDemandKey(a.DemandKey),
                                MaterialId             = a.SupplyMaterialId,
                                MaterialCode           = materialCode ?? string.Empty,
                                DemandFactoryCode      = a.FactoryCode,
                                DemandQty              = a.DemandQuantity,
                                AllocatedQty           = a.AllocatedQuantity,
                                SupplyType             = a.SourceType.ToString(),
                                SupplyFactoryCode      = a.FactoryCode,
                                KnownAvailableTime     = a.AvailableAt,
                                SupplyDocumentNo       = a.SourceReference,
                                // 主：按承接行自身物料查物料级映射（覆盖全部承接关系）
                                // 兜底：按 DemandKey 查运行输入侧已填值（防御性，二者同源，正常结果一致）
                                NextRequiredStageCode  = voucher.RequiredStageByMaterialId.TryGetValue(a.SupplyMaterialId, out var reqStage)
                                                             ? reqStage
                                                             : (requiredStageByDemandKey.TryGetValue(a.DemandKey, out var reqStageByKey)
                                                                 ? reqStageByKey
                                                                 : null),
                                CreatedAt              = now
                            };
                        })
                        .ToList();

                    await conn.ExecuteAsync(
                        @"INSERT INTO PeggingSupplyAllocation (
                              PlanVersionId, ScheduleRunId, AllocationSequence,
                              RootOrderId, MaterialId, MaterialCode,
                              DemandFactoryCode, DemandQty, AllocatedQty,
                              SupplyType, SupplyFactoryCode,
                              KnownAvailableTime, SupplyDocumentNo, NextRequiredStageCode, CreatedAt
                          ) VALUES (
                              @PlanVersionId, @ScheduleRunId, @AllocationSequence,
                              @RootOrderId, @MaterialId, @MaterialCode,
                              @DemandFactoryCode, @DemandQty, @AllocatedQty,
                              @SupplyType, @SupplyFactoryCode,
                              @KnownAvailableTime, @SupplyDocumentNo, @NextRequiredStageCode, @CreatedAt
                          )",
                        supplyRows,
                        transaction: tx);

                    Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] PeggingSupplyAllocation INSERT成功: {supplyRows.Count} 条");
                }

                // B7. INSERT ScheduleExplanationFact (v5.1.2: 排程决策解释事实)
                if (solveResult.ExplanationFacts.Count > 0)
                {
                    var factRows = solveResult.ExplanationFacts
                        .Select(fact =>
                        {
                            finalDraftToTaskId.TryGetValue(fact.FinalDraftId, out var taskId);
                            return new
                            {
                                PlanVersionId = planVersionId,
                                ScheduleRunId = scheduleRunId,
                                ObjectType = fact.ObjectType,
                                OrderId = fact.OrderId,
                                TaskId = taskId,
                                ResourceId = fact.ResourceId,
                                StageCode = fact.StageCode ?? string.Empty,
                                ReasonCode = fact.ReasonCode,
                                Severity = fact.Severity ?? string.Empty,
                                ImpactHours = fact.ImpactHours ?? 0m,
                                EvidenceJson = fact.EvidenceJson,
                                CreatedAt = now
                            };
                        })
                        .Where(f => f.TaskId > 0)
                        .ToList();

                    if (factRows.Count > 0)
                    {
                        await conn.ExecuteAsync(
                            @"INSERT INTO [APS_Production].[dbo].[ScheduleExplanationFact] (
                                  PlanVersionId, ScheduleRunId, ObjectType, OrderId, TaskId,
                                  ResourceId, StageCode, ReasonCode, Severity, ImpactHours,
                                  EvidenceJson, CreatedAt
                              ) VALUES (
                                  @PlanVersionId, @ScheduleRunId, @ObjectType, @OrderId, @TaskId,
                                  @ResourceId, @StageCode, @ReasonCode, @Severity, @ImpactHours,
                                  @EvidenceJson, @CreatedAt
                              )",
                            factRows,
                            transaction: tx);

                        Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] ScheduleExplanationFact INSERT成功: {factRows.Count} 条");
                    }
                }

                // B8. 真无解 Unscheduled 闭合记账（PM 2026-09-11：产能不足≠排不下，Unscheduled 只兜「真无解」异常）
                //   1号位 UnscheduledTaskResult.DraftId = LogicalDemandKey；2号位 反查 NetOutputQty，
                //   复用 ScheduleExplanationFact（不加表），UnscheduledQty = NetOutputQty − Σ已排TaskShare
                //   （V1 不引入「已排份额+Unscheduled剩余」部分切分模型，真无解=整单记账，ΣTaskShare=0）；
                //   数量/血缘键进 EvidenceJson，TaskId=null（无对应 FinalTask）。
                if (solveResult.UnscheduledTasks.Count > 0)
                {
                    var demandByKey = voucher.LogicalProductionDemands
                        .Where(d => !string.IsNullOrEmpty(d.LogicalDemandKey))
                        .GroupBy(d => d.LogicalDemandKey)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                    // 不 Drop 未反查命中的 DraftId（数量不得静默消失）：demand 为 null 时 NetOutputQty 记 0，
                    // 但 EvidenceJson 仍带 LogicalDemandKey 可追溯，问题显性化。
                    var unscheduledRows = solveResult.UnscheduledTasks
                        .Select(u =>
                        {
                            demandByKey.TryGetValue(u.DraftId, out var demand);
                            var netOutputQty = demand?.NetOutputQty ?? 0m;
                            var evidence = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                LogicalDemandKey   = u.DraftId,
                                MaterialId         = demand?.MaterialId,
                                DomainKey          = voucher.DomainKey,
                                AllocationSequence = demand?.AllocationSequence,
                                NetOutputQty       = netOutputQty,
                                UnscheduledQty     = netOutputQty
                            });
                            return new
                            {
                                PlanVersionId = planVersionId,
                                ScheduleRunId = scheduleRunId,
                                ObjectType    = "DEMAND",
                                OrderId       = demand?.OrderId,
                                TaskId        = (long?)null,
                                ResourceId    = (int?)null,
                                StageCode     = demand?.StartStageCode ?? string.Empty,
                                ReasonCode    = string.IsNullOrWhiteSpace(u.Reason) ? "UNSCHEDULABLE" : u.Reason,
                                Severity      = "ERROR",
                                ImpactHours   = (decimal?)null,
                                EvidenceJson  = evidence,
                                CreatedAt     = now
                            };
                        })
                        .ToList();

                    if (unscheduledRows.Count > 0)
                    {
                        await conn.ExecuteAsync(
                            @"INSERT INTO [APS_Production].[dbo].[ScheduleExplanationFact] (
                                  PlanVersionId, ScheduleRunId, ObjectType, OrderId, TaskId,
                                  ResourceId, StageCode, ReasonCode, Severity, ImpactHours,
                                  EvidenceJson, CreatedAt
                              ) VALUES (
                                  @PlanVersionId, @ScheduleRunId, @ObjectType, @OrderId, @TaskId,
                                  @ResourceId, @StageCode, @ReasonCode, @Severity, @ImpactHours,
                                  @EvidenceJson, @CreatedAt
                              )",
                            unscheduledRows,
                            transaction: tx);

                        Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] Unscheduled 闭合记账 ScheduleExplanationFact INSERT成功: {unscheduledRows.Count} 条");
                    }
                }

                // B9. 跨版本连续性 Execution Over-Commit / Demand Mismatch 异常登记（G6，§9.1/§9.2）
                //   复用 ScheduleExplanationFact（与 Q7 Unscheduled 同表，不加表）：E>Q 执行超量 / Q=0 仍开工，
                //   均不产 Task、只登记异常事实供后续对账；ObjectType="DEMAND"、TaskId=null、Severity 按 Kind。
                if (overCommits.Count > 0)
                {
                    var overCommitRows = overCommits
                        .Select(e => new
                        {
                            PlanVersionId = planVersionId,
                            ScheduleRunId = scheduleRunId,
                            ObjectType    = "DEMAND",
                            OrderId       = e.OrderId,
                            TaskId        = (long?)null,
                            ResourceId    = (int?)null,
                            StageCode     = e.StartStageCode,
                            ReasonCode    = e.Kind,
                            Severity      = e.Severity,
                            ImpactHours   = (decimal?)null,
                            EvidenceJson  = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                ProductionInstructionNo = e.ProductionInstructionNo,
                                LogicalDemandKey        = e.LogicalDemandKey,
                                DomainKey               = e.DomainKey,
                                AllocationSequence      = e.AllocationSequence,
                                MaterialId              = e.MaterialId,
                                Quantity                = e.Quantity,
                                ContinuationQty         = e.ContinuationQty,
                                ExcessQty               = e.ExcessQty,
                                Kind                    = e.Kind
                            }),
                            CreatedAt = now
                        })
                        .ToList();

                    await conn.ExecuteAsync(
                        @"INSERT INTO [APS_Production].[dbo].[ScheduleExplanationFact] (
                              PlanVersionId, ScheduleRunId, ObjectType, OrderId, TaskId,
                              ResourceId, StageCode, ReasonCode, Severity, ImpactHours,
                              EvidenceJson, CreatedAt
                          ) VALUES (
                              @PlanVersionId, @ScheduleRunId, @ObjectType, @OrderId, @TaskId,
                              @ResourceId, @StageCode, @ReasonCode, @Severity, @ImpactHours,
                              @EvidenceJson, @CreatedAt
                          )",
                        overCommitRows,
                        transaction: tx);

                    Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] Over-Commit 异常登记 ScheduleExplanationFact INSERT成功: {overCommitRows.Count} 条");
                }

                _logger.LogInformation(
                    "[Pegging] 统一事务提交: Task={Tasks}, Pegging={Pegging} (PlanVersionId={PlanVersionId})",
                    tasks.Count, peggingRows.Count, planVersionId);
                Console.WriteLine($"[PersistDomainAndPeggingInTransactionAsync] 事务即将返回: Tasks={tasks.Count}, Pegging={peggingRows.Count}");

                return (tasks, peggingRows.Count, nonTaskAllocations.Count);
            },
            db: DatabaseId.APS);
    }

    /// <summary>
    /// 结果红线校验（PM 口径，6 项全）。
    /// 在 Solver 与落库之前调用，返回错误列表；空列表 = 通过。
    /// ① DemandQuantity 由 TraverseBomNode 逐节点累计；③ PhysicalSourceKey 由 SupplyPool.Add 填充；
    /// ⑤⑥ SH 读 ShippingInstructionNo（INTER_FACTORY_ORDER 分配落 SH No）+ 池内 SH 段 OriginalQty（=指令总量 Order.Quantity）。
    /// </summary>
    private static List<string> ValidatePeggingResult(SupplyPool pool, PeggingResultVoucher voucher)
    {
        var errors = new List<string>();

        // 1. Demand 闭合：Σ(已分配) + 短缺 不得超过需求总量（DemandQuantity 为 0 表示无需求节点，跳过）
        if (voucher.DemandQuantity > 0m)
        {
            var totalAllocated = voucher.SupplyAllocations.Sum(a => a.AllocatedQuantity);
            if (totalAllocated + voucher.ShortageQuantity > voucher.DemandQuantity)
                errors.Add($"Demand 闭合失败: 已分配 {totalAllocated} + 短缺 {voucher.ShortageQuantity} > 需求 {voucher.DemandQuantity}");
        }

        // 2. SupplyBalance 非负：任一供给条目 RemainingQty < 0 即超额消费
        var overConsumed = pool.GetAllEntries().Where(e => e.RemainingQty < 0m).ToList();
        if (overConsumed.Count > 0)
            errors.Add($"SupplyBalance 为负: {overConsumed.Count} 条供给被超额消费（RemainingQty < 0）");

        // 3. 同物理 Supply 不重复消费：同一 PhysicalSourceKey 的已分配量之和不得超过原始量。
        //    PhysicalSourceKey 由 SupplyPool.Add 按来源填充（PI→PI号 / PO→PO:Line / 库存→INV:Id）；无键（虚拟供给）时跳过。
        foreach (var group in pool.GetAllEntries()
                     .Where(e => !string.IsNullOrWhiteSpace(e.PhysicalSourceKey))
                     .GroupBy(e => e.PhysicalSourceKey!))
        {
            foreach (var entry in group)
            {
                var consumed = entry.Allocations.Sum(a => a.AllocatedQty);
                if (consumed > entry.OriginalQty)
                    errors.Add($"物理供给 {group.Key} 被重复消费: 已分配 {consumed} > 原始量 {entry.OriginalQty}");
            }
        }

        // 4. Allocation 合法
        var items = voucher.SupplyAllocations;
        if (items.Any(a => a.AllocatedQuantity <= 0m))
            errors.Add("存在非法分配量：AllocatedQuantity <= 0");
        var seqs = items.Select(a => a.AllocationSequence).ToList();
        if (seqs.Any(s => s <= 0))
            errors.Add("存在非法 AllocationSequence（<= 0）");
        if (seqs.Distinct().Count() != seqs.Count)
            errors.Add("AllocationSequence 存在重复");
        for (var i = 1; i < seqs.Count; i++)
        {
            if (seqs[i] <= seqs[i - 1])
            {
                errors.Add("AllocationSequence 未单调递增");
                break;
            }
        }
        if (items.Any(a => !Enum.IsDefined(typeof(SupplySourceType), a.SourceType)))
            errors.Add("存在非法 SourceType");

        // 5/6. SH 同 SH 匹配不串 SH / 份额不重复计量：读 ShippingInstructionNo（SH 分配落 SH No）+ 池内 SH 段总量（=Order.Quantity 指令总量）
        var shipmentTotals = pool.GetAllEntries()
            .Where(e => e.SourceType == Core.Enum.SupplySourceType.INTER_FACTORY_ORDER
                     && !string.IsNullOrWhiteSpace(e.PhysicalSourceKey))
            .GroupBy(e => e.PhysicalSourceKey!)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.OriginalQty), StringComparer.Ordinal);
        errors.AddRange(ValidateShConsistency(voucher.SupplyAllocations, shipmentTotals));

        return errors;
    }

    /// <summary>
    /// 红线⑤⑥ SH 校验（纯函数，可单测）。
    /// ⑤ 同一需求（DemandKey）的 SH 分配必须指向同一出荷指示号（不串 SH）；
    /// ⑥ 同一 SH 分配合计不得超过其指令总量（Order.Quantity，shipmentTotals）。
    /// </summary>
    internal static List<string> ValidateShConsistency(
        IReadOnlyList<SupplyAllocationItem> allocations,
        IReadOnlyDictionary<string, decimal> shipmentTotals)
    {
        var errors = new List<string>();
        var shAllocations = allocations
            .Where(a => !string.IsNullOrWhiteSpace(a.ShippingInstructionNo))
            .ToList();
        if (shAllocations.Count == 0) return errors;

        // 5. 不串 SH
        foreach (var group in shAllocations.GroupBy(a => a.DemandKey))
        {
            var shNos = group.Select(a => a.ShippingInstructionNo).Distinct(StringComparer.Ordinal).ToList();
            if (shNos.Count > 1)
                errors.Add($"SH 串单: 需求 {group.Key} 跨出荷指示 {string.Join(", ", shNos)}");
        }

        // 6. 份额不重复计量
        foreach (var group in shAllocations.GroupBy(a => a.ShippingInstructionNo!))
        {
            var consumed = group.Sum(a => a.AllocatedQuantity);
            var total = shipmentTotals.TryGetValue(group.Key, out var t) ? t : 0m;
            if (consumed > total)
                errors.Add($"SH 份额重复计量: {group.Key} 已分配 {consumed} > 实际发生量 {total}");
        }

        return errors;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 私有辅助方法
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record BomEdge(
        string ParentCode,
        string ChildCode,
        int ChildMaterialId,
        decimal Qty,
        int Level,
        bool IsLeaf,
        bool IsPurchased,
        string? ChildRequiredStageCode);

    private sealed record BomSnapshot(
        ILookup<string, BomEdge> ByParent,
        IReadOnlyDictionary<string, int> LLCByMaterial,
        IReadOnlyDictionary<string, bool> IsPurchasedByMaterial,
        int EdgeCount,
        /// <summary>
        /// 本域「BOM 可达物料宇宙」= 本次快照里全部 <c>ParentMaterialCode ∪ ChildMaterialCode</c>。
        ///
        /// 用途：<c>LoadPiRemainingFactsAsync</c> 不再只从 <c>[Order]</c> 取 PI 事实 —— <c>[Order]</c> 的装载带
        /// 产品族闸门（<c>m.ProductFamilyId = @ProductFamilyId</c>），而物料归族规则只覆盖**成品**工艺码，
        /// 下阶加工件一律无族 ⇒ 它们的 MTS 生产指示全被挡在 <c>[Order]</c> 之外，PI 供给池在下阶永远是空的
        /// （实测 PV2：<c>[Order]</c> 22,144 行 / 9,857 物料 **100% 是 FINAL_FG-* 且 0 个是 BOM 子件**）。
        /// PI 事实是**供给事实**，不该被「顶层需求归域」这件事限死，故按本域物料宇宙从 <c>Order_Canonical</c> 补取。
        /// </summary>
        IReadOnlySet<string> MaterialUniverse);

    private sealed class BomRawRow
    {
        public string ParentMaterialCode      { get; set; } = string.Empty;
        public string ChildMaterialCode       { get; set; } = string.Empty;
        public int ChildMaterialId            { get; set; }
        public decimal Quantity               { get; set; }
        public int Level                      { get; set; }
        public int? LLC                       { get; set; }
        public bool IsLeaf                    { get; set; }
        public bool IsPurchased               { get; set; }
        public string? ChildRequiredStageCode { get; set; }
    }

    /// <summary>
    /// <c>MaterialSupplyContext</c> 的 (MaterialCode, SupplyMode) 装载行。
    /// 用途见 <c>LoadBomSnapshotAsync</c> 的「采购/自制判定源修正」：`Material.IsPurchased` 是死列，
    /// `SupplyMode='PURCHASE'` 才是生产上唯一有值的采购判据。
    /// </summary>
    private sealed class MaterialSupplyModeRow
    {
        public string MaterialCode { get; set; } = string.Empty;
        public string SupplyMode   { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 供给池内部数据结构
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 内存供给账本。PeggingLoop 遍历时直接在此对象上扣减，不回写数据库。
    /// 最终扣减结果通过 SupplyAllocationItem 列表落库到 PeggingSupplyAllocation。
    /// </summary>
    private sealed class SupplyPool
    {
        // Key: "MaterialCode|FactoryId"
        private readonly Dictionary<string, List<SupplyLedgerEntry>> _ledger
            = new(StringComparer.Ordinal);

        /// <summary>冻结策略快照（Inventory/PI/Procurement 三类排序参数）；缺省时按稳定兜底排序</summary>
        private readonly FrozenStrategySnapshot? _snapshot;

        public int TotalEntries { get; private set; }

        /// <summary>本次运行供给快照的数据截止时点（= ScheduleRun.DataCutoffTime；编排层 Set，缺省回退 Now）。</summary>
        public DateTime DataCutoffTime { get; set; }

        /// <summary>冻结 DefaultPurchaseLt（天）→ 规划采购占位 AvailableTime = DataCutoffTime + 该天数（P0-07）。</summary>
        public int DefaultPurchaseLtDays => _snapshot == null ? 0 : BuildFrozenFactParameters(_snapshot).DefaultPurchaseLt;

        /// <summary>
        /// Planning Yield（Material 级默认，清单 27）→ PlannedProcessQty 反算用（P1-02）。
        /// YieldPercent 语义为百分数（0 &lt; YieldPercent &lt;= 100，见 PlanningYieldRule 注释 + 3号位校验）。
        /// 返回 100 = 无损兜底（未配置快照 / 无匹配规则 / 越界），调用侧据此不放大。
        /// 当前仅匹配空 StageCode 的 Material 级默认；StageCode 级精确规则待 startStageCode 接入工艺路由后补。
        /// </summary>
        public decimal PlanningYieldPercent(string materialCode)
        {
            if (_snapshot?.Procurement.PlanningYields is not { Count: > 0 } yields) return 100m;
            foreach (var rule in yields)
            {
                if (!string.Equals(rule.MaterialId, materialCode, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(rule.StageCode)) continue;
                if (rule.YieldPercent <= 0m || rule.YieldPercent > 100m) continue;
                return rule.YieldPercent;
            }
            return 100m;
        }

        public SupplyPool(FrozenStrategySnapshot? snapshot = null) => _snapshot = snapshot;

        public SupplyLedgerEntry Add(
            string materialCode, int materialId, int factoryId, decimal qty,
            DateTime? availableAt, Core.Enum.SupplySourceType sourceType,
            string? sourceRef, string factoryCode, long? supplySourceId = null,
            SupplyConfidence confidence = SupplyConfidence.CONFIRMED,
            SupplyCommitment commitment = SupplyCommitment.COMMITTED,
            SupplySortFacts? sort = null,
            string? physicalSourceKey = null)
        {
            var key = BuildKey(materialCode, factoryId);
            if (!_ledger.TryGetValue(key, out var list))
            {
                list = new List<SupplyLedgerEntry>();
                _ledger[key] = list;
            }
            var entry = new SupplyLedgerEntry
            {
                OriginalQty     = qty,
                RemainingQty    = qty,
                MaterialId      = materialId,
                AvailableAt     = availableAt,
                SourceType      = sourceType,
                SourceReference = sourceRef,
                PhysicalSourceKey = physicalSourceKey,
                FactoryCode     = factoryCode,
                FactoryId       = factoryId,
                SupplySourceId  = supplySourceId,
                Confidence      = confidence,
                Commitment      = commitment,
                WarehouseCode   = sort?.WarehouseCode,
                ReleaseDate     = sort?.ReleaseDate,
                PoNo            = sort?.PoNo,
                LineNo          = sort?.LineNo,
                PiNo            = sort?.PiNo,
                IssueDate       = sort?.IssueDate,
                CreatedAt       = sort?.CreatedAt
            };
            list.Add(entry);
            TotalEntries++;
            return entry;
        }

        /// <summary>
        /// 返回指定物料+工厂在当前 Demand 业务身份下允许进入的供给条目（PM 2026-08-28 最终裁决：
        /// 不存在 Inventory/PI/Procurement 三类全局优先级；先定允许供给集合，再类内各自排序）：
        ///   - 允许库存时（includeInventory，即 BOM 下阶）：Inventory 按 Warehouse Priority（SupplyBlock.Inventory）
        ///   - 自制件（isPurchased=false）：PI 按 Issue/Create Time ASC → Stable PiNo（SupplyBlock.PiSort）
        ///   - 采购件（isPurchased=true）：Procurement 按 Warehouse Priority → AvailableTime → ReleaseDate → PO+Line
        ///                               （固定链，不可重排；参数取 ProcurementBlock）
        /// 跨厂 Transit/Received（绑定消费）与缺口（Placeholder/Planned）不进入本排序。
        /// 未配置/字段缺失时自动降级为稳定兜底（SourceReference），不引入随机顺序。
        /// </summary>
        public IReadOnlyList<SupplyLedgerEntry> GetEntries(string materialCode, int factoryId, bool isPurchased, bool includeInventory, bool includePi = true)
        {
            var key = BuildKey(materialCode, factoryId);
            if (!_ledger.TryGetValue(key, out var list)) return Array.Empty<SupplyLedgerEntry>();

            var ordered = new List<SupplyLedgerEntry>(list.Count);
            if (includeInventory)
                ordered.AddRange(SortInventory(list));
            if (isPurchased)
                ordered.AddRange(SortProcurement(list));
            else
            {
                // T2-02：PI 只在 BOM 下阶参与承接（includePi = bomLevel > 0）。
                // 顶层（L0）需求本身的生产是**组装工序**，它找不到 PI —— 部品出口的出荷指示没有对应的组装指示，
                // 出荷指示↔组装指示的挂钩由生管做，APS 不得自造（用户 2026-10-09 口径）。
                // 顶层生产需求的 PI 身份 = 该行挂着的 PI（生管挂好的），没有就用订单号（出荷指示号）。
                if (includePi)
                    ordered.AddRange(SortPi(list));
                // 跨 Domain：上游域生产输出作为分段虚拟供给（§8/D12），按可用时间升序
                ordered.AddRange(SortUpstreamDomainProduction(list));
            }
            return ordered;
        }

        /// <summary>
        /// INTER_FACTORY_ORDER（厂间出荷指示，SH级）供给：按 AvailableAt 升序。
        /// 不进入 GetEntries 三类通用排序（PM 裁决：跨厂 SH 绑定消费，走独立消费路径）。
        /// 每个 SH 是单一供给身份（PhysicalSourceKey=SH No），量=Order.Quantity（指令总量，整单一次入库）。
        /// </summary>
        public IReadOnlyList<SupplyLedgerEntry> GetInterFactoryEntries(string materialCode, int factoryId)
        {
            var key = BuildKey(materialCode, factoryId);
            if (!_ledger.TryGetValue(key, out var list)) return Array.Empty<SupplyLedgerEntry>();

            return list
                .Where(e => e.SourceType == Core.Enum.SupplySourceType.INTER_FACTORY_ORDER)
                .OrderBy(e => e.AvailableAt ?? DateTime.MaxValue)
                .ThenBy(e => e.SourceReference ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private IReadOnlyList<SupplyLedgerEntry> SortUpstreamDomainProduction(IReadOnlyList<SupplyLedgerEntry> list)
            => list
                .Where(e => e.SourceType == Core.Enum.SupplySourceType.UPSTREAM_DOMAIN_PRODUCTION)
                .OrderBy(e => e.AvailableAt ?? DateTime.MaxValue)
                .ThenBy(e => e.SourceReference ?? string.Empty, StringComparer.Ordinal)
                .ToList();

        private IReadOnlyList<SupplyLedgerEntry> SortInventory(IReadOnlyList<SupplyLedgerEntry> list)
        {
            var rankMap = BuildRankMap(_snapshot?.Supply.Inventory.WarehousePriority);
            return list
                .Where(e => e.SourceType == Core.Enum.SupplySourceType.INVENTORY)
                .OrderBy(e => WarehouseRank(e.WarehouseCode, rankMap))
                .ThenBy(e => e.SourceReference ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private IReadOnlyList<SupplyLedgerEntry> SortPi(IReadOnlyList<SupplyLedgerEntry> list)
        {
            var sortBy = _snapshot?.Supply.PiSort?.SortBy ?? PiSortBy.IssueDateAsc;
            var tieBreak = _snapshot?.Supply.PiSort?.UseStablePiNoTieBreak ?? false;

            var eligible = list.Where(e => e.SourceType == Core.Enum.SupplySourceType.WIP
                                        || e.SourceType == Core.Enum.SupplySourceType.PRODUCTION_INSTRUCTION);

            if (sortBy == PiSortBy.StablePiNoAsc)
                return eligible
                    .OrderBy(e => e.PiNo ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(e => e.SourceReference ?? string.Empty, StringComparer.Ordinal)
                    .ToList();

            var ordered = eligible.OrderBy(e => PiSortTime(e, sortBy));
            if (tieBreak)
                ordered = ordered.ThenBy(e => e.PiNo ?? string.Empty, StringComparer.Ordinal);
            return ordered.ThenBy(e => e.SourceReference ?? string.Empty, StringComparer.Ordinal).ToList();
        }

        private IReadOnlyList<SupplyLedgerEntry> SortProcurement(IReadOnlyList<SupplyLedgerEntry> list)
        {
            // 固定链（PM 裁决）：Eligibility → Warehouse Priority → AvailableTime → ReleaseDate → PO+Line，
            // 不可重排、无 Enable/Disable 开关；3号位只治理参数值（此处取 WarehousePriority）。
            var rankMap = BuildRankMap(_snapshot?.Procurement.WarehousePriority);
            return list
                .Where(e => e.SourceType == Core.Enum.SupplySourceType.PIPELINE
                         || e.SourceType == Core.Enum.SupplySourceType.PURCHASE_ORDER)
                .OrderBy(e => WarehouseRank(e.WarehouseCode, rankMap))
                .ThenBy(e => e.AvailableAt ?? DateTime.MaxValue)
                .ThenBy(e => e.ReleaseDate ?? DateTime.MaxValue)
                .ThenBy(e => e.PoNo ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(e => e.LineNo ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(e => e.SourceReference ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private static IReadOnlyDictionary<string, int> BuildRankMap(IReadOnlyList<string>? priority)
            => (priority ?? [])
                .Select((code, i) => new { code, i })
                .ToDictionary(x => x.code, x => x.i, StringComparer.OrdinalIgnoreCase);

        private static int WarehouseRank(string? code, IReadOnlyDictionary<string, int> rankMap)
            => code != null && rankMap.TryGetValue(code, out var i) ? i : int.MaxValue;

        private static DateTime PiSortTime(SupplyLedgerEntry e, PiSortBy sortBy) => sortBy switch
        {
            PiSortBy.CreatedAtAsc => e.CreatedAt ?? DateTime.MaxValue,
            _                     => e.IssueDate ?? DateTime.MaxValue  // IssueDateAsc（默认）；StablePiNoAsc 已在 SortPi 上游单独处理
        };

        /// <summary>返回所有供给条目（用于加载Lock数据）</summary>
        public IEnumerable<SupplyLedgerEntry> GetAllEntries()
        {
            return _ledger.Values.SelectMany(list => list);
        }

        public static string BuildKey(string materialCode, int factoryId)
            => $"{materialCode}|{factoryId}";

        /// <summary>
        /// 深拷贝供给账本（阶段2 S2.3 双跑隔离）。旧 DFS 与新 BFS 必须各自跑一份独立供给账本，
        /// 否则先跑的一遍会把共享 Inventory/PI/Procurement 扣光，后跑的一遍无可比性。
        /// 调用时机 = LoadSupplyPoolAsync（含 LoadActiveLockDataAsync 硬锁装载）之后、Pegging 扣减之前。
        /// </summary>
        public SupplyPool Clone()
        {
            var copy = new SupplyPool(_snapshot) { DataCutoffTime = DataCutoffTime };
            foreach (var (key, list) in _ledger)
                copy._ledger[key] = list.Select(CloneEntry).ToList();
            copy.TotalEntries = TotalEntries;
            return copy;
        }

        private static SupplyLedgerEntry CloneEntry(SupplyLedgerEntry e) => new()
        {
            OriginalQty        = e.OriginalQty,
            RemainingQty       = e.RemainingQty,
            LockedQty          = e.LockedQty,
            SupplyKey          = e.SupplyKey,
            PhysicalSourceKey  = e.PhysicalSourceKey,
            MaterialId         = e.MaterialId,
            FactoryId          = e.FactoryId,
            FactoryCode        = e.FactoryCode,
            SourceType         = e.SourceType,
            AvailableAt        = e.AvailableAt,
            SourceReference    = e.SourceReference,
            SupplySourceId     = e.SupplySourceId,
            WarehouseCode      = e.WarehouseCode,
            ReleaseDate        = e.ReleaseDate,
            PoNo               = e.PoNo,
            LineNo             = e.LineNo,
            PiNo               = e.PiNo,
            IssueDate          = e.IssueDate,
            CreatedAt          = e.CreatedAt,
            Confidence         = e.Confidence,
            Commitment         = e.Commitment,
            Allocations        = e.Allocations.Select(a => new AllocationRecord
            {
                AllocationSequence = a.AllocationSequence,
                AllocatedQty       = a.AllocatedQty,
                SupplyKey          = a.SupplyKey,
                SupplyType         = a.SupplyType,
                DemandKey          = a.DemandKey,
                MaterialId         = a.MaterialId,
                AllocatedAt        = a.AllocatedAt,
                RequiresProduction = a.RequiresProduction
            }).ToList(),
            Locks              = e.Locks.Select(l => new LockRecord
            {
                LockType          = l.LockType,
                LockedQty         = l.LockedQty,
                LockedToOrderId   = l.LockedToOrderId,
                LockedToDemandKey = l.LockedToDemandKey,
                LockedAt          = l.LockedAt
            }).ToList()
        };
    }

    /// <summary>供给排序事实（从 SupplyLoadRow 抽取，供多键排序使用；字段缺失为 null，排序时降级兜底）</summary>
    private sealed record SupplySortFacts(
        string? WarehouseCode = null,
        DateTime? ReleaseDate = null,
        string? PoNo = null,
        string? LineNo = null,
        string? PiNo = null,
        DateTime? IssueDate = null,
        DateTime? CreatedAt = null);

    /// <summary>
    /// 供给侧内存账本（V1.2增强版，对齐实施包§5.1）
    ///
    /// 职责：
    ///   - 维护供给的剩余可用数量（RemainingQty）
    ///   - 记录供给的业务属性（SupplyType、AvailableTime、Confidence等）
    ///   - 支持Lock份额管理
    ///   - 与DemandBalance配合，共同实现供需原子匹配
    ///
    /// V1.2核心红线：
    ///   同一物理数量在同一PlanVersion中只能有一个Supply身份
    ///   例如同一PI：PI总量、PI的XC、PI的在途、PI的Stage WIP 不能被当成四份Supply重复消费
    /// </summary>
    private sealed class SupplyLedgerEntry
    {
        // ═══════════════════════════════════════════════════════════════════════
        // 数量字段
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>原始总量（初始值，不可变）</summary>
        public decimal OriginalQty                      { get; init; }

        /// <summary>剩余可用数量（遍历时可变，初始值=OriginalQty）</summary>
        public decimal RemainingQty                     { get; set; }

        /// <summary>已锁定份额（STRICT_BINDING/DEMAND_PROTECTION/Execution）</summary>
        public decimal LockedQty                        { get; set; }

        // ═══════════════════════════════════════════════════════════════════════
        // 供给身份字段
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>供给唯一身份键（格式：MaterialId|FactoryId|SourceType|SupplySourceId）</summary>
        public string SupplyKey                         { get; init; } = string.Empty;

        /// <summary>物理来源键（例如：ProductionInstructionNo、InventoryBatchNo、PONo等）</summary>
        public string? PhysicalSourceKey                { get; init; }

        /// <summary>供给物料ID</summary>
        public int MaterialId                           { get; init; }

        /// <summary>供给工厂ID</summary>
        public int FactoryId                            { get; init; }

        /// <summary>供给工厂代码</summary>
        public string FactoryCode                       { get; init; } = string.Empty;

        // ═══════════════════════════════════════════════════════════════════════
        // 供给类型与时间
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>供给类型（INVENTORY/PRODUCTION_INSTRUCTION/PURCHASE_ORDER/VMI/IN_TRANSIT等）</summary>
        public Core.Enum.SupplySourceType SourceType    { get; init; }

        /// <summary>供给可用时间（库存=当前时间，采购=ETA，生产=预计完成时间）</summary>
        public DateTime? AvailableAt                    { get; init; }

        /// <summary>供给来源引用（原有字段，用于追溯）</summary>
        public string? SourceReference                  { get; init; }

        /// <summary>供给来源ID（原有字段，用于关联）</summary>
        public long? SupplySourceId                     { get; init; }

        // ═══════════════════════════════════════════════════════════════════════
        // 排序字段（阶段2：按 3号位 Frozen 规则三类独立排序，不扁平混排）
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>仓库/库位编码（Inventory WarehousePriority / Procurement Warehouse Priority）</summary>
        public string? WarehouseCode                    { get; init; }

        /// <summary>PO 发行时间（Procurement Release Time 排序）</summary>
        public DateTime? ReleaseDate                    { get; init; }

        /// <summary>采购订单号（Procurement PO+Line 稳定排序）</summary>
        public string? PoNo                             { get; init; }

        /// <summary>采购订单行号（Procurement PO+Line 稳定排序）</summary>
        public string? LineNo                           { get; init; }

        /// <summary>生产指示号（PI 稳定 Tie-break）</summary>
        public string? PiNo                             { get; init; }

        /// <summary>PI 发行时间（PiSort.IssueDateAsc）</summary>
        public DateTime? IssueDate                      { get; init; }

        /// <summary>PI 创建时间（PiSort.CreatedAtAsc）</summary>
        public DateTime? CreatedAt                      { get; init; }

        // ═══════════════════════════════════════════════════════════════════════
        // 置信度与承诺度（V1.2新增）
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>置信度（CONFIRMED=确定供给，ESTIMATED=估计供给/Planning-only）</summary>
        public SupplyConfidence Confidence              { get; init; } = SupplyConfidence.CONFIRMED;

        /// <summary>承诺度（COMMITTED=已承诺，NOT_COMMITTED=未承诺）</summary>
        public SupplyCommitment Commitment              { get; init; } = SupplyCommitment.COMMITTED;

        // ═══════════════════════════════════════════════════════════════════════
        // Lock与Allocation追溯（V1.2新增）
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>本次运行的Allocation记录列表（用于追溯和校验）</summary>
        public List<AllocationRecord> Allocations       { get; init; } = new();

        /// <summary>Lock记录列表（STRICT_BINDING/DEMAND_PROTECTION/Execution）</summary>
        public List<LockRecord> Locks                   { get; init; } = new();
    }

    /// <summary>供给置信度枚举</summary>
    private enum SupplyConfidence
    {
        /// <summary>确定供给（库存/已确认PO/VMI/确定PI）</summary>
        CONFIRMED,
        /// <summary>估计供给（Planning-only占位）</summary>
        ESTIMATED
    }

    /// <summary>供给承诺度枚举</summary>
    private enum SupplyCommitment
    {
        /// <summary>已承诺（可作为CTP承诺）</summary>
        COMMITTED,
        /// <summary>未承诺（不可作为CTP承诺）</summary>
        NOT_COMMITTED
    }

    /// <summary>Lock类型（§8）</summary>
    private enum LockType
    {
        /// <summary>严格绑定：1对1强绑定，其他需求完全不可用</summary>
        STRICT_BINDING,
        /// <summary>需求保护：1对N保护，保护组内需求可用，组外不可用</summary>
        DEMAND_PROTECTION,
        /// <summary>执行锁：不可逆事实（已投料、已发货），不得再分配</summary>
        EXECUTION
    }

    /// <summary>Allocation记录（用于Supply侧追溯）</summary>
    /// <summary>
    /// Allocation记录（V1.2）
    /// 记录Pegging阶段的通用逻辑分配，不是PeggingSupplyAllocation本身
    /// PlanVersionId + AllocationSequence唯一标识一笔Allocation
    /// </summary>
    private sealed class AllocationRecord
    {
        public long AllocationSequence  { get; init; }
        public decimal AllocatedQty     { get; init; }
        public string SupplyKey         { get; init; } = string.Empty;
        public string SupplyType        { get; init; } = string.Empty;
        public string DemandKey         { get; init; } = string.Empty;
        public int MaterialId           { get; init; }
        public DateTime AllocatedAt     { get; init; }

        /// <summary>
        /// 是否需要通过生产形成
        /// true: 需要生成LogicalProductionDemand交给Solver
        /// false: 库存/PO/VMI/Received等直接承接，不生成Task
        /// </summary>
        public bool RequiresProduction  { get; init; }
    }

    /// <summary>Lock记录（用于Supply侧锁定管理）</summary>
    private sealed class LockRecord
    {
        public LockType LockType        { get; init; }
        public decimal LockedQty        { get; init; }
        public long? LockedToOrderId    { get; init; }
        public string? LockedToDemandKey { get; init; }
        public DateTime LockedAt        { get; init; }
    }

    private sealed class DemandKeyQtyRow
    {
        public string DemandKey { get; set; } = string.Empty;
        public decimal Qty      { get; set; }
    }

    private sealed class SupplyLoadRow
    {
        public string MaterialCode      { get; set; } = string.Empty;
        public int MaterialId           { get; set; }
        public int FactoryId            { get; set; }
        public string FactoryCode       { get; set; } = string.Empty;
        public decimal AvailableQty     { get; set; }
        public DateTime? AvailableAt    { get; set; }
        public string? SourceReference  { get; set; }
        public long? SupplySourceId     { get; set; }
        public string? WarehouseCode    { get; set; }
        public DateTime? ReleaseDate    { get; set; }
        public string? PoNo             { get; set; }
        public string? LineNo           { get; set; }
        public string? PiNo             { get; set; }
        public DateTime? IssueDate      { get; set; }
        public DateTime? CreatedAt      { get; set; }
    }

    /// <summary>跨域依赖行（Domain_Dependency，真实 DomainKey 口径）</summary>
    private sealed class UpstreamDomainDependencyRow
    {
        public string UpstreamDomainCode { get; set; } = string.Empty;
        public string ChildMaterialCode  { get; set; } = string.Empty;
        public int DefaultLeadTimeDays   { get; set; }
    }

    /// <summary>上游域 PlanVersion 定位行</summary>
    private sealed class UpstreamPlanVersionRow
    {
        public int Id { get; set; }
    }

    /// <summary>上游域已落盘 Task 供给行（跨域 Quantity-Time 分段虚拟供给源）</summary>
    private sealed class UpstreamTaskSupplyRow
    {
        public string MaterialCode      { get; set; } = string.Empty;
        public int MaterialId           { get; set; }
        public int FactoryId            { get; set; }
        public string FactoryCode       { get; set; } = string.Empty;
        public decimal Quantity         { get; set; }
        public DateTime? PlannedEndTime { get; set; }
    }

    /// <summary>WIP Stage 明细行（StageProgressSnapshot 逐 Stage 行，供 PI Position 两级装载）</summary>
    private sealed class WipStageLoadRow
    {
        public string ProductionInstructionNo { get; set; } = string.Empty;
        public string MaterialCode            { get; set; } = string.Empty;
        public int MaterialId                 { get; set; }
        public int FactoryId                  { get; set; }
        public string FactoryCode             { get; set; } = string.Empty;
        public string StageCode               { get; set; } = string.Empty;
        public decimal GoodCompletedQty       { get; set; }
        public decimal PlannedQty             { get; set; }
        public decimal RemainingQty           { get; set; }
    }

    /// <summary>
    /// 跨版本连续性装载产物（由 LoadSupplyPoolAsync 随供给池一起返回）。
    /// WipRows = IN_PROGRESS StageProgressSnapshot 行（连续份额位置明细）；PiPositions = 5号位 PI Position 结果（执行起点）。
    /// PiRemainingByPi = 分桶的数量根（ERP 权威 PI 剩余，见 <see cref="PiRemainingFact.PiRemainingQty"/>）。
    /// </summary>
    private sealed class ContinuityFacts
    {
        public ContinuityFacts(
            IReadOnlyList<WipStageLoadRow> wipRows,
            IReadOnlyDictionary<string, ProductionInstructionPositionResult> piPositions,
            IReadOnlyDictionary<string, decimal> piRemainingByPi)
        {
            WipRows          = wipRows;
            PiPositions      = piPositions;
            PiRemainingByPi  = piRemainingByPi;
        }

        public IReadOnlyList<WipStageLoadRow> WipRows { get; }
        public IReadOnlyDictionary<string, ProductionInstructionPositionResult> PiPositions { get; }

        /// <summary>
        /// PI 号 → ERP 权威剩余 <c>max(Quantity − ReceivedQty, 0)</c>（用户 2026-10-09 定盘）。
        /// 这是全链唯一总量根：大工艺/MES工序各层只回答「剩余现在在哪」，**任何一层的量都不得突破它**。
        /// </summary>
        public IReadOnlyDictionary<string, decimal> PiRemainingByPi { get; }
    }

    /// <summary>MESWorkOrderSnapshot 装载行（仅 IN_PROGRESS 工单）</summary>
    private sealed class WorkOrderSnapshotRow
    {
        public string ProductionInstructionNo { get; set; } = string.Empty;
        public string MESWorkOrderNo { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public decimal PlannedQty { get; set; }
        public string WorkOrderStatus { get; set; } = string.Empty;
        public DateTime DataCutoffTime { get; set; }
    }

    /// <summary>OperationProgressSnapshot 装载行（每工单工序进度）</summary>
    private sealed class OperationProgressRow
    {
        public string ProductionInstructionNo { get; set; } = string.Empty;
        public string MESWorkOrderNo { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public string StageCode { get; set; } = string.Empty;
        public decimal PlannedQty { get; set; }
        public decimal GoodQty { get; set; }
        public DateTime? LastReportTime { get; set; }
        public DateTime? SourceUpdatedAt { get; set; }
        public string? LastReportResourceCode { get; set; }

        // 2026-09-30 新增：工单级路线/部门（同一工单所有行同值）
        public string? RouteCode { get; set; }
        public int? ProductionDepartmentId { get; set; }
    }

    /// <summary>Stage 顺序行（APS_BOM_STAGE_PATH_RAW，ChildMaterialCode+StageCode → StageSeq）</summary>
    private sealed class StagePathLoadRow
    {
        public string ChildMaterialCode { get; set; } = string.Empty;
        public string StageCode         { get; set; } = string.Empty;
        public int StageSeq             { get; set; }
    }

    /// <summary>
    /// PI 级库存行（ext_ERP_Inventory_View × ext_MES_ProcessCode_View）
    /// WarehouseCode=6位工序码 LEFT JOIN ProcessCode→StageCode，未命中 StageCode 落 UNKNOWN。
    /// </summary>
    private sealed class PiInventoryLoadRow
    {
        public string MaterialCode  { get; set; } = string.Empty;
        public string FactoryCode   { get; set; } = string.Empty;
        public string WarehouseCode { get; set; } = string.Empty;
        public decimal Quantity     { get; set; }
        public string? StageCode    { get; set; }
    }

    /// <summary>
    /// XC（线边仓）行：ext_MES_ProcessCode_View.ERPProperty='XC' 标记的工序码 × ext_ERP_Inventory_View 库存。
    /// </summary>
    private sealed class XcLoadRow
    {
        public string MaterialCode  { get; set; } = string.Empty;
        public string FactoryCode   { get; set; } = string.Empty;
        public string WarehouseCode { get; set; } = string.Empty;
        public decimal Quantity     { get; set; }
        public string? StageCode    { get; set; }
    }

    /// <summary>
    /// 跨厂边行：ext_MES_APS_BOM_Workset_CrossFactoryEdge。
    /// 物理表无 EdgeSequence，装载时按 (ChildMaterialCode, BatchNo, WorksetId, Id) 稳定排序自编号。
    /// 归组键 = ChildMaterialCode（PI 物料作为子件，其产品跨厂转运的边）。
    /// </summary>
    private sealed class CrossFactoryEdgeLoadRow
    {
        public string ChildMaterialCode { get; set; } = string.Empty;
        public string FromStageCode     { get; set; } = string.Empty;
        public string FromFactoryCode   { get; set; } = string.Empty;
        public string ToStageCode       { get; set; } = string.Empty;
        public string ToFactoryCode     { get; set; } = string.Empty;
    }

    private sealed class TransitLoadRow
    {
        public string MaterialCode      { get; set; } = string.Empty;
        public string FactoryCode       { get; set; } = string.Empty;  // 目标工厂（到货厂）
        public string SourceFactoryCode { get; set; } = string.Empty;  // 源工厂（发货厂）
        public decimal Quantity         { get; set; }
        public DateTime? Eta            { get; set; }
        public DateTime? ReleaseDate     { get; set; }
        public string SourceDocumentNo  { get; set; } = string.Empty;
        public string? OrderType        { get; set; }
    }

    /// <summary>
    /// MaterialStageDeptContext 裁剪行（IsCurrent=1）：
    /// (MaterialId, StageCode) → DefaultProductionDepartmentId。
    /// </summary>
    private sealed class MaterialStageDeptContextLoadRow
    {
        public int MaterialId { get; set; }
        public string StageCode { get; set; } = string.Empty;
        public int DefaultProductionDepartmentId { get; set; }
    }

    private sealed class ManualCapacitySlotKeyDto
    {
        public int ManualSlotId { get; set; }
        public int ProductionDepartmentId { get; set; }
        public string OperationName { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 供给池装载
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 装载供给池（INVENTORY + PIPELINE + WIP）
    /// </summary>
    private async Task<(SupplyPool Pool, ContinuityFacts Continuity)> LoadSupplyPoolAsync(
        PeggingExecutionRequest request,
        FrozenStrategySnapshot frozenSnapshot,
        BomSnapshot bomSnapshot,
        CancellationToken ct,
        bool persistPositionSnapshot = true)
    {
        var pool    = new SupplyPool(frozenSnapshot);
        var cutoff  = request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt;
        pool.DataCutoffTime = cutoff;   // P0-07：占位采购 AvailableTime 基准 = 本 Run 冻结 DataCutoffTime

        // 分段计时（2026-10-09）：本方法是全链最大一块（小样本实测 126s / 总 161s），
        // 且**完全不随订单集收窄** ⇒ 不逐段量出来就是黑盒。定稿验收后可按需摘除。
        var ssw  = System.Diagnostics.Stopwatch.StartNew();
        var last = 0L;
        void Mark(string name)
        {
            var now = ssw.ElapsedMilliseconds;
            _logger.LogInformation("[Pegging][供给池阶段耗时] {Name}={Delta}ms (累计 {Total}ms)",
                name, now - last, now);
            last = now;
        }

        var inventoryRows = await _connectionManager.QueryAsync<SupplyLoadRow>(
            @"SELECT
                  ib.MaterialCode,
                  m.Id         AS MaterialId,
                  ib.FactoryId,
                  f.Code       AS FactoryCode,
                  ib.AvailableQty,
                  NULL         AS AvailableAt,
                  NULL         AS SourceReference,
                  d.Id         AS SupplySourceId
              FROM InventoryBalance ib
              INNER JOIN Material m ON m.MaterialCode = ib.MaterialCode
              INNER JOIN Factory f  ON f.Id = ib.FactoryId
              OUTER APPLY (
                  SELECT TOP 1 Id
                  FROM InventoryAvailableSupplyDetail
                  WHERE MaterialCode   = ib.MaterialCode
                    AND ProductFamilyId = ib.ProductFamilyId
                    AND FactoryId       = ib.FactoryId
                  ORDER BY RulePriority ASC, Id ASC
              ) d
              WHERE ib.AvailableQty > 0",
            db: DatabaseId.APS);

        foreach (var r in inventoryRows)
            pool.Add(r.MaterialCode, r.MaterialId, r.FactoryId, r.AvailableQty,
                     null, Core.Enum.SupplySourceType.INVENTORY,
                     null, r.FactoryCode, r.SupplySourceId,
                     physicalSourceKey: r.SupplySourceId.HasValue ? $"INV:{r.SupplySourceId}" : null);
        Mark("①库存 INVENTORY(含 OUTER APPLY)");

        // 采购/定时供给：切 5号位 ITimedSupplyFactLoader 读原始事实（Eta/ReleaseDate，AvailableTime 留 2号位算），
        // 再由 2号位用 AvailableTimeCalculator（EtaInvariant 三级链 + ArrivalToUsableOffset）内存计算 AvailableTime（阶段 3，
        // 不再直读 sfp.AvailableTime）。
        // 注意：ITimedSupplyFactLoader 白名单已排除 INTERPLANT_IN_TRANSIT（仅 PURCHASE_IN_TRANSIT /
        // OPEN_PO_REMAINING / ARRIVED_NOT_RECEIVED），厂间在途不从此装载入池。
        // 厂间在途分两类（PM 2026-09-09 v1.0）：PI 级 Transit = PI Position（LoadTransitFactsAsync →
        // ProductionInstructionPositionCalculator）；SH 级供给 = [Order] CustomerSegment='跨厂' 订单
        // （LoadInterFactoryShipmentsAsync 单一供给，不再三段式 Transit/Received 闭合）。均待真实事实就绪后正式启用。
        var rawFacts = await _timedSupplyFactLoader.LoadRawFactsAsync(
            new SupplyFactScope { DataCutoffTime = cutoff }, ct);

        Dictionary<(string, int, int, string), DateTime> manualEtaMap = new();
        if (rawFacts.Count > 0)
        {
            // Manual ETA 覆盖表人工维护、天然小：全量查 IsActive=1，内存按业务键 (PO,LineNo,MaterialId,Warehouse)
            // 精确匹配（BuildManualEtaMap 兜底）。避免 distinct MaterialId=36k / PONo=56k 大 IN 列表超
            // SQL Server 2100 参数上限（1号位 2026-09-21 催办）。
            var manualEtaOverrides = await _procurementManualEtaRepo.GetActiveOverridesAsync(ct);
            manualEtaMap = AvailableTimeCalculator.BuildManualEtaMap(manualEtaOverrides);
        }

        foreach (var fact in rawFacts)
        {
            var availableAt = AvailableTimeCalculator.Compute(fact, manualEtaMap, frozenSnapshot);
            if (availableAt.HasValue && availableAt > cutoff)
                continue; // 超出本次运行时窗的未来供给（等价旧 SQL 的 AvailableTime <= Cutoff 过滤）

            pool.Add(fact.MaterialCode, fact.MaterialId, fact.FactoryId, fact.RemainingQty,
                     availableAt, Core.Enum.SupplySourceType.PIPELINE,
                     fact.SourceDocumentNo, fact.FactoryCode,
                     sort: new SupplySortFacts(
                         WarehouseCode: fact.StorageCode,
                         ReleaseDate: fact.ReleaseDate,
                         PoNo: fact.SourceDocumentNo,
                         LineNo: fact.SourceDocumentLineNo),
                     physicalSourceKey: string.IsNullOrWhiteSpace(fact.SourceDocumentNo)
                         ? null
                         : $"PO:{fact.SourceDocumentNo}:{fact.SourceDocumentLineNo}");
        }
        Mark($"②定时供给(采购在途 LoadRawFacts={rawFacts.Count}行 + AvailableTime 计算)");

        // ── T2-01：PI 权威剩余事实（来源 [Order] ∪ 本域 BOM 可达物料的订单链，与 Stage / MES 工单 / PI Position 完全解耦）──
        // 必须**先于**一切 Stage 判断建立 PI 宇宙：真实 PI 即使当前没有任何 Stage 报工行，
        // 也必须在这里出现、也必须有权威剩余量（PM T2-04.2）。
        // 口径：PiRemainingQty = max(Quantity − ISNULL(ReceivedQty, 0), 0)（用户 2026-10-09 定盘）。
        // 范围：见 LoadPiRemainingFactsAsync —— [Order] 只能给出顶层成品（归域闸门所限），
        //       下阶加工件的 PI 事实按本域物料宇宙从 Order_Canonical 补取。
        var piFacts = await LoadPiRemainingFactsAsync(request.PlanVersionId, bomSnapshot.MaterialUniverse, ct);
        Mark($"③PI 权威剩余事实(LoadPiRemainingFacts={piFacts.Count}行)");

        // ── T2-02：真实 PI Supply 入池（PM 1009 §T2-02；供给侧的**唯一**写入点）──
        // 目的：Demand 先承接 PI，缺口才转新生产。缺了这段，所有本可由 PI 承接的需求会整批塌成
        // NEW_REQUIREMENT ⇒ 重复生产（正是代码审核报告 P0-02「PI Position 反向裁剪 PI Supply」的极端形态）。
        //
        // 口径（逐条对应 PM 整改令）：
        //   · 数量 = PiRemainingQty（ERP 权威剩余，T2-01），**不由 Position/Stage 反向裁剪**（T2-02.1 / P0-02 裁定）；
        //   · 一个 PI = 一个物理供给身份（physicalSourceKey = "PI:"+PI号），**不拆成多个 Stage Supply**（T2-02.1）；
        //   · 保留 Material + Factory + PI 身份校验（入池键 = MaterialCode|FactoryId，物料身份另由
        //     `ValidateEligibility` 二次校验，T2-02.1）；
        //   · 同 Run 内同一物理数量只消费一次 —— 由 `SupplyLedgerEntry.RemainingQty` 原子扣减保证（T2-02.2）；
        //   · 先有 PI 号 + 承接量，Position 只用于起点切片，**不是第二笔供给**（T2-02.3）；
        //   · 自消费保护由**层级门**实现，不再用逐行卫语句：`NetAndAllocate` 传 `includePi = bomLevel > 0`
        //     ⇒ 顶层（L0）整层不取 PI 条目，任何 L0 需求（O 行客户订单 / P 行 PI 指令行）都无从消费 PI，
        //     结构上不可能自消费（T2-02.4）；下阶（L1+）组件需求正常取 PI 池。
        //
        // ⛔ 不得恢复「StageProgressSnapshot.RemainingQty 直接当 PI Supply」的旧写法（T2-02.5）——
        //    那是 MES 在制量（执行连续性数量，走 T2-06 分桶），与 ERP PI 剩余是两个不同的量。
        //
        // ⚠️ SupplySourceType 取 WIP：`SortPi`（供给池 PI 内排序）认 WIP|PRODUCTION_INSTRUCTION 两种；
        //    审核报告原文亦称其为「PI/WIP Supply」。**此命名待 PM/1号位 口径确认**（落台账留痕）。
        var piSupplyEntries = 0;
        var piSupplyQty = 0m;
        foreach (var f in piFacts)
        {
            if (f.PiRemainingQty <= 0m) continue;   // 剩余 0 = 已完工入库，无可承接份额（T2-01.2 的 clamp 结果）

            pool.Add(f.MaterialCode, f.MaterialId, f.FactoryId, f.PiRemainingQty,
                     null, Core.Enum.SupplySourceType.WIP,
                     f.ProductionInstructionNo, f.FactoryCode,
                     sort: new SupplySortFacts(
                         PiNo: f.ProductionInstructionNo,
                         IssueDate: f.IssueDate),
                     physicalSourceKey: $"PI:{f.ProductionInstructionNo}");
            piSupplyEntries++;
            piSupplyQty += f.PiRemainingQty;
        }
        _logger.LogInformation(
            "[Pegging][T2-02] PI Supply 入池完成：条目={Entries} 个（PI 事实 {Facts} 个，剩余≤0 跳过 {Skipped} 个），" +
            "Σ承接量={Qty}；身份=PI号（physicalSourceKey=\"PI:<PI号>\"），数量=ERP PiRemainingQty，未经 Position 裁剪",
            piSupplyEntries, piFacts.Count, piFacts.Count - piSupplyEntries, piSupplyQty);

        // ── Stage 定位明细（**不再是 PI 总量来源**）──
        // PM 2026-08-28（先选 PI → 再消费该 PI 内部 PI Position）+ PM 2026-09-07 P0-02 正式回复的原始三步：
        //   1) 读 Stage 明细（StageCode/GoodCompletedQty/RemainingQty），按 PI 聚合取 PI 级 RemainingQty；
        //   2) 调 5号位 IProductionInstructionPositionCalculator 拆 located/unlocated（供执行起点切片，不决定供给数量）；
        //   3) PI Supply = ERP RemainingQty 全额入池；Located + UNLOCATED 合计 = RemainingQty（数量不闭合由快照登记）。
        //
        // ⚠️ T2-04.1（2026-10-09 整改）：第 1 条的「按 PI 聚合取 PI 级 RemainingQty」**已废止**。
        //    本查询从此只回答「该 PI 已报到哪些 Stage、各 Stage 进度多少」——是**定位明细**，
        //    绝不再充当 PI 总量（旧写法 `ErpRemainingQty = group.Max(r => r.RemainingQty)` 已删）。
        //    总量唯一来源 = 上面的 piFacts。
        var wipStageRows = (await _connectionManager.QueryAsync<WipStageLoadRow>(
            @"SELECT sp.ProductionInstructionNo,
                     sp.MaterialCode,
                     m.Id             AS MaterialId,
                     o.FactoryId      AS FactoryId,
                     f.Code           AS FactoryCode,
                     sp.StageCode,
                     sp.GoodCompletedQty,
                     sp.PlannedQty,
                     sp.RemainingQty
              FROM StageProgressSnapshot sp
              INNER JOIN Material m ON m.MaterialCode = sp.MaterialCode
              -- 圈定在制 PI 集合用「需求侧订单」([Order] 携带 MTS_InstructionNo)，不得用 [Task]：
              -- [Task] 是本轮求解的落库输出，首跑为空 → join 恒 0 → wipStageRows 空 → 连续性切不出（鸡生蛋）。
              INNER JOIN (
                  SELECT DISTINCT o.MTS_InstructionNo, o.FactoryId
                  FROM [Order] o
                  WHERE o.PlanVersionId = @PlanVersionId
                    AND o.MTS_InstructionNo IS NOT NULL
              ) o ON o.MTS_InstructionNo = sp.ProductionInstructionNo
              INNER JOIN Factory f ON f.Id = o.FactoryId
              WHERE sp.ScheduleRunId = (
                  SELECT TOP 1 pv.SourceScheduleRunId
                  FROM PlanVersion pv
                  WHERE pv.Id = @PlanVersionId
              )
              AND sp.RemainingQty > 0",
            new { request.PlanVersionId },
            db: DatabaseId.APS)).ToList();

        Mark($"④Stage 定位明细(StageProgressSnapshot={wipStageRows.Count}行)");

        var piPositions = await LoadPiPositionsAsync(piFacts, wipStageRows, frozenSnapshot, request.PlanVersionId, ct, persistPositionSnapshot);
        Mark($"⑤PI Position(5号位计算器,={piPositions.Count}个PI)");

        // ── W1（PM 0914 红线）：IN_PROGRESS 的 RemainingQty 不再作为 WIP Supply 把需求 Q 扣掉。
        // 被覆盖部分必须以 Continuation Slice + Free Slice 一起进 1号位（见 ApplyContinuityBucketing）。
        // wipStageRows / piPositions 仍保留并随返回带出：供「逐 PI 识别连续份额 + 执行起点（StartOperation）」，
        // 以及 LoadPiPositionsAsync 的 PI Position 快照落库 / 数量闭环校验副作用。

        // ── INTER_FACTORY_ORDER（厂间出荷指示，SH级）供给：SH 单一身份（PM 2026-09-09 v1.0）──
        // SH 本身是 Order（OrderType=SALES_ORDER + CustomerSegment='跨厂'，OrderNo=SH号），
        // 单一 Supply 身份，禁拆 Transit/Received/Unproduced 三段；单个 SH 整单一次入库。
        // 每 SH 只入池一条（PhysicalSourceKey=SH号=OrderNo），量=Order.Quantity（指令总量），由红线③⑥ 防重复计量。
        // AvailableAt = SourceReadyTime + CrossFactoryLT；SourceReadyTime = max(源厂 Task PlannedEndTime)，
        // 依赖 Layer-2「源厂需求→求解→回传」（待 PM 接口/源厂方向字段落地）——当前置 null（源厂未排），见台账。
        var shipments = await LoadInterFactoryShipmentsAsync(ct);
        Mark($"⑥厂间 SH 供给({shipments.Count}行)");
        foreach (var s in shipments)
        {
            // TODO(Layer-2)：s.SourceFactoryId 就绪后，读上游源厂域落盘 Task 取 max(PlannedEndTime)=SourceReadyTime，
            // 再 +CrossFactoryLT 得 availableAt；当前 SourceReadyTime 尚未产出，置 null。
            pool.Add(s.MaterialCode, s.MaterialId, s.TargetFactoryId, s.InstructionQty,
                     null, Core.Enum.SupplySourceType.INTER_FACTORY_ORDER,
                     s.ShipmentNo, s.TargetFactoryCode,
                     physicalSourceKey: s.ShipmentNo);
        }

        _logger.LogDebug(
            "[Pegging] 供给池明细: INVENTORY={Inv}, WIP(PI)={Wip}, PIPELINE={Pipe}, PI Position={PiPos}, SH={Sh}；" +
            "PI 权威事实={PiFacts}（Stage 定位明细行={StageRows}，覆盖 PI={StagePi}）",
            inventoryRows.Count(), piFacts.Count,
            rawFacts.Count, piPositions.Count, shipments.Count,
            piFacts.Count, wipStageRows.Count,
            wipStageRows.Select(r => r.ProductionInstructionNo).Distinct().Count());

        // 跨 Domain Quantity-Time（§8/D12）：注入上游域生产输出为分段虚拟供给
        await LoadUpstreamDomainSupplyAsync(pool, request, ct);

        Mark("⑦上游域虚拟供给(跨Domain)");

        await LoadActiveLockDataAsync(pool, ct);
        Mark("⑧活跃锁数据");

        // 诊断（2026-09-18 BOM 无记忆化缺陷期间）：连续事实在 BOM 回路【之前】就已算完，这里直接打 DerivedQty>0 与 ΣE，
        // 不必等随后的 BOM 展开（专项 ① 落地后可删）。
        var ctxAll = piPositions.Values
            .SelectMany(p => p.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>())
            .ToList();
        _logger.LogInformation(
            "[Pegging][Continuity] 事实摘要(回路前): PI Position={PiPos}, 上下文总数={Ctx}, DerivedQty>0={Active}, ΣE={E}",
            piPositions.Count, ctxAll.Count,
            ctxAll.Count(c => c.DerivedRemainingQty > 0m),
            ctxAll.Sum(c => c.DerivedRemainingQty));

        // 数量根：PI 号 → ERP 权威剩余（用户 2026-10-09 定盘：PI总剩余 = max(Quantity−ReceivedQty,0)，
        // **与 MES 无关**）。同一 PI 多行取「剩余最大」——与 LoadPiRemainingFactsAsync 同口径，不静默求和。
        var piRemainingByPi = piFacts
            .Where(f => !string.IsNullOrEmpty(f.ProductionInstructionNo) && f.PiRemainingQty > 0m)
            .GroupBy(f => f.ProductionInstructionNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(f => f.PiRemainingQty), StringComparer.Ordinal);

        return (pool, new ContinuityFacts(wipStageRows, piPositions, piRemainingByPi));
    }

    /// <summary>
    /// 跨版本连续性分桶（PM 0914 红线）：对 Pegging 后已形成的 LogicalProductionDemand，按 IN_PROGRESS 既存执行
    /// 切 Continuation/Free 两片一起进 1号位。Q 不因 WIP 供给减小；被覆盖份额以 Continuation Slice（IsContinuation=true）
    /// ＋自由份额 Free Slice（IsContinuation=false）提交，1号位必须同时看到 各工单 Continuation 与 Free。
    /// 连续份额 = 5号位 ExistingExecutionContexts：每 IN_PROGRESS MES 工单一条，绝不合并（§8/§9，P0 红线）。
    /// </summary>
    private (List<LogicalProductionDemand> Demands, List<ContinuityOverCommitEvent> OverCommits) ApplyContinuityBucketing(
        List<LogicalProductionDemand> demands,
        ContinuityFacts facts)
    {
        // PI → 逐工单既存执行上下文（5号位 ExistingExecutionContexts；每 IN_PROGRESS 工单一条）
        var contextsByPi = new Dictionary<string, IReadOnlyList<ExistingExecutionContextDto>>(StringComparer.Ordinal);
        foreach (var (pi, pos) in facts.PiPositions)
        {
            if (pos.ExistingExecutionContexts is { Count: > 0 } list)
                contextsByPi[pi] = list;
        }

        // 诊断：分桶输入规模（无既存执行上下文 = 无 IN_PROGRESS 工单进入，连续份额切不出）
        _logger.LogInformation(
            "[Pegging][Continuity] 分桶输入: PI Position数={PiPos}, 既存执行上下文总数={Ctx}, 有上下文的PI={PiCtx}",
            facts.PiPositions.Count,
            facts.PiPositions.Values.Sum(p => p.ExistingExecutionContexts?.Count ?? 0),
            contextsByPi.Count);

        if (contextsByPi.Count == 0)
        {
            _logger.LogWarning("[Pegging][Continuity] 无 IN_PROGRESS 既存执行上下文，不切分（Continuation/Free 未产生）");
            return (demands, new List<ContinuityOverCommitEvent>());
        }

        // 诊断：上下文的派生剩余量 + 需求侧命中情况（定位切不出 Continuation 的根因）
        var ctxList = facts.PiPositions.Values
            .SelectMany(p => p.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>())
            .ToList();
        var demandsWithPi = demands.Count(d => !string.IsNullOrEmpty(d.ProductionInstructionNo));
        var demandsMatching = demands.Count(d => !string.IsNullOrEmpty(d.ProductionInstructionNo) && contextsByPi.ContainsKey(d.ProductionInstructionNo));
        _logger.LogInformation(
            "[Pegging][Continuity] 明细: 上下文总数={Tot}, DerivedQty>0={Active}, ΣE={E}; 需求总数={Dem}, 带PI={WithPi}, 命中上下文PI={Match}",
            ctxList.Count, ctxList.Count(c => c.DerivedRemainingQty > 0m), ctxList.Sum(c => c.DerivedRemainingQty),
            demands.Count, demandsWithPi, demandsMatching);

        // 数量根可用性：MES 位置量全 0 时，分桶靠这张表兜底（见 BucketContinuityShares §9.4 分支）
        _logger.LogInformation(
            "[Pegging][Continuity] 数量根(ERP PI权威剩余): 可根PI={RootPi}个 ΣP={SumP}; 其中「有上下文但位置量全0」={NoPos}个",
            facts.PiRemainingByPi.Count, facts.PiRemainingByPi.Values.Sum(),
            contextsByPi.Keys.Count(pi =>
                contextsByPi[pi].All(c => c.DerivedRemainingQty <= 0m) &&
                facts.PiRemainingByPi.ContainsKey(pi)));

        var overCommits = new List<ContinuityOverCommitEvent>();
        var result = BucketContinuityShares(demands, contextsByPi, overCommits.Add, facts.PiRemainingByPi);

        // ── PI续排起点接入（2026-09-24）：Free 增产从 5号位 NextOperationContext 开始，不复从首工序 ──
        // 5号位 CalculateNextOperationContexts（r13369）产出各 PI 的 NextOperation/StartOperationCode，
        // 2号位 此前白拿未消费（Free 恒 startOperationCode=null→从头开始）。
        // 现改为：Free 若无 MES 工单覆盖，从 5号位 给的 StartOperationCode 起（跳过已完成工序）。
        var nextOpsByPi = facts.PiPositions
            .Where(kv => kv.Value.NextOperationContexts is { Count: > 0 })
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value.NextOperationContexts!
                    .Where(c => !string.IsNullOrEmpty(c.StartOperationCode))
                    .Select(c => (StartOperationCode: c.StartOperationCode!, StartStageCode: c.StartStageCode))
                    .FirstOrDefault(),
                StringComparer.Ordinal);

        _logger.LogInformation(
            "[Pegging][Continuity] 5号位 NextOperation 起点可用性: PI Position={PiPos}个, 其中有可用起点={NextOpPi}个",
            facts.PiPositions.Count, nextOpsByPi.Count);

        foreach (var r in result)
        {
            // 已带起点的片不动（MES 工单上下文已给出 StartOperationCode）；
            // 其余 —— Free，以及 §9.4 兜底切出的「MES 位置事实缺失」Continuation —— 退回 5号位 NextOperation 起点，
            // 跳过 MES 已报工工序、不复从首工序。
            if (r.StartOperationCode != null) continue;
            var pi = r.ProductionInstructionNo;
            if (pi is null or "") continue;
            if (!nextOpsByPi.TryGetValue(pi, out var nextOp) || nextOp.StartOperationCode is null) continue;
            r.StartOperationCode = nextOp.StartOperationCode;
            // Free 沿用原行为（以 NextOperation 的 Stage 为准）；Continuation 的 Stage 来自 MES 工单上下文（位置事实），仅在空时补
            if (!string.IsNullOrEmpty(nextOp.StartStageCode)
                && (!r.IsContinuation || string.IsNullOrEmpty(r.StartStageCode)))
                r.StartStageCode = nextOp.StartStageCode;
        }

        foreach (var evt in overCommits)
            _logger.LogWarning("[Pegging][Continuity]{Kind} {Msg}", evt.Kind, evt.Message);

        // 红线验收日志：正常 E≤Q 切分路径此前静默，补一条结构化计数，跑完可直接核「同 PI 下 Continuation + Free 同现」。
        var contSlices = result.Where(d => d.IsContinuation).ToList();
        var freeSlices = result.Where(d => !d.IsContinuation && d.LogicalDemandKey.EndsWith("/FREE", StringComparison.Ordinal)).ToList();
        if (contSlices.Count > 0)
        {
            _logger.LogInformation(
                "[Pegging][Continuity] 分桶完成: Continuation={ContCount}片 ΣE={E}, Free={FreeCount}片 ΣQ={FreeQty}, 涉及PI={PiCount}个",
                contSlices.Count, contSlices.Sum(d => d.NetOutputQty),
                freeSlices.Count, freeSlices.Sum(d => d.NetOutputQty),
                contSlices.Select(d => d.ProductionInstructionNo).Distinct().Count());

            // ── A/B 输入完整性显式告警（不静默）──
            // 1号位 `PhaseTwoInitialScheduler.cs:285-301` 对 `IsContinuation` 需求硬 Fail Closed：
            //   ContinuationKey / RouteCode / PathId / StartOperationCode / NoSplitMerge 缺任一
            //   ⇒ 记 `UnscheduledDemandKeys` 且**不产任何 Task** —— 从 2号位 视角表现为「A/B 数量凭空消失」。
            //   ContinuationKey / NoSplitMerge 已由 `CloneDemandSlice` 补齐；**RouteCode/PathId 补不了**：
            //   一个 demand 按 MES 工单切成多片，各片可能走不同 Path，真实值只能由 5号位 按 slice 交付
            //   （猜唯一 Path 正是 Q-3 明令禁止）。故此处把缺口**显式打到日志**，便于对账而非静默丢单。
            var noPath = contSlices.Count(d => string.IsNullOrEmpty(d.RouteCode) || d.PathId is null);
            if (noPath > 0)
                _logger.LogWarning(
                    "[Pegging][Continuity] A/B 输入不完整：{NoPath}/{Total} 片缺真实 RouteCode/PathId ⇒ " +
                    "1号位 将按 Fail Closed 记 Unscheduled 且不产 Task（待 5号位 按 slice 交付路径身份）。涉及PI={PiCount}个",
                    noPath, contSlices.Count,
                    contSlices.Select(d => d.ProductionInstructionNo).Distinct().Count());
        }

        return (result, overCommits);
    }

    /// <summary>
    /// 连续份额分桶纯函数（可单测）。逐 MES 工单独立产出 Continuation（§8，P0 红线：绝不合并多工单）。
    /// 规则：
    ///   无 PI / 无 IN_PROGRESS 工单 → 原样透传（自由需求，不切）；
    ///   Q≤0 且仍有开工中剩余 → 不产 Demand/Task + 登记 Demand Mismatch（§9.2）；
    ///   每工单 → Continuation = min(DerivedRemainingQty, 剩余额度)，**ΣE 封顶 = min(Q, PI权威剩余)**；
    ///   MES 位置量缺失（DerivedRemainingQty 全 ≤0）但 PI 有权威剩余 → §9.4 保守兜底：总量取 PI 剩余、
    ///     位置退回 5号位 上下文，量在工单间均分（与 5号位 AllocateStageRemaining 的 totalPlannedQty=0 兜底同惯例）；
    ///   Free = max(Q − ΣE, 0)；执行申报量 &gt; 封顶 → 不砍单、登记 Execution Over-Commit（§9.1）。
    /// Slice 键（LogicalDemandKey）= 源 LogicalDemandKey + "/WO:" + MESWorkOrderNo；Free = 源 key + "/FREE"；
    /// **ContinuationKey 与 Slice 键是两回事**（红线 Q3）：ContinuationKey = f(ScheduleRun, MESWorkOrderNo)，
    ///   同一 MES 工单的全部 Slice **共享同一个** ContinuationKey，**不得把 LogicalDemandKey 拼进去**（否则同一工单裂分）。
    /// 共用源 AllocationSequence/DemandKey（P3.2 不新建 Allocation）。
    /// PlannedProcessQty 逐片按材料级良率单位投入比（源 PlannedProcessQty/NetOutputQty）反算（§16）。
    /// </summary>
    /// <param name="piRemainingByPi">
    /// PI 号 → ERP 权威剩余（用户 2026-10-09 定盘口径）。传 null = 退回旧行为（不封顶、不兜底），
    /// 便于既有单测与「无权威剩余来源」的调用方保持不变。
    /// </param>
    internal static List<LogicalProductionDemand> BucketContinuityShares(
        List<LogicalProductionDemand> demands,
        IReadOnlyDictionary<string, IReadOnlyList<ExistingExecutionContextDto>> contextsByPi,
        System.Action<ContinuityOverCommitEvent>? onOverCommit = null,
        IReadOnlyDictionary<string, decimal>? piRemainingByPi = null)
    {
        var result = new List<LogicalProductionDemand>(demands.Count + 8);

        // F-04 / PM T2-06.2 红线：**一个 MES 工单的既存执行份额，在本 Run 内只消费一次**。
        // 多个 Demand 合法共用同一真实 PI 时，MES 工单份额只有**一份物理量**；不得让每条 Demand
        // 都完整复制同一份 E（PM C-Q4：不按 Q 比例猜分、不平均切、不放大需求）。
        // 键 = PI 号 | MESWorkOrderNo；值 = 本 Run 已消费掉的份额。
        var consumedByWo = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var demand in demands)
        {
            var pi = demand.ProductionInstructionNo;
            if (string.IsNullOrEmpty(pi))
            {
                result.Add(demand);
                continue;
            }

            if (!contextsByPi.TryGetValue(pi, out var contexts) || contexts.Count == 0)
            {
                result.Add(demand);
                continue;
            }

            var q = demand.NetOutputQty;

            // ── 数量根（用户 2026-10-09 定盘）──────────────────────────────────────────────
            // PI总剩余 = max(Quantity − ReceivedQty, 0)，**与 MES 无关**，是全链唯一总量；
            // 大工艺(Stage) 与 MES 各工序只回答「剩余现在在哪」，**任何一层的量都不得突破 PI 剩余**。
            // 上限同时受 Q 约束（§4.2A：连续份额仍属于当前 Q，不得在 Q 之外额外叠加）。
            var pRoot = piRemainingByPi is not null
                        && piRemainingByPi.TryGetValue(pi, out var pv)
                        && pv > 0m
                ? pv
                : (decimal?)null;
            var cap = pRoot.HasValue ? Math.Min(q, pRoot.Value) : q;

            // 逐工单申报量：正常路径取 5号位 DerivedRemainingQty；位置量全 0 时走 §9.4 兜底
            var active = contexts.Where(c => c.DerivedRemainingQty > 0m).ToList();
            var shareQty = new List<(ExistingExecutionContextDto Ctx, decimal Qty)>(active.Count);
            if (active.Count > 0)
            {
                foreach (var c in active) shareQty.Add((c, c.DerivedRemainingQty));
            }
            else if (pRoot.HasValue)
            {
                // ── MES 位置事实缺失的保守兜底（§2.2 / §9.4）────────────────────────────────
                // 5号位 的 Stage 位置事实取自 StageProgressSnapshot，实测该快照**未覆盖 5,706/35,278 个
                // 在制 PI**（PV2 样本 284/284 全落在缺口里）⇒ stageFact.RemainingQty=0 ⇒ DerivedRemainingQty
                // 恒 0 ⇒ 连续份额切不出。按用户口径，总量仍以 ERP PI 剩余为准，位置退回 5号位 上下文
                // （起点由调用方按 NextOperationContext 回填）；无位置量时按工单**均分**——与 5号位
                // AllocateStageRemaining 的 totalPlannedQty=0 兜底同惯例。
                var perWo = Math.Round(cap / contexts.Count, 4);
                if (perWo > 0m)
                    foreach (var c in contexts) shareQty.Add((c, perWo));
            }

            // ── F-04 / PM T2-06.2：同一 MES 工单份额在本 Run 内**只消费一次** ────────────────
            // 多个 Demand 合法共用同一真实 PI 时，MES 工单只有**一份物理可承接量**；先到先得，
            // 后来者不得再复制同一份 E（PM C-Q4：不按 Q 比例猜分、不平均切、不放大需求）。
            // 被别的 Demand 抢先消费掉的部分 → 登记 DUPLICATE_WO_SHARE（保守保留 Issue，不静默）。
            for (var i = 0; i < shareQty.Count; i++)
            {
                var (ctxI, qtyI) = shareQty[i];
                consumedByWo.TryGetValue(pi + "|" + ctxI.MESWorkOrderNo, out var already);
                var avail = qtyI - already;
                if (avail <= 0m)
                {
                    onOverCommit?.Invoke(new ContinuityOverCommitEvent(
                        pi, demand.OrderId, demand.MaterialId, demand.DomainKey, demand.AllocationSequence,
                        demand.StartStageCode, demand.LogicalDemandKey, qtyI, already, qtyI, "DUPLICATE_WO_SHARE"));
                    shareQty[i] = (ctxI, 0m);
                }
                else if (avail < qtyI)
                {
                    shareQty[i] = (ctxI, avail);
                }
            }
            shareQty.RemoveAll(x => x.Qty <= 0m);

            if (shareQty.Count == 0)
            {
                result.Add(demand);
                continue;
            }

            // §9.2：Q=0 但仍有开工中剩余 → 不产正常 Demand/Task，登记 Demand Mismatch
            if (q <= 0m)
            {
                var leftover = shareQty.Sum(x => x.Qty);
                onOverCommit?.Invoke(new ContinuityOverCommitEvent(
                    pi, demand.OrderId, demand.MaterialId, demand.DomainKey, demand.AllocationSequence,
                    demand.StartStageCode, demand.LogicalDemandKey, q, leftover, leftover, "DEMAND_MISMATCH"));
                continue;
            }

            // §16：材料级良率单位投入比（PlannedProcessQty / NetOutputQty），逐片反算 PlannedProcessQty
            var processRatio = demand.PlannedProcessQty > 0m && q > 0m
                ? demand.PlannedProcessQty / q
                : 1m;

            // §8：逐工单各出一条 Continuation，绝不合并。
            // 有权威根（PI剩余）→ ΣE 封顶 = min(Q, PI剩余)；无根（调用方未提供）→ 保持旧行为，不封顶、只登记。
            var rawSumE = shareQty.Sum(x => x.Qty);
            var sumE = 0m;
            foreach (var (ctx, rawQty) in shareQty)
            {
                var ci = pRoot.HasValue
                    ? (rawQty <= cap - sumE ? rawQty : cap - sumE)   // 逐工单不得越封顶
                    : rawQty;                                        // 无根 → 逐字旧行为
                if (ci <= 0m) continue;
                sumE += ci;

                // F-04：登记本 Run 已消费的该 MES 工单份额（后续同 PI 的 Demand 只能拿剩余）
                var woKey = pi + "|" + ctx.MESWorkOrderNo;
                consumedByWo.TryGetValue(woKey, out var prevConsumed);
                consumedByWo[woKey] = prevConsumed + ci;

                var key = demand.LogicalDemandKey + "/WO:" + ctx.MESWorkOrderNo;
                var startOp = string.IsNullOrEmpty(ctx.StartOperationCode) ? null : ctx.StartOperationCode;
                var startStage = !string.IsNullOrEmpty(ctx.StartStageCode) ? ctx.StartStageCode : demand.StartStageCode;

                result.Add(CloneDemandSlice(demand, key, ci, Math.Round(ci * processRatio, 4),
                    isContinuation: true, startStage, startOp,
                    continuationKey: ContinuationKeyOf(demand.PlanVersionId, ctx.MESWorkOrderNo)));
            }

            var freeQty = q - sumE;
            if (freeQty > 0m)
            {
                // Free 是新生产量：起点从路由首工序起（StartOperation=null，StartStage 由 FillStartStageCodes 回填）
                result.Add(CloneDemandSlice(demand, demand.LogicalDemandKey + "/FREE", freeQty,
                    Math.Round(freeQty * processRatio, 4),
                    isContinuation: false, demand.StartStageCode, startOperationCode: null));
            }
            else if (freeQty < 0m && !pRoot.HasValue)
            {
                // 旧行为（无权威根）：E>Q 执行超量 —— 不砍单、不产新自由 Task，登记 Execution Over-Commit
                onOverCommit?.Invoke(new ContinuityOverCommitEvent(
                    pi, demand.OrderId, demand.MaterialId, demand.DomainKey, demand.AllocationSequence,
                    demand.StartStageCode, demand.LogicalDemandKey, q, sumE, -freeQty, "EXECUTION_OVER_COMMIT"));
            }

            // §9.1 红线（有权威根时）：申报量 > 封顶（= min(Q, PI权威剩余)）——不砍单、登记，绝不静默
            if (pRoot.HasValue && rawSumE > cap)
            {
                onOverCommit?.Invoke(new ContinuityOverCommitEvent(
                    pi, demand.OrderId, demand.MaterialId, demand.DomainKey, demand.AllocationSequence,
                    demand.StartStageCode, demand.LogicalDemandKey, cap, rawSumE, rawSumE - cap, "EXECUTION_OVER_COMMIT"));
            }
        }

        return result;
    }

    private static LogicalProductionDemand CloneDemandSlice(
        LogicalProductionDemand s,
        string logicalDemandKey,
        decimal netQty,
        decimal processQty,
        bool isContinuation,
        string? startStageCode,
        string? startOperationCode,
        string? continuationKey = null) => new()
    {
        LogicalDemandKey       = logicalDemandKey,
        PlanVersionId          = s.PlanVersionId,
        DomainKey              = s.DomainKey,
        AllocationSequence     = s.AllocationSequence,
        DemandKey              = s.DemandKey,
        OrderId                = s.OrderId,
        MaterialId             = s.MaterialId,
        FactoryId              = s.FactoryId,
        StartStageCode         = string.IsNullOrEmpty(startStageCode) ? s.StartStageCode : startStageCode,
        StartOperationCode     = startOperationCode,
        NetOutputQty           = netQty,
        PlannedProcessQty      = processQty,
        UOM                    = s.UOM,
        RequiredAvailableTime  = s.RequiredAvailableTime,
        DemandSequence         = s.DemandSequence,
        ProductionInstructionNo = s.ProductionInstructionNo,
        IsUnlocated            = s.IsUnlocated,
        PreferredResourceId    = s.PreferredResourceId,
        FallbackResourceId     = s.FallbackResourceId,
        IsContinuation         = isContinuation,
        // ── v1.6 §1 + 0号位 2026-10-07 裁决：A/B 切片必须**显式**带齐身份字段，不靠默认值 ──
        // 1号位 `PhaseTwoInitialScheduler.cs:285-301` 对 `IsContinuation` 需求做**硬 Fail Closed**：
        //   ContinuationKey / RouteCode / PathId / StartOperationCode / NoSplitMerge 缺任一 ⇒
        //   进 `UnscheduledDemandKeys`、**不产任何 Task**（静默丢单）。
        //   ⇒ 本方法逐项显式赋值；缺项必须在此处可见，不得依赖「反正有默认值」。
        ContinuationKey        = continuationKey,
        RouteCode              = s.RouteCode,
        PathId                 = s.PathId,
        NoSplitMerge           = isContinuation,
        PreferredResourceCode  = s.PreferredResourceCode,
    };

    /// <summary>
    /// 连续份额身份键（红线 Q3：一个 ScheduleRun 内，一个 MESWorkOrderNo 有且仅有一个 ContinuationKey）。
    ///
    /// 【口径】`CK-{PlanVersionId}-{MESWorkOrderNo}`
    ///   · 生成权属 **2号位**（1号位 只透明消费 + FinalTask 原样回传，不生成/不解析/不拼接）；
    ///   · 作用域 = 本 ScheduleRun（以 `PlanVersionId` 为运行身份）⇒ **同一工单跨 Run 的 Key 不同**，符合红线「Run 内唯一」；
    ///   · **刻意不含 `LogicalDemandKey`** —— 拼进去会让同一 MES 工单的多个 Slice 各自得到不同的 Key，
    ///     即红线明令禁止的「同一 MES 工单裂分」。
    ///   · 确定性纯函数：同输入必得同值，可复算、可单测。
    /// </summary>
    internal static string ContinuationKeyOf(long planVersionId, string mesWorkOrderNo)
        => $"CK-{planVersionId}-{mesWorkOrderNo}";

    /// <summary>
    /// 连续性分桶异常事件（G6）：E&gt;Q 执行超量 / Q=0 仍开工 两类，登记到 ScheduleExplanationFact（复用不加表）。
    /// </summary>
    internal sealed record ContinuityOverCommitEvent(
        string ProductionInstructionNo,
        long? OrderId,
        int MaterialId,
        string DomainKey,
        long AllocationSequence,
        string StartStageCode,
        string LogicalDemandKey,
        decimal Quantity,
        decimal ContinuationQty,
        decimal ExcessQty,
        string Kind)   // "EXECUTION_OVER_COMMIT" | "DEMAND_MISMATCH" | "DUPLICATE_WO_SHARE"
    {
        /// <summary>DEMAND_MISMATCH（Q=0 仍开工）=错误级；其余（E&gt;Q 超量 / 同工单份额被重复引用）=警告级（登记不阻断）。</summary>
        public string Severity => Kind == "DEMAND_MISMATCH" ? "ERROR" : "WARNING";

        public string Message => Kind switch
        {
            "EXECUTION_OVER_COMMIT" =>
                $"PI {ProductionInstructionNo}: E={ContinuationQty} > Q={Quantity}，执行超量 {ExcessQty}（现场执行/数量不一致），不产自由 Task",
            // PM 2026-10-09 T2-06.2（F-04）：同一 MES 工单份额在本 Run 内已被别的需求消费完，
            // 本需求不得再复制同一份 E ⇒ 登记并转 Free（不砍单、不按 Q 比例猜分）。
            "DUPLICATE_WO_SHARE" =>
                $"PI {ProductionInstructionNo}: MES 工单份额 {ExcessQty} 已被本 Run 内其它需求消费（累计 {ContinuationQty}），不再重复计提，转自由份额",
            _ =>
                $"PI {ProductionInstructionNo}: Q=0 但存在开工中剩余 ΣE={ContinuationQty}（Demand Mismatch），不产 Demand/Task"
        };
    }

    /// <summary>
    /// Task 落库阶段旧 TaskNo 反查行（G4）：TaskNo ↔ MESWorkOrderNo 1:1 绑定，来源 = 下发中间表 TaskDispatch（冻结 §7.1）。
    /// </summary>
    private sealed record TaskNoBindingRow(string MESWorkOrderNo, string TaskNo);

    /// <summary>
    /// 从 FinalTaskDraft.SourceDraftId（= LogicalDemandKey，1号位 T08 原样带回）解析 ContinuationKey 携带的 MES 工单号。
    /// 连续份额键 = 源 key + "/WO:" + MESWorkOrderNo（BucketContinuityShares 生成）；自由份额源 key + "/FREE" / 普通需求不含 "/WO:" → 返回 null。
    /// 身份复用 LogicalDemandKey 承载、不另设字段（冻结 §7.2 + LogicalProductionDemand 注释），故落库前从这里反解。
    /// </summary>
    internal static string? ExtractMesWorkOrderNo(string? sourceDraftId)
    {
        if (string.IsNullOrEmpty(sourceDraftId))
            return null;

        const string marker = "/WO:";
        var idx = sourceDraftId.IndexOf(marker, StringComparison.Ordinal);
        return idx >= 0 ? sourceDraftId[(idx + marker.Length)..] : null;
    }

    /// <summary>
    /// TaskNo 三分支（冻结规则清单 v1.5 **T-002 / T-003 / T-004**，红线 Q2）：
    ///
    ///   · **T-002** TaskNo 是「**Stage 内 MES 执行批**」的 APS 跨版本业务身份，**一个 TaskNo 可对应 N 条 Operation Task**；
    ///   · **T-003** 一个 TaskNo 绑定一个 MESWorkOrderNo；**MES 工单不跨 Stage**；
    ///   · **T-004** A 继续旧 TaskNo/原 WO；B Solver 前 TaskNo=NULL、FinalTask 后由 2号位 生成新 TaskNo 绑定原 WO；
    ///              只有 C 新建 MES 工单；
    ///   · **T-005** `ExecutionBatchDraftKey` 是 **Solver 归组键，不是 TaskNo** ⇒ 本方法只拿它当**归组判据**，号另行生成。
    ///
    /// 分支：
    ///   ① **A 桶**（有 MES 工单，且 `TaskDispatch` 反查到旧号）⇒ **继承旧号**（跨版本连续性，不新建）；
    ///   ② **B 桶**（有 MES 工单但无旧号）⇒ 本 Run 内按 **(MESWorkOrderNo, StageCode)** 归组，同批共用一个新号；
    ///   ③ **C 桶**（无 MES 工单）⇒ 本 Run 内按 **(ExecutionBatchDraftKey, StageCode)** 归组，同批共用一个新号。
    ///
    /// 为何 ②③ 都要再叠 `StageCode`：`ExecutionBatchDraftKey` 的键域是 `(需求键, 批序号)`
    /// （`PhaseTwoInitialScheduler.ExecutionBatchKey`）**不含 Stage**，单用它会让一个 TaskNo 跨 Stage ⇒ 违反 T-003。
    ///
    /// ⚠ 旧实现（2026-10-08 前）：非继承分支一律 `PEGG-{pv}-{finalDraftId[..8]}` —— **按 Operation 一条一个号**，
    ///   同一执行批的 N 条工序各拿一个号，直接违反 T-002「一个 TaskNo 可对应 N 条 Operation Task」。
    /// </summary>
    internal static string ResolveTaskNo(
        long planVersionId,
        FinalTaskDraft final,
        string? mesWorkOrderNo,
        IReadOnlyDictionary<string, string> oldTaskNoByWorkOrder,
        IDictionary<string, string> taskNoByBatch)
    {
        // ① A 桶：跨版本继承（T-004「A 继续旧 TaskNo/原 WO」）
        if (mesWorkOrderNo != null && oldTaskNoByWorkOrder.TryGetValue(mesWorkOrderNo, out var oldTaskNo))
            return oldTaskNo;

        // ② / ③ B、C 桶：本 Run 内按「Stage 内 MES 执行批」归组，同批共号（T-002）
        var batchKey = TaskNoBatchKey(mesWorkOrderNo, final.ExecutionBatchDraftKey, final.StageCode);
        if (taskNoByBatch.TryGetValue(batchKey, out var existing))
            return existing;

        var newTaskNo = $"PEGG-{planVersionId}-{ShortHash16(batchKey)}";
        taskNoByBatch[batchKey] = newTaskNo;
        return newTaskNo;
    }

    /// <summary>
    /// TaskNo 归组键 = 「Stage 内 MES 执行批」（T-002 + T-003「MES 工单不跨 Stage」）。
    ///   · B 桶（<paramref name="mesWorkOrderNo"/> 非空）⇒ 以 **MES 工单 + Stage** 为批身份（一个 WO 一个号）；
    ///   · C 桶（为空）⇒ 以 **Solver 归组键 + Stage** 为批身份（此时 MES 工单尚未创建，T-004）。
    /// 两级键都带前缀（`WO|` / `EB|`）以免「工单号恰好等于某批键」时两类撞键。
    /// </summary>
    internal static string TaskNoBatchKey(string? mesWorkOrderNo, string? executionBatchDraftKey, string? stageCode)
        => mesWorkOrderNo is not null
            ? $"WO|{mesWorkOrderNo}|{stageCode}"
            : $"EB|{executionBatchDraftKey}|{stageCode}";

    /// <summary>
    /// 批身份 → TaskNo 后缀的**确定性**短哈希（SHA-256 前 16 个十六进制字符 = 64 bit）。
    ///
    /// 为什么不能用 <c>string.GetHashCode()</c>：.NET Core 起它对 string 每进程随机加盐，
    ///   **同一批键跨运行会得到不同值** ⇒ TaskNo 不可复算，破坏 T-002「跨版本业务身份」。
    /// 为什么取 16 位：10 万批规模下生日碰撞概率 ≈ 3×10⁻¹¹，可忽略；且 `PEGG-{pv}-{16hex}` 远短于列宽 NVARCHAR(50)。
    /// </summary>
    internal static string ShortHash16(string s)
        => Convert.ToHexString(
               System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)),
               0, 8);

    /// <summary>
    /// 跨 Domain Quantity-Time（§8/D12）：下游域启动前，从上游域已落盘 Task 读取 ChildMaterialCode
    /// 的完工时间 + DefaultLeadTimeDays，构造成分段虚拟供给注入本域供给池。
    /// 保持多段（40@15日 + 60@17日），禁止压平——每一条上游 Task 独立 Add 一段。
    /// 工厂口径：Domain_Dependency 承载同厂跨族血缘（FAMILY 域），上游 Task 的 Order.FactoryId 即消耗侧工厂；
    /// 跨厂流动走 CrossFactoryEdge 主链，不经此路径。
    /// Domain_Dependency 为空（V1 现状）时直接跳过，零额外开销。
    /// </summary>
    private async System.Threading.Tasks.Task LoadUpstreamDomainSupplyAsync(
        SupplyPool pool,
        PeggingExecutionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DomainKey))
            return;

        var runId = request.SchedulingContext?.ScheduleRunId;
        if (!runId.HasValue || runId.Value <= 0)
            return;

        var dependencies = (await _connectionManager.QueryAsync<UpstreamDomainDependencyRow>(
            @"SELECT UpstreamDomainCode, ChildMaterialCode, DefaultLeadTimeDays
              FROM Domain_Dependency
              WHERE DownstreamDomainCode = @DomainKey",
            new { DomainKey = request.DomainKey },
            db: DatabaseId.APS)).ToList();

        if (dependencies.Count == 0)
            return;

        foreach (var dep in dependencies)
        {
            // 定位同一 ScheduleRun 内上游域的 PlanVersion（分域后每 Domain 一个 PlanVersion）
            var upstreamPv = await _connectionManager.QueryFirstOrDefaultAsync<UpstreamPlanVersionRow>(
                @"SELECT TOP 1 Id
                  FROM PlanVersion
                  WHERE SourceScheduleRunId = @RunId AND DomainKey = @UpstreamDomainCode
                  ORDER BY Id DESC",
                new { RunId = runId.Value, UpstreamDomainCode = dep.UpstreamDomainCode },
                db: DatabaseId.APS);

            if (upstreamPv == null)
            {
                _logger.LogWarning(
                    "[Pegging] 上游域 {UpstreamDomain} 无对应 PlanVersion（跨域 Quantity-Time 跳过）",
                    dep.UpstreamDomainCode);
                continue;
            }

            // 读上游已落盘 Task 的完工时间，逐条 Add 为分段虚拟供给（保留多段，不压平）
            var upstreamTasks = (await _connectionManager.QueryAsync<UpstreamTaskSupplyRow>(
                @"SELECT m.MaterialCode,
                         t.MaterialId,
                         o.FactoryId,
                         f.Code          AS FactoryCode,
                         t.Quantity,
                         t.PlannedEndTime
                  FROM [Task] t
                  INNER JOIN Material m ON m.Id = t.MaterialId
                  INNER JOIN [Order]  o ON o.Id = t.OrderId
                  INNER JOIN Factory  f ON f.Id = o.FactoryId
                  WHERE t.PlanVersionId = @UpstreamPlanVersionId
                    AND m.MaterialCode = @ChildMaterialCode
                    AND t.PlannedEndTime IS NOT NULL",
                new { UpstreamPlanVersionId = upstreamPv.Id, ChildMaterialCode = dep.ChildMaterialCode },
                db: DatabaseId.APS)).ToList();

            if (upstreamTasks.Count == 0)
                continue;

            var leadTimeDays = dep.DefaultLeadTimeDays > 0 ? dep.DefaultLeadTimeDays : 0;
            foreach (var t in upstreamTasks)
            {
                pool.Add(
                    materialCode: t.MaterialCode,
                    materialId: t.MaterialId,
                    factoryId: t.FactoryId,
                    qty: t.Quantity,
                    availableAt: t.PlannedEndTime.Value.AddDays(leadTimeDays),
                    sourceType: Core.Enum.SupplySourceType.UPSTREAM_DOMAIN_PRODUCTION,
                    sourceRef: $"UPSTREAM_{upstreamPv.Id}_{t.MaterialId}_{t.PlannedEndTime:yyyyMMddHHmmss}",
                    factoryCode: t.FactoryCode);
            }
        }
    }

    private async System.Threading.Tasks.Task LoadActiveLockDataAsync(SupplyPool pool, CancellationToken ct)
    {
        var allSupplyKeys = pool.GetAllEntries()
            .Select(e => e.SupplyKey)
            .Distinct()
            .ToList();

        if (allSupplyKeys.Count == 0)
        {
            _logger.LogDebug("[Pegging] 供给池为空，跳过 Lock 数据加载");
            return;
        }

        // 批量查询所有供给上的活跃 Lock
        var lockTasks = allSupplyKeys.Select(key => _lockRepo.GetActiveLocksOnSupplyAsync(key, ct));
        var lockResults = await System.Threading.Tasks.Task.WhenAll(lockTasks);
        var allLocks = lockResults.SelectMany(x => x).ToList();

        if (allLocks.Count == 0)
        {
            _logger.LogDebug("[Pegging] 未发现活跃 Lock 记录");
            return;
        }

        // 按 SupplyKey 分组，附加到对应的 SupplyLedgerEntry
        var locksBySupplyKey = allLocks.GroupBy(l => l.SupplyKey).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var entry in pool.GetAllEntries())
        {
            if (locksBySupplyKey.TryGetValue(entry.SupplyKey, out var locks))
            {
                foreach (var dbLock in locks)
                {
                    var lockType = dbLock.LockType switch
                    {
                        "STRICT_BINDING" => LockType.STRICT_BINDING,
                        "DEMAND_PROTECTION" => LockType.DEMAND_PROTECTION,
                        _ => (LockType?)null
                    };

                    if (!lockType.HasValue)
                    {
                        _logger.LogWarning(
                            "[Pegging] 未识别的 LockType: {LockType}，SupplyKey={SupplyKey}",
                            dbLock.LockType, entry.SupplyKey);
                        continue;
                    }

                    entry.Locks.Add(new LockRecord
                    {
                        LockType = lockType.Value,
                        LockedQty = dbLock.LockedQty,
                        LockedToOrderId = dbLock.SourcePlanVersionId.HasValue
                            ? null
                            : ExtractOrderIdFromDemandKey(dbLock.DemandKey),
                        LockedToDemandKey = dbLock.DemandKey,
                        LockedAt = dbLock.CreatedAt
                    });

                    // 更新 LockedQty 累计
                    entry.LockedQty += dbLock.LockedQty;
                }
            }
        }

        _logger.LogDebug(
            "[Pegging] 已加载 {LockCount} 条活跃 Lock 记录到供给池",
            allLocks.Count);
    }

    /// <summary>
    /// 从 DemandKey 提取 OrderId（如：ORDER_12345_MAT001_F01 → 12345）
    /// </summary>
    private static long? ExtractOrderIdFromDemandKey(string demandKey)
    {
        if (string.IsNullOrEmpty(demandKey)) return null;

        var parts = demandKey.Split('_');
        if (parts.Length >= 2 && parts[0] == "ORDER" && long.TryParse(parts[1], out var orderId))
            return orderId;

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PI Position 两级装载（§8）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 从 3号位 FrozenStrategySnapshot 投影出 5号位 FrozenFactParameters（C2-6：2号位在集成层投影，不落 DTO）。
    /// V1 最小集仅 Stage 进度走 PI Position；DefaultPurchaseLt / OverdueMargin / ArrivalToUsableOffsets
    /// 供 5号位后续事实（XC/Transit/Received）消费时使用，当前先按已冻结 ProcurementBlock 参数值投影。
    /// </summary>
    internal static FrozenFactParameters BuildFrozenFactParameters(FrozenStrategySnapshot snapshot)
    {
        // Warehouse 级默认 LT（MaterialId 为空）作为 DefaultPurchaseLt；无配置时 0。
        var defaultLtDays = snapshot.Procurement.DefaultPurchaseLt?
            .FirstOrDefault(r => string.IsNullOrWhiteSpace(r.MaterialId))?.DefaultLtDays ?? 0;

        return new FrozenFactParameters
        {
            StrategyProfileVersionId = snapshot.StrategyProfileVersionId,
            DefaultPurchaseLt = (int)Math.Round(defaultLtDays, MidpointRounding.AwayFromZero),
            // OverdueMargin 语义对齐：FrozenFactParameters.OverdueMargin 为「天」，取 ProcurementBlock 的 MinimumExtraDays。
            OverdueMargin = snapshot.Procurement.OverdueMargin?.MinimumExtraDays ?? 0,
            ArrivalToUsableOffsets = (snapshot.Procurement.ArrivalToUsableOffsets ?? [])
                .GroupBy(r => r.WarehouseCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (int)Math.Round(g.First().OffsetHours, MidpointRounding.AwayFromZero),
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private const int SqlServerInParameterLimit = 2000;

    /// <summary>
    /// 分批执行带 IN 列表的查询，规避 SQL Server TDS 2100 参数上限。
    /// wipStageRows 非空后 distinct MaterialCode/PI 可达数千，单条 `IN @List`（Dapper 展开为逐值参数）会溢出。
    /// </summary>
    private static async Task<List<TRow>> QueryChunkedInAsync<TValue, TRow>(
        IReadOnlyList<TValue> values,
        Func<IReadOnlyList<TValue>, Task<IEnumerable<TRow>>> query)
    {
        var rows = new List<TRow>();
        for (var i = 0; i < values.Count; i += SqlServerInParameterLimit)
        {
            var chunk = values.Skip(i).Take(SqlServerInParameterLimit).ToList();
            rows.AddRange(await query(chunk));
        }

        return rows;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // T2-01（2026-10-09）：PI 权威剩余事实 —— 来源 [Order] ∪ 本域订单链，与 Stage/MES 解耦
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>[Order] / Order_Canonical → PI 权威剩余事实 的装载行（Dapper 映射用，一 PI 可能多行，装载后归并）。</summary>
    private sealed class PiRemainingLoadRow
    {
        public string ProductionInstructionNo { get; set; } = string.Empty;
        public int MaterialId { get; set; }
        public string MaterialCode { get; set; } = string.Empty;
        public int FactoryId { get; set; }
        public string FactoryCode { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal? ReceivedQty { get; set; }

        /// <summary>来源 <c>[Order].Id</c>。来自 <c>Order_Canonical</c> 的补取行没有 <c>[Order]</c> 承载行 ⇒ 恒 0。</summary>
        public long OrderId { get; set; }

        /// <summary>来源 <c>Order_Canonical.Id</c>（两种来源都有；追溯与 T2-01.5 缺口样本核验用）。</summary>
        public long OrderCanonicalId { get; set; }

        public string OrderType { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
        public DateTime? IssueDate { get; set; }
    }

    /// <summary>
    /// 装载 PI 权威剩余事实（T2-01）。**任何 Stage / MES 工单 / PI Position 判断之前**先做这一步，
    /// 建立 <c>PI号 → {原始量, 已入库量, 剩余量}</c> 的运行期权威映射。
    ///
    /// 口径（用户 2026-10-09 定盘）：<c>PiRemainingQty = max(Quantity − ISNULL(ReceivedQty,0), 0)</c>，
    /// 两列在整条链（ODS 视图 → Staging → Canonical → [Order]）原样透传、零运算，故先后 - 原样等价。
    /// 判别「有 PI」只看 <c>MTS_InstructionNo IS NOT NULL</c>——不看 OrderType 细分、不看 ZPQF、不看 OrderNo 前缀。
    ///
    /// 边界（PM T2-01.2：不用 0 / 客户需求量 / Stage MAX **偷偷**替代）：
    ///   · <c>ReceivedQty IS NULL</c> → 按 0 参与公式，但置 <c>HasMissingReceivedQty</c> 并留痕；
    ///   · <c>ReceivedQty &gt; Quantity</c> → 剩余 clamp 到 0，置 <c>HasOverReceivedQty</c> 并留痕；
    ///   · 同一 PI 出现在多行且数量矛盾 → 取「剩余量最大」的那行为准（不静默求和，避免重复计量），并留痕。
    ///
    /// ── 范围（2026-10-09 整改，PM T2-01.5「若现有 [Order] 确实无法取得某些真实 PI 的原始量，
    ///    应报告具体样本与最小现有链路缺口」对应的落码）──
    /// 两个来源合并，按 <c>OrderCanonicalId</c> 去重：
    ///   A) <c>[Order]</c>：本 PlanVersion 已装载的行（顶层成品）。见 <see cref="BomSnapshot.MaterialUniverse"/> 说明
    ///      —— <c>[Order]</c> 的装载带产品族闸门，而下阶加工件物料的 <c>ProductFamilyId</c> 恒 NULL（归族规则
    ///      只覆盖成品工艺码），**PI 事实在供给池里因此永远只覆盖顶层**。
    ///   B) <c>Order_Canonical</c>：本域 <paramref name="materialUniverse"/>（BOM 父件∪子件）内、
    ///      <c>Status IN ('Open','Released')</c> 的 PI 行 —— **不加产品族闸门**。PI 是供给事实，
    ///      与「顶层需求归哪个域」是两件事；不改归域口径、不改 <c>[Order]</c> 内容（加工件 PI 行若装进
    ///      <c>[Order]</c> 会变成顶层需求 ⇒ 重复生产）。
    ///
    /// ⚠️ B 源**不加 <c>DueDate</c> 时间窗**（<c>[Order]</c> 装载带的「90 天」是**需求侧**规划视界）。
    ///    供给侧装载（库存/采购在途）从无时间窗，PI 同属供给；若给 B 源加 90 天窗，则「交期在外、但父件要得急」
    ///    的下阶 PI 会被误判为「无供给」⇒ 塌成新增生产（正是本次要修的失败形态）。实测 PV2：
    ///    宇宙内 PI 行 118,154（Σ剩余 ≈6,265万）→ 加 90 天窗后只剩 25,713（Σ剩余 ≈2,485万）。
    ///
    /// ⛔ 不在此处构造无 PI 的客户订单（PM T2-03.3：无 PI 需求不走 PI 公式，按冻结 Order.Quantity 处理）。
    /// ⛔ 不预置任何数据库列/表/接口（PM T2-01.3：现有 DTO/内存上下文最小实现）。
    /// </summary>
    private async Task<IReadOnlyList<PiRemainingFact>> LoadPiRemainingFactsAsync(
        int planVersionId,
        IReadOnlySet<string> materialUniverse,
        CancellationToken ct)
    {
        var rows = (await _connectionManager.QueryAsync<PiRemainingLoadRow>(
            @"SELECT o.MTS_InstructionNo AS ProductionInstructionNo,
                     o.MaterialId,
                     m.MaterialCode,
                     o.FactoryId,
                     f.Code           AS FactoryCode,
                     o.Quantity,
                     o.ReceivedQty,
                     o.Id             AS OrderId,
                     ISNULL(o.OrderCanonicalId, 0) AS OrderCanonicalId,
                     o.OrderType,
                     o.CustomerDueDate AS DueDate,
                     o.IssueDate
              FROM [Order] o
              INNER JOIN Material m ON m.Id = o.MaterialId
              INNER JOIN Factory  f ON f.Id = o.FactoryId
              WHERE o.PlanVersionId = @PlanVersionId
                AND o.MTS_InstructionNo IS NOT NULL
                AND o.MTS_InstructionNo <> ''",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS)).ToList();

        var orderRowCount = rows.Count;

        // ── B 源：本域物料宇宙内的订单链 PI 行（无产品族闸门）──
        // 与 A 源的列一一对应（OrderId 恒 0；DueDate 用 oc.DueDate 对齐 A 源的 CustomerDueDate 语义）。
        // 分块执行 —— 宇宙物料实测可达 6 万+，裸 IN 必撞 2100 参数上限（见 loader-no-raw-in-list 教训）。
        if (materialUniverse.Count > 0)
        {
            var universeCodes = materialUniverse.ToList();
            var extra = await QueryChunkedInAsync(universeCodes, chunk =>
                _connectionManager.QueryAsync<PiRemainingLoadRow>(
                    @"SELECT oc.MTS_InstructionNo AS ProductionInstructionNo,
                             m.Id             AS MaterialId,
                             oc.MaterialCode,
                             f.Id             AS FactoryId,
                             f.Code           AS FactoryCode,
                             oc.Quantity,
                             oc.ReceivedQty,
                             0                AS OrderId,
                             oc.Id            AS OrderCanonicalId,
                             oc.OrderType,
                             oc.DueDate,
                             oc.IssueDate
                      FROM Order_Canonical oc
                      INNER JOIN Material m ON m.MaterialCode = oc.MaterialCode
                      INNER JOIN Factory  f ON f.Code = oc.FactoryCode AND f.IsActive = 1
                      WHERE oc.Status IN ('Open','Released')
                        AND oc.MTS_InstructionNo IS NOT NULL
                        AND oc.MTS_InstructionNo <> ''
                        AND oc.MaterialCode IN @MaterialCodes
                        AND NOT EXISTS (SELECT 1 FROM [Order] o
                                        WHERE o.PlanVersionId = @PlanVersionId
                                          AND o.OrderCanonicalId = oc.Id)",
                    new { PlanVersionId = planVersionId, MaterialCodes = chunk },
                    db: DatabaseId.APS));

            rows.AddRange(extra);
        }

        _logger.LogInformation(
            "[Pegging][T2-01] PI 事实装载来源：A=[Order] {OrderRows} 行 / B=Order_Canonical(本域物料宇宙 {Universe} 个物料，" +
            "无产品族闸门) {ExtraRows} 行；合计 {Rows} 行（PlanVersionId={PlanVersionId}）",
            orderRowCount, materialUniverse.Count, rows.Count - orderRowCount, rows.Count, planVersionId);

        if (rows.Count == 0)
        {
            _logger.LogWarning(
                "[Pegging][T2-01] 无任何带 MTS_InstructionNo 的 PI 行（[Order] 与本域订单链均为空，PlanVersionId={PlanVersionId}）" +
                "⇒ 本次无真实 PI 事实，PI Position 与 PI Supply 均为空。请确认订单同步链（ODS → Staging → Canonical）已跑。",
                planVersionId);
            return Array.Empty<PiRemainingFact>();
        }

        var facts = new List<PiRemainingFact>(rows.Count);
        var missingReceived = 0;
        var overReceived = 0;
        var duplicatePi = 0;
        var duplicateConflict = 0;

        foreach (var group in rows.GroupBy(r => r.ProductionInstructionNo, StringComparer.Ordinal))
        {
            var first = group.First();
            if (group.Count() > 1) duplicatePi++;

            PiRemainingLoadRow chosen = first;
            decimal chosenRemaining = decimal.MinValue;

            foreach (var r in group)
            {
                if (r.ReceivedQty is null) missingReceived++;
                if (r.ReceivedQty is { } rcv && rcv > r.Quantity) overReceived++;

                var remaining = Math.Max(r.Quantity - (r.ReceivedQty ?? 0m), 0m);

                // 同一 PI 多行：取剩余量最大者为准（不是求和——同一 PI 的总量只应计量一次）。
                if (remaining > chosenRemaining)
                {
                    chosen = r;
                    chosenRemaining = remaining;
                }
            }

            if (group.Count() > 1)
            {
                // 多行之间若 (Quantity, ReceivedQty) 不一致，即为 PM T2-01.2 所说的「PI 映射重复矛盾」。
                var qtySet = group.Select(r => (r.Quantity, r.ReceivedQty)).Distinct().Count();
                if (qtySet > 1)
                {
                    duplicateConflict++;
                    _logger.LogWarning(
                        "[Pegging][T2-01] PI {Pi} 在 [Order] 中出现 {N} 行且数量字段不一致（{Vals}），" +
                        "按剩余量最大行取值（OrderId={OrderId}，剩余={Remaining}）。同一物理数量不得重复计量，请核上游 PI 映射。",
                        group.Key, group.Count(),
                        string.Join(" | ", group.Select(r => $"{r.Quantity}/{r.ReceivedQty?.ToString() ?? "NULL"}")),
                        chosen.OrderId, chosenRemaining);
                }
            }

            var fact = new PiRemainingFact
            {
                ProductionInstructionNo = chosen.ProductionInstructionNo,
                MaterialId              = chosen.MaterialId,
                MaterialCode            = chosen.MaterialCode,
                FactoryId               = chosen.FactoryId,
                FactoryCode             = chosen.FactoryCode,
                PiQuantity              = chosen.Quantity,
                PiReceivedQty           = chosen.ReceivedQty,
                PiRemainingQty          = chosenRemaining,
                OrderId                 = chosen.OrderId,
                OrderType               = chosen.OrderType,
                OrderCanonicalId        = chosen.OrderCanonicalId,
                DueDate                 = chosen.DueDate,
                IssueDate               = chosen.IssueDate,
                HasMissingReceivedQty   = chosen.ReceivedQty is null,
                HasOverReceivedQty      = chosen.ReceivedQty is { } c && c > chosen.Quantity
            };

            if (fact.HasMissingReceivedQty)
                _logger.LogWarning(
                    "[Pegging][T2-01] PI {Pi} 的 ReceivedQty 为 NULL（[Order].Id={OrderId} / OrderCanonicalId={CanonicalId}）" +
                    "——已按 0 参与公式，剩余={Remaining}。该 PI 的已入库事实不可得，请核上游同步链。",
                    fact.ProductionInstructionNo, fact.OrderId, fact.OrderCanonicalId, fact.PiRemainingQty);

            if (fact.HasOverReceivedQty)
                _logger.LogWarning(
                    "[Pegging][T2-01] PI {Pi} 的 ReceivedQty({Rcv}) > Quantity({Qty})（[Order].Id={OrderId} / " +
                    "OrderCanonicalId={CanonicalId}）——剩余已 clamp 到 0，不再作为可排产供给。请核上游数量口径。",
                    fact.ProductionInstructionNo, fact.PiReceivedQty, fact.PiQuantity, fact.OrderId, fact.OrderCanonicalId);

            facts.Add(fact);
        }

        _logger.LogInformation(
            "[Pegging][T2-01] PI 权威剩余事实装载完成：PI={Pi} 个（装载行 {Rows} 行 = [Order] {OrderRows} + 订单链补取 {ExtraRows}；" +
            "重复PI {Dup} 个其中数量矛盾 {Conflict} 个；ReceivedQty NULL {Miss} 处；超量 {Over} 处）；" +
            "Σ原始量={SumQ}，Σ已入库={SumR}，Σ剩余={SumRem}，剩余>0 的 PI={Pos} 个",
            facts.Count, rows.Count, orderRowCount, rows.Count - orderRowCount,
            duplicatePi, duplicateConflict, missingReceived, overReceived,
            facts.Sum(f => f.PiQuantity), facts.Sum(f => f.PiReceivedQty ?? 0m), facts.Sum(f => f.PiRemainingQty),
            facts.Count(f => f.PiRemainingQty > 0m));

        return facts;
    }

    /// <summary>
    /// 装载 PI Position（消费 5号位 IProductionInstructionPositionCalculator）。返回 ProductionInstructionNo → 结果。
    ///
    /// ⚠️ T2-04（2026-10-09 整改）——**PI 宇宙 = <paramref name="piFacts"/>（[Order]），不是 Stage**：
    ///   · 真实 PI 即使当前没有任何 Stage 报工行，也必须出现在 inputs / Routing / 快照里（PM T2-04.2），
    ///     否则它会从 PI Position 里整个消失，下游也就永远看不到这个在制 PI。
    ///   · PI 总量 <c>ErpRemainingQty</c> 只取 <c>piFacts.PiRemainingQty</c>（= max(Quantity − ReceivedQty, 0)）；
    ///     旧写法 <c>ErpRemainingQty = group.Max(r =&gt; r.RemainingQty)</c>（Stage MAX）**已删除**（PM T2-04.1）。
    ///     Stage 明细从此只回答「已报到哪些 Stage、各 Stage 进度多少」——是**定位明细**，不是总量。
    ///
    /// 事实范围（V1 最小集）：Stage 进度（StageProgressSnapshot）→ StageProgressFact（可空）；
    /// Stage 顺序取 APS_BOM_STAGE_PATH_RAW（ChildMaterialCode+StageCode → StageSeq，多批次/BOMNO/Scope 取 MIN，近似）；
    /// PiInventory / XC / CrossFactoryEdge / Transit 已绑定（ext_ 同义词 Loader，Transit 实测 0 行）；
    /// Received 已退出计算主链（不再装载，见 PM 裁定）；
    /// 计算器按 UNLOCATED 兜底闭合，2号位据此把未定位份额路由到新增生产。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ProductionInstructionPositionResult>> LoadPiPositionsAsync(
        IReadOnlyList<PiRemainingFact> piFacts,
        IReadOnlyList<WipStageLoadRow> wipStageRows,
        FrozenStrategySnapshot frozenSnapshot,
        int planVersionId,
        CancellationToken ct,
        bool persistPositionSnapshot = true)
    {
        if (piFacts.Count == 0)
        {
            // 无真实 PI（[Order] 里没有任何带 MTS_InstructionNo 的行）⇒ 本次没有在制 PI 可定位。
            // 注意：这与「有 PI 但 Stage 明细为空」是两回事——后者仍须逐 PI 产出 Position（T2-04.2）。
            _logger.LogWarning(
                "[Pegging][T2-04] PI 权威事实为空 ⇒ 本次不产出任何 PI Position（无在制 PI）。" +
                "若业务上确有在制 PI，请先核订单同步链是否已把 MTS_InstructionNo 带进 [Order]。");
            return new Dictionary<string, ProductionInstructionPositionResult>();
        }

        // T2-04.2：driverRows = 「每个 PI 一行身份行」（StageCode 空、数量 0，仅供下游装载器枚举 PI/物料/工厂）
        //           ∪ 真实 Stage 定位明细行。
        // 下游装载器（库存/XC/跨厂边/在途/工单快照/工序进度/Routing）一律收 driverRows：
        // 它们要回答的是「这个 PI 有哪些事实」，不是「这个 PI 报到哪了」——后者才需要 Stage 行。
        var driverRows = new List<WipStageLoadRow>(piFacts.Count + wipStageRows.Count);
        driverRows.AddRange(piFacts.Select(f => new WipStageLoadRow
        {
            ProductionInstructionNo = f.ProductionInstructionNo,
            MaterialCode            = f.MaterialCode,
            MaterialId              = f.MaterialId,
            FactoryId               = f.FactoryId,
            FactoryCode             = f.FactoryCode,
            StageCode               = string.Empty,
            GoodCompletedQty        = 0m,
            PlannedQty              = 0m,
            RemainingQty            = 0m
        }));
        driverRows.AddRange(wipStageRows);

        // 1) Stage 顺序映射：ChildMaterialCode+StageCode → StageSeq
        //    【2026-09-28 改造，1号位 回执 §4.2 双方同意】原实现是**全局** `MIN(StageSeq)`、无任何批次上下文
        //    （PM §二十一.3 明令禁止的「跨订单/BOM/父件汇总」形态）。现改为三态：
        //      ① 每条 WIP 行按自身 PI → 订单 → BOM 批次，取**该批次**的 StagePath（精确，优先）；
        //      ② 该批次内查不到 ⇒ 回退**全局** `MIN(StageSeq)`（= 原行为，**保证不回归**：不因批次缺失而归零，
        //         避免重演「StagePathFact 空 → startStageCode 空 → derivedRemainingQty=0 → 406 上下文全 0」）；
        //      ③ 三态**可计数**（1号位 要求：兜底占比必须可监控 —— 悄悄涨到 100% 的兜底等于没改）。
        var materialCodes = driverRows.Select(r => r.MaterialCode).Distinct().ToList();

        // 1.a) PI → 本次 BOM 批次
        var piNos = driverRows.Select(r => r.ProductionInstructionNo)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var batchByPi = await LoadBatchByPiAsync(piNos);

        // 1.b) 本批 StagePath（逐批一次分块查询）：(BatchNo, MaterialCode) → { StageCode → StageSeq }
        var batchStageByMaterial = new Dictionary<(string BatchNo, string MaterialCode), Dictionary<string, int>>();
        foreach (var batch in batchByPi.Values.Distinct(StringComparer.Ordinal).ToList())
        {
            var rows = await QueryChunkedInAsync(materialCodes, chunk =>
                _connectionManager.QueryAsync<StagePathLoadRow>(
                    @"SELECT ChildMaterialCode, StageCode, MIN(StageSeq) AS StageSeq
                      FROM APS_BOM_STAGE_PATH_RAW
                      WHERE BatchNo = @BatchNo AND ChildMaterialCode IN @MaterialCodes
                      GROUP BY ChildMaterialCode, StageCode",
                    new { BatchNo = batch, MaterialCodes = chunk },
                    db: DatabaseId.APS));

            foreach (var r in rows)
            {
                var key = (batch, r.ChildMaterialCode);
                if (!batchStageByMaterial.TryGetValue(key, out var m))
                    batchStageByMaterial[key] = m = new Dictionary<string, int>(StringComparer.Ordinal);
                m[r.StageCode] = r.StageSeq;
            }
        }

        // 1.c) 全局兜底（= 原实现口径）
        var globalRows = await QueryChunkedInAsync(materialCodes, chunk =>
            _connectionManager.QueryAsync<StagePathLoadRow>(
                @"SELECT ChildMaterialCode, StageCode, MIN(StageSeq) AS StageSeq
                  FROM APS_BOM_STAGE_PATH_RAW
                  WHERE ChildMaterialCode IN @MaterialCodes
                  GROUP BY ChildMaterialCode, StageCode",
                new { MaterialCodes = chunk },
                db: DatabaseId.APS));

        var globalStageByMaterial = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var r in globalRows)
        {
            if (!globalStageByMaterial.TryGetValue(r.ChildMaterialCode, out var m))
                globalStageByMaterial[r.ChildMaterialCode] = m = new Dictionary<string, int>(StringComparer.Ordinal);
            m[r.StageCode] = r.StageSeq;
        }

        // 1.d) 合并：批次优先、全局兜底；同时统计三态。
        //      同物料多 WIP 行时取其**首个可解析批次**（确定性优先，避免同物料不同行取到不同批次）。
        var batchByMaterial = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in driverRows)
        {
            if (batchByMaterial.ContainsKey(r.MaterialCode)) continue;
            if (!string.IsNullOrEmpty(r.ProductionInstructionNo)
                && batchByPi.TryGetValue(r.ProductionInstructionNo, out var b))
                batchByMaterial[r.MaterialCode] = b;
        }

        var stageSeqMap = new Dictionary<(string MaterialCode, string StageCode), int>();
        var stageSeqByMaterial = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var batchHitPairs = 0;
        var globalFallbackPairs = 0;
        var unresolvedStageSeqMaterials = 0;
        foreach (var code in materialCodes)
        {
            globalStageByMaterial.TryGetValue(code, out var globalMap);
            Dictionary<string, int>? batchMap = null;
            if (batchByMaterial.TryGetValue(code, out var batch) && !string.IsNullOrEmpty(batch))
                batchStageByMaterial.TryGetValue((batch, code), out batchMap);

            var merged = new Dictionary<string, int>(StringComparer.Ordinal);
            if (globalMap != null)
                foreach (var kv in globalMap) merged[kv.Key] = kv.Value;
            if (batchMap != null)
                foreach (var kv in batchMap) merged[kv.Key] = kv.Value;

            if (merged.Count == 0)
            {
                unresolvedStageSeqMaterials++;
                continue;
            }

            var batchCnt = batchMap?.Count ?? 0;
            batchHitPairs += batchCnt;
            globalFallbackPairs += merged.Count - batchCnt;

            stageSeqByMaterial[code] = merged;
            foreach (var kv in merged)
                stageSeqMap[(code, kv.Key)] = kv.Value;
        }

        // 兜底占比可监控（1号位 要求）：若「全局兜底」长期接近「批次内命中」，说明 PI→批次 解析失效
        // 或批次内确无 StagePath —— 兜底形同虚设，必须能从日志直接发现，而非等结果变差再回溯。
        _logger.LogInformation(
            "[Pegging] PI Stage 顺序装载：物料 {Materials} 个（批次内命中 {BatchHit} 个(物料,阶段) / 全局兜底 {GlobalFallback} 个 / 无 StagePath {Unresolved} 个物料；PI→批次解析 {BatchResolved} 个 PI）",
            materialCodes.Count, batchHitPairs, globalFallbackPairs, unresolvedStageSeqMaterials, batchByPi.Count);

        // 每物料 Stage 路径（按 StageSeq 升序，首/末标记 IsStartStage/IsEndStage）。
        // 关键：5号位 DetermineEffectiveStartStage / CalculateStagePositions(isFirstStage) /
        //       CalculateNextOperationContexts 都消费 input.StagePath；缺了会 startStageCode 为空 →
        //       BuildExistingExecutionContexts 的 derivedRemainingQty=0 → 连续份额切不出（实测 406 上下文全 0）。
        var stagePathByMaterial = stageSeqByMaterial
            .ToDictionary(
                kv => kv.Key,
                kv =>
                {
                    var ordered = kv.Value
                        .Select(x => (StageCode: x.Key, StageSequence: x.Value))
                        .OrderBy(x => x.StageSequence)
                        .ToList();
                    return (IReadOnlyList<StagePathFact>)ordered.Select((x, i) => new StagePathFact
                    {
                        StageCode = x.StageCode,
                        StageSequence = x.StageSequence,
                        IsStartStage = i == 0,
                        IsEndStage = i == ordered.Count - 1
                    }).ToList();
                },
                StringComparer.Ordinal);

        // 1.5) PI 级库存事实（ext_ 同义词：ERP_Inventory_View.WarehouseCode=6位工序码 → MES_ProcessCode_View.StageCode）
        var piInventoryMap = await LoadPiInventoryFactsAsync(driverRows, ct);

        // 1.6) XC（线边仓）事实（ERPProperty='XC' × 库存）
        var xcMap = await LoadXcFactsAsync(driverRows, ct);

        // 1.7) 跨厂边事实（ext_MES_APS_BOM_Workset_CrossFactoryEdge，按 ChildMaterialCode 归组）
        var crossFactoryEdgeMap = await LoadCrossFactoryEdgesAsync(driverRows, ct);

        // 1.9) 厂间在途事实（Transit，ext_ERP_InterplantInTransit_View；0 行，待 5号位 ODS 数据）
        // PM 0910：Transit 必须按生产指示号（TransitDocumentNo = PI No）精确归属，Material/目标厂仅做一致性校验。
        var transitFacts = await LoadTransitFactsAsync(driverRows, ct);
        var piIdentities = driverRows
            .GroupBy(r => r.ProductionInstructionNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.First().MaterialCode, g.First().FactoryCode), StringComparer.Ordinal);
        var (transitByPi, transitIssues) = AttributeInterplantTransitToPi(transitFacts, piIdentities);
        foreach (var issue in transitIssues)
            _logger.LogWarning("[Pegging] Transit 归属 Issue: {Issue}", issue);

        // 1.95) 本次快照批次 ScheduleRunId（OperationProgress / MESWorkOrder 与 StageProgress 同批快照）
        var scheduleRunId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            "SELECT SourceScheduleRunId FROM PlanVersion WHERE Id = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        // 1.96) 每工单快照（IN_PROGRESS）+ 每工单工序进度——5号位 BuildExistingExecutionContexts 的两路事实源
        var workOrderFactsByPi = await LoadWorkOrderSnapshotFactsAsync(scheduleRunId, driverRows, ct);
        var operationProgressByPi = await LoadOperationProgressFactsAsync(scheduleRunId, driverRows, ct);
        var (routingOpsByPi, routingDepsByPi) = await LoadRoutingFactsAsync(driverRows, ct);

        // 诊断：事实装载计数（连续份额依赖 WorkOrder IN_PROGRESS + OperationProgress）
        _logger.LogInformation(
            "[Pegging][Diag] PI Position 事实: WIP行={Wip}, IN_PROGRESS工单={Wo}, 工序进度={Op行}, 工单PI={WoPi}",
            wipStageRows.Count,
            workOrderFactsByPi.Values.Sum(v => (long)v.Count),
            operationProgressByPi.Values.Sum(v => (long)v.Count),
            workOrderFactsByPi.Count);

        // 2) 按 PI 构建输入 —— 宇宙 = piFacts（[Order]），Stage 明细可为空（T2-04.2）
        var stageRowsByPi = wipStageRows
            .GroupBy(r => r.ProductionInstructionNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var inputs = new List<ProductionInstructionPositionInput>(piFacts.Count);
        foreach (var fact in piFacts)
        {
            var pi = fact.ProductionInstructionNo;
            stageRowsByPi.TryGetValue(pi, out var stageRows);

            var stageProgress = new List<StageProgressFact>(stageRows?.Count ?? 0);
            if (stageRows != null)
            {
                foreach (var s in stageRows)
                {
                    stageProgress.Add(new StageProgressFact
                    {
                        StageCode = s.StageCode,
                        GoodCompletedQty = s.GoodCompletedQty,
                        PlannedQty = s.PlannedQty,
                        RemainingQty = s.RemainingQty,
                        StageSequence = stageSeqMap.TryGetValue((s.MaterialCode, s.StageCode), out var seq) ? seq : 0,
                        SnapshotId = null
                    });
                }
            }

            var key = (fact.MaterialCode, fact.FactoryCode);
            piInventoryMap.TryGetValue(key, out var piInventories);
            xcMap.TryGetValue(key, out var xcFacts);
            crossFactoryEdgeMap.TryGetValue(fact.MaterialCode, out var crossFactoryEdges);
            transitByPi.TryGetValue(pi, out var piTransitFacts);
            workOrderFactsByPi.TryGetValue(pi, out var workOrderFacts);
            operationProgressByPi.TryGetValue(pi, out var operationProgress);
            routingOpsByPi.TryGetValue(pi, out var routingOperations);
            routingDepsByPi.TryGetValue(pi, out var routingDependencies);

            inputs.Add(new ProductionInstructionPositionInput
            {
                ProductionInstructionNo = pi,
                MaterialId = fact.MaterialId,
                FactoryId = fact.FactoryId,
                MaterialCode = fact.MaterialCode,
                FactoryCode = fact.FactoryCode,
                // T2-04.1：PI 总量只取权威事实 max(Quantity − ReceivedQty, 0)。
                // 旧写法 `group.Max(r => r.RemainingQty)`（Stage MAX，实测可达 700 而 PI 只有 600）已删除。
                ErpRemainingQty = fact.PiRemainingQty,
                StageProgress = stageProgress,
                StagePath = stagePathByMaterial.TryGetValue(fact.MaterialCode, out var spList)
                    ? spList
                    : (IReadOnlyList<StagePathFact>)Array.Empty<StagePathFact>(),
                WorkOrders = workOrderFacts ?? (IReadOnlyList<WorkOrderSnapshotFact>)Array.Empty<WorkOrderSnapshotFact>(),
                OperationProgress = operationProgress ?? (IReadOnlyList<OperationProgressFact>)Array.Empty<OperationProgressFact>(),
                PiInventories = piInventories ?? (IReadOnlyList<PiInventoryFact>)Array.Empty<PiInventoryFact>(),
                XcFacts = xcFacts ?? (IReadOnlyList<XcFact>)Array.Empty<XcFact>(),
                CrossFactoryEdges = crossFactoryEdges ?? (IReadOnlyList<CrossFactoryEdgeFact>)Array.Empty<CrossFactoryEdgeFact>(),
                TransitFacts = piTransitFacts ?? (IReadOnlyList<InterplantTransitFact>)Array.Empty<InterplantTransitFact>(),
                RoutingOperations = routingOperations ?? (IReadOnlyList<RoutingOperationFact>)Array.Empty<RoutingOperationFact>(),
                RoutingDependencies = routingDependencies ?? (IReadOnlyList<RoutingDependencyFact>)Array.Empty<RoutingDependencyFact>()
            });
        }

        // 3) 调 5号位计算器（纯计算，无 I/O）
        var parameters = BuildFrozenFactParameters(frozenSnapshot);
        var results = await _piPositionCalculator.CalculateProductionInstructionPositionsAsync(inputs, parameters, ct);

        // 3.5) 续排起点出口核验（2026-09-29）：StartOperationCode 是需求「从哪道工序续排」的唯一入口。
        // 1号位 按 OperationCode 建图/匹配该值（PhaseOneConstraintBuilder.BuildReachableStages 的
        // nodesByOperationCode[demand.StartOperationCode]、PhaseTwoInitialScheduler.GetOperationsFromStage 的
        // k.OperationCode == startOperationCode）——非空但查不中时无兜底，需求直接判 S26 Unscheduled。
        // ⚠️ 该风险「随时会发」而非「潜在」：5号位 出口值取的是工序名（OperationName）、1号位 匹配空间是
        //    工序号（OperationCode），两侧取值集合交集为 0（432 名 ∩ 188 号 = ∅）⇒ 一旦有值产出，100% 落 S26。
        //    产能源侧 confirms 该形态普遍存在：OperationProgressSnapshot 无 OperationCode 列（源头只有名）；
        //    全库 (MaterialCode,StageCode) 在制工序数=1 的有 6,530 组（唯一前沿，最多的档）、PI 在制工序数=1 的 49,586 个。
        // ⛔ 反面口径（勿再复述）：曾据「PV 633 实测非空 0 条」判其「未触发/上了膛没击发」——**那是抽样假象**：
        //    PV 633 的 4,109 个 MTS_InstructionNo 在快照批次里在制工序数恰为 0，整批落进 remainingOps.Count==0
        //    的设计内合法空分支。一次交集为 0 不等于代码不会产出值，别拿单次抽样当普遍规律。
        // 此处只观测、不改写：把「出口值是否落在该PI的 RoutingOperation.OperationCode 空间内」打成计数，
        // 跑完可直接从日志核，避免该类静默失败再靠结果变差回溯。
        var opCodeSpaceByPi = inputs.ToDictionary(
            i => i.ProductionInstructionNo,
            i => new HashSet<string>(i.RoutingOperations.Select(o => o.OperationCode), StringComparer.Ordinal),
            StringComparer.Ordinal);
        var startOpNonEmpty = 0;
        var startOpUnmatchable = 0;
        var startOpPis = new HashSet<string>(StringComparer.Ordinal);
        var startOpSamples = new List<string>();
        var startOps = results
            .SelectMany(r => (r.NextOperationContexts ?? (IReadOnlyList<NextOperationContextDto>)Array.Empty<NextOperationContextDto>())
                .Select(c => (r.ProductionInstructionNo, Value: c.StartOperationCode))
                .Concat((r.ExistingExecutionContexts ?? (IReadOnlyList<ExistingExecutionContextDto>)Array.Empty<ExistingExecutionContextDto>())
                    .Select(c => (r.ProductionInstructionNo, Value: c.StartOperationCode))));
        foreach (var (pi, value) in startOps)
        {
            if (string.IsNullOrEmpty(value)) continue;
            startOpNonEmpty++;
            // 只在「该PI有 Routing 节点」时判不中：无 Routing 的物料是另一类问题（无工序），不混计。
            if (opCodeSpaceByPi.TryGetValue(pi, out var codeSpace)
                && codeSpace.Count > 0 && !codeSpace.Contains(value!))
            {
                startOpUnmatchable++;
                startOpPis.Add(pi);
                if (startOpSamples.Count < 10) startOpSamples.Add(value!);
            }
        }
        if (startOpNonEmpty > 0)
            _logger.LogInformation(
                "[Pegging][红线] 续排起点出口核验：StartOperationCode 非空 {Tot} 条（涉及 {Pi} 个PI），其中 {Bad} 条不在该PI的 RoutingOperation.OperationCode 空间内（1号位 按 OperationCode 匹配 ⇒ 这类需求必然 S26 Unscheduled，无兜底）；样例=[{Samples}]",
                startOpNonEmpty, startOpPis.Count, startOpUnmatchable,
                string.Join(",", startOpSamples.Distinct()));
        else
            _logger.LogInformation("[Pegging][红线] 续排起点出口核验：本次 StartOperationCode 全为空。⚠️ 这**不代表风险不存在**——它只说明本次这一批 PI 恰好全落在「工序已完/无 Routing 数据」的合法空分支（详见下条断口定位）。产工序名的分支在全库形态上普遍存在（(Material,Stage) 单在制工序 6,530 组），换批数据即会命中 ⇒ 别把「本次全空」记成「已排除」");

        // 3.6) 续排起点「全空」时定位断口在哪一级：无Routing节点 / 无上下文 / 上下文E=0 / 只到Stage级。
        var nextCtx = results.Sum(r => (r.NextOperationContexts ?? Array.Empty<NextOperationContextDto>()).Count);
        var nextCtxWithStartStage = results.Sum(r => (r.NextOperationContexts ?? Array.Empty<NextOperationContextDto>()).Count(c => !string.IsNullOrEmpty(c.StartStageCode)));
        var nextCtxWithQty = results.Sum(r => (r.NextOperationContexts ?? Array.Empty<NextOperationContextDto>()).Count(c => c.SliceQty > 0m));
        var existCtx = results.Sum(r => (r.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>()).Count);
        var existCtxWithStage = results.Sum(r => (r.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>()).Count(c => !string.IsNullOrEmpty(c.StartStageCode)));
        var existCtxWithQty = results.Sum(r => (r.ExistingExecutionContexts ?? Array.Empty<ExistingExecutionContextDto>()).Count(c => c.DerivedRemainingQty > 0m));
        _logger.LogInformation(
            "[Pegging][红线] 续排起点断口定位：结果 {Res} 个PI；其中 RoutingOperations 非空 {PiWithRouting} 个 / RoutingDependencies 非空 {PiWithDeps} 个；" +
            "NextOperationContext 共 {NextCtx} 条（有StartStage {NextStage} / SliceQty>0 {NextQty}），ExistingExecutionContext 共 {ExistCtx} 条（有StartStage {ExistStage} / E>0 {ExistQty}）",
            results.Count,
            inputs.Count(i => i.RoutingOperations.Count > 0),
            inputs.Count(i => i.RoutingDependencies.Count > 0),
            nextCtx, nextCtxWithStartStage, nextCtxWithQty,
            existCtx, existCtxWithStage, existCtxWithQty);

        // 4) 保存 PI Position 快照 + 数量闭环校验（2号位职责，不修正 5号位 事实）
        //    `persistPositionSnapshot=false`（BFS-only / 双跑对照探针）：**只做闭环校验与留痕，
        //    不落库**。原因：该落库按 `PlanVersion.SourceScheduleRunId` DELETE+INSERT，探针跑一次就会
        //    覆盖夜间 run 的真实 PI Position 快照；而探针本身宣称「只读、不落库」。
        await SavePiPositionSnapshotsAsync(planVersionId, inputs, results, ct, persistPositionSnapshot);

        return results.ToDictionary(r => r.ProductionInstructionNo, StringComparer.Ordinal);
    }

    /// <summary>
    /// 每工单快照事实装载（IN_PROGRESS 工单）——5号位 ExistingExecutionContext 的工单身份源。
    /// 冻结红线（v1.6/v2.6）：只传 IN_PROGRESS；RELEASED/CLOSED/DELETED 不传；工单级不带 RemainingQty。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<WorkOrderSnapshotFact>>> LoadWorkOrderSnapshotFactsAsync(
        int scheduleRunId,
        IReadOnlyList<WipStageLoadRow> wipStageRows,
        CancellationToken ct)
    {
        if (scheduleRunId <= 0)
            return new Dictionary<string, IReadOnlyList<WorkOrderSnapshotFact>>();

        var piNos = wipStageRows.Select(r => r.ProductionInstructionNo).Distinct().ToList();
        var rows = await QueryChunkedInAsync(piNos, chunk =>
            _connectionManager.QueryAsync<WorkOrderSnapshotRow>(
                @"SELECT ProductionInstructionNo, MESWorkOrderNo, MaterialCode, PlannedQty, WorkOrderStatus, DataCutoffTime
                  FROM MESWorkOrderSnapshot
                  WHERE ScheduleRunId = @ScheduleRunId
                    AND WorkOrderStatus = 'IN_PROGRESS'
                    AND ProductionInstructionNo IN @PiNos",
                new { ScheduleRunId = scheduleRunId, PiNos = chunk },
                db: DatabaseId.APS));

        return rows
            .GroupBy(r => r.ProductionInstructionNo, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<WorkOrderSnapshotFact>)g.Select(r => new WorkOrderSnapshotFact
                {
                    ProductionInstructionNo = r.ProductionInstructionNo,
                    MESWorkOrderNo = r.MESWorkOrderNo,
                    MaterialCode = r.MaterialCode,
                    PlannedQty = r.PlannedQty,
                    WorkOrderStatus = r.WorkOrderStatus,
                    DataCutoffTime = r.DataCutoffTime
                }).ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// 每工单工序进度事实装载——5号位按 MESWorkOrderNo 找当前有效工序的输入事实源。
    /// V1 口径：工序身份 = OperationName（MES 无稳定工序码）。PM 2026-09-15 裁决已废弃线性 OperationSeq：
    /// OperationSequence 保留兼容字段（=0），不参与 StartOperation/Routing/连续份额/Solver 判断；
    /// StartOperationCode 由 5号位基于 RoutingOperation + RoutingDependency 拓扑前沿解析输出，2号位只消费、不拼序。
    /// RemainingQty = max(PlannedQty − GoodQty, 0)，与 v1.6 DerivedRemainingQty 同一公式。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, IReadOnlyList<OperationProgressFact>>> LoadOperationProgressFactsAsync(
        int scheduleRunId,
        IReadOnlyList<WipStageLoadRow> wipStageRows,
        CancellationToken ct)
    {
        if (scheduleRunId <= 0)
            return new Dictionary<string, IReadOnlyList<OperationProgressFact>>();

        var piNos = wipStageRows.Select(r => r.ProductionInstructionNo).Distinct().ToList();
        var rows = await QueryChunkedInAsync(piNos, chunk =>
            _connectionManager.QueryAsync<OperationProgressRow>(
                @"SELECT ProductionInstructionNo, MESWorkOrderNo, OperationName, StageCode,
                         PlannedQty, GoodQty, LastReportTime, SourceUpdatedAt, LastReportResourceCode,
                         RouteCode, ProductionDepartmentId
                  FROM OperationProgressSnapshot
                  WHERE ScheduleRunId = @ScheduleRunId
                    AND ProductionInstructionNo IN @PiNos",
                new { ScheduleRunId = scheduleRunId, PiNos = chunk },
                db: DatabaseId.APS));

        return rows
            .GroupBy(r => r.ProductionInstructionNo, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OperationProgressFact>)g.Select(r => new OperationProgressFact
                {
                    OperationCode = r.OperationName,   // V1：工序身份 = OperationName，无独立工序码列
                    OperationName = r.OperationName,
                    StageCode = r.StageCode,
                    // 2026-09-30：MES 工单完整计划工序清单 + 工单创建部门，供 5号位 做路线定位
                    RouteCode = r.RouteCode ?? string.Empty,
                    ProductionDepartmentId = r.ProductionDepartmentId,
                    MESWorkOrderNo = r.MESWorkOrderNo,
                    PlannedQty = r.PlannedQty,
                    GoodQty = r.GoodQty,
                    RemainingQty = Math.Max(r.PlannedQty - r.GoodQty, 0m),
                    OperationSequence = 0,             // PM 裁决（2026-09-15）：废弃线性序，保留兼容字段，不参与业务判断
                    LastReportTime = r.LastReportTime,
                    LastReportResourceCode = string.IsNullOrWhiteSpace(r.LastReportResourceCode) ? null : r.LastReportResourceCode,
                    SnapshotId = null,
                    UpdatedAt = r.SourceUpdatedAt
                }).ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Routing 节点/边事实装载（按物料+部门过滤后的 PI 子集）——5号位 DAG 拓扑前沿算法的输入事实源。
    /// 部门维度复用 MaterialStageDeptContext((MaterialId, StageCode) → DefaultProductionDepartmentId)，
    /// 与 1号位 部门锁定同源（5号位 落码执行说明 2026-09-16 §4.3）。
    /// RouteCode/PathId 忠实传真实值（RouteCode 实为工序名序列串、非 "DEFAULT"，勿硬编码）。
    /// V1 简化：按 (MaterialId, ProductionDepartmentId) 过滤；跨部门拼接口径待 2↔5 另行对齐，不阻塞。
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, IReadOnlyList<RoutingOperationFact>> OpsByPi,
                        IReadOnlyDictionary<string, IReadOnlyList<RoutingDependencyFact>> DepsByPi)>
        LoadRoutingFactsAsync(IReadOnlyList<WipStageLoadRow> wipStageRows, CancellationToken ct)
    {
        var emptyOps = new Dictionary<string, IReadOnlyList<RoutingOperationFact>>(StringComparer.Ordinal);
        var emptyDeps = new Dictionary<string, IReadOnlyList<RoutingDependencyFact>>(StringComparer.Ordinal);

        if (wipStageRows.Count == 0)
            return (emptyOps, emptyDeps);

        var materialIds = wipStageRows.Select(r => r.MaterialId).Distinct().ToList();

        // a) (MaterialId, StageCode) → 部门集合（与 1号位 部门锁定同源；缺映射时该 Stage 无部门）
        var deptCtx = await LoadMaterialStageDeptContextAsync(materialIds, ct);
        var deptByMaterialStage = deptCtx
            .GroupBy(c => (c.MaterialId, c.StageCode))
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ProductionDepartmentId).Distinct().ToHashSet());

        // b) 装载 Routing 节点/边（IsActive=1），按 PI 材料集合一次取回，再逐 PI 按部门过滤
        var operations = await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<RoutingOperation>(
                @"SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode, OperationName, StageCode
                  FROM RoutingOperation
                  WHERE IsActive = 1 AND MaterialId IN @MaterialIds",
                new { MaterialIds = chunk },
                db: DatabaseId.APS));

        var dependencies = await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<RoutingDependency>(
                @"SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId,
                         FromOperationCode, ToOperationCode, DependencyType
                  FROM RoutingDependency
                  WHERE IsActive = 1 AND MaterialId IN @MaterialIds",
                new { MaterialIds = chunk },
                db: DatabaseId.APS));

        // c) 按 PI 组装：每个 PI 的部门 = 其 Stage 集在 MSC 的去重部门；无映射时回退到物料全路由
        //
        // ⚠ 2026-10-09 性能整改（`dotnet-stack` 钉死）：原实现逐 PI 写
        //   `operations.Where(o => o.MaterialId == materialId)` —— `operations` 是**全域**路由节点列表，
        //   于是每个 PI 都对整表线性扫一遍 ⇒ O(PI 数 × 全域节点数)。PV2 实测 118,154 个 PI，热线程整段卡在
        //   本方法的 `.Where(...).ToList()` 闭包里；期间 DB 全程空闲（`sys.dm_exec_requests` 无用户会话）
        //   ⇒ 纯客户端 CPU 空转，装载被拖到十几分钟。改为**一次建 MaterialId 索引**后 O(1) 取用。
        var opsByMaterial = operations
            .GroupBy(o => o.MaterialId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var depsByMaterial = dependencies
            .GroupBy(d => d.MaterialId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var opsByPi = new Dictionary<string, IReadOnlyList<RoutingOperationFact>>(StringComparer.Ordinal);
        var depsByPi = new Dictionary<string, IReadOnlyList<RoutingDependencyFact>>(StringComparer.Ordinal);

        foreach (var group in wipStageRows.GroupBy(r => r.ProductionInstructionNo, StringComparer.Ordinal))
        {
            var pi = group.Key;
            var materialId = group.First().MaterialId;

            var deptIds = new HashSet<int>();
            foreach (var s in group)
            {
                if (deptByMaterialStage.TryGetValue((materialId, s.StageCode), out var set))
                    deptIds.UnionWith(set);
            }

            IEnumerable<RoutingOperation> ops = opsByMaterial.TryGetValue(materialId, out var matOps)
                ? matOps
                : Array.Empty<RoutingOperation>();
            IEnumerable<RoutingDependency> deps = depsByMaterial.TryGetValue(materialId, out var matDeps)
                ? matDeps
                : Array.Empty<RoutingDependency>();

            if (deptIds.Count > 0)
            {
                ops = ops.Where(o => deptIds.Contains(o.ProductionDepartmentId));
                deps = deps.Where(d => deptIds.Contains(d.ProductionDepartmentId));
            }
            else
            {
                // 无 MSC 部门映射时回退物料全路由（避免静默漏装致 NEXT_OPERATION 误报），与 LoadRoutingContextAsync 同口径。
                // T2-04.2：只在「该 PI 确实有 Stage 定位行」时告警——PI 无任何 Stage 报工（driverRows 身份行
                // StageCode 为空）是合法形态，不是映射缺失，不该刷告警。
                if (group.Any(r => !string.IsNullOrEmpty(r.StageCode)))
                    _logger.LogWarning("[Pegging] PI {Pi} 无 MaterialStageDeptContext 部门映射，Routing 事实回退到物料全路由", pi);
            }

            opsByPi[pi] = ops.Select(o => new RoutingOperationFact
            {
                OperationCode = o.OperationCode,
                OperationName = o.OperationName,
                StageCode = o.StageCode ?? string.Empty,
                RouteCode = o.RouteCode,
                PathId = o.PathId
            }).ToList();

            depsByPi[pi] = deps.Select(d => new RoutingDependencyFact
            {
                FromOperationCode = d.FromOperationCode,
                ToOperationCode = d.ToOperationCode,
                DependencyType = d.DependencyType,
                RouteCode = d.RouteCode,
                PathId = d.PathId
            }).ToList();
        }

        return (opsByPi, depsByPi);
    }

    /// <summary>
    /// 保存 PI Position 快照 + 数量闭环校验（2号位职责，2026-09-02 双专项冻结）。
    /// 冻结约束：Σ Position.Quantity = ERP PI RemainingQty；异常（数量不闭合 / 计算失败 / 位置缺失）
    /// 只登记（IssueCode 落快照 + 日志），不自行修正 5号位 计算出的位置数量。
    /// </summary>
    private async System.Threading.Tasks.Task SavePiPositionSnapshotsAsync(
        int planVersionId,
        IReadOnlyList<ProductionInstructionPositionInput> inputs,
        IReadOnlyList<ProductionInstructionPositionResult> results,
        CancellationToken ct,
        bool persist = true)
    {
        if (inputs.Count == 0)
            return;

        var scheduleRunId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            "SELECT SourceScheduleRunId FROM PlanVersion WHERE Id = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        if (scheduleRunId <= 0)
        {
            _logger.LogWarning("[Pegging] PI Position 快照跳过：PlanVersionId={PlanVersionId} 无 SourceScheduleRunId", planVersionId);
            return;
        }

        var resultsByPi = results.ToDictionary(r => r.ProductionInstructionNo, StringComparer.Ordinal);
        // T2-04.2：物料码取自 inputs（= piFacts 权威身份），不再取自 Stage 行——
        // 无 Stage 报工的 PI 在旧实现里查不到物料码，快照会缺列。
        var materialCodeByPi = inputs
            .GroupBy(i => i.ProductionInstructionNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().MaterialCode, StringComparer.Ordinal);

        var (rows, issues) = MapPositionSnapshots(scheduleRunId, planVersionId, inputs, resultsByPi, materialCodeByPi);

        foreach (var issue in issues)
            _logger.LogWarning("[Pegging] PI Position 闭环异常: {Issue}", issue);

        if (rows.Count == 0)
            return;

        if (!persist)
        {
            _logger.LogInformation(
                "[Pegging] PI Position 快照**只校验不落库**（探针/双跑路径）：{Rows} 行未写入 " +
                "(ScheduleRunId={RunId}, PlanVersionId={PlanVersionId})；闭环异常 {Issues} 条已在上方登记",
                rows.Count, scheduleRunId, planVersionId, issues.Count);
            return;
        }

        await _piPositionSnapshotRepo.SaveBatchAsync(scheduleRunId, planVersionId, rows, ct);
    }

    /// <summary>
    /// 把 5号位 计算结果映射为快照行，并做数量闭环校验（纯函数，可单测）。
    /// 校验规则（不修正事实，只登记）：
    ///   - 数量不闭合：Σ Position.Quantity != ErpRemainingQty（容差 0.0001）→ IssueCode=QUANTITY_GAP
    ///   - 计算失败：result.IsSuccess == false → IssueCode=POSITION_FAILED
    ///   - 位置缺失：inputs 有 PI 但计算器无结果 → 记 issue，不落行
    /// </summary>
    internal static (List<ProductionInstructionPositionSnapshot> Rows, List<string> Issues) MapPositionSnapshots(
        int scheduleRunId,
        int planVersionId,
        IReadOnlyList<ProductionInstructionPositionInput> inputs,
        IReadOnlyDictionary<string, ProductionInstructionPositionResult> resultsByPi,
        IReadOnlyDictionary<string, string> materialCodeByPi)
    {
        const decimal tolerance = 0.0001m;

        var rows = new List<ProductionInstructionPositionSnapshot>();
        var issues = new List<string>();

        foreach (var input in inputs)
        {
            if (!resultsByPi.TryGetValue(input.ProductionInstructionNo, out var result))
            {
                issues.Add($"PI {input.ProductionInstructionNo}: 位置缺失（计算器无结果）");
                continue;
            }

            materialCodeByPi.TryGetValue(input.ProductionInstructionNo, out var materialCode);

            var sum = result.Positions.Sum(p => p.Quantity);
            var hasClosureGap = Math.Abs(sum - input.ErpRemainingQty) > tolerance;
            var isFailed = !result.IsSuccess;

            if (hasClosureGap)
                issues.Add($"PI {input.ProductionInstructionNo}: 数量不闭合 ΣPosition={sum} vs ERP RemainingQty={input.ErpRemainingQty}");
            else if (isFailed)
                issues.Add($"PI {input.ProductionInstructionNo}: 计算失败 {result.FailureReason}");

            var issueCode = hasClosureGap ? "QUANTITY_GAP"
                          : isFailed      ? "POSITION_FAILED"
                          : null;

            foreach (var slice in result.Positions)
            {
                rows.Add(new ProductionInstructionPositionSnapshot
                {
                    ScheduleRunId = scheduleRunId,
                    PlanVersionId = planVersionId,
                    ProductionInstructionNo = input.ProductionInstructionNo,
                    MaterialId = input.MaterialId,
                    MaterialCode = materialCode ?? string.Empty,
                    PositionType = slice.PositionType.ToString(),
                    Quantity = slice.Quantity,
                    CurrentStageCode = slice.StageCode,
                    NextStageCode = null,
                    AvailableTime = slice.AvailableTime,
                    SourceType = null,
                    SourceKey = slice.SourceKey,
                    IssueCode = issueCode,
                    Confidence = null
                });
            }
        }

        return (rows, issues);
    }

    /// <summary>
    /// 装载 PI 级库存事实（PiInventory）。
    ///
    /// 结构：ext_ERP_Inventory_View（MaterialCode/FactoryCode/WarehouseCode=6位工序码/Quantity）
    ///       LEFT JOIN ext_MES_ProcessCode_View（ProcessCode→StageCode）。
    /// LocationCategory 由 2号位按映射结果判定：
    ///   - 命中 StageCode → STAGE_INVENTORY（RelatedStageCode=StageCode）
    ///   - 未命中 → UNKNOWN（RelatedStageCode=null，由计算器 UNLOCATED 兜底）
    /// 返回 (MaterialCode, FactoryCode) → 事实列表，供按 PI 归属装载。
    /// </summary>
    private async Task<IReadOnlyDictionary<(string MaterialCode, string FactoryCode), List<PiInventoryFact>>>
        LoadPiInventoryFactsAsync(IReadOnlyList<WipStageLoadRow> wipStageRows, CancellationToken ct)
    {
        var materialCodes = wipStageRows.Select(r => r.MaterialCode).Distinct().ToList();
        var factoryCodes  = wipStageRows.Select(r => r.FactoryCode).Distinct().ToList();

        var rows = await QueryChunkedInAsync(materialCodes, chunk =>
            _connectionManager.QueryAsync<PiInventoryLoadRow>(
                @"SELECT d.MaterialCode,
                         fa.Code        AS FactoryCode,
                         d.StorageCode   AS WarehouseCode,
                         d.Quantity,
                         pc.StageCode
                  FROM InventoryAvailableSupplyDetail d
                  INNER JOIN Factory fa
                    ON fa.Id = d.FactoryId
                   AND fa.IsActive = 1
                  LEFT JOIN ext_MES_ProcessCode_View pc
                    ON pc.ProcessCode = d.StorageCode
                  WHERE d.Quantity > 0
                    AND d.MaterialCode IN @MaterialCodes
                    AND fa.Code         IN @FactoryCodes",
                new { MaterialCodes = chunk, FactoryCodes = factoryCodes },
                db: DatabaseId.APS, commandTimeout: 300));

        var map = new Dictionary<(string MaterialCode, string FactoryCode), List<PiInventoryFact>>();
        foreach (var row in rows)
        {
            var key = (row.MaterialCode, row.FactoryCode);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<PiInventoryFact>();
                map[key] = list;
            }

            list.Add(new PiInventoryFact
            {
                WarehouseCode = row.WarehouseCode,
                Quantity      = row.Quantity,
                AvailableTime = null,
                SourceDocument = null,
                RelatedStageCode = row.StageCode,
                LocationCategory = string.IsNullOrWhiteSpace(row.StageCode) ? "UNKNOWN" : "STAGE_INVENTORY"
            });
        }

        return map;
    }

    /// <summary>
    /// 装载 XC（线边仓）事实。
    /// 链：ext_MES_ProcessCode_View.ERPProperty='XC'（标记线边仓工序码）× ext_ERP_Inventory_View 库存。
    /// 返回 (MaterialCode, FactoryCode) → 事实列表，供按 PI 归属装载。
    /// </summary>
    private async Task<IReadOnlyDictionary<(string MaterialCode, string FactoryCode), List<XcFact>>>
        LoadXcFactsAsync(IReadOnlyList<WipStageLoadRow> wipStageRows, CancellationToken ct)
    {
        var materialCodes = wipStageRows.Select(r => r.MaterialCode).Distinct().ToList();
        var factoryCodes  = wipStageRows.Select(r => r.FactoryCode).Distinct().ToList();

        var rows = await QueryChunkedInAsync(materialCodes, chunk =>
            _connectionManager.QueryAsync<XcLoadRow>(
                @"SELECT d.MaterialCode,
                         fa.Code        AS FactoryCode,
                         d.StorageCode   AS WarehouseCode,
                         d.Quantity,
                         pc.StageCode
                  FROM InventoryAvailableSupplyDetail d
                  INNER JOIN Factory fa
                    ON fa.Id = d.FactoryId
                   AND fa.IsActive = 1
                  INNER JOIN ext_MES_ProcessCode_View pc
                    ON pc.ProcessCode = d.StorageCode
                   AND pc.ERPProperty = 'XC'
                  WHERE d.Quantity > 0
                    AND d.MaterialCode IN @MaterialCodes
                    AND fa.Code         IN @FactoryCodes",
                new { MaterialCodes = chunk, FactoryCodes = factoryCodes },
                db: DatabaseId.APS, commandTimeout: 300));

        var map = new Dictionary<(string MaterialCode, string FactoryCode), List<XcFact>>();
        foreach (var row in rows)
        {
            var key = (row.MaterialCode, row.FactoryCode);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<XcFact>();
                map[key] = list;
            }

            list.Add(new XcFact
            {
                XcWarehouseCode = row.WarehouseCode,
                Quantity        = row.Quantity,
                RelatedStageCode = row.StageCode,
                AvailableTime   = null,
                SourceDocument  = null
            });
        }

        return map;
    }

    /// <summary>
    /// 装载跨厂边事实（CrossFactoryEdge）。
    /// 链：ext_MES_APS_BOM_Workset_CrossFactoryEdge（18374 行）。
    /// 归组键 = ChildMaterialCode（PI 物料作为子件、其产品跨厂转运的边）；
    /// 组内按 (From,To) 四元组去重后自编号 EdgeSequence（物理表无此列）。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, List<CrossFactoryEdgeFact>>>
        LoadCrossFactoryEdgesAsync(IReadOnlyList<WipStageLoadRow> wipStageRows, CancellationToken ct)
    {
        var materialCodes = wipStageRows.Select(r => r.MaterialCode).Distinct().ToList();

        var rows = await QueryChunkedInAsync(materialCodes, chunk =>
            _connectionManager.QueryAsync<CrossFactoryEdgeLoadRow>(
                @"SELECT ChildMaterialCode,
                         FromStageCode,
                         FromFactoryCode,
                         ToStageCode,
                         ToFactoryCode
                  FROM ext_MES_APS_BOM_Workset_CrossFactoryEdge
                  WHERE ChildMaterialCode IN @MaterialCodes
                  ORDER BY ChildMaterialCode, BatchNo, WorksetId, Id",
                new { MaterialCodes = chunk },
                db: DatabaseId.APS));

        var map = new Dictionary<string, List<CrossFactoryEdgeFact>>();
        foreach (var group in rows.GroupBy(r => r.ChildMaterialCode))
        {
            var edges = new List<CrossFactoryEdgeFact>();
            var seen = new HashSet<(string FromStage, string FromFactory, string ToStage, string ToFactory)>();
            foreach (var row in group)
            {
                var tuple = (row.FromStageCode, row.FromFactoryCode, row.ToStageCode, row.ToFactoryCode);
                if (!seen.Add(tuple))
                    continue; // 多 BatchNo/WorksetId 的重复边去重

                edges.Add(new CrossFactoryEdgeFact
                {
                    FromStageCode   = row.FromStageCode,
                    FromFactoryCode = row.FromFactoryCode,
                    ToStageCode     = row.ToStageCode,
                    ToFactoryCode   = row.ToFactoryCode,
                    EdgeSequence    = edges.Count
                });
            }
            map[group.Key] = edges;
        }

        return map;
    }

    /// <summary>
    /// 将厂间在途事实按生产指示号精确归属到 PI（PM 0910 裁决 §十三，§11 Stage Handoff INTERPLANT_TRANSIT Position 强事实）。
    /// 归属键 = TransitDocumentNo（= SourceDocumentNo = PI No）；Material/目标厂仅做一致性校验，不得替代 PI 号归属（禁 Material+Factory 归组/按 PI 排序/按 RemainingQty 比例分摊）。
    /// 校验顺序（PM §十）：PI 号非空 → 命中唯一有效 PI → Material 一致 → 目标厂一致 → Quantity>0 → 归入该 PI 的 INTERPLANT_TRANSIT。
    /// 任一失败则该事实不作为已定位 Position 消费，记 Issue（TRANSIT_PI_*），对应剩余数量由 PI Position 闭合逻辑落入 UNLOCATED（不得删除 Supply / 转新增生产）。
    /// 同一 PI 多行聚合（PM §十一：TransitQty ≤ PI RemainingQty 由 5号位计算器闭合校验，不在此裁剪）。
    /// </summary>
    internal static (Dictionary<string, List<InterplantTransitFact>> ByPi, List<string> Issues)
        AttributeInterplantTransitToPi(
            IReadOnlyList<InterplantTransitFact> transits,
            IReadOnlyDictionary<string, (string MaterialCode, string FactoryCode)> piIdentities)
    {
        var byPi = new Dictionary<string, List<InterplantTransitFact>>(StringComparer.Ordinal);
        var issues = new List<string>();

        foreach (var t in transits)
        {
            var piNo = t.TransitDocumentNo;
            if (string.IsNullOrWhiteSpace(piNo))
            {
                issues.Add("TRANSIT_PI_NO_MISSING: 厂间在途事实缺少生产指示号（TransitDocumentNo 为空），无法归属 PI。");
                continue;
            }

            if (!piIdentities.TryGetValue(piNo, out var pi))
            {
                issues.Add($"TRANSIT_PI_NOT_FOUND: 在途单号 {piNo} 找不到对应 PI。");
                continue;
            }

            if (!string.Equals(t.MaterialCode, pi.MaterialCode, StringComparison.Ordinal))
            {
                issues.Add($"TRANSIT_PI_MATERIAL_MISMATCH: 在途单号 {piNo} 物料 {t.MaterialCode} ≠ PI 物料 {pi.MaterialCode}。");
                continue;
            }

            if (!string.Equals(t.TargetFactoryCode, pi.FactoryCode, StringComparison.Ordinal))
            {
                issues.Add($"TRANSIT_PI_FACTORY_MISMATCH: 在途单号 {piNo} 目标厂 {t.TargetFactoryCode} ≠ PI 工厂 {pi.FactoryCode}。");
                continue;
            }

            if (t.Quantity <= 0m)
            {
                issues.Add($"TRANSIT_PI_INVALID_QUANTITY: 在途单号 {piNo} 数量 {t.Quantity} ≤ 0。");
                continue;
            }

            if (!byPi.TryGetValue(piNo, out var list))
            {
                list = new List<InterplantTransitFact>();
                byPi[piNo] = list;
            }
            list.Add(t);
        }

        return (byPi, issues);
    }

    /// <summary>
    /// 装载厂间在途事实（Transit），仅 PI 级（§11 大工艺接续 Stage Handoff）。链：ext_ERP_InterplantInTransit_View（实测 0 行，待 5号位 ODS 数据）。
    /// 分流依据：SourceDocumentNo = [Order].OrderNo（PM 已确认 OrderNo 是唯一连接键）→ 仅取 OrderType=PRODUCTION_INSTRUCTION；
    /// OrderType=SALES_ORDER（含 CustomerSegment='跨厂' 的 SH）的在途 = 履行状态，走 INTER_FACTORY_ORDER 单一 Supply（量已并入 Order.Quantity），不进 PI Position。
    /// 返回扁平事实列表（不按键归组），由 AttributeInterplantTransitToPi 按 TransitDocumentNo（= PI No）精确归属（PM 0910 裁决，禁 Material+Factory 归组）。
    /// </summary>
    private async Task<IReadOnlyList<InterplantTransitFact>>
        LoadTransitFactsAsync(IReadOnlyList<WipStageLoadRow> wipStageRows, CancellationToken ct)
    {
        var materialCodes = wipStageRows.Select(r => r.MaterialCode).Distinct().ToList();
        var factoryCodes  = wipStageRows.Select(r => r.FactoryCode).Distinct().ToList();

        var rows = await QueryChunkedInAsync(materialCodes, chunk =>
            _connectionManager.QueryAsync<TransitLoadRow>(
                @"SELECT t.MaterialCode,
                         t.FactoryCode,
                         t.SourceFactoryCode,
                         t.Quantity,
                         t.ETA,
                         t.ReleaseDate,
                         t.SourceDocumentNo,
                         o.OrderType
                  FROM ext_ERP_InterplantInTransit_View t
                  INNER JOIN [Order] o ON o.OrderNo = t.SourceDocumentNo
                  WHERE t.Quantity > 0
                    AND o.OrderType = 'PRODUCTION_INSTRUCTION'
                    AND t.MaterialCode IN @MaterialCodes
                    AND t.FactoryCode  IN @FactoryCodes",
                new { MaterialCodes = chunk, FactoryCodes = factoryCodes },
                db: DatabaseId.APS, commandTimeout: 300));

        var facts = new List<InterplantTransitFact>(rows.Count);
        foreach (var row in rows)
        {
            facts.Add(new InterplantTransitFact
            {
                TransitDocumentNo    = row.SourceDocumentNo,
                MaterialCode         = row.MaterialCode,
                SourceFactoryCode    = row.SourceFactoryCode,
                TargetFactoryCode    = row.FactoryCode,
                Quantity             = row.Quantity,
                EstimatedArrivalTime = row.Eta,
                SourceDocument       = row.SourceDocumentNo,
                ShippedAt            = row.ReleaseDate
            });
        }

        return facts;
    }

    /// <summary>厂间出荷指示（SH）单一供给事实：直接来自 [Order]（CustomerSegment='跨厂' 的 SALES_ORDER）。</summary>
    private sealed class InterFactoryShipmentFact
    {
        /// <summary>SH号 = OrderNo</summary>
        public string ShipmentNo { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public int MaterialId { get; set; }
        /// <summary>目标/到货厂代码（= Order.FactoryCode）</summary>
        public string TargetFactoryCode { get; set; } = string.Empty;
        /// <summary>目标/到货厂 Id（= Order.FactoryId）</summary>
        public int TargetFactoryId { get; set; }
        /// <summary>源/发货厂 Id（= Order.SourceFactoryId；装载链已就绪，重跑夜间编排器重载订单后落库）</summary>
        public int? SourceFactoryId { get; set; }
        /// <summary>指令总量（= Order.Quantity，整单一次入库）</summary>
        public decimal InstructionQty { get; set; }
    }

    /// <summary>
    /// 装载厂间出荷指示（INTER_FACTORY_ORDER，SH级）事实。
    /// v1.0 口径：SH 本身是 Order（OrderType=SALES_ORDER + CustomerSegment='跨厂'，OrderNo=SH号），
    /// 单一 Supply 身份，量=Order.Quantity（指令总量），不再从 Transit/Received 视图归并两段。
    /// </summary>
    private async Task<IReadOnlyList<InterFactoryShipmentFact>> LoadInterFactoryShipmentsAsync(CancellationToken ct)
    {
        var rows = await _connectionManager.QueryAsync<InterFactoryShipmentFact>(
            @"SELECT o.OrderNo         AS ShipmentNo,
                     m.MaterialCode,
                     o.MaterialId,
                     f.Code           AS TargetFactoryCode,
                     o.FactoryId      AS TargetFactoryId,
                     o.SourceFactoryId,
                     o.Quantity       AS InstructionQty
              FROM [Order] o
              INNER JOIN Material m ON m.Id = o.MaterialId
              INNER JOIN Factory  f ON f.Id = o.FactoryId
              WHERE o.CustomerSegment = N'跨厂'
                AND o.OrderType = 'SALES_ORDER'
                AND o.Quantity > 0",
            db: DatabaseId.APS);

        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Routing 三件套 + 部门归属上下文装载（PM 裁定：最小 B）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 装载「本次 BOM 上下文下的有效 Stage 集合」——MaterialId → 该物料本次需经过的 StageCode 集合。
    ///
    /// 【依据】PM《BOM取用_Pegging_Stage_Routing完整链路说明》§二十一/§二十二：
    ///   Stage 的存在性与顺序由 `APS_BOM_STAGE_PATH_RAW`（本次 BOM 上下文）决定，
    ///   **不得从 Routing 反推、不得按 MaterialCode 全局聚合**。本方法按「当前批次的 StagePath」取：
    ///     · ROOT 行（ParentMaterialCode=NULL）= 订单根产品自身的工艺路径；
    ///     · EDGE 行（父件→子件）= 该子件自身需经过的工艺路径（做到 IsSupplyThreshold 即可供父件）。
    ///   同一子件在不同父件下若 Stage 不同，此处取**并集**（保守：宁可多给 Stage 也不漏），
    ///   避免因漏 Stage 导致 1号位 找不到工序而误判 Unscheduled。
    ///
    /// 【无 StagePath 时的兜底】若某物料在本批 StagePath 里无记录（数据缺口 / 采购件），
    ///   则**不为其限定 Stage**（结果里不含该 MaterialId），由调用方回退「按 MaterialId 全量装载」。
    /// </summary>
    private async Task<EffectiveStageLoad> LoadEffectiveStageKeysAsync(
        int planVersionId,
        IReadOnlyList<int> materialIds,
        CancellationToken ct)
    {
        if (materialIds.Count == 0)
            return EffectiveStageLoad.Empty;

        // 1) 当前批次（与 LoadBomSnapshotAsync 同口径：经 OrderBomRequestLink 取 BatchNo）
        var batchNos = await ResolveLinkedBatchNosAsync(planVersionId);
        var batchNo = batchNos.FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrEmpty(batchNo))
        {
            _logger.LogWarning(
                "[Pegging] 无 OrderBomRequestLink（PlanVersionId={PlanVersionId}），跳过有效 Stage 过滤（回退按 MaterialId 全量装载）",
                planVersionId);
            return EffectiveStageLoad.Empty;
        }

        // 多批次诊断：本 PV 关联多个 BOM 批次时，取最新一版（与 LoadBomSnapshotAsync 同口径）。
        // 择最新是既有设计，但必须留痕——否则「取错批次」无从回溯（1号位 核验 §3 类问题的成因即静默）。
        var distinctBatchCount = batchNos.Distinct(StringComparer.Ordinal).Count();
        if (distinctBatchCount > 1)
        {
            _logger.LogWarning(
                "[Pegging] 本 PlanVersion 关联 {BatchCount} 个 BOM 批次，有效 Stage 装载取最新批次 {BatchNo}（与 LoadBomSnapshotAsync 同口径）",
                distinctBatchCount, batchNo);
        }

        // 2) 物料编码（StagePath 用 MaterialCode 字符串）
        //    分片：`materialIds` = 本域需求物料，真实规模上千（2026-09-29 真跑：需求 42,193 条 / 物料数千）；
        //    紧邻的 ③ 步对**由它派生的** `materialCodes` 已经在走 QueryChunkedInAsync —— 同源同量级的这一条却漏了。
        var materialCodes = (await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<string>(
                "SELECT MaterialCode FROM Material WHERE Id IN @Ids",
                new { Ids = chunk },
                db: DatabaseId.APS))).ToList();

        if (materialCodes.Count == 0)
            return EffectiveStageLoad.Empty;

        // 3) 取本批 StagePath 的 (ChildMaterialCode, StageCode, StageSeq)——含 ROOT 与 EDGE 两类。
        //    StageSeq 必须一起取：PM §七 认定它是顺序的唯一权威，1号位 截断 RequiredStageCode 依赖它
        //    （原实现只取两组码、返回 HashSet，**顺序在装载第一步即丢失** —— 1号位 核验 §3.2）。
        //    同一 (Child, Stage) 取 MIN(StageSeq)（本批内同一 Stage 可能出现在多父件/多 BOM 边下）。
        var stageRows = await QueryChunkedInAsync(materialCodes, chunk =>
            _connectionManager.QueryAsync<EffectiveStageRow>(
                @"SELECT ChildMaterialCode, StageCode, MIN(StageSeq) AS StageSeq
                  FROM APS_BOM_STAGE_PATH_RAW
                  WHERE BatchNo = @BatchNo
                    AND ChildMaterialCode IN @Codes
                    AND StageCode IS NOT NULL
                  GROUP BY ChildMaterialCode, StageCode",
                new { BatchNo = batchNo, Codes = chunk },
                db: DatabaseId.APS));

        // 4) MaterialCode → MaterialId 反查，组装结果
        //    分片：同上 `materialCodes` 与 `materialIds` 同量级（上千）⇒ 裸 `IN @Codes` 同样会撞 2100。
        var idByCode = (await QueryChunkedInAsync(materialCodes, chunk =>
            _connectionManager.QueryAsync<MaterialIdCodeRow>(
                "SELECT Id, MaterialCode FROM Material WHERE MaterialCode IN @Codes",
                new { Codes = chunk },
                db: DatabaseId.APS)))
            .ToDictionary(r => r.MaterialCode, r => r.Id, StringComparer.Ordinal);

        // 5) 两条产出：集合（供 Routing 三件套存在性过滤）+ 有序链（供 1号位 判断 Stage 先后）
        var result = new Dictionary<int, HashSet<string>>();
        var stepsByMaterial = new Dictionary<int, List<StageSequenceStep>>();
        foreach (var row in stageRows)
        {
            if (string.IsNullOrEmpty(row.StageCode)) continue;
            if (!idByCode.TryGetValue(row.ChildMaterialCode, out var matId)) continue;

            if (!result.TryGetValue(matId, out var set))
                result[matId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(row.StageCode);

            if (!stepsByMaterial.TryGetValue(matId, out var steps))
                stepsByMaterial[matId] = steps = new List<StageSequenceStep>();
            steps.Add(new StageSequenceStep { StageCode = row.StageCode, StageSeq = row.StageSeq });
        }

        // 链内按 StageSeq 升序（1号位 撤回上报的硬前提：① 每物料一条完整有序链 ② 每步带 StageSeq 数值）
        var chains = stepsByMaterial
            .Select(kv => new StageSequenceChain
            {
                MaterialId = kv.Key,
                Stages = kv.Value.OrderBy(s => s.StageSeq).ToList()
            })
            .ToList();

        _logger.LogInformation(
            "[Pegging] 有效 Stage 装载：需求物料={DemandMats}，命中 StagePath={HitMats}，Stage 组合={Combos}，有序链={Chains}（BatchNo={BatchNo}）",
            materialIds.Count, result.Count, result.Values.Sum(s => s.Count), chains.Count, batchNo);

        return new EffectiveStageLoad { ByMaterial = result, OrderedChains = chains };
    }

    /// <summary>
    /// 「本次 BOM 有效 Stage」装载结果（两种形态，用途不同，勿混用）：
    /// - `ByMaterial`（集合）：供 Routing 三件套**存在性**过滤（该物料本次有没有这个 Stage）；
    /// - `OrderedChains`（有序链）：供 1号位 判断 Stage **先后**（`RequiredStageCode` 截断 / `StageLeadTimes` 前后依赖）。
    /// </summary>
    private sealed class EffectiveStageLoad
    {
        public static readonly EffectiveStageLoad Empty = new();

        public IReadOnlyDictionary<int, HashSet<string>> ByMaterial { get; init; }
            = new Dictionary<int, HashSet<string>>();

        public IReadOnlyList<StageSequenceChain> OrderedChains { get; init; }
            = Array.Empty<StageSequenceChain>();
    }

    /// <summary>StagePath 有效 Stage 行（ChildMaterialCode + StageCode + StageSeq）</summary>
    private sealed class EffectiveStageRow
    {
        public string ChildMaterialCode { get; set; } = string.Empty;
        public string? StageCode { get; set; }
        public int StageSeq { get; set; }
    }

    /// <summary>OrderBomRequestLink 批次行（SQL 已按 SyncedAt DESC 排序，取首个 = 最新批次）</summary>
    private sealed class BatchLinkRow
    {
        public string? BatchNo { get; set; }
    }

    /// <summary>
    /// 「三级均未命中」时的兜底提前期（小时）= 3 天。
    /// PM 2026-09-29 裁决：「未来没有 routing 如果没有数据，则是在 StageLeadTimeParam 中查找，按照三级查找，
    ///   如果三级均未命中，则记入 STAGE_LEADTIME_MISSING，**并按 3 天兜底**」。
    /// ⚠️ 与 0号位 §4.4「不得默认 0 小时」不冲突：§4.4 禁的是**默认 0 小时**（= 不给时间），
    ///    兜底给的是**非 0 的显式值**且同时出码，两者可区分、可审计。
    /// </summary>
    private const decimal MissingStageLeadTimeFallbackHours = 72m;

    /// <summary>
    /// 「三级均未命中 ⇒ 3 天兜底」这条 Fact 的 `MatchLevel` 取值 = **`STAGE_LEADTIME_MISSING`**（PM 原码逐字）。
    ///
    /// ⚠️ 2026-09-29 与 1号位 对齐后改名：我方曾自起 `STAGE_LEADTIME_MISSING_3DAY_FALLBACK`，
    ///   而 1号位 回执《无RoutingStage时间出口字段_字段载体落码提请_v1.0》§四 已把 V1 合法值域**定死为 4 个**：
    ///   `DEPT_EXACT` / `FACTORY_STAGE_DEFAULT` / `GLOBAL_STAGE_DEFAULT` / `STAGE_LEADTIME_MISSING`，
    ///   并明文「4 个之外的任何值 ⇒ **契约违例**，不用该值」⇒ 我方的自起名会被消费侧当违例。
    ///   ⇒ 改回 PM 原码。语义仍清楚：**本条 Fact 带值**（72h），MISSING 指的是「参数未命中」。
    /// </summary>
    private const string MissingStageLeadTimeFallbackLevel = "STAGE_LEADTIME_MISSING";

    /// <summary>
    /// 装载「无 Routing 阶段的提前期」——PM《无Routing Stage统一处理建议》§九：2号位 装载 → 1号位 消费。
    ///
    /// 口径：对每个需求物料的每个「本次有效 Stage」，若该 Stage **无任何 RoutingOperation**，
    ///   则查 `StageLeadTimeParam` 解析提前期。命中梯次 **三级**（2026-09-29 用户明确口径，逐字）：
    ///     ① `DEPT_EXACT`             = ProductionDeptCode + FactoryCode + StageCode
    ///     ② `FACTORY_STAGE_DEFAULT`  = FactoryCode + StageCode
    ///     ③ `GLOBAL_STAGE_DEFAULT`   = **StageCode**（**不带 IsDefault 条件**；见 ResolveStageLeadTime 内注）
    ///   （旧的 MaterialCode 级 / ProductFamilyCode 级 §4.1 定为「在V1不作为正常有效匹配级别」，**已移除**，
    ///     遇旧数据由 <see cref="HasLegacyLevelRow"/> 登记兼容告警。）
    ///   命中即停（同层多条取 Priority 最小）；
    ///   **走完三级仍未命中** ⇒ 记 `STAGE_LEADTIME_MISSING` 并**按 3 天兜底产出 Fact**（PM 2026-09-29 裁决，见
    ///   <see cref="MissingStageLeadTimeFallbackHours"/>）——**不再省略**。
    ///   **兜底只看「算不算命中」，不按成因分治**（甲/乙/丙 都走同一支）——**只有一种情形算未命中：三级全不中**。
    ///   ⚠️ **2026-09-29 用户更正**：本节原有第二句「命中到参数行但折算值为 0（或负）⇒ 视同未命中」
    ///   **已作废**。**命中就是命中**：折算值为 0（或负）**仍按命中产出**，`LeadTimeHours = 0`、
    ///   `MatchLevel` = 实际命中层级（实测 4 行 `*_FINAL` 的 `LeadTimeDays = 0.00`
    ///   ⇒ 产出 **0 小时 + `FACTORY_STAGE_DEFAULT`**，**不带** `STAGE_LEADTIME_MISSING`）。
    ///   0 是参数行写下的值，装载层**不得替参数侧改判**；仅单记 `zeroHitCount`（信息级日志）供参数 Owner 查看。
    ///
    /// **性能（2026-09-29 修复 N+1）**：循环体内原对每个 `(物料, 阶段)` 调一次
    ///   `LoadMaterialStageDeptContextAsync(new[] { materialId })` 取部门码 —— 单元素查询 × gapPairs 条数，
    ///   而 `MaterialStageDeptContext` 实测 813,424 行（`IsCurrent=1`）且无缓存 ⇒ 一次 Pegging 发万级串行往返
    ///   （实测 PV=539 BOM 范围内 gapPairs = **16,383**；每个 Domain 各来一遍）。
    ///   现改为**复用调用方已整批装好的 `materialStageDeptContexts`** 建查表（其物料集 = 需求物料 ⊇ gapPairs 物料），
    ///   循环内零查询；本方法自身仍保留 5 次批量预取（参数表 / 物料 / 产品族 / 部门 / 工厂），无逐行查询。
    /// </summary>
    private async System.Threading.Tasks.Task<IReadOnlyList<StageLeadTimeFact>> LoadStageLeadTimesAsync(
        IReadOnlyDictionary<int, HashSet<string>> effectiveStages,
        List<RoutingOperation> operations,
        IReadOnlyList<LogicalProductionDemand> demands,
        IReadOnlyList<MaterialStageDepartmentContextDto> materialStageDeptContexts)
    {
        if (effectiveStages.Count == 0)
            return Array.Empty<StageLeadTimeFact>();

        // 1) 找出「有效 Stage 但零工序」的 (MaterialId, StageCode)
        var opsByMatStage = operations
            .Where(o => !string.IsNullOrEmpty(o.StageCode))
            .Select(o => (o.MaterialId, o.StageCode!))
            .ToHashSet();

        var gapPairs = effectiveStages
            .SelectMany(kv => kv.Value.Select(stage => (MaterialId: kv.Key, StageCode: stage)))
            .Where(p => !opsByMatStage.Contains((p.MaterialId, p.StageCode)))
            .ToList();

        if (gapPairs.Count == 0)
            return Array.Empty<StageLeadTimeFact>();

        // 2) 一次取全参数表（人工维护、体量小；避免 IN 大列表）
        var paramRows = (await _connectionManager.QueryAsync<StageLeadTimeParamRow>(
            @"SELECT FactoryCode, StageCode, ProductionDeptCode, MaterialCode, ProductFamilyCode,
                     LeadTimeDays, LeadTimeHours, Priority, IsDefault,
                     CAST(CASE WHEN EffectiveFrom IS NULL THEN 1 ELSE 0 END AS bit) AS EffectiveFromNull
              FROM StageLeadTimeParam
              WHERE IsActive = 1
                AND (EffectiveTo   IS NULL OR EffectiveTo   >= CAST(GETDATE() AS DATE))
                -- ⚠️ 2026-09-29 修复：EffectiveFrom 必须与 EffectiveTo **对称**处理（NULL = 不限）。
                --    实测 3号位 补进来的 282 行 **EffectiveFrom 全为 NULL**，而 NULL 比较返回 UNKNOWN
                --    ⇒ 旧谓词 `EffectiveFrom <= GETDATE()` 把这 282 行**全部滤掉**，装载层看到 0 行、
                --    静默走「整表空」分支 ⇒ 参数表「看起来补完了」，实际一条也没生效。
                AND (EffectiveFrom IS NULL OR EffectiveFrom <= CAST(GETDATE() AS DATE))",
            db: DatabaseId.APS)).ToList();

        if (paramRows.Count == 0)
        {
            // ⚠️ 2026-09-29 PM 裁决（口径变更）：三级均未命中 ⇒ 记 `STAGE_LEADTIME_MISSING` **并按 3 天兜底**。
            //    整表为空 = 全部 gapPairs 三级均未命中 ⇒ **不再早退**。旧实现 `return 空` 让 1号位 拿到「无提前期」，
            //    净效果等价于 0 小时，正是 0号位 §4.4 明令禁止的「默认 0 小时」（省略 ≠ 合规）。
            //    此处只把「整表空」这个比逐条未命中更强的信号单独吼一声，随后仍走下面的兜底产出。
            _logger.LogWarning(
                "[Pegging] StageLeadTimeParam 无任何有效参数行，但存在 {Count} 个「有效 Stage 但无 RoutingOperation」的 (物料,阶段)"
                + " —— 全部记 STAGE_LEADTIME_MISSING 并按 {Hours} 小时兜底（PM 2026-09-29 裁决；Owner=3号位、粒度=工厂+StageCode+生产部门，待其铺数）",
                gapPairs.Count, MissingStageLeadTimeFallbackHours);
        }
        else
        {
            // 生效日期缺口不静默：3号位 补的参数若没填 `EffectiveFrom`，装载层按「不限」采用，
            // 但必须让它可见 —— 否则「参数到底生效了没有」谁也说不清。
            var nullFrom = paramRows.Count(r => r.EffectiveFromNull);
            if (nullFrom > 0)
            {
                _logger.LogWarning(
                    "[Pegging] StageLeadTimeParam 有 {NullFrom}/{Total} 行未填 `EffectiveFrom`（NULL）—— 装载层按「不限」采用"
                    + "（与 `EffectiveTo IS NULL` 对称）。若 3号位 本意是「尚未生效」，请补日期，否则这批参数已直接生效。",
                    nullFrom, paramRows.Count);
            }
        }

        // 3) 取物料编码 + 产品族 + 工厂（供命中降级）
        //    分片：`gapPairs` 是**跨全单聚合**的 (物料,阶段) 集合，真实规模下物料数可超 2100
        //    （同 FillRequiredStageCodesAsync 的实测踩点，2026-09-29）。走统一帮手，别再写裸 `IN @`。
        var matIds = gapPairs.Select(p => p.MaterialId).Distinct().ToList();
        var matRows = (await QueryChunkedInAsync(matIds, chunk =>
            _connectionManager.QueryAsync<StageLeadTimeMatRow>(
                "SELECT Id, MaterialCode, ProductFamilyId FROM Material WHERE Id IN @Ids",
                new { Ids = chunk },
                db: DatabaseId.APS))).ToList();
        var matById = matRows.ToDictionary(r => r.Id, r => r);

        // 产品族编码（键 = int Id，勿传 StringComparer）
        var familyCodes = (await _connectionManager.QueryAsync<FamilyCodeRow>(
            "SELECT Id, Code FROM ProductFamily",
            db: DatabaseId.APS)).ToDictionary(r => r.Id, r => r.Code);

        // 部门编码（StageLeadTimeParam.ProductionDeptCode 用 DeptCode 而非 Id）
        var deptCodes = (await _connectionManager.QueryAsync<DeptCodeRow>(
            "SELECT Id, SourceDeptCode FROM ProductionDepartment WHERE IsActive = 1 AND SourceDeptCode IS NOT NULL",
            db: DatabaseId.APS)).ToDictionary(r => r.Id, r => r.SourceDeptCode ?? string.Empty);

        // 工厂编码
        var factoryCodes = (await _connectionManager.QueryAsync<FactoryCodeRow>(
            "SELECT Id, Code FROM Factory WHERE IsActive = 1",
            db: DatabaseId.APS)).ToDictionary(r => r.Id, r => r.Code);

        // 物料 → 本次需求的工厂（同一物料可能跨厂，取首个；命中降级用）
        var factoryByMaterial = demands
            .GroupBy(d => d.MaterialId)
            .ToDictionary(g => g.Key, g => g.First().FactoryId);

        // (物料, 阶段) → 首个非空部门码（MSC 给，与 1号位 部门锁定同源）—— **循环外一次建好**。
        //   数量守恒：`materialStageDeptContexts` 是调用方按 demandMaterialIds 整批装好的，
        //   而 effectiveStages 的键 ⊆ demandMaterialIds（见 LoadEffectiveStageKeysAsync 的 Id 反查）
        //   ⇒ 本表覆盖 gapPairs 的全部物料，循环内无需再查库。
        //   `TryAdd` 保留**首个**非空值 = 原 `FirstOrDefault(dc => !string.IsNullOrEmpty(dc))` 语义
        //   （同一物料的行在批量查询与单物料查询中均按同一自然序返回，逐物料顺序不变）。
        var deptCodeByMaterialStage = new Dictionary<(int MaterialId, string StageCode), string>();
        foreach (var c in materialStageDeptContexts)
        {
            if (string.IsNullOrEmpty(c.StageCode)) continue;
            if (!deptCodes.TryGetValue(c.ProductionDepartmentId, out var dc) || string.IsNullOrEmpty(dc)) continue;

            deptCodeByMaterialStage.TryAdd((c.MaterialId, c.StageCode), dc);
        }

        var result = new List<StageLeadTimeFact>(gapPairs.Count);
        var missingCount = 0;
        // 与 `missingCount` 分开计：「参数行没配」与「配了值 = 0」对参数 Owner 是两件事。
        // ⚠️ 口径提醒：`zeroHitCount` **不是**未命中 —— 它属于**命中**（0 是参数行写下的值），见下方内注。
        var zeroHitCount = 0;
        var legacyLevelCount = 0;
        var matchLevelTally = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (materialId, stageCode) in gapPairs)
        {
            matById.TryGetValue(materialId, out var mat);
            var materialCode = mat?.MaterialCode;
            var familyCode = mat?.ProductFamilyId is int fid && familyCodes.TryGetValue(fid, out var fc) ? fc : null;

            // 该物料本次涉及的部门（MSC 给）→ 取首个用于 DEPT_EXACT 级命中（查表，零查询）
            var deptCode = deptCodeByMaterialStage.TryGetValue((materialId, stageCode), out var dept)
                ? dept
                : null;

            var factoryCode = factoryByMaterial.TryGetValue(materialId, out var fid2)
                              && factoryCodes.TryGetValue(fid2, out var fcode) ? fcode : null;

            // §4.3 兼容告警：存在已废止的 Material/Family 级旧数据即计数（与本次是否命中无关）。
            if (HasLegacyLevelRow(paramRows, materialCode, familyCode, factoryCode, stageCode))
                legacyLevelCount++;

            var hit = ResolveStageLeadTime(paramRows, deptCode, factoryCode, stageCode);
            if (hit is null)
            {
                // ⚠️ 2026-09-29 PM 裁决（口径变更，推翻本节旧写法）：
                //    「按照三级查找，如果三级均未命中，则记入 STAGE_LEADTIME_MISSING，**并按 3 天兜底**」。
                // ⇒ **既出码、也产出带值的 Fact**：
                //    ① 出码：`missingCount` + 告警，让「参数未配」在日志上可见（旧旧实现是静默 continue，缺口不可见）；
                //    ② 产出：`LeadTimeHours = 72`（3 天）——**不得省略**。省略会让 1号位 拿到「无提前期」，
                //       净效果等价于 0 小时，正是 0号位 §4.4 禁止的效果；§4.4 禁的是「默认 0 小时」，不是「兜底」。
                // `MatchLevel` 取 `STAGE_LEADTIME_MISSING`（PM 原码逐字 = 1号位 回执 §四 定死的 4 值之一，
                //   见 <see cref="MissingStageLeadTimeFallbackLevel"/> 的改名说明）。
                missingCount++;
                matchLevelTally[MissingStageLeadTimeFallbackLevel] =
                    matchLevelTally.TryGetValue(MissingStageLeadTimeFallbackLevel, out var nm) ? nm + 1 : 1;

                result.Add(new StageLeadTimeFact
                {
                    MaterialId    = materialId,
                    StageCode     = stageCode,
                    LeadTimeHours = MissingStageLeadTimeFallbackHours,
                    MatchLevel    = MissingStageLeadTimeFallbackLevel
                });
                continue;
            }

            // 换算口径（DTO 注释逐字）：LeadTimeHours > 0 取之，否则按 LeadTimeDays × 24 折算。
            // 两者均可空（实测 LeadTimeHours 全 NULL、值在 LeadTimeDays）⇒ 先 ?? 归零再判。
            var hitHours = hit.LeadTimeHours ?? 0m;
            var leadTimeHours = hitHours > 0m
                ? hitHours
                : (hit.LeadTimeDays ?? 0m) * 24m;

            // ⚠️ 2026-09-29 用户更正（口径）：**命中到参数行就是命中** —— 折算值为 0（或负）**不视同未命中**。
            //    【推翻什么】本节曾写「命中但折算值 ≤ 0 ⇒ 视同未命中、与三级全不中一视同仁走 3 天兜底」，
            //      该口径**已作废**：0 是参数行**明明白白写下的值**，装载层不得替参数侧改判成 72。
            //    【实测影响】4 行 `*_FINAL`（BJ/CN/SH/TJ 完工，`LeadTimeDays = 0.00`、`ProductionDeptCode` 空）
            //      ⇒ 现在产出 **`LeadTimeHours = 0` + `MatchLevel = FACTORY_STAGE_DEFAULT`**，不再带 MISSING 码。
            //    【只保留可见性】`zeroHitCount` 与 `missingCount` 分开计 + 一条**信息级**日志，
            //      让参数 Owner 能看见「有 N 行命中的值是 0」，但**不改写它的值、不动它的层级码**。
            if (leadTimeHours <= 0m) zeroHitCount++;

            matchLevelTally[hit.MatchLevel] = matchLevelTally.TryGetValue(hit.MatchLevel, out var n) ? n + 1 : 1;

            result.Add(new StageLeadTimeFact
            {
                MaterialId     = materialId,
                StageCode      = stageCode,
                LeadTimeHours  = leadTimeHours,
                MatchLevel     = hit.MatchLevel
            });
        }

        if (result.Count > 0)
        {
            _logger.LogInformation(
                "[Pegging] 无 Routing 阶段提前期装载：缺口 (物料,阶段)={Gap}，产出 Fact={Fact}（其中三级全不中⇒3 天兜底 {Missing} 条；命中级里折算值 = 0 ⇒ 按命中产出 0 小时 {Zero} 条），命中层级={Levels}",
                gapPairs.Count, result.Count, missingCount, zeroHitCount,
                string.Join(",", matchLevelTally.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        if (missingCount > 0)
        {
            // STAGE_LEADTIME_MISSING：出码 + **按 3 天兜底产出**（PM 2026-09-29 裁决）。
            //   注意口径：禁的是「默认 0 小时」（§4.4），不是「兜底」——省略 Fact 才是等价 0 小时的那个错。
            _logger.LogWarning(
                "[Pegging] STAGE_LEADTIME_MISSING：{Missing}/{Gap} 个「有效 Stage 但无 RoutingOperation」的 (物料,阶段) 命中链（DEPT_EXACT→FACTORY_STAGE_DEFAULT→GLOBAL_STAGE_DEFAULT）走完仍未命中，已按 {Hours} 小时（3 天）兜底产出（PM 2026-09-29 裁决；不删 Stage、不默认 0 小时，0号位 §4.4）",
                missingCount, gapPairs.Count, MissingStageLeadTimeFallbackHours);
        }

        if (zeroHitCount > 0)
        {
            // 仅作**事实可见**，不是告警：这批 (物料,阶段) 命中了参数行、值是 0，**按命中产出 0 小时**。
            //   保留这条是因为「值 = 0」与「压根没配」在报表上看起来一样，参数 Owner 需要能分清。
            //   ⚠️ 口径已定（2026-09-29 用户更正）：0 = **命中**，不换成 3 天兜底，**勿按缺陷处理**。
            _logger.LogInformation(
                "[Pegging] 无 Routing 阶段提前期：{Zero}/{Gap} 个 (物料,阶段) 命中到参数行且折算值为 0（LeadTimeHours 与 LeadTimeDays×24 均 ≤ 0），按**命中**产出 LeadTimeHours=0，层级码 = 实际命中级（**不是** STAGE_LEADTIME_MISSING）。实测为 4 行 `*_FINAL`（完工阶段零时长）",
                zeroHitCount, gapPairs.Count);
        }

        if (legacyLevelCount > 0)
        {
            // §4.3：不让 1号位 自行解释旧数据。这里只告警不采用——旧口径下这两级比 DEPT 更细、会抢占命中，
            // 若确有此类行，本次装载结果与旧口径**已经不同**，必须让 3号位 知道去下线/迁移。
            _logger.LogWarning(
                "[Pegging] StageLeadTimeParam 兼容告警：{Legacy} 个 (物料,阶段) 存在已废止的 Material/ProductFamily 级参数行（V1 不作为正常有效匹配级别，0号位 §4.1/§4.3），本次装载**已不采用**该两级。请 3号位 下线/迁移该批参数",
                legacyLevelCount);
        }

        return result;
    }

    /// <summary>
    /// 按命中顺序从细到粗降级解析提前期；命中即停，同层取 Priority 最小。返回 null = 全部未命中。
    /// 层级码：`DEPT_EXACT` / `FACTORY_STAGE_DEFAULT` / `GLOBAL_STAGE_DEFAULT`。
    ///
    /// ⚠️ 0号位 2026-09-29《Stage顺序_无RoutingStage_Operation标识与TaskMES粒度》§4.1/§4.3：
    ///    V1 主业务粒度 = **工厂 + StageCode + 生产部门**；Material 级 / ProductFamily 级
    ///    「**在V1不作为正常有效匹配级别**」、3号位 后续停止发布 ⇒ **本梯次已移除这两级**。
    ///    旧口径下 Material/Family 比 DEPT 更细、会**抢占**命中，留着等于继续按废止口径跑。
    ///    遇旧数据**不静默采用也不静默忽略**，由调用方登记兼容告警（见 <see cref="HasLegacyLevelRow"/>，§4.3）。
    /// </summary>
    private static StageLeadTimeParamRow? ResolveStageLeadTime(
        IReadOnlyList<StageLeadTimeParamRow> rows,
        string? deptCode,
        string? factoryCode,
        string stageCode)
    {
        // 各层候选筛选器：(谓词, 层级码)，按从细到粗顺序；第一、二层均含 FactoryCode（冻结口径）
        bool StageOk(StageLeadTimeParamRow r) =>
            string.Equals(r.StageCode, stageCode, StringComparison.Ordinal);
        bool SameFactory(StageLeadTimeParamRow r) => SameFactoryCode(r, factoryCode);

        // ⚠️ 2026-09-29 用户明确口径（**字面**）：第三层就是「Stage」，**不带 `IsDefault` 条件**。
        //    即：只要该 StageCode 有参数行，第三层就命中；**不再要求该行声明 IsDefault = 1**。
        //    行为影响：本改动**不改变今天的任何结果** —— 实测 282 行的 `IsDefault` 全 = 1，
        //    即「带条件」与「不带条件」命中的是同一组行。故这是一次**口径对齐**，不是行为变更。
        //    已去掉的旧条件曾使 `IsDefault` 成为第三层的门槛；现该列在**本装载链上不再被读取**。
        var levels = new (Func<StageLeadTimeParamRow, bool> Match, string Level)[]
        {
            (r => !string.IsNullOrEmpty(deptCode) && string.Equals(r.ProductionDeptCode, deptCode, StringComparison.Ordinal)
                  && SameFactory(r) && StageOk(r), "DEPT_EXACT"),
            (r => SameFactory(r) && StageOk(r),    "FACTORY_STAGE_DEFAULT"),
            (r => StageOk(r),                      "GLOBAL_STAGE_DEFAULT")
        };

        foreach (var (match, level) in levels)
        {
            var hit = rows.Where(match)
                          .OrderBy(r => r.Priority)
                          .FirstOrDefault();
            if (hit is not null)
            {
                hit.MatchLevel = level;
                return hit;
            }
        }

        return null;
    }

    /// <summary>工厂口径：参数行未限厂时视为通配（冻结口径，FACTORY_STAGE_DEFAULT 依赖此语义）。</summary>
    private static bool SameFactoryCode(StageLeadTimeParamRow r, string? factoryCode) =>
        string.IsNullOrEmpty(factoryCode) || string.Equals(r.FactoryCode, factoryCode, StringComparison.Ordinal);

    /// <summary>
    /// 是否存在**已被 V1 废止的匹配层级**（Material 级 / ProductFamily 级）的参数行。
    /// 0号位 2026-09-29 §4.1/§4.3：这两级「在V1不作为正常有效匹配级别」、3号位 后续停止发布。
    /// 判断与「本次是否命中」**无关**——旧口径下它们比 DEPT 级更细、会优先抢占命中，
    /// 所以只要存在，本次装载的命中层级与提前期就已经和旧口径不同，必须登记兼容告警
    /// （§4.3「运行中如遇旧数据，可登记兼容告警，不让1号位自行解释」），而不是静默忽略。
    /// </summary>
    private static bool HasLegacyLevelRow(
        IReadOnlyList<StageLeadTimeParamRow> rows,
        string? materialCode,
        string? familyCode,
        string? factoryCode,
        string stageCode)
    {
        if (string.IsNullOrEmpty(materialCode) && string.IsNullOrEmpty(familyCode))
            return false;

        return rows.Any(r => SameFactoryCode(r, factoryCode)
            && string.Equals(r.StageCode, stageCode, StringComparison.Ordinal)
            && ((!string.IsNullOrEmpty(materialCode)
                 && string.Equals(r.MaterialCode, materialCode, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(familyCode)
                    && string.Equals(r.ProductFamilyCode, familyCode, StringComparison.Ordinal))));
    }

    /// <summary>StageLeadTimeParam 参数行（解析用，含命中层级回填）</summary>
    private sealed class StageLeadTimeParamRow
    {
        public string FactoryCode { get; set; } = string.Empty;
        public string StageCode { get; set; } = string.Empty;
        public string? ProductionDeptCode { get; set; }
        public string? MaterialCode { get; set; }
        public string? ProductFamilyCode { get; set; }
        // ⚠️ 必须可空：实测 282 行的 `LeadTimeHours` **全为 NULL**、值只落在 `LeadTimeDays`（5.00）。
        //    非空 decimal 遇上 DBNull 会抛，且会把"没给小时"和"小时=0"混为一谈。
        public decimal? LeadTimeDays { get; set; }
        public decimal? LeadTimeHours { get; set; }
        public int Priority { get; set; }
        public bool IsDefault { get; set; }

        /// <summary>诊断用：该行的 `EffectiveFrom` 是否为 NULL（= 参数发布方没填生效起点）。</summary>
        public bool EffectiveFromNull { get; set; }

        /// <summary>命中层级（ResolveStageLeadTime 回填，非表列）</summary>
        public string MatchLevel { get; set; } = string.Empty;
    }

    /// <summary>物料编码 + 产品族（提前期命中用）</summary>
    private sealed class StageLeadTimeMatRow
    {
        public int Id { get; set; }
        public string MaterialCode { get; set; } = string.Empty;
        public int? ProductFamilyId { get; set; }
    }

    private sealed class FamilyCodeRow { public int Id { get; set; } public string Code { get; set; } = string.Empty; }
    private sealed class DeptCodeRow { public int Id { get; set; } public string? SourceDeptCode { get; set; } }
    private sealed class FactoryCodeRow { public int Id { get; set; } public string Code { get; set; } = string.Empty; }

    /// <summary>MaterialCode → Id 反查行</summary>
    private sealed class MaterialIdCodeRow
    {
        public int Id { get; set; }
        public string MaterialCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// 按当前 Domain 涉及的 MaterialId 装载 Routing 三件套。
    /// `effectiveStages`（本次 BOM 有效 Stage）非空时按 (MaterialId, StageCode) 过滤——见方法内注释；
    /// 部门锁定仍由 1号位消费 MaterialStageDepartmentContexts 执行（PM 最小 B）。
    /// 三件套缺失时 1号位将把对应新增生产需求判定为 Unscheduled。
    /// </summary>
    private async Task<(List<RoutingOperation> Operations, List<RoutingDependency> Dependencies, List<OperationResourceEligibility> Eligibility)>
        LoadRoutingContextAsync(
            IReadOnlyList<int> materialIds,
            IReadOnlyDictionary<int, HashSet<string>> effectiveStages,
            CancellationToken ct)
    {

        if (materialIds.Count == 0)
            return (new List<RoutingOperation>(), new List<RoutingDependency>(), new List<OperationResourceEligibility>());

        // TransferBatchSize 冻结口径（DDL v5.1.4）：批量/规划参数从原 Routing 拆出到 RoutingPlanningParam，
        // 不污染工艺事实层 RoutingOperation。故此处 LEFT JOIN RoutingPlanningParam 取 TransferBatchSize，
        // 键 = (MaterialId, RouteCode, PathId, OperationCode)——RoutingPlanningParam 非部门维度（无 ProductionDepartmentId），
        // 同一工序跨部门共享同一 TransferBatchSize。无参数行时 TransferBatchSize=NULL，1号位按无阈值跳过。
        // ro.SetupTime：兼容废弃（v1.2 §20.3/§21）。1号位 2026-09-20 回执确认 Solver 零读取 → 停填（不再 SELECT/装载）；
        // RoutingOperation.SetupTime 实体属性 + 物理列保留兼容废弃，新 Setup 走 SetupTransitionRule，禁止以此兜底。
        var operations = await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<RoutingOperation>(
                @"SELECT ro.MaterialId, ro.ProductionDepartmentId, ro.RouteCode, ro.PathId, ro.OperationCode,
                         ro.OperationName, ro.ProcessType, ro.StageCode, ro.OperationPlanningMode, ro.StandardDuration,
                         rpp.TransferBatchSize, ro.IsActive
                  FROM RoutingOperation ro
                  LEFT JOIN RoutingPlanningParam rpp
                    ON rpp.MaterialId    = ro.MaterialId
                   AND rpp.RouteCode     = ro.RouteCode
                   AND rpp.PathId        = ro.PathId
                   AND rpp.OperationCode = ro.OperationCode
                  WHERE ro.IsActive = 1 AND ro.MaterialId IN @MaterialIds",
                new { MaterialIds = chunk },
                db: DatabaseId.APS));

        var dependencies = await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<RoutingDependency>(
                @"SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId,
                         FromOperationCode, ToOperationCode, DependencyType, LagTime, IsActive
                  FROM RoutingDependency
                  WHERE IsActive = 1 AND MaterialId IN @MaterialIds",
                new { MaterialIds = chunk },
                db: DatabaseId.APS));

        var eligibility = await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<OperationResourceEligibility>(
                @"SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode,
                         ResourceId, Priority, CapacityFactor, IsPrimary, IsActive
                  FROM OperationResourceEligibility
                  WHERE IsActive = 1 AND MaterialId IN @MaterialIds",
                new { MaterialIds = chunk },
                db: DatabaseId.APS));

        // ── 按「本次 BOM 有效 Stage」过滤三件套（PM 文档 §二十一/§二十二，2026-09-28）──
        // 规则：
        //   ① 工序：只保留 (MaterialId, StageCode) 命中 StagePath 的；
        //      · 未命中 StagePath 的物料（数据缺口 / 采购件）**不过滤**——保持既有「按 MaterialId 全量」兜底，
        //        避免因数据缺失把工序全过滤掉导致 1号位 误判 Unscheduled。
        //   ② 依赖：两端工序都必须存活（否则该边指向被裁掉的 Stage）；
        //   ③ 资质：按存活的 (MaterialId, ProductionDepartmentId, OperationCode) 三元组过滤。
        // 注：用「三元组」而非 OperationCode 单键——同一物料的不同 Stage 可能出现相同 OperationCode（实测 117 物料）。
        if (effectiveStages.Count > 0)
        {
            var beforeOps = operations.Count;
            var beforeDeps = dependencies.Count;
            var beforeElig = eligibility.Count;

            // ① 工序过滤
            var keptOps = operations
                .Where(o => !effectiveStages.TryGetValue(o.MaterialId, out var stages)
                            || string.IsNullOrEmpty(o.StageCode)
                            || stages.Contains(o.StageCode))
                .ToList();

            // 存活工序的三元组（供 ③ 资质过滤：资质自身的自然键 = (MaterialId, Dept, OperationCode)）
            var keptKeys = keptOps
                .Select(o => (o.MaterialId, o.ProductionDepartmentId, o.OperationCode))
                .ToHashSet();

            // ② 依赖：两端工序都必须存活。键 = (MaterialId, OperationCode) —— **不带部门维度**。
            //    理由（1号位 2026-09-28 回执 §三）：本判据是「工序是否**存在**」而非「选哪个」，
            //    存在性检查不需要部门维度；而原三元组键会**静默丢掉跨部门/跨 Stage 边** ——
            //    `RoutingDependency.ProductionDepartmentId` 是单值 int，一条跨部门边两端不可能同时命中同一部门键。
            //    同名工序在别处存活带来的**假阳性**（边多留一次）由 1号位 `validOperationKeys` 再判一次丢弃，不产生错排。
            var keptOpCodes = keptOps
                .Select(o => (o.MaterialId, o.OperationCode))
                .ToHashSet();

            var keptDeps = dependencies
                .Where(d => keptOpCodes.Contains((d.MaterialId, d.FromOperationCode))
                         && keptOpCodes.Contains((d.MaterialId, d.ToOperationCode)))
                .ToList();

            // ③ 资质：按存活三元组过滤
            var keptElig = eligibility
                .Where(e => keptKeys.Contains((e.MaterialId, e.ProductionDepartmentId, e.OperationCode)))
                .ToList();

            // ── 去静默（1号位 核验 §3.3 / 2026-09-28 回执 §六 第 4 项）──
            // 被丢的依赖边必须**分类可数**：否则「数据缺口（端点工序根本不存在）」与「合法裁 Stage」
            // 在日志里无法分辨。实测（2026-09-28，APS_Production）：1,156,465 条活跃依赖边中，
            // 端点不存在者 1,592/1,602 为 M 系（M101–M104 无设备/配品工序族，见 PM「无设备工序三分类」），非随机脏数据。
            var loadedOpCodes = operations
                .Select(o => (o.MaterialId, o.OperationCode))
                .ToHashSet();
            var droppedDeps = beforeDeps - keptDeps.Count;
            var absentDeps = dependencies.Count(d =>
                !loadedOpCodes.Contains((d.MaterialId, d.FromOperationCode))
                || !loadedOpCodes.Contains((d.MaterialId, d.ToOperationCode)));

            _logger.LogInformation(
                "[Pegging] 有效 Stage 过滤：工序 {OpsBefore}→{OpsAfter}，依赖 {DepsBefore}→{DepsAfter}（丢弃 {DepsDropped}：端点工序本批完全不存在 {DepsAbsent} / 仅被 Stage 裁掉 {DepsStageCut}），资质 {EligBefore}→{EligAfter}（需求物料={Mats}，命中 StagePath={HitMats}）",
                beforeOps, keptOps.Count, beforeDeps, keptDeps.Count,
                droppedDeps, absentDeps, droppedDeps - absentDeps,
                beforeElig, keptElig.Count,
                materialIds.Count, effectiveStages.Count);

            operations = keptOps;
            dependencies = keptDeps;
            eligibility = keptElig;
        }

        // ── S3b 人工槽 eligibility 展开（PM 0923 资源模型裁决）──
        // ManualCapacitySlot 主关联 = (ProductionDepartmentId, OperationName)——业务工序名才是「小工序」的 key；
        // OperationCode 只是工艺路线内顺序号（M101/M103…），无业务意义，2026-09-24 已随 5号位 重灌切换主关联。
        // 对每个启用槽，为该部门下所有 OperationName 匹配的 RoutingOperation 工序追加一条「合成 Resource」的 eligibility，
        // 合成 ResourceId = ManualSlotResourceOffset + ManualSlotId（与 S3a 装载投影同键）。
        // 合成键只进运行时 OperationResourceEligibility 列表，不触碰正式表。
        // 注：匹配键用 OperationName，但 eligible 行的 OperationCode 字段仍落各工序自身顺序号（见下方），
        //     因为它须与 1号位 按 RoutingOperation.OperationCode 对齐 eligibility 的口径一致。
        var manualSlots = (await _connectionManager.QueryAsync<ManualCapacitySlotKeyDto>(
            @"SELECT ManualSlotId, ProductionDepartmentId, OperationName
                FROM ManualCapacitySlot
               WHERE IsActive = 1",
            db: DatabaseId.APS)).ToList();

        if (manualSlots.Count > 0)
        {
            var opsByDeptOp = operations
                .GroupBy(o => (o.ProductionDepartmentId, o.OperationName))
                .ToDictionary(g => g.Key, g => g.ToList());

            var expanded = new List<OperationResourceEligibility>(eligibility);
            foreach (var slot in manualSlots)
            {
                if (!opsByDeptOp.TryGetValue((slot.ProductionDepartmentId, slot.OperationName), out var matchedOps))
                    continue;

                // Priority：设备(via ODS)小者优先；人工槽作为有限产能兜底，取较大固定值让真实设备优先（暂定，未 PM 裁决）。
                var syntheticResourceId = SolverResourceProjection.ManualSlotResourceOffset + slot.ManualSlotId;
                foreach (var op in matchedOps)
                {
                    expanded.Add(new OperationResourceEligibility
                    {
                        MaterialId           = op.MaterialId,
                        ProductionDepartmentId = op.ProductionDepartmentId,
                        RouteCode            = op.RouteCode,
                        PathId               = op.PathId,
                        OperationCode        = op.OperationCode,
                        ResourceId           = syntheticResourceId,
                        Priority             = 100,
                        CapacityFactor       = 1.0m,
                        IsPrimary            = false,
                        IsActive             = true
                    });
                }
            }
            eligibility = expanded;
        }

        // ── 兜底诊断（2026-09-24 用户嘱「加入兜底功能即可，不需过度设计」）：仅日志、不落表、不改排程行为 ──
        // FINITE_RESOURCE 工序在「设备资质 + 人工槽展开」之后仍零合格资源 = 配置遗漏（新工序未同步建设备资质/人工槽）。
        // 这类工序会被 1号位 PhaseTwo 标 Unscheduled（结果兜底已有）；此处补一道「定位兜底」，
        // 让遗漏可一眼定位到 (物料,部门,工序名)，避免像早前配品 M101 那样静默翻 SQL。
        var eligibleKeys = eligibility
            .Select(e => (e.MaterialId, e.ProductionDepartmentId, e.RouteCode, e.PathId, e.OperationCode))
            .ToHashSet();

        var gapOps = operations
            .Where(o => o.OperationPlanningMode == "FINITE_RESOURCE")
            .Where(o => !eligibleKeys.Contains((o.MaterialId, o.ProductionDepartmentId, o.RouteCode, o.PathId, o.OperationCode)))
            .ToList();

        if (gapOps.Count > 0)
        {
            _logger.LogWarning(
                "[Pegging] 零合格资源兜底诊断：{Count} 道 FINITE_RESOURCE 工序无设备资质且无人工槽（疑似新工序遗漏配置，1号位将标 Unscheduled）。样例：{Sample}",
                gapOps.Count,
                string.Join("; ", gapOps.Take(20).Select(o =>
                    $"Mat={o.MaterialId},Dept={o.ProductionDepartmentId},Op={o.OperationCode}/{o.OperationName}")));
        }

        // ── V1 单路径归一化（2026-09-24）：冻结文档 RouteCode='DEFAULT'/PathId=1，MES 数据填入中文工艺路线名，须装载层规范化 ──
        // 此前原样透传给 1号位 → PhaseOne 按 RouteCode='DEFAULT' 过滤全丢 → 整域 FinalTasks=0。
        // 归一化后统一写 DEFAULT/PathId=1；同 MaterialId 同 OperationCode 的跨 RouteCode 冲突取首条 + Warning。
        NormalizeToSingleRoute(operations, "RouteCode归一化", _logger);
        var opSeen = new HashSet<(int, int, string, int, string)>();
        var opDeduped = operations
            .Where(o => opSeen.Add((o.MaterialId, o.ProductionDepartmentId, o.RouteCode, o.PathId, o.OperationCode)))
            .ToList();
        if (opDeduped.Count < operations.Count)
            _logger.LogWarning("[Pegging] RouteCode归一化: 工序去重 {Dup} 条（跨RouteCode同名OpCode冲突）", operations.Count - opDeduped.Count);
        operations = opDeduped;

        NormalizeToSingleRoute(dependencies, "RouteCode归一化(dep)", _logger);
        NormalizeToSingleRoute(eligibility, "RouteCode归一化(elig)", _logger);

        // ── 资质/依赖去重：归一化**自己制造的**重复（2026-09-29 真跑实测崩溃点）──
        // 🔴 现象（Run #3，PlanVersionId=632）：1号位 `PhaseOneConstraintBuilder.BuildOperationResourceEligibility`
        //    的 `g.ToDictionary(e => e.ResourceId, e => e.CapacityFactor)` 抛
        //    `An item with the same key has already been added. Key: 14235`，整域求解中断。
        // 根因：上游 MES 工艺对同一 (物料,部门,小工序) 存有**多条 RouteCode 变体**。实测
        //    ResourceId=14235 / 物料 5365690 / 部门 1031 / 工序 M102 —— 库内两行 RouteCode 分别为
        //    `ASSY` 与 `ASSY,组装`，**其余列全同**（Priority 同为 1）。`NormalizeToSingleRoute` 把两行都改写为
        //    RouteCode='DEFAULT' ⇒ 在 1号位 分组键 `EligibilityLookupKey(MaterialId, ProductionDepartmentId,
        //    RouteCode, OperationCode)`（PhaseOneConstraintBuilder.cs:1108）下**塌进同一组**，
        //    组内按 ResourceId 建字典 ⇒ 同键 14235 撞字典。**重复不是查询查出来的，是归一化造出来的。**
        // 实测规模（2026-09-29，APS_Production，按 1号位 撞键口径 = 分组键 + ResourceId，COUNT(*)>1）：
        //    **5,533 组 / 98 个 ResourceId / 2,278 个物料** ⇒ 任何真实全量跑必崩；小规模测试撞不到
        //    （与上面 2100 参数溢出同一类「只有真规模才暴露」）。
        // 为何工序没崩：紧接着 3714-3720 对 operations 做了同键去重 + Warning；**资质这一路当时漏了**。
        // 故此处按**与 1号位 完全相同的撞键口径**去重（分组键 4 元 + ResourceId）。
        // 注：撞键两行在 1号位 可见字段上**逐列等同**（RouteCode 已同归一为 DEFAULT）⇒「取首条」无歧义、
        //    结果与取哪条无关。**不含 PathId**——1号位 分组键里没有它，带上它就会漏掉真正的撞键行。
        var eligSeen = new HashSet<(int, int, string, string, int)>();
        var eligDeduped = eligibility
            .Where(e => eligSeen.Add((e.MaterialId, e.ProductionDepartmentId, e.RouteCode, e.OperationCode, e.ResourceId)))
            .ToList();
        if (eligDeduped.Count < eligibility.Count)
            _logger.LogWarning(
                "[Pegging] RouteCode归一化(elig): 资质去重 {Dup} 条（跨 RouteCode 变体塌入同键，1号位 分组键=物料+部门+路线+工序，组内按资源建字典）",
                eligibility.Count - eligDeduped.Count);
        eligibility = eligDeduped;

        // 依赖同类问题：1号位 装进 `List<DependencyEdge>`（`preds.Add`，非字典 Add）⇒ 重复边**不抛异常**，
        // 但同一 from→to 会进两次成为**重复前驱边**。按边业务自然键去重；`DependencyType`/`LagTime` 不同的
        // **真并行边予以保留**（不作合并）。
        var depSeen = new HashSet<(int, string, string, string, decimal)>();
        var depDeduped = dependencies
            .Where(d => depSeen.Add((d.MaterialId, d.FromOperationCode, d.ToOperationCode, d.DependencyType, d.LagTime)))
            .ToList();
        if (depDeduped.Count < dependencies.Count)
            _logger.LogWarning(
                "[Pegging] RouteCode归一化(dep): 依赖去重 {Dup} 条（跨 RouteCode 变体同边）",
                dependencies.Count - depDeduped.Count);
        dependencies = depDeduped;

        return (operations, dependencies, eligibility);
    }

    private static void NormalizeToSingleRoute<T>(List<T> items, string label, ILogger? logger = null) where T : class
    {
        var changed = 0;
        foreach (var item in items)
        {
            var rc = item.GetType().GetProperty("RouteCode");
            var pi = item.GetType().GetProperty("PathId");
            if (rc != null && rc.CanWrite && rc.GetValue(item) is string rv && rv != "DEFAULT")
            {
                rc.SetValue(item, "DEFAULT");
                changed++;
            }
            if (pi != null && pi.CanWrite && pi.GetValue(item) is int pv && pv != 1)
            {
                pi.SetValue(item, 1);
            }
        }
        if (changed > 0)
            logger?.LogDebug("[Pegging] {Label}: 已归一化 {Count} 条 RouteCode→DEFAULT", label, changed);
    }

    /// <summary>
    /// ③b 供给阈值 Stage 填值（PM《BOM取用_Pegging_Stage_Routing完整链路说明》§八，2026-09-28）：
    /// 为每个新增生产需求回填「要做到哪个大工艺阶段即算可供给父件」。
    ///
    /// 【2026-09-29：范围扩为**需求 ∪ 承接行**、产出**物料级映射**供两个消费者共用】
    ///   本方法一次算好 `Dictionary&lt;MaterialId, RequiredStageCode&gt;` 并落 `voucher.RequiredStageByMaterialId`：
    ///     · 消费者① = `LogicalProductionDemand.RequiredStageCode`（运行输入，Solver 用）；
    ///     · 消费者② = `PeggingSupplyAllocation.NextRequiredStageCode`（追溯列）。
    ///   之所以不能只算「需求物料」：PM §十一 界定追溯列的用途是「追溯**为什么这个 Supply 可以承接这个需求**」，
    ///   追溯对象 = **全部承接关系**；而 `LogicalProductionDemands` **只为「有生产缺口」的需求生成**，
    ///   纯供给承接的需求不在其中 ⇒ 只按需求物料算，追溯列必然大面积留空
    ///   （实测 PlanVersionId=540：1,205 行只填出 40 行，而 BOM 侧 30,652 个物料带阈值）。
    ///   承接行物料取自 `voucher.SupplyAllocations[].SupplyMaterialId`；`ValidateEligibility` 红线1
    ///   保证供需同物料，故它即需求侧物料，**无需给 `SupplyAllocationItem` 加字段**。
    ///
    /// 【主源 = BOM 边的 `ChildRequiredStageCode`（按边、本批上下文）】
    ///   该字段由 `LoadBomSnapshotAsync` 按 `BatchNo + ResolvedBOMNO` 装载进 `BomEdge`（口径正确），
    ///   但此前**零消费**（1号位 2026-09-28 核验 §3.5 判为**漏接**）。此处即「翻正主次」：直接在内存消费，
    ///   不再另发一条全局查询。
    /// 【兜底 = StagePath 的 `IsSupplyThreshold = 1`】主源为空时按**本次批次**取
    ///   `StageScopeType='EDGE' AND IsSupplyThreshold=1`。原实现**无 `BatchNo`** 且 `First()` 任取，
    ///   正是 PM §二十一.3 明令禁止的「跨订单/BOM/父件汇总」形态（1号位 核验 §3.5）。
    /// 【末位 = null】两级都无 ⇒ 留空 = 保守口径（全工艺完成才可供给，与冻结 DDL 设计决策一致）。
    ///
    /// 实测（2026-09-28，APS_Production）：不存在「同一子件多个阈值 Stage」——
    ///   按 `(BatchNo, ChildMaterialCode)` 与按 `ChildMaterialCode` 全局 `HAVING COUNT(DISTINCT StageCode) &gt; 1`
    ///   的组数**均为 0**。故当前按边取单值与按物料取单值等价；但**按边取**保住了上下文，
    ///   多父件/多 BOM 结构一旦出现也不会静默任取（歧义时打 Warning）。
    ///
    /// 语义（1号位 2026-09-28 回执 §5.2）：落到 `LogicalProductionDemand.RequiredStageCode` 的值必须是
    ///   「做到此 Stage 即为终点」；**1号位 消费口径只认该字段、不看来源**。
    /// </summary>
    private async System.Threading.Tasks.Task FillRequiredStageCodesAsync(
        PeggingResultVoucher voucher,
        BomSnapshot bomSnapshot,
        long planVersionId,
        CancellationToken ct)
    {
        // ── 目标物料集合 = **需求物料 ∪ 承接行物料** ──
        //   为何要并承接行：PM §十一 明确本值的第二处用途是「追溯**为什么这个 Supply 可以承接这个需求**」，
        //   即追溯对象是**全部承接关系**；而 `LogicalProductionDemands` 只为「有生产缺口」的需求生成，
        //   纯供给承接的需求不在其中。若只按需求物料算，追溯列必然大面积留空。
        //   承接行 = `SupplyAllocations`；`SupplyAllocationItem` 无需求物料字段，但 `ValidateEligibility`
        //   红线1（`supply.MaterialId != demand.MaterialId` ⇒ 拒绝）保证供需同物料 ⇒ 用 `SupplyMaterialId` 即可。
        var targetMaterialIds = voucher.LogicalProductionDemands.Select(d => d.MaterialId)
            .Concat(voucher.SupplyAllocations.Select(a => a.SupplyMaterialId))
            .Distinct()
            .ToList();

        if (targetMaterialIds.Count == 0)
            return;

        // MaterialId → MaterialCode（键 = int，勿传 StringComparer）
        // ⚠️ **必须分片**（走 QueryChunkedInAsync）：Dapper 把 `IN @Ids` 展开成**逐个参数**，SQL Server 单次 RPC 上限 2100。
        //   真实规模实测（2026-09-29，PlanVersionId=632 / 4,109 单）：目标物料数超 2100 ⇒ 本方法抛
        //   「传入的请求具有过多的参数。该服务器支持最多 2100 个参数」⇒ `Pegging 失败 1/1` ⇒
        //   Task/Pegging/PSA **全 0**（整条 Pegging 被一个 IN 列表打断，日志里前面所有正常步骤都是假象）。
        //   小规模测试永远碰不到 —— 只有真实全量跑才暴露。
        var codeById = (await QueryChunkedInAsync(targetMaterialIds, chunk =>
                _connectionManager.QueryAsync<MaterialIdCodeRow>(
                    "SELECT Id, MaterialCode FROM Material WHERE Id IN @Ids",
                    new { Ids = chunk },
                    db: DatabaseId.APS)))
            .ToDictionary(r => r.Id, r => r.MaterialCode);

        if (codeById.Count == 0)
            return;

        // ── 主源：BOM 边 ChildRequiredStageCode（按边、本批上下文；内存消费，零查询）──
        var thresholdByChildCode = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in bomSnapshot.ByParent.SelectMany(g => g))
        {
            if (string.IsNullOrEmpty(edge.ChildCode)) continue;
            if (string.IsNullOrEmpty(edge.ChildRequiredStageCode)) continue;

            if (thresholdByChildCode.TryGetValue(edge.ChildCode, out var existing))
            {
                if (!string.Equals(existing, edge.ChildRequiredStageCode, StringComparison.Ordinal))
                {
                    // 当前数据未出现（实测 0 组）；出现即需业务定性，不得静默任取。
                    _logger.LogWarning(
                        "[Pegging] 供给阈值歧义：子件 {ChildCode} 在本次 BOM 的不同父件边上有多个阈值 Stage（{Existing} vs {Incoming}），本次取先遇者，请业务确认口径",
                        edge.ChildCode, existing, edge.ChildRequiredStageCode);
                }
                continue;
            }
            thresholdByChildCode[edge.ChildCode] = edge.ChildRequiredStageCode;
        }

        // 按物料落「阈值映射」——一次算好，两个消费者共用（运行输入 LPD + 追溯列）
        var stageByMaterialId = new Dictionary<int, string>();
        foreach (var (matId, code) in codeById)
        {
            if (thresholdByChildCode.TryGetValue(code, out var stage))
                stageByMaterialId[matId] = stage;
        }

        var filledFromBomEdge = 0;
        foreach (var demand in voucher.LogicalProductionDemands)
        {
            if (!string.IsNullOrEmpty(demand.RequiredStageCode)) continue;
            if (!stageByMaterialId.TryGetValue(demand.MaterialId, out var stage)) continue;

            demand.RequiredStageCode = stage;
            filledFromBomEdge++;
        }

        // ── 兜底：StagePath EDGE + IsSupplyThreshold=1，**按本次批次**（PM §二十一.3：不得跨订单/BOM 汇总）──
        var stillPendingMaterialIds = codeById.Keys
            .Where(id => !stageByMaterialId.ContainsKey(id))
            .ToList();
        var filledFromStagePath = 0;
        var usedBatchNo = "(未解析)";
        if (stillPendingMaterialIds.Count > 0)
        {
            var batchNo = (await ResolveLinkedBatchNosAsync(planVersionId)).FirstOrDefault();
            if (string.IsNullOrEmpty(batchNo))
            {
                _logger.LogWarning(
                    "[Pegging] 供给阈值兜底未执行：无 OrderBomRequestLink（PlanVersionId={PlanVersionId}），{Count} 个物料留空（保守口径）",
                    planVersionId, stillPendingMaterialIds.Count);
            }
            else
            {
                usedBatchNo = batchNo;
                var codes = stillPendingMaterialIds
                    .Select(id => codeById.TryGetValue(id, out var c) ? c : null)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Select(c => c!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var thresholdRows = await QueryChunkedInAsync(codes, chunk =>
                    _connectionManager.QueryAsync<EffectiveStageRow>(
                        @"SELECT DISTINCT ChildMaterialCode, StageCode
                          FROM APS_BOM_STAGE_PATH_RAW
                          WHERE BatchNo = @BatchNo
                            AND StageScopeType = 'EDGE'
                            AND IsSupplyThreshold = 1
                            AND StageCode IS NOT NULL
                            AND ChildMaterialCode IN @Codes",
                        new { BatchNo = batchNo, Codes = chunk },
                        db: DatabaseId.APS));

                var thresholdByCode = thresholdRows
                    .Where(r => !string.IsNullOrEmpty(r.StageCode))
                    .GroupBy(r => r.ChildMaterialCode, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().StageCode!, StringComparer.Ordinal);

                foreach (var id in stillPendingMaterialIds)
                {
                    if (!codeById.TryGetValue(id, out var code)) continue;
                    if (!thresholdByCode.TryGetValue(code, out var stage)) continue;

                    stageByMaterialId[id] = stage;
                    filledFromStagePath++;
                }
            }
        }

        // 兜底结果回填「仍未取值」的运行输入（主源循环已填的不重复动）
        foreach (var demand in voucher.LogicalProductionDemands)
        {
            if (!string.IsNullOrEmpty(demand.RequiredStageCode)) continue;
            if (!stageByMaterialId.TryGetValue(demand.MaterialId, out var stage)) continue;

            demand.RequiredStageCode = stage;
        }

        // ── 落给追溯列消费（PM §十一 第二处保留）──
        // 追溯列按**承接行自身物料**直接命中，不经 `LogicalProductionDemand.DemandKey` 中转
        // （LPD 只为有生产缺口的需求生成 ⇒ 中转必然漏掉纯供给承接行）。
        voucher.RequiredStageByMaterialId = stageByMaterialId;

        var unresolved = voucher.LogicalProductionDemands.Count(d => string.IsNullOrEmpty(d.RequiredStageCode));
        _logger.LogInformation(
            "[Pegging] 供给阈值 Stage 回填：主源 BOM 边填需求 {FromEdge} 个 / 兜底 StagePath(本批 {BatchNo}) 补物料 {FromPath} 个 / 物料映射共 {MapSize} 个 / 需求仍留空 {Unresolved} 个（需求总数 {Total}，承接行总数 {AllocTotal}）",
            filledFromBomEdge, usedBatchNo, filledFromStagePath, stageByMaterialId.Count, unresolved,
            voucher.LogicalProductionDemands.Count, voucher.SupplyAllocations.Count);
    }

    /// <summary>
    /// **AllocationTaskShare 不变量校验（PM《回复0928-2》§三/§六 裁决，fail-closed）**
    ///
    /// PM 冻结的 4 条不变量：
    /// - **Inv1** 每 `(AllocationSequence, FinalDraftId)` 至多一行 —— 防止一个 Task 重复写份额；
    /// - **Inv2** 同 `AllocationSequence` 的 `Σ ShareQty` == 该 seq 全部 Demand 的 `NetOutputQty` 之和（补差闭合）；
    /// - **Inv3** 按 `DemandKey` 聚合不得因「一个 TaskNo 多 Operation Task」膨胀 —— 由 Inv1 + 「仅末端 Task 记份额」
    ///   共同在**产出侧**保证；其**落库后**的 DB 级校验见 `ValidateStoredShareInvariantsAsync`；
    /// - **Inv4** 没有生产任务不得产生份额。
    ///
    /// **为何 fail-closed**：PM §六 明确「防重复不是显示问题，而是 Candidate 正确性的前置条件」——
    /// 份额一旦膨胀，`ComputeChangeSeedKeysAsync` 的 `SUM(ShareQty) BY DemandKey` 基线立即错误，
    /// 导致候选排程定位到错误的受影响范围。宁可不落库，也不给候选留错误基线（与「禁止静默回退」红线一致）。
    /// </summary>
    private void ValidateAllocationShareInvariants(
        DomainSolveResult solveResult,
        PeggingResultVoucher voucher,
        IReadOnlyDictionary<string, long> finalDraftToTaskId)
    {
        if (solveResult.AllocationShares.Count == 0)
            return;

        var errors = new List<string>();

        // Inv1：同 (AllocationSequence, FinalDraftId) 多行
        var inv1 = solveResult.AllocationShares
            .GroupBy(s => (s.AllocationSequence, s.FinalDraftId))
            .Where(g => g.Count() > 1)
            .Take(5)
            .Select(g => $"(seq={g.Key.AllocationSequence}, draft={g.Key.FinalDraftId})×{g.Count()}")
            .ToList();
        if (inv1.Count > 0)
            errors.Add($"Inv1 同分配+同Task多行：{string.Join("; ", inv1)}");

        // Inv4：份额指向的 Draft 无对应生产任务
        var inv4 = solveResult.AllocationShares
            .Where(s => !finalDraftToTaskId.ContainsKey(s.FinalDraftId))
            .Select(s => s.FinalDraftId)
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();
        if (inv4.Count > 0)
            errors.Add($"Inv4 份额指向无生产任务的Draft：{string.Join("; ", inv4)}");

        // Inv2：按 AllocationSequence 闭合（闭合目标 = 该 seq 全部 Demand 的 NetOutputQty 之和）
        var expectedByAlloc = voucher.LogicalProductionDemands
            .GroupBy(d => d.AllocationSequence)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.NetOutputQty));
        var actualByAlloc = solveResult.AllocationShares
            .GroupBy(s => s.AllocationSequence)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.ComponentQty));

        foreach (var kv in actualByAlloc)
        {
            if (!expectedByAlloc.TryGetValue(kv.Key, out var expected))
            {
                errors.Add($"Inv2 seq={kv.Key} 有份额但无对应 Demand");
                continue;
            }
            if (Math.Abs(kv.Value - expected) > 0.001m)
                errors.Add($"Inv2 seq={kv.Key} ΣShareQty={kv.Value} ≠ ΣNetOutputQty={expected}");
        }

        if (errors.Count > 0)
        {
            foreach (var e in errors)
                _logger.LogError("[AllocationTaskShare] 不变量校验失败: {Error}", e);
            throw new InvalidOperationException(
                $"AllocationTaskShare 不变量校验失败（{errors.Count} 项）——已阻止落库，避免污染 Candidate 变化基线：{string.Join(" | ", errors)}");
        }

        _logger.LogInformation(
            "[AllocationTaskShare] 不变量校验通过：份额行={Rows}，分配数={Allocs}",
            solveResult.AllocationShares.Count, actualByAlloc.Count);
    }

    /// <summary>
    /// **落库后 DB 级份额不变量校验**（PM《回复0928-2》§六：Candidate ChangeSeed **读取前**必须校验）。
    ///
    /// 与 `ValidateAllocationShareInvariants`（产出侧、内存）互补：本方法查**已落库**的 Base 版本，
    /// 覆盖「历史数据被其它路径写脏」的情形（唯一约束要到阶段C 才加，故此刻必须靠读取前校验兜住）。
    /// 查 Inv1 / Inv3 / Inv4；任一 &gt; 0 即 fail-closed。
    /// </summary>
    private async System.Threading.Tasks.Task ValidateStoredShareInvariantsAsync(int planVersionId)
    {
        var row = await _connectionManager.QueryFirstOrDefaultAsync<ShareInvariantRow>(
            @"SELECT
                (SELECT COUNT(*) FROM (
                    SELECT AllocationSequence, TaskId FROM AllocationTaskShare
                    WHERE PlanVersionId = @PlanVersionId
                    GROUP BY AllocationSequence, TaskId HAVING COUNT(*) > 1) a) AS Inv1,
                (SELECT COUNT(*) FROM (
                    SELECT s.AllocationSequence, t.TaskNo FROM AllocationTaskShare s
                    JOIN [Task] t ON t.Id = s.TaskId
                    WHERE s.PlanVersionId = @PlanVersionId
                    GROUP BY s.AllocationSequence, t.TaskNo HAVING COUNT(DISTINCT s.TaskId) > 1) b) AS Inv3,
                (SELECT COUNT(*) FROM AllocationTaskShare s
                 WHERE s.PlanVersionId = @PlanVersionId
                   AND NOT EXISTS (SELECT 1 FROM [Task] t WHERE t.Id = s.TaskId)) AS Inv4",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        var inv1 = row?.Inv1 ?? 0;
        var inv3 = row?.Inv3 ?? 0;
        var inv4 = row?.Inv4 ?? 0;

        if (inv1 > 0 || inv3 > 0 || inv4 > 0)
        {
            _logger.LogError(
                "[AllocationTaskShare] 落库数据不变量校验失败（PlanVersionId={PlanVersionId}）：Inv1={Inv1}, Inv3={Inv3}, Inv4={Inv4}",
                planVersionId, inv1, inv3, inv4);
            throw new InvalidOperationException(
                $"AllocationTaskShare 落库数据违反不变量（PlanVersionId={planVersionId}）：Inv1={inv1}, Inv3={inv3}, Inv4={inv4} —— 拒绝据此计算 Candidate ChangeSeed（份额膨胀会直接污染变化基线）");
        }
    }

    /// <summary>份额不变量 DB 校验结果行</summary>
    private sealed class ShareInvariantRow
    {
        public int Inv1 { get; set; }
        public int Inv3 { get; set; }
        public int Inv4 { get; set; }
    }

    /// <summary>
    /// 组装 PM《Stage、生产部门、Routing、Dependency、StageLeadTimeParam 接口裁决回复》(2026-09-28) §三
    /// 定义的 **`EffectiveStagePath`**：2号位 提供给 1号位 的**业务事实** =
    /// `MaterialId + StageCode + StageSeq + ProductionDepartmentId`（四条一组，不是只有码与序号）。
    ///
    /// 【部门来源】`MaterialStageDeptContext` —— PM §四/§六 逐字：
    ///   「StageCode 不能直接决定生产部门，必须通过 MaterialStageDeptContext 确定」「Stage 不是部门，
    ///    Stage 必须经过 Master 裁决」。
    /// 【null 语义】MSC 无 (MaterialId, StageCode) 映射 ⇒ 该步部门未知，**不推导、不猜**；
    ///   1号位 按既有最小B 口径记 `MISSING_PRODUCTION_DEPARTMENT_CONTEXT` 并置 Unscheduled。
    /// 【PM §三 分工】2号位 只提供业务事实，**不生成 Solver 内部对象**（StageDependency 由 1号位 自建）。
    /// </summary>
    private static List<StageSequenceChain> BuildEffectiveStagePaths(
        IReadOnlyList<StageSequenceChain> chains,
        IReadOnlyList<MaterialStageDepartmentContextDto> deptContexts)
    {
        var deptByMaterialStage = deptContexts
            .GroupBy(c => (c.MaterialId, c.StageCode))
            .ToDictionary(g => g.Key, g => g.First().ProductionDepartmentId);

        return chains
            .Select(chain => new StageSequenceChain
            {
                MaterialId = chain.MaterialId,
                Stages = chain.Stages
                    .Select(s => new StageSequenceStep
                    {
                        StageCode = s.StageCode,
                        StageSeq  = s.StageSeq,
                        ProductionDepartmentId =
                            deptByMaterialStage.TryGetValue((chain.MaterialId, s.StageCode), out var dept)
                                ? dept
                                : null
                    })
                    .ToList()
            })
            .ToList();
    }

    /// <summary>
    /// 解析「本次 BOM 批次」——与 `LoadBomSnapshotAsync` 同口径：
    /// 经 `OrderBomRequestLink` 取本 PlanVersion 关联的批次；SQL 已按 `SyncedAt DESC` 排序，
    /// 故返回列表的**首个 = 最新批次**。返回**全部**关联批次，供调用方同时派生「最新批次」与「多批次诊断」。
    /// </summary>
    private async Task<List<string>> ResolveLinkedBatchNosAsync(long planVersionId)
    {
        var rows = await _connectionManager.QueryAsync<BatchLinkRow>(
            @"SELECT r.BatchNo
              FROM OrderBomRequestLink r
              INNER JOIN [Order] o ON o.Id = r.OrderId
              WHERE o.PlanVersionId = @PlanVersionId AND r.BatchNo IS NOT NULL
              ORDER BY r.SyncedAt DESC",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        return rows.Select(r => r.BatchNo ?? string.Empty)
                   .Where(b => !string.IsNullOrEmpty(b))
                   .ToList();
    }

    /// <summary>
    /// 解析「生产指示号 → 本次 BOM 批次」：经 `[Order].MTS_InstructionNo` + `OrderBomRequestLink` 桥接。
    ///
    /// 用途：`LoadPiPositionsAsync` 的 Stage 顺序装载要**按每条 WIP 行自身的 PI 所属批次**收窄
    /// （而非全局 `MIN(StageSeq)`，PM §二十一.3）。一个 PI 若关联多个批次（异常情形），
    /// 取**字典序最小**者以保证结果稳定可复现——不取「最新」，避免同一份输入两次运行得到不同 Stage 顺序。
    /// </summary>
    private async Task<Dictionary<string, string>> LoadBatchByPiAsync(List<string> piNos)
    {
        var batchByPi = new Dictionary<string, string>(StringComparer.Ordinal);
        if (piNos.Count == 0)
            return batchByPi;

        var rows = await QueryChunkedInAsync(piNos, chunk =>
            _connectionManager.QueryAsync<PiBatchRow>(
                @"SELECT DISTINCT o.MTS_InstructionNo, r.BatchNo
                  FROM [Order] o
                  INNER JOIN OrderBomRequestLink r ON r.OrderId = o.Id
                  WHERE o.MTS_InstructionNo IN @PiNos AND r.BatchNo IS NOT NULL",
                new { PiNos = chunk },
                db: DatabaseId.APS));

        var ambiguous = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.MTS_InstructionNo)) continue;
            if (string.IsNullOrEmpty(row.BatchNo)) continue;

            if (batchByPi.TryGetValue(row.MTS_InstructionNo, out var existing))
            {
                if (!string.Equals(existing, row.BatchNo, StringComparison.Ordinal))
                {
                    ambiguous++;
                    if (string.CompareOrdinal(row.BatchNo, existing) >= 0) continue;
                }
                else
                {
                    continue;
                }
            }
            batchByPi[row.MTS_InstructionNo] = row.BatchNo;
        }

        if (ambiguous > 0)
        {
            _logger.LogWarning(
                "[Pegging] {Count} 个生产指示号关联了多个 BOM 批次，Stage 顺序装载取字典序最小批次以保证可复现（请核 PI 与批次的对应关系）",
                ambiguous);
        }

        return batchByPi;
    }

    /// <summary>生产指示号 → BOM 批次 行</summary>
    private sealed class PiBatchRow
    {
        public string? MTS_InstructionNo { get; set; }
        public string? BatchNo { get; set; }
    }

    /// <summary>
    /// ③ StartStageCode 填值（2026-09-11，5号位 O3 回复划归 2号位）：为每个新增生产需求回填
    /// 起点大工艺阶段码 = 该物料 Routing 有向图中「无入边源结点」工序的 StageCode（新生产从第一道工序起）。
    /// 无源结点 / 无 StageCode 时留空（与 1号位 PhaseTwo 未匹配兜底一致）。
    /// </summary>
    private static void FillStartStageCodes(
        PeggingResultVoucher voucher,
        List<RoutingOperation> operations,
        List<RoutingDependency> dependencies)
    {
        if (operations.Count == 0)
            return;

        // 无入边源结点 = OperationCode 不作任何 RoutingDependency.ToOperationCode 出现（该物料）
        var toNodesByMaterial = dependencies
            .GroupBy(d => d.MaterialId)
            .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(d => d.ToOperationCode), StringComparer.Ordinal));

        var firstStageByMaterial = operations
            .Where(o => !string.IsNullOrEmpty(o.StageCode))
            .GroupBy(o => o.MaterialId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    toNodesByMaterial.TryGetValue(g.Key, out var toNodes);
                    return g.Where(o => toNodes == null || !toNodes.Contains(o.OperationCode))
                            .OrderBy(o => o.OperationCode, StringComparer.Ordinal)
                            .Select(o => o.StageCode!)
                            .FirstOrDefault() ?? string.Empty;
                });

        foreach (var demand in voucher.LogicalProductionDemands)
        {
            if (string.IsNullOrEmpty(demand.StartStageCode) &&
                firstStageByMaterial.TryGetValue(demand.MaterialId, out var startStage))
            {
                demand.StartStageCode = startStage;
            }
        }
    }

    /// <summary>
    /// ③c 连续份额路径身份回填（2026-10-09）：为每条 <c>IsContinuation</c> 需求补 <c>RouteCode / PathId</c>。
    ///
    /// 为什么必须补：1号位 <c>PhaseTwoInitialScheduler.cs:310-325</c> 对 A/B 做**硬 Fail Closed** ——
    /// <c>ContinuationKey / RouteCode / PathId / StartOperationCode / NoSplitMerge</c> 缺任一即记
    /// <c>UnscheduledDemandKeys</c> 且**不产任何 Task**；从 2号位 视角表现为「A/B 数量凭空消失」。
    /// 原实现只在 <c>CloneDemandSlice</c> 里从源需求**照抄**，而源需求这两个字段**从来没有被赋过值** ⇒
    /// PV2 实测 284/284 片缺 RouteCode/PathId。
    ///
    /// 取值源 = **本次已归一化、即将随 <c>DomainSolveRequest</c> 交给 1号位的那份路由上下文**。
    /// <c>NormalizeToSingleRoute</c> 后 V1 恒为 <c>DEFAULT / 1</c>；1号位 的 <c>RoutingGraph</c> 字典就是
    /// 按这份载荷建的 ⇒ 给这个值才查得到图，**不是「猜唯一 Path」**（Q-3 禁的是图中存在多条时挑一条）。
    ///
    /// 保守规则：该物料在本次载荷里存在 **0 条或多条不同 (RouteCode, PathId)** ⇒ **不填**、留 null，
    /// 并计数告警 —— 保持 1号位 Fail Closed 原状，绝不静默挑一条。
    /// </summary>
    private static void FillContinuationRouteIdentities(
        PeggingResultVoucher voucher,
        List<RoutingOperation> operations,
        ILogger? logger = null)
    {
        var contDemands = voucher.LogicalProductionDemands.Where(d => d.IsContinuation).ToList();
        if (contDemands.Count == 0)
            return;

        // 物料 → 本次载荷里的 (RouteCode, PathId) 集合
        var routesByMaterial = operations
            .Where(o => !string.IsNullOrEmpty(o.RouteCode))
            .GroupBy(o => o.MaterialId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(o => (o.RouteCode, o.PathId)).Distinct().ToList());

        var filled = 0;
        var noRouting = 0;
        var ambiguous = 0;
        var samples = new List<string>(4);

        foreach (var demand in contDemands)
        {
            if (!string.IsNullOrEmpty(demand.RouteCode) && demand.PathId is not null)
                continue;   // 已带（未来 5号位 按 slice 交付时不再覆盖）

            if (!routesByMaterial.TryGetValue(demand.MaterialId, out var routes) || routes.Count == 0)
            {
                noRouting++;
                if (samples.Count < 4) samples.Add($"mat={demand.MaterialId} 无路由节点");
                continue;
            }
            if (routes.Count > 1)
            {
                ambiguous++;
                if (samples.Count < 4)
                    samples.Add($"mat={demand.MaterialId} 多路径[{string.Join(",", routes.Select(r => $"{r.RouteCode}/{r.PathId}"))}]");
                continue;
            }

            demand.RouteCode = routes[0].RouteCode;
            demand.PathId = routes[0].PathId;
            filled++;
        }

        logger?.LogInformation(
            "[Pegging][Continuity] A/B 路径身份回填: 片数={Total} 已填={Filled} 物料无路由={NoRouting} 多路径未填={Ambiguous}",
            contDemands.Count, filled, noRouting, ambiguous);

        if (noRouting + ambiguous > 0)
            logger?.LogWarning(
                "[Pegging][Continuity] A/B 路径身份**未填满**：{Unfilled} 片仍缺 RouteCode/PathId ⇒ " +
                "1号位 将按 Fail Closed 记 Unscheduled 且不产 Task。样本: {Samples}",
                noRouting + ambiguous, string.Join(" | ", samples));
    }

    /// <summary>物料 → UOM 装载行（Dapper 映射用）。</summary>
    private sealed class MaterialUomRow
    {
        public int Id { get; set; }
        public string UOM { get; set; } = string.Empty;
    }
    /// <summary>
    /// ④ 数量单位回填（2026-10-09，P1-08 方案a 取值源修正）：为每个新增生产需求回填**该需求自身物料**的
    /// <c>Material.UOM</c>。取值口径与理由见 <see cref="Core.Dto.LogicalProductionDemand.UOM"/>。
    ///
    /// 为什么放在 Pegging 后的统一回填、而不是在 <c>BuildLogicalProductionDemand</c> 里直接传：
    ///   ① <c>BuildLogicalProductionDemand</c> 在 Pegging 热循环内逐条调用，那里没有「物料 → UOM」的**同步**来源；
    ///   ② 与 <c>StartStageCode</c> / <c>RequiredStageCode</c> 同款——「Pegging 后按需求物料集一次性解析」，
    ///      只按需求物料集查一轮（分块，`QueryChunkedInAsync`），不引入 N+1；
    ///   ③ 按**需求自身 materialId** 取值 ⇒ 结构上不可能把成品单位串到子件（与 PI 身份整改同源）。
    ///
    /// 取不到时**保持 null**（不猜、不回退根订单 UOM）——1号位按 <c>demand.UOM ?? string.Empty</c> 处理，
    /// 与旧行为一致，但此时会打出 Warning 便于核对物料主档是否缺行。
    /// </summary>
    private async System.Threading.Tasks.Task FillDemandUomAsync(
        PeggingResultVoucher voucher,
        CancellationToken cancellationToken)
    {
        var demands = voucher.LogicalProductionDemands;
        if (demands.Count == 0)
            return;

        var materialIds = demands.Select(d => d.MaterialId).Distinct().ToList();
        var rows = await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<MaterialUomRow>(
                "SELECT Id, UOM FROM Material WHERE Id IN @Ids",
                new { Ids = chunk },
                db: DatabaseId.APS));

        var uomByMaterial = new Dictionary<int, string>(rows.Count);
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(r.UOM))
                uomByMaterial[r.Id] = r.UOM;
        }

        var filled = 0;
        var missing = 0;
        foreach (var demand in demands)
        {
            if (uomByMaterial.TryGetValue(demand.MaterialId, out var uom))
            {
                demand.UOM = uom;
                filled++;
            }
            else
            {
                missing++;
            }
        }

        if (missing > 0)
        {
            var missingSample = string.Join(",",
                demands.Where(d => !uomByMaterial.ContainsKey(d.MaterialId))
                       .Select(d => d.MaterialId).Distinct().Take(10));
            _logger.LogWarning(
                "[Pegging] 数量单位装载：{Missing}/{Total} 条需求取不到自身物料 UOM（Material 缺行或 UOM 为空）" +
                "⇒ 1号位 Task.UOM 将为空串；样例 MaterialId={Sample}",
                missing, demands.Count, missingSample);
        }
        else
        {
            _logger.LogDebug(
                "[Pegging] 数量单位装载：{Filled}/{Total} 条需求已回填自身物料 UOM（物料数={Materials}）",
                filled, demands.Count, materialIds.Count);
        }
    }

    /// <summary>
    /// 按当前 Domain 涉及的 MaterialId 裁剪 MaterialStageDeptContext（IsCurrent=1），
    /// 组装为 MaterialStageDepartmentContextDto 传入 1号位。只传 (MaterialId, StageCode, ProductionDepartmentId)，
    /// 不带 SourceType / SourceDetail / ValidFrom 等治理字段（1号位不需要）。
    /// </summary>
    private async Task<List<MaterialStageDepartmentContextDto>>
        LoadMaterialStageDeptContextAsync(IReadOnlyList<int> materialIds, CancellationToken ct)
    {
        if (materialIds.Count == 0)
            return new List<MaterialStageDepartmentContextDto>();

        var rows = await QueryChunkedInAsync(materialIds, chunk =>
            _connectionManager.QueryAsync<MaterialStageDeptContextLoadRow>(
                @"SELECT MaterialId, StageCode, DefaultProductionDepartmentId
                  FROM MaterialStageDeptContext
                  WHERE IsCurrent = 1 AND MaterialId IN @MaterialIds",
                new { MaterialIds = chunk },
                db: DatabaseId.APS));

        return rows
            .Select(r => new MaterialStageDepartmentContextDto
            {
                MaterialId = r.MaterialId,
                StageCode = r.StageCode,
                ProductionDepartmentId = r.DefaultProductionDepartmentId
            })
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BOM 快照装载
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 从 APS_BOM_RAW 加载 BOM 快照（按父件编码索引，供 PeggingLoop BOM 树遍历使用）
    ///
    /// 批次策略：优先取当前 PlanVersion 关联的 BatchNo（经由 OrderBomRequestLink）；
    /// 若关联缺失，兜底取最新 SyncedAt 批次（夜批顺序保证此批为最新）。
    /// </summary>
    /// <param name="scopeOrderIds">
    /// **可选**：把 BOM 快照再收窄到「这些订单」所使用的 BOM（默认 <c>null</c> = 不收窄，与既有行为逐字一致）。
    /// 用途 = **小样本定向跑**（改一行代码要在几秒内看到结果，而不是等全量 160s）：不传时按整 PV 的
    /// <c>ResolvedBOMNO</c> 装载，实测 PV2 = 846,170 行 BOM / 118,151 条 PI 事实 ⇒ 单是 5号位 PI Position
    /// 计算就要 **105s**；传 5 个订单后整个下游物料宇宙一起塌下来。
    /// ⚠️ 生产路径**不传**本参数 —— 「BOM 宇宙是否随订单子集收窄」是业务口径问题（部分订单运行时
    /// 是否仍需看到域内其它物料的 BOM），须 PM 裁决后才可动主链。见台账 §T-1009p。
    /// </param>
    private async Task<BomSnapshot> LoadBomSnapshotAsync(
        int planVersionId,
        CancellationToken ct,
        IReadOnlyCollection<long>? scopeOrderIds = null)
    {
        // 【2026-09-28 收窄】按 `OrderBomRequestLink.ResolvedBOMNO` 限定「本 PV 订单实际使用的 BOM」，
// 替代原「只按 BatchNo 取整批」——实测（PV539+540）3,672 BOMNO → 486（13%），
// 行数 3,272,701 → 846,170（**砍掉 74%**）。PM《BOM取用链接说明》§9.2 明文：
// 「使用 BatchNo + ResolvedBOMNO 进入 APS_BOM_RAW 得到本次 BOM 父子结构」。
// 兜底：该 PV 无 OrderBomRequestLink（如手工建的测试 PV）⇒ 不过滤 BOMNO，保持既有整批行为。
        var scopeIds = scopeOrderIds?.Where(id => id > 0).Distinct().ToArray();
        var resolvedBomNos = scopeIds is { Length: > 0 }
            ? (await QueryChunkedInAsync(scopeIds, chunk =>
                  _connectionManager.QueryAsync<string>(
                      @"SELECT DISTINCT r.ResolvedBOMNO
                        FROM OrderBomRequestLink r
                        WHERE r.ResolvedBOMNO IS NOT NULL AND r.OrderId IN @Ids",
                      new { Ids = chunk },
                      db: DatabaseId.APS))).ToList()
            : (await _connectionManager.QueryAsync<string>(
                  @"SELECT DISTINCT r.ResolvedBOMNO
                    FROM OrderBomRequestLink r
                    INNER JOIN [Order] o ON o.Id = r.OrderId
                    WHERE o.PlanVersionId = @PlanVersionId AND r.ResolvedBOMNO IS NOT NULL",
                  new { PlanVersionId = planVersionId },
                  db: DatabaseId.APS)).ToList();

        // 【2026-09-29 分片】`IN @ResolvedBomNos` 的列表 = 本 PV 用到的**全部** BOMNO（上面注释的收窄口径实测 486，
        //   但那是 PV539/540；全量 FAMILY_X 域可上千）⇒ 裸 `IN` 会撞 2100，把 BOM 快照整个取空（下游全判无 BOM）。
        //   主查询是「按 BatchNo 过滤 APS_BOM_RAW + LEFT JOIN Material」的单次取数，**无聚合、无跨行 ORDER BY**，
        //   故按 BOMNO 分批取并集与单条 `IN` 等价。
        //   兜底路径（该 PV 无 OrderBomRequestLink ⇒ resolvedBomNos 为空）保持**单跑整批**，与旧实现逐字一致。
        async Task<List<BomRawRow>> LoadBomRowsAsync(IReadOnlyList<string>? bomNos)
        {
            var loaded = await _connectionManager.QueryAsync<BomRawRow>(
                @"SELECT b.ParentMaterialCode,
                         b.ChildMaterialCode,
                         ISNULL(mc.Id, 0)          AS ChildMaterialId,
                         b.Quantity,
                         b.Level,
                         b.LLC,
                         b.IsLeaf,
                         ISNULL(mc.IsPurchased, 0) AS IsPurchased,
                         b.ChildRequiredStageCode
                  FROM APS_BOM_RAW b
                  LEFT JOIN Material mc ON mc.MaterialCode = b.ChildMaterialCode
                  WHERE b.BatchNo = ISNULL(
                      (SELECT TOP 1 r.BatchNo
                       FROM OrderBomRequestLink r
                       INNER JOIN [Order] o ON o.Id = r.OrderId
                       WHERE o.PlanVersionId = @PlanVersionId
                       ORDER BY r.SyncedAt DESC),
                      (SELECT TOP 1 BatchNo FROM APS_BOM_RAW ORDER BY SyncedAt DESC)
                  )
                    AND (@HasBomNoFilter = 0 OR b.BOMNO IN @ResolvedBomNos)",
                new
                {
                    PlanVersionId = planVersionId,
                    HasBomNoFilter = bomNos is { Count: > 0 } ? 1 : 0,
                    ResolvedBomNos = bomNos is { Count: > 0 } ? bomNos : new List<string> { "" }
                },
                db: DatabaseId.APS);
            return loaded.ToList();
        }

        var rows = new List<BomRawRow>();
        if (resolvedBomNos.Count == 0)
        {
            rows.AddRange(await LoadBomRowsAsync(null));
        }
        else
        {
            for (var offset = 0; offset < resolvedBomNos.Count; offset += SqlServerInParameterLimit)
            {
                var chunk = resolvedBomNos.Skip(offset).Take(SqlServerInParameterLimit).ToList();
                rows.AddRange(await LoadBomRowsAsync(chunk));
            }
        }

        if (resolvedBomNos.Count > 0)
        {
            _logger.LogInformation(
                "[Pegging] BOM 快照按 ResolvedBOMNO 收窄：{BomNos} 个 BOMNO，装载 {Rows} 行（PlanVersionId={PlanVersionId}）",
                resolvedBomNos.Count, rows.Count, planVersionId);
        }

        if (rows.Count == 0)
        {
            _logger.LogWarning(
                "[Pegging] APS_BOM_RAW 无数据（PlanVersionId={PlanVersionId}），BOM 快照为空",
                planVersionId);
            return new BomSnapshot(
                Enumerable.Empty<BomEdge>().ToLookup(e => e.ParentCode),
                new Dictionary<string, int>(),
                new Dictionary<string, bool>(),
                0,
                new HashSet<string>(StringComparer.Ordinal));
        }

        // 【2026-10-09 修复·T2-02 尾随】精确重复行去重。
        //
        // APS_BOM_RAW 里同一 (BOMNO, Parent, Child) 存在大量**逐列完全相同**的重复行：
        // 源头 MES 一份 BOM 被多个订单共用，ODS 侧靠 `RequestDetailId` 区分（见 09-28
        // `ODS_BOMWorkset真重复自查_给5号位`）；APS_BOM_RAW **未落该列** ⇒ 在 APS 侧退化成纯重复。
        // 实测（全表）：4,425,669 行 → 874,337 组 (BatchNo,BOMNO,Parent,Child)，**80.2% 是纯重复**。
        //
        // ⛔ 影响：原先这些重复行在 `ToBomStructure` 被 `Sum(Qty)` 累加，把真配比 **2.0 累加成 38.0**
        //    （实测 FINAL_FG-MGPL20-50Z → RAW-C2U20-48-AY700：同 BOMNO 同量 19 行）⇒ 逐层复合放大，
        //    1 张 3,744 的订单 4 层内涨到 **7.4e11**，Σ需求 虚高到 14.05T。
        //    **2026-09-28 我判「不影响计算正确性」是错的**——`BomExplosionService` 只在**求和之后**
        //    按 parentId 去重，配比早已被污染。
        //
        // 去重口径：精确重复不携带信息 ⇒ 按 (Parent, Child, Quantity) 折叠为一条。
        // 保留「同父同子不同配比」的少量真实多行（全表仅 3,243/679,537 对，交由 ToBomStructure 累加）。
        var dedupedRows = rows
            .GroupBy(r => (r.ParentMaterialCode, r.ChildMaterialCode, r.Quantity))
            .Select(g => g.First())
            .ToList();

        if (dedupedRows.Count != rows.Count)
        {
            _logger.LogInformation(
                "[Pegging] BOM 精确重复去重：{Raw} 行 → {Deduped} 行（去掉 {Dup} 行，{Pct:0.0}%）（PlanVersionId={PlanVersionId}）",
                rows.Count, dedupedRows.Count, rows.Count - dedupedRows.Count,
                rows.Count == 0 ? 0d : 100d * (rows.Count - dedupedRows.Count) / rows.Count,
                planVersionId);
        }

        // ── BOM 子件主档缺失：剔除该边（2026-10-09）──
        // 上面 `ISNULL(mc.Id, 0) AS ChildMaterialId` 把「APS_BOM_RAW 有子件码、Material 主档无此码」
        // **静默压成 0**。若放行，下游会生成 `MaterialId=0` 的 LogicalProductionDemand 交给 1号位
        // （PV2 实测 25 条），并把 `RAW-*` / `WIP-*` 原材料当**自制件**走生产缺口分支。
        // 单点卡口放在**装载**：剔边 ⇒ `edges` / `IsPurchasedByMaterial` / `MaterialUniverse` /
        // `ToBomStructure` 一次全部干净，BFS 与 DFS 两条回路都不再看到 materialId=0。
        // ⚠ 这是**边界**而非猜测：主档缺行 ⇒ 该子件既无单位也无采买属性（`ISNULL(mc.IsPurchased,0)=0`），
        //   APS 侧无从判定，交主档 Owner 修数据（样例见告警），不在 APS 里替它编属性。
        var missingChildRows = dedupedRows.Where(r => r.ChildMaterialId <= 0).ToList();
        if (missingChildRows.Count > 0)
        {
            var missingCodes = missingChildRows
                .Select(r => r.ChildMaterialCode)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();
            _logger.LogWarning(
                "[Pegging] BOM 子件主档缺失：{Edges} 条 BOM 边指向 {Codes} 个在 Material 主档**无对应行**的子件物料码，" +
                "已**剔除**（不再生成 MaterialId=0 的生产需求、不再误当自制件）。样例={Sample}；请数据 Owner 补主档" +
                "（这些码多为 RAW-*/WIP-* 原材料，缺行时 APS 无法判定单位与采买属性）。",
                missingChildRows.Count, missingCodes.Count,
                string.Join(",", missingCodes.Take(10)));

            dedupedRows = dedupedRows.Where(r => r.ChildMaterialId > 0).ToList();
        }

        // ── 采购/自制判定源修正（2026-10-09）──
        // 【缺陷】上面 `ISNULL(mc.IsPurchased, 0) AS IsPurchased` 是**唯一**的采购/自制判据，而
        //   `Material.IsPurchased` 是**死列**：全库 1,022,985 行中 IsPurchased=1 **零行**。冻结同步 SP
        //   （DDL v5.1.8.3 `MERGE Material`）只写 MaterialCode/MaterialName/Spec/MaterialType/UOM/IsActive
        //   ——INSERT 与 UPDATE 分支**都不含 IsPurchased** ⇒ 恒为列默认值 0。
        //   ⇒ ⇒ **采购件分支（PLANNING_PURCHASE_PLACEHOLDER / `if (isPurchased)`）在生产上永不可达。**
        // 【后果·实测 PV2】本域 BOM 子件 42,930 个物料中 15,616 个 `SupplyMode='PURCHASE'`，其中
        //   **15,550 个没有 RoutingOperation**（采购件本就不该有工艺）。旧行为把它们**全当自制件**，
        //   每个缺口都生成 LogicalProductionDemand 交给 1号位，1号位建不出工序 ⇒ 大面积弃排；
        //   这正是装载审计里「需求物料无任何 Routing 节点」的主因。
        // 【修正】改以 `MaterialSupplyContext.SupplyMode`（IsCurrent=1 共 1,801,031 行：MAKE 1,402,860 /
        //   PURCHASE 398,171；实测**每物料 FactoryId 唯一**，多厂物料=0）为准：
        //     · 主档显式 `IsPurchased=1` 仍**优先**（保留，兼容未来主档回填）；
        //     · MSC 有 PURCHASE ⇒ 采购件；
        //     · MSC 有 MAKE ⇒ 自制件（保持 false）；
        //     · 无 MSC 行 / OUTSOURCE / 其它 ⇒ **保守按自制**（与旧 `ISNULL(...,0)` 逐字同口径，不新增行为）。
        //   同物料多仓 SupplyMode 冲突时**采购优先**（「任一所辖仓为 PURCHASE 即按采购」）：宁可建采购占位，
        //   也不给没工艺的采购件造生产需求。冲突物料打 Warning 留证（本域实测仅 74 个物料）。
        // ⚠ 「用哪一列表达 采购/自制」属**业务口径**。本处不是换口径，而是**同一口径（采购⇒占位 / 自制⇒生产）
        //   换一个有值的来源字段**：原字段结构性无值 ⇒ 规则等价于未实现。若 PM 另有权威字段，以 PM 口径覆盖。
        var childCodes = dedupedRows
            .Select(r => r.ChildMaterialCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var mscRows = await QueryChunkedInAsync(childCodes, chunk =>
            _connectionManager.QueryAsync<MaterialSupplyModeRow>(
                @"SELECT MaterialCode, SupplyMode
                  FROM MaterialSupplyContext
                  WHERE IsCurrent = 1 AND MaterialCode IN @Codes",
                new { Codes = chunk },
                db: DatabaseId.APS));

        var purchaseModeCodes = new HashSet<string>(StringComparer.Ordinal);
        var makeModeCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in mscRows)
        {
            if (string.Equals(r.SupplyMode, "PURCHASE", StringComparison.OrdinalIgnoreCase))
                purchaseModeCodes.Add(r.MaterialCode);
            else if (string.Equals(r.SupplyMode, "MAKE", StringComparison.OrdinalIgnoreCase))
                makeModeCodes.Add(r.MaterialCode);
        }

        var conflictMaterials = purchaseModeCodes.Intersect(makeModeCodes, StringComparer.Ordinal).ToList();

        var purchaseOverridden = 0;
        var noSupplyModeSource = 0;
        foreach (var r in dedupedRows)
        {
            if (r.IsPurchased) continue;                    // 主档显式标记优先
            if (purchaseModeCodes.Contains(r.ChildMaterialCode))
            {
                r.IsPurchased = true;                       // 冲突时采购优先（见上）
                purchaseOverridden++;
            }
            else if (!makeModeCodes.Contains(r.ChildMaterialCode))
            {
                noSupplyModeSource++;                       // 无 MSC 行：保守按自制，不新增行为
            }
        }

        _logger.LogInformation(
            "[Pegging] 采购/自制判定源修正：按 MaterialSupplyContext.SupplyMode 判为采购件的 BOM 边={Overridden} 条" +
            "（主档 IsPurchased 全库恒 0，无判据）；无 MSC 行按自制保守={NoSource} 条；多仓冲突物料={Conflict}",
            purchaseOverridden, noSupplyModeSource, conflictMaterials.Count);

        if (conflictMaterials.Count > 0)
        {
            _logger.LogWarning(
                "[Pegging] SupplyMode 跨仓冲突物料 {Count} 个（同一物料既有 MAKE 又有 PURCHASE 仓，本处按采购优先）：样例={Sample}",
                conflictMaterials.Count, string.Join(",", conflictMaterials.Take(10)));
        }

        var edges = dedupedRows.Select(r => new BomEdge(
            r.ParentMaterialCode,
            r.ChildMaterialCode,
            r.ChildMaterialId,
            r.Quantity,
            r.Level,
            r.IsLeaf,
            r.IsPurchased,
            r.ChildRequiredStageCode)).ToList();

        // LLC 取各物料在所有 BOM 路径中出现的最小值
        var llcByMaterial = dedupedRows
            .Where(r => r.LLC.HasValue)
            .GroupBy(r => r.ChildMaterialCode)
            .ToDictionary(g => g.Key, g => g.Min(r => r.LLC!.Value));

        // IsPurchased 按物料编码分组（每个物料只有一个IsPurchased值）
        var isPurchasedByMaterial = dedupedRows
            .GroupBy(r => r.ChildMaterialCode)
            .ToDictionary(g => g.Key, g => g.First().IsPurchased);

        // 本域 BOM 可达物料宇宙（父件 ∪ 子件）：PI 事实装载按它放宽，见 BomSnapshot.MaterialUniverse 说明。
        var materialUniverse = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in dedupedRows)
        {
            materialUniverse.Add(r.ParentMaterialCode);
            materialUniverse.Add(r.ChildMaterialCode);
        }

        _logger.LogInformation(
            "[Pegging] BOM 可达物料宇宙：{Materials} 个物料（父件∪子件，PlanVersionId={PlanVersionId}）",
            materialUniverse.Count, planVersionId);

        return new BomSnapshot(edges.ToLookup(e => e.ParentCode), llcByMaterial, isPurchasedByMaterial, edges.Count,
                               materialUniverse);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 订单装载（PeggingLoop 前置步骤）
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class OrderPeggingRow
    {
        public long     OrderId          { get; set; }
        public long?    OrderCanonicalId { get; set; }
        public int      MaterialId       { get; set; }
        public string   MaterialCode     { get; set; } = string.Empty;
        public int      FactoryId        { get; set; }
        public string   FactoryCode      { get; set; } = string.Empty;
        public string?  OrderType        { get; set; }
        public string?  CustomerTier     { get; set; }
        public DateTime? IssueDate       { get; set; }
        public decimal  DemandQty        { get; set; }
        public DateTime DueDate          { get; set; }
        public string   UOM              { get; set; } = string.Empty;
        public int?     ProductFamilyId  { get; set; }
        public string?  MTS_InstructionNo { get; set; }

        /// <summary>订单号（<c>[Order].OrderNo</c>）：P 前缀 = PI 自身指令行，O 前缀 = 客户订单行。</summary>
        public string?  OrderNo          { get; set; }

        /// <summary>源订单 ID（<c>[Order].SourceOrderId</c>）：P 行时恒等于自身 PI 号（实测 100% 一致）。</summary>
        public string?  SourceOrderId    { get; set; }

        /// <summary>
        /// 是否「PI 自身指令行」（P 行）。判据 = <c>OrderNo</c> 以 <c>'P'</c> 开头。
        ///
        /// 为什么不用 <c>OrderType</c>：PV2 实测有 **533 行** <c>OrderType='PRODUCTION_INSTRUCTION'</c>
        /// 却是 O 行（客户订单引用了 PI），用 OrderType 判会把它们误当 P 行。
        /// 为什么不用 <c>SourceOrderId == MTS_InstructionNo</c>：实测与 <c>OrderNo LIKE 'P%'</c>
        /// **22,144 行 100% 一致**，但那是数据巧合式的判据，业务语义不如订单号前缀直白；此处取前缀。
        ///
        /// ⚠️ 2026-10-09 起**不再用于供给侧卫语句**：T2-02.4 自消费保护改由**层级门**实现
        /// （<c>NetAndAllocate</c> 的 <c>includePi = bomLevel &gt; 0</c>），L0 整层不取 PI 条目。
        /// 本属性与 <see cref="OrderNo"/> / <see cref="SourceOrderId"/> 保留给 T2-03「顶层生产需求的
        /// PI 身份 = 订单号（出荷指示号）」用（用户 2026-10-09 口径）。
        /// </summary>
        public bool IsPiSelfRow => !string.IsNullOrEmpty(OrderNo)
                                   && (OrderNo[0] == 'P' || OrderNo[0] == 'p');
    }

    private async Task<IReadOnlyList<OrderPeggingRow>> LoadOrdersForPeggingAsync(
        PeggingExecutionRequest request,
        CancellationToken ct)
    {
        var rows = await QueryChunkedInAsync(request.OrderIds, chunk =>
            _connectionManager.QueryAsync<OrderPeggingRow>(
                @"SELECT o.Id          AS OrderId,
                         o.OrderCanonicalId,
                         o.MaterialId,
                         m.MaterialCode,
                         o.FactoryId,
                         f.Code        AS FactoryCode,
                         o.OrderType,
                         o.CustomerTier,
                         o.IssueDate,
                         o.Quantity    AS DemandQty,
                         o.CustomerDueDate AS DueDate,
                         o.UOM,
                         m.ProductFamilyId,
                         o.MTS_InstructionNo,
                         o.OrderNo,
                         o.SourceOrderId
                  FROM [Order] o
                  INNER JOIN Material m ON m.Id = o.MaterialId
                  INNER JOIN Factory  f ON f.Id = o.FactoryId
                  WHERE o.Id IN @OrderIds",
                new { OrderIds = chunk },
                db: DatabaseId.APS));

        return rows.ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeggingLoop 主逻辑
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// BOM 遍历 + 供给扣减主函数。
    /// 每笔订单从根物料出发递归展开 BOM 树，在每个节点对 SupplyPool 执行贪婪扣减。
    /// 扣减结果累积到同一 PeggingResultVoucher。
    /// </summary>
    private async Task<PeggingResultVoucher> ExecutePeggingLoopAsync(
        PeggingExecutionRequest request,
        BomSnapshot bom,
        SupplyPool supplyPool,
        CancellationToken ct)
    {
        var orders = await LoadOrdersForPeggingAsync(request, ct);

        // ── Demand 优先级排序（2号位消费3号位 DemandPriorityConfig）──
        // 位置：LoadOrdersForPeggingAsync 之后、Pegging 循环之前（PM 冻结口径，不进 SQL 排序）
        // 结果：OrderId → DemandSequence，决定订单处理顺序，并透传给 LogicalProductionDemand
        var demandSequenceByOrder = await BuildDemandSequenceMapAsync(orders, request, ct);

        // 主链切新 BFS（2026-09-18 用户裁决：旧 DFS 无记忆化在真实 BOM 上 10 单/20 分钟不收敛，弃旧计算）：
        // 分配层 O(订单×物料×工厂去重数) + 共享子件合一 LPD；旧 RunDfsLoop 仅留 RunDualCompareAsync 对照用。
        var bomStructure = ToBomStructure(bom);
        return RunBfsLoop(orders, demandSequenceByOrder, request, bom, bomStructure, supplyPool, ct, out _);
    }

    /// <summary>
    /// 旧 DFS 循环（阶段2 S2.3 从 ExecutePeggingLoopAsync 原封抽出，语义零变化）：
    /// 按 DemandSequence 升序逐订单从根物料递归展开 BOM，在每个节点对 SupplyPool 贪婪扣减。
    /// 已弃旧（2026-09-18 用户裁决：路径级 visited 无记忆化，真实 BOM 上共享子件指数重遍历不收敛）。
    /// 主链已切 <see cref="RunBfsLoop"/>；本方法仅保留给 <see cref="RunDualCompareAsync"/> 跑「旧」侧对照，非每日排程路径。
    /// </summary>
    private PeggingResultVoucher RunDfsLoop(
        IReadOnlyList<OrderPeggingRow> orders,
        IReadOnlyDictionary<long, int> demandSequenceByOrder,
        PeggingExecutionRequest request,
        BomSnapshot bom,
        SupplyPool supplyPool,
        CancellationToken ct,
        out BomTraversalStats traversalStats)
    {
        var voucher = BuildResultVoucher(request, orders);

        // 按 DemandSequence 升序遍历：优先级高的订单先抢供给
        var orderedOrders = orders
            .OrderBy(o => demandSequenceByOrder.GetValueOrDefault(o.OrderId, int.MaxValue))
            .ToList();

        var orderIdx = 0;
        var loopSw = System.Diagnostics.Stopwatch.StartNew();
        traversalStats = new BomTraversalStats();
        foreach (var order in orderedOrders)
        {
            ct.ThrowIfCancellationRequested();
            orderIdx++;
            if (orderIdx == 1 || orderIdx % 25 == 0)
                _logger.LogInformation("[Pegging][进度] 回路 {Idx}/{Total} OrderId={OrderId} 物料={Material} 累计={Elapsed}ms",
                    orderIdx, orderedOrders.Count, order.OrderId, order.MaterialCode, loopSw.ElapsedMilliseconds);

            var demandSequence = demandSequenceByOrder.GetValueOrDefault(order.OrderId, 0);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            _ = TraverseBomNode(
                order,
                order.MaterialCode,
                order.MaterialId,
                order.FactoryId,
                order.FactoryCode,
                order.DemandQty,
                bomLevel: 0,
                demandSequence: demandSequence,
                bom,
                supplyPool,
                voucher,
                visited,
                traversalStats: traversalStats);
        }

        _logger.LogInformation(
            "[Pegging][BOM埋点] 展开总次数={Total} 唯一节点={Unique} 重复展开={Dup} 最大共享节点={MaxMat}(访问{MaxCnt}) 订单={Orders} 总耗时={Elapsed}ms 平均单节点={Avg:F3}ms",
            traversalStats.TotalVisits,
            traversalStats.UniqueNodes,
            traversalStats.DuplicateExpansions,
            traversalStats.MaxSharedMaterialCode,
            traversalStats.MaximumVisits,
            orderedOrders.Count,
            loopSw.ElapsedMilliseconds,
            traversalStats.TotalVisits == 0 ? 0d : (double)loopSw.ElapsedMilliseconds / traversalStats.TotalVisits);

        if (traversalStats.MissingChildMaterialEdges > 0)
        {
            _logger.LogWarning(
                "[Pegging] BOM 子件主档缺失：{Edges} 条 BOM 边在**回路内**才暴露（装载层卡口应已拦住；此处兜底留证）。样例={Sample}",
                traversalStats.MissingChildMaterialEdges,
                string.Join(",", traversalStats.MissingChildMaterialCodes.Take(10)));
        }

        voucher.IsFullyAllocated = voucher.ShortageQuantity == 0;
        voucher.ExecutionTimeMs = loopSw.ElapsedMilliseconds;
        return voucher;
    }

    /// <summary>构造结果凭证头（阶段2 S2.3 双跑复用：旧 DFS 与新 BFS 各自独立凭证，其余字段由扣减过程填充）。</summary>
    private static PeggingResultVoucher BuildResultVoucher(
        PeggingExecutionRequest request,
        IReadOnlyList<OrderPeggingRow> orders)
    {
        var firstOrder = orders.FirstOrDefault();
        return new PeggingResultVoucher
        {
            PlanVersionId    = request.PlanVersionId,
            DomainKey        = request.DomainKey,
            OrderId          = firstOrder?.OrderId ?? request.OrderIds.FirstOrDefault(),
            DemandMaterialId = firstOrder?.MaterialId ?? 0,
            UOM              = firstOrder?.UOM ?? string.Empty,
            IsSuccess        = true,
            ExecutedAt       = DateTime.Now
        };
    }

    /// <summary>
    /// BomSnapshot → BomStructure 适配（阶段2 S2.3）：把旧主链的 BOM 快照转成 Explosion 纯函数层的结构。
    /// 同 (parent → child) 且**配比不同**的多条原始行在此求和（配比累加），对齐「干净 BOM」语义；
    /// 旧 DFS 会逐行各递归一次，新去重结构父边唯一——两者仅在「同父同子多配比」下分离，正常数据等价。
    /// ⚠️ **精确重复行已在 <see cref="LoadBomSnapshotAsync"/> 装载时去重**（2026-10-09 修复）；
    ///    此处 `Sum` 只对**真正不同**配比生效（全表仅 3,243/679,537 对）。
    /// </summary>
    private static BomStructure ToBomStructure(BomSnapshot bom) => new(
        bom.ByParent.ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<BomComponent>)g
                .GroupBy(e => e.ChildCode, StringComparer.Ordinal)
                .Select(grp => new BomComponent(grp.Key, grp.First().ChildMaterialId, grp.Sum(e => e.Qty)))
                .ToList(),
            StringComparer.Ordinal),
        bom.IsPurchasedByMaterial);

    /// <summary>
    /// 新 BFS 逐层主流程（阶段2 S2.3，PM 0918-3 §二 批准的「宽度优先逐层 + 每(order,material)同一计算上下文一次净额」）。
    /// 不切主链，仅作双跑对照。与旧 DFS 的等价性见方案 §三.3；核心差异：
    ///   a) 每 (order, material, factory) 恰一次 NetAndAllocate（共享子件不再指数重复展开）；
    ///   b) 共享子件生产合并为一张 LPD（碎片 300+300 → 一张 600）；
    ///   c) 血缘边由本层按 ParentEdges 逐边建（一个节点多父 → 多条 Consumer→Producer 边，RequiredQty=父净×配比）。
    /// </summary>
    private PeggingResultVoucher RunBfsLoop(
        IReadOnlyList<OrderPeggingRow> orders,
        IReadOnlyDictionary<long, int> demandSequenceByOrder,
        PeggingExecutionRequest request,
        BomSnapshot bom,
        BomStructure bomStructure,
        SupplyPool supplyPool,
        CancellationToken ct,
        out BomTraversalStats traversalStats)
    {
        var voucher = BuildResultVoucher(request, orders);
        var explosion = new BomExplosionService();

        var orderedOrders = orders
            .OrderBy(o => demandSequenceByOrder.GetValueOrDefault(o.OrderId, int.MaxValue))
            .ToList();

        var orderIdx = 0;
        var loopSw = System.Diagnostics.Stopwatch.StartNew();
        traversalStats = new BomTraversalStats();

        foreach (var order in orderedOrders)
        {
            ct.ThrowIfCancellationRequested();
            orderIdx++;
            if (orderIdx == 1 || orderIdx % 25 == 0)
                _logger.LogInformation("[Pegging][BFS进度] 回路 {Idx}/{Total} OrderId={OrderId} 物料={Material} 累计={Elapsed}ms",
                    orderIdx, orderedOrders.Count, order.OrderId, order.MaterialCode, loopSw.ElapsedMilliseconds);

            var demandSequence = demandSequenceByOrder.GetValueOrDefault(order.OrderId, 0);
            var root = new BomOrderDemand(order.OrderId, order.MaterialId, order.MaterialCode, order.FactoryId, order.DemandQty);
            var nodes = explosion.ExplodeOrderStructure(bomStructure, root);

            // produced：本订单内 (material|factory) → 净额承接结果（父净驱动的源量）。
            // 合并 Key = 订单(循环外层) + material + factory；生产上下文(Stage/Domain/必要Routing)为扩展点
            // （PM 0918-3 §二：不得只按 MaterialId 合并；当前数据单订单内 factory 唯一，预留扩展位）。
            var produced = new Dictionary<string, NetAllocationOutcome>(StringComparer.Ordinal);
            var maxLevel = nodes.Count == 0 ? 0 : nodes.Max(n => n.Level);
            for (var level = 0; level <= maxLevel; level++)
            {
                foreach (var node in nodes.Where(n => n.Level == level))
                {
                    traversalStats.Record(node.MaterialCode, node.FactoryId);

                    // 下级依赖需求 = Σ 父件净产出 × 配比（等效旧 L3800 AllocatedQty × edge.Qty）
                    decimal gross = level == 0
                        ? order.DemandQty
                        : node.ParentEdges.Sum(e =>
                            produced.TryGetValue(SupplyPool.BuildKey(e.ParentMaterialCode, node.FactoryId), out var p)
                                ? p.ProducedQty * e.QtyPerUnit
                                : 0m);

                    var outcome = NetAndAllocate(
                        order, node.MaterialCode, node.MaterialId, node.FactoryId, order.FactoryCode,
                        gross, level, demandSequence, bom, supplyPool, voucher,
                        consumerLogicalDemandKey: null, consumerMaterialId: 0);

                    // 血缘建边（父 → 本物料需求/生产）：只要父件有净产出，无论本节点是否生产，都产 link。
                    // Case B（子件全库存/采购占位）：ProducerLogicalDemandKey=null，仍保留「父需求→子需求」真相（0910 §二十）。
                    // 与旧 DFS 差异：旧在 NetAndAllocate 内建「单 consumer」边；BFS 一个节点多父，在此按 ParentEdges 逐边建。
                    foreach (var edge in node.ParentEdges)
                    {
                        if (!produced.TryGetValue(SupplyPool.BuildKey(edge.ParentMaterialCode, node.FactoryId), out var parent))
                            continue;
                        // ⚠ parent.ProducerLogicalDemandKey! 安全不变式：produced 仅在本方法内「ProducerLogicalDemandKey is null 卫语句」之后才写入，
                        // 故父件有生产 → 其 LPD 键必非空，此处 ! 非掩盖空值；若日后放开该卫语句（如采购占位也入字典），
                        // 此 ! 会静默失守（ConsumerLogicalDemandKey=null），须同步改。（符号锚·勿行号引用——行号会随注释自身插入而漂移）
                        voucher.MaterialRequirementLinks.Add(new Core.Dto.MaterialRequirementLink
                        {
                            ConsumerLogicalDemandKey   = parent.ProducerLogicalDemandKey!,
                            ProducerLogicalDemandKey   = outcome?.ProducerLogicalDemandKey,
                            ConsumerMaterialId         = edge.ParentMaterialId,
                            ProducerMaterialId         = node.MaterialId,
                            // ChildDemandKey 与 NetAndAllocate 内 demandKey 同格式（ORDER_{OrderId}_{MaterialCode}_{FactoryId}）
                            ChildDemandKey             = $"ORDER_{order.OrderId}_{node.MaterialCode}_{node.FactoryId}",
                            RequiredQty                = parent.ProducedQty * edge.QtyPerUnit,
                            ProducerAllocationSequence = outcome?.ProducerAllocationSequence ?? 0
                        });
                    }

                    if (outcome is null) continue;                    // 无生产（真实供给补足/采购占位/失败）：不下钻、不存 produced
                    if (outcome.ProducerLogicalDemandKey is null) continue; // 防御：NEW_REQUIREMENT 生产恒有 LPD 身份
                    produced[SupplyPool.BuildKey(node.MaterialCode, node.FactoryId)] = outcome;
                }
            }
        }

        _logger.LogInformation(
            "[Pegging][BFS埋点] 展开总次数={Total} 唯一节点={Unique} 重复展开={Dup} 订单={Orders} 总耗时={Elapsed}ms",
            traversalStats.TotalVisits,
            traversalStats.UniqueNodes,
            traversalStats.DuplicateExpansions,
            orderedOrders.Count,
            loopSw.ElapsedMilliseconds);

        // ── T2-02 承接汇总（诊断，可删）：按供给类型看需求到底被谁承接 ──
        // 用途：判「PI Supply 入池」是否真的在承接需求，以及有多少塌成 NEW_REQUIREMENT（重复生产）。
        // Σ 需求（voucher.DemandQuantity）与 Σ 承接 + 短缺 应闭合。
        _logger.LogInformation(
            "[Pegging][T2-02] 承接汇总: {BySrc}; Σ需求={DemandQty}, 短缺={ShortageQty}, LPD={Lpd}",
            string.Join(", ", voucher.SupplyAllocations
                .GroupBy(a => a.SourceType)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Sum(x => x.AllocatedQuantity):0.####}({g.Count()}笔)")),
            voucher.DemandQuantity, voucher.ShortageQuantity, voucher.LogicalProductionDemands.Count);

        // T2-02 口径诊断：PI 承接按 BOM 层级拆分——顶层（BomLevel=0，即 [Order] 各订单行本身）
        // vs 下阶（BomLevel>0，BOM 展开出的子件需求）。用来判「PI 到底被谁承接了」。
        var wipAllocs = voucher.SupplyAllocations
            .Where(a => a.SourceType == Core.Enum.SupplySourceType.WIP).ToList();
        _logger.LogInformation(
            "[Pegging][T2-02] PI 承接分层: 顶层(L0)={L0Qty}({L0N}笔) 下阶(L1+)={L1Qty}({L1N}笔)",
            wipAllocs.Where(a => a.BomLevel == 0).Sum(a => a.AllocatedQuantity),
            wipAllocs.Count(a => a.BomLevel == 0),
            wipAllocs.Where(a => a.BomLevel > 0).Sum(a => a.AllocatedQuantity),
            wipAllocs.Count(a => a.BomLevel > 0));

        // ── 需求侧 PI 身份诊断（2026-10-09 整改 R-18/C-Q1.2）──
        // 判「根订单 PI 号是否还在被无条件传给 BOM 子件」：LPD 上的 PI 号必须**只**出现在
        // 「需求物料 == 该 PI 所属订单物料」的需求上。带 PI 的 LPD 若其物料是下阶子件，即为串味残留。
        _logger.LogInformation(
            "[Pegging][T2-02] 需求侧PI身份: LPD={Lpd} 带PI={WithPi} 无PI={NoPi}(=规划缺口) 去重PI={DistinctPi}",
            voucher.LogicalProductionDemands.Count,
            voucher.LogicalProductionDemands.Count(d => !string.IsNullOrEmpty(d.ProductionInstructionNo)),
            voucher.LogicalProductionDemands.Count(d => string.IsNullOrEmpty(d.ProductionInstructionNo)),
            voucher.LogicalProductionDemands
                .Where(d => !string.IsNullOrEmpty(d.ProductionInstructionNo))
                .Select(d => d.ProductionInstructionNo!)
                .Distinct(StringComparer.Ordinal).Count());

        voucher.IsFullyAllocated = voucher.ShortageQuantity == 0;
        voucher.ExecutionTimeMs = loopSw.ElapsedMilliseconds;
        return voucher;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 阶段2 S2.3 双跑对照（不切主链）：旧 DFS vs 新 BFS，产出 PM 0918-3 §八 7 项 + §六 I1-I4 报告
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc cref="IPeggingOrchestrator.RunDualCompareAsync" />
    public async System.Threading.Tasks.Task<DualRunReport> RunDualCompareAsync(
        PeggingExecutionRequest request,
        CancellationToken ct = default)
    {
        // 同输入：订单 / DemandSequence / BOM 快照 / 冻结策略快照 / 供给池（含硬锁）
        var orders = await LoadOrdersForPeggingAsync(request, ct);
        var demandSequenceByOrder = await BuildDemandSequenceMapAsync(orders, request, ct);
        var bomSnapshot = await LoadBomSnapshotAsync(request.PlanVersionId, ct);

        var strategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId;
        if (!strategyProfileVersionId.HasValue || strategyProfileVersionId.Value <= 0)
            throw new InvalidOperationException(
                "双跑策略上下文不完整：SchedulingContext.StrategyProfileVersionId 为空。正式运行必须有冻结策略版本，禁止静默回退。");
        var frozenSnapshot = await _frozenStrategySnapshotProvider
            .GetFrozenStrategySnapshotAsync(strategyProfileVersionId.Value, ct);

        // `persistPositionSnapshot: false` —— 双跑对照 / BFS 单跑都是**探针路径**（无生产调用方），
        // 只校验不落库，避免覆盖夜间 run 的 PI Position 快照（并省掉 11.8 万行逐行 INSERT 的往返）。
        var (supplyPool, _) = await LoadSupplyPoolAsync(request, frozenSnapshot, bomSnapshot, ct,
            persistPositionSnapshot: false);
        var bomStructure = ToBomStructure(bomSnapshot);

        // 旧 DFS / 新 BFS 各自独立供给账本（Clone 隔离），记录耗时与展开统计
        var oldSw = Stopwatch.StartNew();
        var oldVoucher = RunDfsLoop(orders, demandSequenceByOrder, request, bomSnapshot, supplyPool, ct, out var oldStats);
        oldSw.Stop();

        var newSw = Stopwatch.StartNew();
        var newVoucher = RunBfsLoop(orders, demandSequenceByOrder, request, bomSnapshot, bomStructure, supplyPool.Clone(), ct, out var newStats);
        newSw.Stop();

        var report = ComparePeggingVouchers(oldVoucher, newVoucher, request, orders.Count) with
        {
            OldTraversalVisits = oldStats.TotalVisits,
            NewTraversalVisits = newStats.TotalVisits,
            OldMs              = oldSw.ElapsedMilliseconds,
            NewMs              = newSw.ElapsedMilliseconds
        };

        LogDualRunReport(report);
        return report;
    }

    /// <summary>
    /// 主链 BFS 单跑（只读，不落库不 Solver）：仅跑 RunBfsLoop，验证新 BFS 展开耗时 + I1/I2 红线校验（ValidatePeggingResult）。
    /// 用户 2026-09-18 裁决弃旧 DFS（真实 BOM 上 10 单/20 分钟不收敛）、主链切 BFS 后，作为快速验收入口。
    /// </summary>
    public async System.Threading.Tasks.Task<BfsRunReport> RunBfsOnlyAsync(
        PeggingExecutionRequest request,
        CancellationToken ct = default)
    {
        var orders = await LoadOrdersForPeggingAsync(request, ct);
        var demandSequenceByOrder = await BuildDemandSequenceMapAsync(orders, request, ct);
        var bomSnapshot = await LoadBomSnapshotAsync(request.PlanVersionId, ct);

        var strategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId;
        if (!strategyProfileVersionId.HasValue || strategyProfileVersionId.Value <= 0)
            throw new InvalidOperationException(
                "BFS 单跑策略上下文不完整：SchedulingContext.StrategyProfileVersionId 为空。正式运行必须有冻结策略版本，禁止静默回退。");
        var frozenSnapshot = await _frozenStrategySnapshotProvider
            .GetFrozenStrategySnapshotAsync(strategyProfileVersionId.Value, ct);

        // `persistPositionSnapshot: false` —— 双跑对照 / BFS 单跑都是**探针路径**（无生产调用方），
        // 只校验不落库，避免覆盖夜间 run 的 PI Position 快照（并省掉 11.8 万行逐行 INSERT 的往返）。
        var (supplyPool, _) = await LoadSupplyPoolAsync(request, frozenSnapshot, bomSnapshot, ct,
            persistPositionSnapshot: false);
        var bomStructure = ToBomStructure(bomSnapshot);

        var sw = Stopwatch.StartNew();
        var voucher = RunBfsLoop(orders, demandSequenceByOrder, request, bomSnapshot, bomStructure, supplyPool, ct, out var stats);
        sw.Stop();

        var redLineErrors = ValidatePeggingResult(supplyPool, voucher);

        return new BfsRunReport
        {
            PlanVersionId       = request.PlanVersionId,
            OrderCount          = orders.Count,
            RedLineErrors       = redLineErrors,
            LpdCount            = voucher.LogicalProductionDemands.Count,
            AllocationCount     = voucher.SupplyAllocations.Count,
            LineageCount        = voucher.MaterialRequirementLinks.Count,
            DemandQuantity      = voucher.DemandQuantity,
            ShortageQuantity    = voucher.ShortageQuantity,
            TraversalVisits     = stats.TotalVisits,
            UniqueNodes         = stats.UniqueNodes,
            DuplicateExpansions = stats.DuplicateExpansions,
            ElapsedMs           = sw.ElapsedMilliseconds
        };
    }

    /// <summary>双跑逐维比对（纯函数）：I1-I3 + Δ1-Δ3。I4 恒成立；Δ4 与 Task 影响由 DeferredToPosition1 标注待 1号位。</summary>
    private static DualRunReport ComparePeggingVouchers(
        PeggingResultVoucher oldVoucher,
        PeggingResultVoucher newVoucher,
        PeggingExecutionRequest request,
        int orderCount)
    {
        // I1：NEW_REQUIREMENT 按 DemandKey 聚合净产出（旧碎片 N 笔、新 1 笔，聚合后总量应相等）
        var oldNet = AggregateByDemandKey(oldVoucher, SupplySourceType.NEW_REQUIREMENT);
        var newNet = AggregateByDemandKey(newVoucher, SupplySourceType.NEW_REQUIREMENT);
        var netDiffs = DiffMaps(oldNet, newNet);

        // I2：真实供给（非 NEW_REQUIREMENT / 规划占位）按物理供给身份聚合
        var oldSupply = AggregateByPhysicalSupply(oldVoucher);
        var newSupply = AggregateByPhysicalSupply(newVoucher);
        var supplyDiffs = DiffMaps(oldSupply, newSupply);

        // I2-PI：PI 承接（WIP + PRODUCTION_INSTRUCTION）
        var oldPi = AggregateByPhysicalSupply(oldVoucher, SupplySourceType.WIP, SupplySourceType.PRODUCTION_INSTRUCTION);
        var newPi = AggregateByPhysicalSupply(newVoucher, SupplySourceType.WIP, SupplySourceType.PRODUCTION_INSTRUCTION);
        var piDiffs = DiffMaps(oldPi, newPi);

        // I3：血缘边集 (Consumer, Producer, RequiredQty) 多集比对
        var lineageMismatches = CompareLineage(oldVoucher, newVoucher);

        return new DualRunReport
        {
            PlanVersionId      = request.PlanVersionId,
            OrderCount         = orderCount,
            NetOutputClosed    = netDiffs.Count == 0,
            NetOutputDiffs     = netDiffs,
            SupplyClosed       = supplyDiffs.Count == 0,
            SupplyDiffs        = supplyDiffs,
            PiClosed           = piDiffs.Count == 0,
            PiDiffs            = piDiffs,
            LineageClosed      = lineageMismatches.Count == 0,
            LineageMismatches  = lineageMismatches,
            OldLpdCount        = oldVoucher.LogicalProductionDemands.Count,
            NewLpdCount        = newVoucher.LogicalProductionDemands.Count,
            OldAllocationCount = oldVoucher.SupplyAllocations.Count,
            NewAllocationCount = newVoucher.SupplyAllocations.Count,
            OldDemandQuantity  = oldVoucher.DemandQuantity,
            NewDemandQuantity  = newVoucher.DemandQuantity,
            DeferredToPosition1 = new[]
            {
                "AllocationTaskShare 差异：1号位 PhaseFive 派生，2号位 Pegging 段无产物，待排程后对比",
                "Continuation 差异：1号位 排程后份额切片，2号位 Pegging 段无产物，待排程后对比",
                "Task 数量与资源负荷影响分析：PM 0918-3 §风险2 已判属 1号位 阶段",
                "I4 PI Position 最终一致性：2号位未触碰装载，待 1号位 bitmap 级确认"
            }
        };
    }

    private static IReadOnlyDictionary<string, decimal> AggregateByDemandKey(
        PeggingResultVoucher voucher, params SupplySourceType[] sourceTypes)
    {
        var types = sourceTypes.ToHashSet();
        return voucher.SupplyAllocations
            .Where(a => types.Contains(a.SourceType))
            .GroupBy(a => a.DemandKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedQuantity), StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, decimal> AggregateByPhysicalSupply(
        PeggingResultVoucher voucher, params SupplySourceType[] sourceTypes)
    {
        var types = sourceTypes.Length == 0 ? null : sourceTypes.ToHashSet();
        return voucher.SupplyAllocations
            .Where(a => a.SourceType != SupplySourceType.NEW_REQUIREMENT
                     && a.SourceType != SupplySourceType.PLANNING_PURCHASE_PLACEHOLDER
                     && (types == null || types.Contains(a.SourceType)))
            .GroupBy(a => PhysicalSupplyKey(a), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedQuantity), StringComparer.Ordinal);
    }

    private static string PhysicalSupplyKey(Core.Dto.SupplyAllocationItem a)
        => $"{a.SourceType}|{a.SupplyMaterialId}|{a.FactoryCode}|{a.SupplySourceId?.ToString() ?? "-"}";

    private static IReadOnlyList<DualRunDiff> DiffMaps(
        IReadOnlyDictionary<string, decimal> oldMap,
        IReadOnlyDictionary<string, decimal> newMap)
    {
        var diffs = new List<DualRunDiff>();
        foreach (var kv in oldMap)
        {
            var newQty = newMap.TryGetValue(kv.Key, out var n) ? n : 0m;
            if (kv.Value != newQty)
                diffs.Add(new DualRunDiff(kv.Key, kv.Value, newQty));
        }
        foreach (var kv in newMap)
            if (!oldMap.ContainsKey(kv.Key))
                diffs.Add(new DualRunDiff(kv.Key, 0m, kv.Value));
        return diffs;
    }

    private static IReadOnlyList<string> CompareLineage(
        PeggingResultVoucher oldVoucher,
        PeggingResultVoucher newVoucher)
    {
        var oldEdges = LineageMultiset(oldVoucher);
        var newEdges = LineageMultiset(newVoucher);
        var mismatches = new List<string>();
        foreach (var (key, count) in oldEdges)
        {
            var newCount = newEdges.TryGetValue(key, out var n) ? n : 0;
            if (count != newCount)
                mismatches.Add($"边({key})旧={count}新={newCount}");
        }
        foreach (var key in newEdges.Keys)
            if (!oldEdges.ContainsKey(key))
                mismatches.Add($"边({key})仅新侧存在");
        return mismatches;
    }

    /// <summary>血缘边多集：键 = ConsumerMaterialId|ProducerMaterialId|RequiredQty，值 = 出现次数（供同边重复折叠比对）。</summary>
    private static IReadOnlyDictionary<string, int> LineageMultiset(PeggingResultVoucher voucher)
        => voucher.MaterialRequirementLinks
            .GroupBy(l => $"{l.ConsumerMaterialId}|{l.ProducerMaterialId}|{l.RequiredQty}", StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private void LogDualRunReport(DualRunReport r)
    {
        _logger.LogInformation(
            "[Pegging][双跑] PlanVersionId={Pv} 订单={Orders} I1净产出={I1} I2供给={I2} PI承接={Pi} I3血缘={I3} I4_PIPos={I4} PASS={Pass}",
            r.PlanVersionId, r.OrderCount, r.NetOutputClosed, r.SupplyClosed, r.PiClosed, r.LineageClosed, r.PiPositionClosed, r.Pass);
        _logger.LogInformation(
            "[Pegging][双跑] Δ1 LPD {OldLpd}->{NewLpd} | Δ2 Allocation笔数 {OldAlloc}->{NewAlloc} | Δ3 DemandQuantity {OldDq}->{NewDq} | 重复展开 {OldVisits}->{NewVisits} | 耗时 {OldMs}ms->{NewMs}ms",
            r.OldLpdCount, r.NewLpdCount, r.OldAllocationCount, r.NewAllocationCount,
            r.OldDemandQuantity, r.NewDemandQuantity, r.OldTraversalVisits, r.NewTraversalVisits, r.OldMs, r.NewMs);
        foreach (var d in r.NetOutputDiffs)
            _logger.LogWarning("[Pegging][双跑][I1差异] {Key} 旧={Old} 新={New}", d.Key, d.OldQty, d.NewQty);
        foreach (var d in r.SupplyDiffs)
            _logger.LogWarning("[Pegging][双跑][I2差异] {Key} 旧={Old} 新={New}", d.Key, d.OldQty, d.NewQty);
        foreach (var d in r.PiDiffs)
            _logger.LogWarning("[Pegging][双跑][PI差异] {Key} 旧={Old} 新={New}", d.Key, d.OldQty, d.NewQty);
        foreach (var m in r.LineageMismatches)
            _logger.LogWarning("[Pegging][双跑][I3差异] {Msg}", m);
    }

    /// <summary>
    /// 将订单转换为 UpstreamDemand，消费3号位 DemandPriorityConfig 排序，返回 OrderId → DemandSequence 映射。
    /// </summary>
    private async Task<Dictionary<long, int>> BuildDemandSequenceMapAsync(
        IReadOnlyList<OrderPeggingRow> orders,
        PeggingExecutionRequest request,
        CancellationToken ct)
    {
        var demands = orders.Select(o => new UpstreamDemand
        {
            DemandKey    = o.OrderId.ToString(),
            OrderCanonicalId = o.OrderCanonicalId,
            OrderType    = o.OrderType,
            CustomerTier = o.CustomerTier,
            DueDate      = o.DueDate,
            IssueDate    = o.IssueDate,
            // DelayStatus / ProtectionStatus：Order 表暂无对应列，保持 null（不造假），待5号位事实标准化后接入
            SourceDemand = o
        }).ToList();

        // 4.1：策略版本必须取自本 Run 冻结上下文，不再固定传 0；缺失即视为运行上下文不完整，禁止静默回退 Fixture
        var strategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId;
        if (!strategyProfileVersionId.HasValue || strategyProfileVersionId.Value <= 0)
        {
            throw new InvalidOperationException(
                "DemandPriority 策略上下文不完整：SchedulingContext.StrategyProfileVersionId 为空。正式运行必须有冻结策略版本，禁止静默回退 Fixture。");
        }

        var config = await _demandPriorityConfigProvider.GetPriorityConfigAsync(strategyProfileVersionId.Value, ct);

        // S5（PM 0923）：EXPEDITE 身份 = ScopeJsonV2.OrderTargets.OrderCanonicalId，仅 PriorityMode=EXPEDITE 时生效；
        // 作为「可调整 Demand 排序前置竞争层」传给 Executor（V1 全量重排：expedite 需求整体前置，层内仍按冻结策略竞争）。
        var expediteOrderCanonicalIds = await LoadExpediteOrderCanonicalIdsAsync(request, ct);

        // 方案A：外部按 CalculationLayer 调用 Executor —— 只对「第一层：顶层独立需求（订单）」取当前层 Segments
        const int currentCalculationLayer = 1;
        var layerConfig = new DemandPriorityConfig
        {
            Segments = config.Segments
                .Where(s => s.CalculationLayer == currentCalculationLayer)
                .ToList()
        };

        var sorted = _demandPriorityExecutor.ExecutePrioritySort(demands, layerConfig, expediteOrderCanonicalIds);

        var map = new Dictionary<long, int>();
        foreach (var demand in sorted)
        {
            if (long.TryParse(demand.DemandKey, out var orderId))
            {
                map[orderId] = demand.DemandSequence;
            }
        }

        return map;
    }

    /// <summary>
    /// S5（PM 0923）：解析本 Run 的 EXPEDITE 目标订单集合。
    /// 仅当 ScopeJsonV2.PriorityMode == EXPEDITE 且 OrderTargets 非空时，返回 OrderCanonicalId 去重集合；否则空集。
    /// </summary>
    private async Task<IReadOnlySet<long>> LoadExpediteOrderCanonicalIdsAsync(
        PeggingExecutionRequest request,
        CancellationToken ct)
    {
        var scheduleRunId = request.SchedulingContext?.ScheduleRunId;
        if (scheduleRunId == null || scheduleRunId <= 0)
            return new HashSet<long>();

        var scopeJson = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT ScopeJson FROM ScheduleRun WHERE Id = @Id",
            new { Id = scheduleRunId.Value }, db: DatabaseId.APS);

        if (string.IsNullOrWhiteSpace(scopeJson))
            return new HashSet<long>();

        var scope = JsonSerializer.Deserialize<ScopeJsonV2>(scopeJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (scope == null || scope.PriorityMode != PriorityMode.Expedite || scope.OrderTargets is not { Count: > 0 })
            return new HashSet<long>();

        return scope.OrderTargets.Select(t => t.OrderCanonicalId).ToHashSet();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V1.2 原子 Allocation 机制（§5.3）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 原子分配结果
    /// </summary>
    private sealed class AllocationResult
    {
        public bool Success { get; init; }
        public decimal AllocatedQty { get; init; }
        public long AllocationSequence { get; init; }
        public string? FailureReason { get; init; }
        public AllocationRecord? Record { get; init; }

        public static AllocationResult Succeeded(decimal qty, long seq, AllocationRecord record) =>
            new() { Success = true, AllocatedQty = qty, AllocationSequence = seq, Record = record };

        public static AllocationResult Failed(string reason) =>
            new() { Success = false, FailureReason = reason };
    }

    /// <summary>
    /// V1.2 原子 Allocation 机制（§5.3）：9步原子动作
    ///
    /// 一笔供需Allocation成功时，必须在同一内存动作中完成：
    /// 1. 校验Demand还有余额
    /// 2. 校验Supply还有余额
    /// 3. 校验资格（Eligibility，当前版本暂不实现，待5号位规则引擎接入）
    /// 4. 校验Strict Binding
    /// 5. 校验Demand Protection
    /// 6. 校验Execution不可逆事实
    /// 7. 扣DemandBalance
    /// 8. 扣SupplyBalance
    /// 9. 此时生成AllocationSequence
    /// 10. 生成逻辑Allocation/LedgerEntry
    ///
    /// 任何一步失败：Demand/Supply余额均不得部分修改（通过在所有校验通过后才执行扣减实现原子性）
    /// </summary>
    private static AllocationResult TryAtomicAllocation(
        SupplyLedgerEntry supply,
        DemandBalance demand,
        int bomLevel,
        PeggingResultVoucher voucher,
        decimal requestedQty)
    {
        // ══════════════════════════════════════════════════════════════════════
        // 第一阶段：校验（所有校验必须通过才能进入扣减阶段）
        // ══════════════════════════════════════════════════════════════════════

        // Step 1: 校验Demand还有余额
        if (demand.RemainingQty <= 0m)
            return AllocationResult.Failed("Demand has no remaining balance");

        // Step 2: 校验Supply还有余额
        if (supply.RemainingQty <= 0m)
            return AllocationResult.Failed("Supply has no remaining balance");

        // 计算本次分配数量 = Min(供应余额, 需求余额, 请求数量)
        var allocQty = Math.Min(Math.Min(supply.RemainingQty, demand.RemainingQty), requestedQty);

        if (allocQty <= 0m)
            return AllocationResult.Failed("Calculated allocation quantity is zero");

        // Step 3: 校验资格（Eligibility）
        if (!ValidateEligibility(supply, demand))
            return AllocationResult.Failed($"Eligibility violation: Supply {supply.SupplyKey} (material {supply.MaterialId}) cannot serve demand (material {demand.MaterialId})");

        // Step 4: 校验Strict Binding
        if (!ValidateStrictBinding(supply, demand.CurrentOrderId, demand.DemandKey))
            return AllocationResult.Failed($"Strict Binding violation: Supply {supply.SupplyKey} is locked to another demand");

        // Step 5: 校验Demand Protection
        if (!ValidateDemandProtection(supply, demand.CurrentOrderId, demand.DemandKey))
            return AllocationResult.Failed($"Demand Protection violation: Supply {supply.SupplyKey} cannot be used for this demand");

        // Step 6: 校验Execution不可逆事实（Execution Lock）
        // Execution Lock表示供给已被不可逆地消耗（如已投料、已发货），不得再分配
        var executionLock = supply.Locks.FirstOrDefault(l => l.LockType == LockType.EXECUTION);
        if (executionLock != null)
            return AllocationResult.Failed($"Execution Lock violation: Supply {supply.SupplyKey} has been irreversibly consumed");

        // ══════════════════════════════════════════════════════════════════════
        // 第二阶段：原子扣减（所有校验通过，开始修改状态）
        // ══════════════════════════════════════════════════════════════════════

        // Step 7: 扣DemandBalance（§5.2要求：使用DemandBalance对象维护需求余额）
        demand.RemainingQty -= allocQty;

        // Step 8: 扣SupplyBalance
        supply.RemainingQty -= allocQty;

        // Step 9: 生成AllocationSequence（在扣减成功时生成，符合§5.4要求）
        var allocationSeq = voucher.NextAllocationSequence++;

        // Step 10: 生成逻辑Allocation/LedgerEntry
        var allocationRecord = new AllocationRecord
        {
            AllocationSequence = allocationSeq,
            AllocatedQty = allocQty,
            SupplyKey = supply.SupplyKey,
            SupplyType = supply.SourceType.ToString(),
            DemandKey = demand.DemandKey,
            MaterialId = supply.MaterialId,
            AllocatedAt = DateTime.Now,
            RequiresProduction = supply.SourceType == Core.Enum.SupplySourceType.NEW_REQUIREMENT
        };

        supply.Allocations.Add(allocationRecord);

        // 添加到凭证的SupplyAllocations（用于持久化）
        voucher.SupplyAllocations.Add(new Core.Dto.SupplyAllocationItem
        {
            AllocationSequence = allocationSeq,
            DemandKey = demand.DemandKey,
            DemandQuantity = demand.RequiredQty,
            SupplyMaterialId = supply.MaterialId,
            SupplySourceId = supply.SupplySourceId,
            AllocatedQuantity = allocQty,
            SourceType = supply.SourceType,
            SourceReference = supply.SourceReference,
            // SH 级（INTER_FACTORY_ORDER）分配落出荷指示号，供红线⑤⑥ 校验「同 SH 匹配不串 SH / 份额不重复计量」
            ShippingInstructionNo = supply.SourceType == Core.Enum.SupplySourceType.INTER_FACTORY_ORDER
                ? supply.PhysicalSourceKey
                : null,
            FactoryCode = supply.FactoryCode,
            BomLevel = bomLevel,
            AvailableAt = supply.AvailableAt,
            Priority = demand.Priority
        });

        // 添加到凭证的LedgerEntries（BOM遍历内存账本，§5.5要求）
        voucher.LedgerEntries.Add(new Core.Dto.PeggingLedgerEntry
        {
            OrderId = demand.CurrentOrderId ?? demand.RootOrderId ?? 0,
            DemandMaterialId = demand.MaterialId,
            DemandQuantity = demand.RequiredQty,
            SupplyMaterialId = supply.MaterialId,
            AllocatedQuantity = allocQty,
            SourceType = supply.SourceType,
            SourceId = supply.SupplySourceId,
            BomLevel = bomLevel,
            FactoryCode = supply.FactoryCode,
            ProductFamilyId = demand.ProductFamilyId,
            IsInFrozenZone = demand.IsInFrozenZone,
            Strategy = Core.Enum.PeggingStrategyType.FIFO,
            AvailableAt = supply.AvailableAt ?? DateTime.Now
        });

        return AllocationResult.Succeeded(allocQty, allocationSeq, allocationRecord);
    }

    /// <summary>
    /// 校验供给资格（§5.3 Step 3，Eligibility）
    ///
    /// 当前实现仅做「数量真相 Owner」身份的防御性红线：供给与需求物料必须一致。
    /// 该不变量在入池筛选已保证，此处是原子分配点的二次保险，防止入池装载逻辑演进后出现物料错配被静默承接。
    ///
    /// 复杂资格规则（客户指定供应商、渠道限制、质量等级、OperationResourceEligibility 事实源）待 5号位规则引擎接入，
    /// 注入点在此方法内扩展，2号位不发明规则语义。
    /// 跨厂 INTER_FACTORY_ORDER 的工厂归属由入池装载层归一（落 ShippingInstructionNo），此处不做工厂相等推导，避免与分域口径冲突。
    /// </summary>
    private static bool ValidateEligibility(SupplyLedgerEntry supply, DemandBalance demand)
    {
        // 红线1：物料身份一致（防御性；正常已在 LoadSupplyPoolAsync/LoadDemandPoolAsync 按 Material 归池）
        if (supply.MaterialId != demand.MaterialId)
            return false;

        // 扩展点（V1验收前）：待 5号位规则引擎接入复杂资格规则，例如：
        //   - 客户指定供应商 / 渠道限制
        //   - 质量等级门槛
        //   - OperationResourceEligibility 事实源（操作-资源/供给-需求资格）
        return true;
    }

    /// <summary>
    /// 校验Strict Binding Lock（§8.1）
    ///
    /// Strict Binding表示供给被严格绑定到特定需求，其他需求不得使用。
    /// 场景：客户指定料、工单专用料、冻结区锁定等。
    /// </summary>
    private static bool ValidateStrictBinding(
        SupplyLedgerEntry supply,
        long? demandOrderId,
        string demandKey)
    {
        var strictLock = supply.Locks.FirstOrDefault(l => l.LockType == LockType.STRICT_BINDING);
        if (strictLock == null)
            return true; // 无Strict Binding锁，校验通过

        // 有Strict Binding锁，必须锁定到当前需求才允许分配
        var isLockedToCurrentDemand =
            (strictLock.LockedToOrderId.HasValue && strictLock.LockedToOrderId == demandOrderId) ||
            (strictLock.LockedToDemandKey == demandKey);

        return isLockedToCurrentDemand;
    }

    /// <summary>
    /// 校验Demand Protection Lock（§8.2）
    ///
    /// Demand Protection表示供给被保护给特定需求组，其他需求不得使用。
    /// 场景：优先级保护、产品族保护、客户保护等。
    ///
    /// 与Strict Binding的区别：
    /// - Strict Binding：1对1强绑定，其他需求完全不可用
    /// - Demand Protection：1对N保护，保护组内的需求可用，组外不可用
    /// </summary>
    private static bool ValidateDemandProtection(
        SupplyLedgerEntry supply,
        long? demandOrderId,
        string demandKey)
    {
        var protectionLock = supply.Locks.FirstOrDefault(l => l.LockType == LockType.DEMAND_PROTECTION);
        if (protectionLock == null)
            return true; // 无Demand Protection锁，校验通过

        // 有Demand Protection锁，检查当前需求是否在保护组内
        // V1.2 当前版本：暂不实现复杂的保护组规则，待后续补充
        // 简化实现：检查是否锁定到当前需求
        var isProtectedForCurrentDemand =
            (protectionLock.LockedToOrderId.HasValue && protectionLock.LockedToOrderId == demandOrderId) ||
            (protectionLock.LockedToDemandKey == demandKey);

        return isProtectedForCurrentDemand;
    }

    /// <summary>
    /// 构建LogicalProductionDemand（V1.2）
    ///
    /// 将需要生产的AllocationRecord转换成Solver输入
    /// 按PM回复Answer 1规范：一个LogicalProductionDemand对应一个AllocationSequence
    /// </summary>
    private static Core.Dto.LogicalProductionDemand BuildLogicalProductionDemand(
        AllocationRecord allocation,
        string demandKey,
        long? orderId,
        string? productionInstructionNo,
        int materialId,
        int factoryId,
        string materialCode,
        decimal planningYieldPercent,
        DateTime requiredTime,
        int demandSequence,
        PeggingResultVoucher voucher)
    {
        // LogicalDemandKey格式：PlanVersion_AllocationSeq
        var logicalDemandKey = $"{voucher.PlanVersionId}_{allocation.AllocationSequence}";

        // StartStageCode 由 Pegging 后 FillStartStageCodes 统一回填（Routing 无入边源结点），此处仍占位空。
        var startStageCode = string.Empty;

        // 数量双口径：
        // - NetOutputQty：净产出数量（扣除损耗后的有效产出）
        // - PlannedProcessQty：计划加工数量（含损耗的实际投入）
        // PM C2-5：PlannedProcessQty = NetOutputQty / yield 反算（PlanningYield，清单27）。
        // 红线：已有 PI Supply 不得按 Yield 再次放大——反算只作用于「需要生产的缺口」，不碰已有供给。
        // 无匹配规则/越界（YieldPercent ∉ (0,100)）按 100 无损回退；四舍五入 4 位对齐 DECIMAL(18,4)。
        var netOutputQty = allocation.AllocatedQty;
        var plannedProcessQty = planningYieldPercent > 0m && planningYieldPercent < 100m
            ? Math.Round(netOutputQty * 100m / planningYieldPercent, 4)
            : netOutputQty;

        return new Core.Dto.LogicalProductionDemand
        {
            LogicalDemandKey = logicalDemandKey,
            PlanVersionId = voucher.PlanVersionId,
            DomainKey = voucher.DomainKey,
            AllocationSequence = allocation.AllocationSequence,
            DemandKey = demandKey,
            OrderId = orderId,
            MaterialId = materialId,
            FactoryId = factoryId,
            StartStageCode = startStageCode,
            NetOutputQty = netOutputQty,
            PlannedProcessQty = plannedProcessQty,
            RequiredAvailableTime = requiredTime,
            DemandSequence = demandSequence,
            ProductionInstructionNo = productionInstructionNo, // W2：Pegging已确定该需求归属哪张PI（无PI=null）
            IsUnlocated = false // INTEGRATION TODO: 联调占位，V1验收前必须接入5号位PI Position实际计算结果
        };
    }

    /// <summary>
    /// BOM 遍历统计（PM 0918-2 埋点）：量化旧主链「共享子件重复展开」的基线。
    /// 阶段1 只记录 Old 侧基线；阶段2 Explosion/Allocation 解耦后同口径对照「重复展开 → 0」验证整改效果。
    /// 仅统计、不改变任何 Allocation 语义。
    /// </summary>
    private sealed class BomTraversalStats
    {
        private readonly HashSet<string> _uniqueNodeKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _visitsByMaterial = new(StringComparer.Ordinal);

        public long TotalVisits { get; private set; }
        public long UniqueNodes => _uniqueNodeKeys.Count;
        public long DuplicateExpansions => TotalVisits - _uniqueNodeKeys.Count;
        public string? MaxSharedMaterialCode { get; private set; }
        public long MaximumVisits { get; private set; }

        /// <summary>
        /// BOM 子件在 <c>Material</c> 主档中**缺行**的边数（被跳过的下钻次数）。
        /// 见 <see cref="RecordMissingChildMaterial"/>：`LoadBomSnapshotAsync` 的
        /// `ISNULL(mc.Id, 0)` 把「子件码无主档行」静默压成 `ChildMaterialId=0`。
        /// </summary>
        public long MissingChildMaterialEdges { get; private set; }

        /// <summary>缺主档的子件物料码（去重，用于告警取样）。</summary>
        private readonly HashSet<string> _missingChildMaterialCodes = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> MissingChildMaterialCodes => _missingChildMaterialCodes;

        /// <summary>
        /// 登记一条「子件码在 Material 主档缺行」的 BOM 边（2026-10-09）。
        ///
        /// 背景：`LoadBomSnapshotAsync` 的 `ISNULL(mc.Id, 0) AS ChildMaterialId` 把「APS_BOM_RAW 有子件码、
        /// 但 Material 主档无此码」静默压成 **0**。放行会生成 `MaterialId=0` 的 LogicalProductionDemand
        /// 交给 1号位（实测 PV2：25 条，样例 `mat=0`），并把 `RAW-*` / `WIP-*` 原材料当**自制件**走生产缺口分支。
        ///
        /// ⚠ **主卡口在装载层**（`LoadBomSnapshotAsync` 剔边）：正常情形本计数**恒 0**。
        ///   本方法只作回路内兜底留证——万一将来有别的路径绕过装载层过滤塞进 materialId&lt;=0 的边，
        ///   能立刻从日志看到，而不是静默产出物料不存在的需求。
        /// </summary>
        public void RecordMissingChildMaterial(string childMaterialCode)
        {
            MissingChildMaterialEdges++;
            if (!string.IsNullOrEmpty(childMaterialCode))
                _missingChildMaterialCodes.Add(childMaterialCode);
        }

        public void Record(string materialCode, int factoryId)
        {
            TotalVisits++;
            _uniqueNodeKeys.Add($"{materialCode}|{factoryId}");
            _visitsByMaterial.TryGetValue(materialCode, out var cnt);
            cnt++;
            _visitsByMaterial[materialCode] = cnt;
            if (cnt > MaximumVisits)
            {
                MaximumVisits = cnt;
                MaxSharedMaterialCode = materialCode;
            }
        }
    }

    /// <summary>
    /// 递归 BOM 节点供给扣减。
    /// 贪婪扣减成功 → 记录 SupplyAllocationItem；有短缺（自制件）→ 累加 NEW_REQUIREMENT 虚拟供给并
    /// 生成 LogicalProductionDemand，再递归子节点。子件确有生产缺口时，产出「父需求 → 子需求」
    /// MaterialRequirementLink（consumerLogicalDemandKey 非空即本节点是子件）。
    /// 叶节点短缺计入 voucher.ShortageQuantity（真正无法拆解的缺口）。
    /// visited 集合防止当前遍历路径循环，退出时移除以允许 BOM 中的共用子件。
    /// </summary>
    private static string? TraverseBomNode(
        OrderPeggingRow order,
        string materialCode,
        int materialId,
        int factoryId,
        string factoryCode,
        decimal demandQty,
        int bomLevel,
        int demandSequence,
        BomSnapshot bom,
        SupplyPool supplyPool,
        PeggingResultVoucher voucher,
        HashSet<string> visited,
        string? consumerLogicalDemandKey = null,
        int consumerMaterialId = 0,
        BomTraversalStats? traversalStats = null)
    {
        var nodeKey = SupplyPool.BuildKey(materialCode, factoryId);
        if (!visited.Add(nodeKey)) return null;
        traversalStats?.Record(materialCode, factoryId);

        try
        {
            // 净额 + 承接 + LPD 生成（阶段2 S2.2 抽取）：与旧递归内联逻辑逐字等价，唯「子件递归」留在本方法。
            var outcome = NetAndAllocate(
                order, materialCode, materialId, factoryId, factoryCode,
                demandQty, bomLevel, demandSequence, bom, supplyPool, voucher,
                consumerLogicalDemandKey, consumerMaterialId);

            if (outcome is null) return null; // 无生产（真实供给补足 / 采购件占位 / 生产失败）：不下钻

            // 自制件确有生产 → 子件需求 = 父件净产出 × 配比（等效旧 result.AllocatedQty × edge.Qty）
            var children = bom.ByParent[materialCode].ToList();
            if (children.Count > 0)
            {
                foreach (var edge in children)
                {
                    TraverseBomNode(
                        order,
                        edge.ChildCode,
                        edge.ChildMaterialId,
                        factoryId,
                        factoryCode,
                        outcome.ProducedQty * edge.Qty,
                        bomLevel + 1,
                        demandSequence,
                        bom,
                        supplyPool,
                        voucher,
                        visited,
                        consumerLogicalDemandKey: outcome.ProducerLogicalDemandKey,
                        consumerMaterialId: materialId,
                        traversalStats: traversalStats);
                }
            }

            return null;
        }
        finally
        {
            visited.Remove(nodeKey);
        }
    }

    /// <summary>某节点净额承接的结果：NetOutput（NEW_REQUIREMENT 净产出）与生产 LPD 身份（未生产=null）。</summary>
    private sealed class NetAllocationOutcome
    {
        public required decimal ProducedQty { get; init; }
        public required string? ProducerLogicalDemandKey { get; init; }

        /// <summary>生产 Allocations 的序号（阶段2 S2.3 BFS 建血缘边用，Key=LogicalDemandKey 尾段）。</summary>
        public required long ProducerAllocationSequence { get; init; }
    }

    /// <summary>
    /// 单一净额承接入口（阶段2 S2.2 抽取，PM 0918-3 §六 唯一净额入口）。
    /// 从旧 TraverseBomNode 递归体内原封搬出的：DemandBalance 构建 → 三类供给扣减 → 跨厂 SH → 缺口处理
    /// （采购占位 / PLANNED_PRODUCTION）+ LPD + 任务喂任务血缘。唯独「子件递归」留在调用方（阶段2 由 BFS 逐层驱动）。
    /// 返回 null = 无生产、不下钻；返回 outcome = 生产成功、ProducedQty 供下级按「父净 × 配比」重算。
    /// </summary>
    private static NetAllocationOutcome? NetAndAllocate(
        OrderPeggingRow order,
        string materialCode,
        int materialId,
        int factoryId,
        string factoryCode,
        decimal demandQty,
        int bomLevel,
        int demandSequence,
        BomSnapshot bom,
        SupplyPool supplyPool,
        PeggingResultVoucher voucher,
        string? consumerLogicalDemandKey,
        int consumerMaterialId)
    {
        var demandKey = $"ORDER_{order.OrderId}_{materialCode}_{factoryId}";

        // 红线① Demand 闭合：累加本节点需求量，供 ValidatePeggingResult 校验
        // 「Σ(已分配) + 短缺 ≤ 需求总量」。跨层级 BOM 展开时总量严格大于叶级分配，属有效上界校验。
        voucher.DemandQuantity += demandQty;

        // §5.2 DemandBalance：构建需求侧内存账本
        var demand = new DemandBalance
        {
            // 【2026-09-29 修复】`RequiredQty` 是 `init` 属性，此前**从未赋值** ⇒ 恒为默认值 0。
            //   它是**不可变的需求节点总量**，与 `RemainingQty`（遍历中递减）不同：后者可扣减，它必须保持初始值。
            //   漏赋的直接后果：`TryAtomicAllocation` 落 `SupplyAllocationItem.DemandQuantity = demand.RequiredQty`
            //   ⇒ `PeggingSupplyAllocation.DemandQty` **全表 0**（实测 PV632：53,651/53,651 行 = 0，而 AllocatedQty 正常）。
            //   注意它**不影响分配结果**（分配只读 RemainingQty），只污染落库追溯列 —— 故两轮真跑均未暴露。
            RequiredQty = demandQty,
            RemainingQty = demandQty,
            MaterialId = materialId,
            MaterialCode = materialCode,
            FactoryId = factoryId,
            FactoryCode = factoryCode,
            DemandType = DemandType.ORDER,
            DemandKey = demandKey,
            RootOrderId = order.OrderId,
            CurrentOrderId = order.OrderId,
            BomLevel = bomLevel,
            DueTime = order.DueDate,
            Priority = demandSequence, // §6 Priority Segment：订单级需求优先级（由 DemandPriorityExecutor 排序结果透传）
            ProductFamilyId = 0, // V1.2：暂不使用产品族
            IsInFrozenZone = false,
            WorksetId = null
        };

        // 供给链选择（PM 2026-08-28 最终裁决 + Pegging 专项 v1.1）：不存在 Inventory/PI/Procurement 三类全局
        // 优先级；先按当前 Demand 业务身份确定允许进入的供给集合，再调用对应类内排序规则。
        //  - 顶层 SALES_ORDER（bomLevel=0）：ERP 已扣成品库存，不再搜普通成品库存（§2.1）。
        //  - 顶层（bomLevel=0）**不消费 PI**：顶层需求本身的生产是**组装工序**，它找不到 PI
        //    （部品出口的出荷指示没有对应的组装指示；出荷指示↔组装指示的挂钩由生管做，APS 不得自造）。
        //    顶层生产需求的 PI 身份 = 该行挂着的 PI（生管挂好的），没有就用订单号（出荷指示号）。
        //    PI 供给池服务的是**下阶（组件）需求**（用户 2026-10-09 口径）。
        //  - 下阶自制件（isPurchased=false）：合资格库存 → PI → 生产缺口。
        //  - 采购件（isPurchased=true）：合资格库存 → 正式采购/在途 → 规划采购占位。
        var isPurchased = bom.IsPurchasedByMaterial.TryGetValue(materialCode, out var purchased) && purchased;
        var includeInventory = bomLevel > 0;
        var includePi = bomLevel > 0;

        // ── 需求侧 PI 身份（PM `PM回复1009-1.md` §五 C-Q1 第 2 项 / §九 R-18，2026-10-09 整改）──
        // 规则：`ProductionInstructionNo` 是**承载该 PI 的那张 PI 所属物料**的物理身份。
        //   只有当「本需求物料 == 承载该 PI 的订单物料」时，该 PI 号才属于本需求。
        //
        // ⛔ 旧写法（已废止）：两处 LPD 构造都无条件传 `order.MTS_InstructionNo`。`order` 是**根订单**，
        //    经 `TraverseBomNode` 逐层原样下传 ⇒ 每个 BOM 子件需求都挂着**成品件**的 PI 号。后果：
        //    ① `BucketContinuityShares` 按 `demand.ProductionInstructionNo` 查 `contextsByPi`
        //       ⇒ 子件需求被切出「挂在**父件物料** MES 执行批上」的 Continuation Slice —— 跨物料串味
        //       （PM C-Q4「不能把父件PI号无条件给子件」、R-18「M2 不得继承 M1 PI/ExistingExecution」）；
        //    ② 该 PI 号继续经 `piByDemandKey` 流入 Task 的 `MTS_InstructionNo`（见本文件 W3 注释，
        //       「无 PI 需求 = null，**不得下发 MES**」）⇒ 子件 Task 会带着成品的 PI 号被当成可下发；
        //    ③ 同一根 PI 的执行上下文被**每个**子件需求重复计入 ⇒ ΣE 虚高（PV2 实测 ΣE=7,424,806，
        //       而 PI 承接总量仅 1,836,894）。
        // 顶层（L0，materialId == order.MaterialId）行为**不变**：该行挂的 PI 就是它自己的 PI
        //   （见上方「顶层生产需求的 PI 身份 = 该行挂着的 PI」口径）。
        // 下阶拿不到「自己物料的 PI」时**保守置 null**（无 PI 需求），不猜、不平均切（PM C-Q4）。
        var demandProductionInstructionNo =
            materialId == order.MaterialId ? order.MTS_InstructionNo : null;

        // 贪婪扣减：使用原子Allocation机制，确保供需扣减、Lock校验、AllocationSequence生成的原子性
        foreach (var entry in supplyPool.GetEntries(materialCode, factoryId, isPurchased, includeInventory, includePi))
        {
            if (demand.RemainingQty <= 0m) break;

            var result = TryAtomicAllocation(
                supply: entry,
                demand: demand,
                bomLevel: bomLevel,
                voucher: voucher,
                requestedQty: demand.RemainingQty);

            if (!result.Success)
            {
                // 原子分配失败（Lock冲突、余额不足等），跳过此供给，尝试下一个
                // 失败原因：result.FailureReason（可在调试时输出）
                continue;
            }

            // 原子分配成功，demand.RemainingQty已在TryAtomicAllocation中扣减

            // V1.2：判断是否需要生产，生成LogicalProductionDemand
            if (result.Record != null && result.Record.RequiresProduction)
            {
                voucher.LogicalProductionDemands.Add(BuildLogicalProductionDemand(
                    allocation: result.Record,
                    demandKey: demand.DemandKey,
                    orderId: order.OrderId,
                    productionInstructionNo: demandProductionInstructionNo,
                    materialId: materialId,
                    factoryId: factoryId,
                    materialCode: materialCode,
                    planningYieldPercent: supplyPool.PlanningYieldPercent(materialCode),
                    requiredTime: order.DueDate,
                    demandSequence: demandSequence,
                    voucher: voucher));
            }
        }

        // ── 跨厂出荷指示（SH级）绑定消费：三类通用排序扣减后仍短缺时，按 AvailableAt 升序消费 SH 供给 ──
        // PM 裁决（§七.2）：SH 保持单一 Supply 身份；Transit/Received 是其履行状态两段，PhysicalSourceKey=SH No，
        // 由红线⑤⑥ 校验「同 SH 匹配不串 SH / 份额不重复计量」。TryAtomicAllocation 落 ShippingInstructionNo=SH No。
        if (demand.RemainingQty > 0m)
        {
            foreach (var shEntry in supplyPool.GetInterFactoryEntries(materialCode, factoryId))
            {
                if (demand.RemainingQty <= 0m) break;

                var shResult = TryAtomicAllocation(
                    supply: shEntry,
                    demand: demand,
                    bomLevel: bomLevel,
                    voucher: voucher,
                    requestedQty: demand.RemainingQty);

                if (!shResult.Success)
                {
                    // 原子分配失败（Lock冲突、余额不足等），跳过此 SH 供给，尝试下一个
                    continue;
                }
            }
        }

        if (demand.RemainingQty <= 0m) return null;

        // V1.2：缺口处理 - 根据IsPurchased区分采购件/自制件
        if (isPurchased)
        {
            // 采购件：生成 Planning-only Purchase Placeholder（§9.3）
            // 特征：仅内存、ESTIMATED、NOT_COMMITTED、不生成采购单、不生成Task、不可作为CTP承诺
            // 不生成LogicalProductionDemand，不触发Task生成
            supplyPool.Add(
                materialCode: materialCode,
                materialId: materialId,
                factoryId: factoryId,
                qty: demand.RemainingQty,
                // P0-07：规划采购占位 AvailableTime = DataCutoffTime + 冻结 DefaultPurchaseLT（不再 DueDate-7 天倒推）
                availableAt: (supplyPool.DataCutoffTime == default ? DateTime.Now : supplyPool.DataCutoffTime)
                    .AddDays(supplyPool.DefaultPurchaseLtDays),
                sourceType: Core.Enum.SupplySourceType.PLANNING_PURCHASE_PLACEHOLDER,
                sourceRef: $"PLANNING_PLACEHOLDER_{voucher.PlanVersionId}_{materialCode}_{Guid.NewGuid():N}",
                factoryCode: factoryCode,
                confidence: SupplyConfidence.ESTIMATED,
                commitment: SupplyCommitment.NOT_COMMITTED);

            voucher.ShortageQuantity += demand.RemainingQty;
            return null;
        }

        // 自制件：生成PLANNED_PRODUCTION虚拟供给，通过原子Allocation流程处理
        // 符合§5.3和§5.4要求：必须经过完整的10步原子校验，AllocationSequence在成功时生成

        // 添加虚拟PLANNED_PRODUCTION供给到SupplyPool（返回引用直接分配；NEW_REQUIREMENT 属缺口结果，
        // 不参与 GetEntries 三类排序，故不能依赖 .Last() 取回）
        var virtualSupply = supplyPool.Add(
            materialCode: materialCode,
            materialId: materialId,
            factoryId: factoryId,
            qty: demand.RemainingQty,
            availableAt: order.DueDate,
            sourceType: Core.Enum.SupplySourceType.NEW_REQUIREMENT,
            sourceRef: $"NEW_REQ_{voucher.PlanVersionId}_{materialCode}_{Guid.NewGuid():N}",
            factoryCode: factoryCode);
        var productionResult = TryAtomicAllocation(
            supply: virtualSupply,
            demand: demand,
            bomLevel: bomLevel,
            voucher: voucher,
            requestedQty: demand.RemainingQty);

        if (!productionResult.Success)
        {
            voucher.ShortageQuantity += demand.RemainingQty;
            return null;
        }

        string? producerLogicalDemandKey = null;
        if (productionResult.Record != null && productionResult.Record.RequiresProduction)
        {
            producerLogicalDemandKey = $"{voucher.PlanVersionId}_{productionResult.Record.AllocationSequence}";
            voucher.LogicalProductionDemands.Add(BuildLogicalProductionDemand(
                allocation: productionResult.Record,
                demandKey: demand.DemandKey,
                orderId: order.OrderId,
                productionInstructionNo: demandProductionInstructionNo,
                materialId: materialId,
                factoryId: factoryId,
                materialCode: materialCode,
                planningYieldPercent: supplyPool.PlanningYieldPercent(materialCode),
                requiredTime: order.DueDate,
                demandSequence: demandSequence,
                voucher: voucher));

            // 任务喂任务血缘（R2 / PM 2026-09-10）：本节点是子件且确有生产缺口时，
            // 产出「父需求 → 子需求」MaterialRequirementLink，1号位据此生成真实 TaskDependency。
            if (!string.IsNullOrEmpty(consumerLogicalDemandKey))
            {
                voucher.MaterialRequirementLinks.Add(new Core.Dto.MaterialRequirementLink
                {
                    ConsumerLogicalDemandKey   = consumerLogicalDemandKey!,
                    ProducerLogicalDemandKey   = producerLogicalDemandKey!,
                    ConsumerMaterialId         = consumerMaterialId,
                    ProducerMaterialId         = materialId,
                    RequiredQty                = demandQty,
                    ProducerAllocationSequence = productionResult.Record.AllocationSequence
                });
            }
        }

        return new NetAllocationOutcome
        {
            ProducedQty = productionResult.AllocatedQty,
            ProducerLogicalDemandKey = producerLogicalDemandKey,
            ProducerAllocationSequence = productionResult.AllocationSequence
        };
    }

    /// <summary>
    /// P0-04：构造 CandidateContext（FULL 返回 null）。
    /// Base 锚点 = request.BasePlanVersionId（3号位创建 Run 时冻结当前 ACTIVE；2号位运行期不得再查「此刻最新 ACTIVE」）。
    /// ChangeSeed = Candidate 重新 Pegging 相对 Base ACTIVE 的真实逻辑生产需求差异集（新增/减少/数量变化），不是新订单 ID。
    /// ExternalDomainResourceBlocks = 编排层已装载的其它 Domain ACTIVE 共享资源占用（§11，Immutable=true）。
    /// </summary>
    private async Task<CandidateContext?> BuildCandidateContextAsync(
        PeggingExecutionRequest request,
        PeggingResultVoucher voucher)
    {
        if (!request.IsCandidate)
            return null;

        var changeSeedKeys = request.BasePlanVersionId.HasValue
            ? await ComputeChangeSeedKeysAsync(request.BasePlanVersionId.Value, voucher)
            : Array.Empty<string>();

        return new CandidateContext
        {
            BasePlanVersionId            = request.BasePlanVersionId,
            ChangeSeedKeys               = changeSeedKeys,
            ExternalDomainResourceBlocks = request.ExternalDomainResourceBlocks ?? Array.Empty<ResourceBlock>()
        };
    }

    /// <summary>
    /// P0-04 §2.2：ChangeSeed = Candidate Pegging 相对 Base ACTIVE 的差异集。
    /// Base 侧事实源 = AllocationTaskShare（全库唯一持久化 DemandKey 的分配分享表），按 DemandKey 聚合 ShareQty；
    /// Candidate 侧 = 本次 voucher.SupplyAllocations（含 NEW_REQUIREMENT 缺口）按 DemandKey 聚合 AllocatedQuantity。
    /// 差异判定：新增（候选有基无）/减少（基有候选无）/数量变化（|Δ| > 1e-4）。键值为 DemandKey，1号位 PhaseFour 按其匹配。
    /// </summary>
    private async Task<IReadOnlyList<string>> ComputeChangeSeedKeysAsync(
        int basePlanVersionId,
        PeggingResultVoucher voucher)
    {
        // PM《回复0928-2》§六 裁决：ChangeSeed **读取前**必须先校验 Base 侧份额无重复 ——
        // 本方法的 `SUM(ShareQty) BY DemandKey` 是 Candidate 变化基线的唯一来源，膨胀即误判。
        await ValidateStoredShareInvariantsAsync(basePlanVersionId);

        var baseRows = await _connectionManager.QueryAsync<DemandKeyQtyRow>(
            @"SELECT DemandKey, SUM(ShareQty) AS Qty
              FROM AllocationTaskShare
              WHERE PlanVersionId = @PlanVersionId
              GROUP BY DemandKey",
            new { PlanVersionId = basePlanVersionId },
            db: DatabaseId.APS);

        var baseMap = baseRows.ToDictionary(r => r.DemandKey, r => r.Qty, StringComparer.Ordinal);

        var candMap = voucher.SupplyAllocations
            .GroupBy(a => a.DemandKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.AllocatedQuantity), StringComparer.Ordinal);

        var seeds = new HashSet<string>(StringComparer.Ordinal);

        // 新增 / 减少
        foreach (var k in candMap.Keys)
            if (!baseMap.ContainsKey(k)) seeds.Add(k);
        foreach (var k in baseMap.Keys)
            if (!candMap.ContainsKey(k)) seeds.Add(k);

        // 数量变化（两侧都存在，但本次分配量发生显著变化）
        foreach (var (k, candQty) in candMap)
            if (baseMap.TryGetValue(k, out var baseQty) && Math.Abs(candQty - baseQty) > 0.0001m)
                seeds.Add(k);

        return seeds.ToList();
    }

    private IReadOnlyList<AllocationLineage> BuildAllocationLineage(PeggingResultVoucher voucher)
    {
        var lineage = new List<AllocationLineage>();

        foreach (var alloc in voucher.SupplyAllocations)
        {
            lineage.Add(new AllocationLineage
            {
                AllocationSequence = alloc.AllocationSequence,
                DemandKey = alloc.DemandKey,
                MaterialId = alloc.SupplyMaterialId,
                SupplyType = alloc.SourceType.ToString(),
                SupplyKey = alloc.SourceReference ?? alloc.SupplySourceId?.ToString() ?? "",
                Quantity = alloc.AllocatedQuantity,
                AvailableTime = alloc.AvailableAt
            });
        }

        return lineage;
    }

    private IReadOnlyList<MaterialAvailabilitySlice> BuildMaterialConstraints(PeggingResultVoucher voucher)
    {
        var constraints = new List<MaterialAvailabilitySlice>();

        foreach (var alloc in voucher.SupplyAllocations)
        {
            if (alloc.AvailableAt.HasValue)
            {
                var factoryId = 0;
                if (int.TryParse(alloc.FactoryCode, out var fid))
                    factoryId = fid;

                constraints.Add(new MaterialAvailabilitySlice
                {
                    AllocationSequence = alloc.AllocationSequence,
                    MaterialId = alloc.SupplyMaterialId,
                    FactoryId = factoryId,
                    Quantity = alloc.AllocatedQuantity,
                    AvailableTime = alloc.AvailableAt.Value,
                    SourceType = alloc.SourceType.ToString(),
                    SourceKey = alloc.SourceReference ?? alloc.SupplySourceId?.ToString() ?? "",
                    Commitment = null,
                    Confidence = null
                });
            }
        }

        return constraints;
    }

    /// <summary>
    /// ScopeJsonV2 → RunScope 投影（M2）。从 ScheduleRun.ScopeJson 反序列化，按 1号位 内存键体系投影。
    /// OrderCanonicalId / TaskId 在投影层终止，不进 Solver 契约。null = FULL 语义。
    /// </summary>
    private async Task<RunScope?> BuildRunScopeAsync(
        PeggingExecutionRequest request,
        PeggingResultVoucher voucher,
        int? basePlanVersionId,
        CancellationToken ct)
    {
        var scheduleRunId = request.SchedulingContext?.ScheduleRunId;
        if (scheduleRunId == null || scheduleRunId <= 0)
            return null;

        var scopeJson = await _connectionManager.QueryFirstOrDefaultAsync<string>(
            "SELECT ScopeJson FROM ScheduleRun WHERE Id = @Id",
            new { Id = scheduleRunId.Value }, db: DatabaseId.APS);

        if (string.IsNullOrWhiteSpace(scopeJson))
            return null;

        var scope = JsonSerializer.Deserialize<ScopeJsonV2>(scopeJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (scope == null)
            return null;

        // 1号位 §三 补充1：Trigger 零值防御（八码从 1 起，非法值 fail-closed）
        if (!Enum.IsDefined(scope.Trigger) || (int)scope.Trigger is < 1 or > 8)
        {
            throw new InvalidOperationException(
                $"ScopeJsonV2.Trigger 非法值: {(int)scope.Trigger}（不在八码 1~8 范围内）");
        }

        // DueDateOverrides：OrderCanonicalId → LogicalDemandKey（仅 ManualTargetDueDate 非空行投影）
        IReadOnlyList<DueDateOverride> dueDateOverrides = Array.Empty<DueDateOverride>();
        if (scope.OrderTargets is { Count: > 0 })
        {
            var targets = scope.OrderTargets
                .Where(t => t.ManualTargetDueDate.HasValue)
                .ToList();
            if (targets.Count > 0)
            {
                var ocIds = targets.Select(t => t.OrderCanonicalId).Distinct().ToList();
                // 分片：`ocIds` = ScopeJson.OrderTargets 里带人工目标交期的订单（批量插单/改期可上千）
                //   ⇒ 裸 `IN @OcIds` 会撞 2100，整个 DueDateOverride 投影静默落空。走统一帮手。
                var orderRows = await QueryChunkedInAsync(ocIds, chunk =>
                    _connectionManager.QueryAsync<dynamic>(
                        @"SELECT obl.OrderId, obl.OrderCanonicalId
                          FROM OrderBomRequestLink obl
                          WHERE obl.OrderCanonicalId IN @OcIds AND obl.PlanVersionId = @PvId",
                        new { OcIds = chunk, PvId = request.PlanVersionId }, db: DatabaseId.APS));
                var orderIdByOcId = orderRows
                    .ToDictionary(r => (long)r.OrderCanonicalId, r => (long)r.OrderId);

                // DueDateOverride 的键是 LogicalDemandKey（Run 内语义，非跨版本比对键）。
                // 跨版本比对键统一用 DemandKey（1号位 回执 20260923 §2.4），此变量勿与 DemandKey 字段混淆。
                var logicalDemandKeyByOrderId = voucher.LogicalProductionDemands
                    .Where(d => d.OrderId.HasValue)
                    .GroupBy(d => d.OrderId!.Value)
                    .ToDictionary(g => g.Key, g => g.First().LogicalDemandKey);

                var list = new List<DueDateOverride>(targets.Count);
                foreach (var t in targets)
                {
                    if (orderIdByOcId.TryGetValue(t.OrderCanonicalId, out var orderId)
                        && logicalDemandKeyByOrderId.TryGetValue(orderId, out var logicalDemandKey))
                    {
                        list.Add(new DueDateOverride
                        {
                            LogicalDemandKey = logicalDemandKey,
                            ManualTargetDueDate = t.ManualTargetDueDate!.Value
                        });
                    }
                }
                dueDateOverrides = list;
            }
        }

        // TaskTargetOverrides：TaskId → (DraftId, OperationCode)（Pegging 结果回查）
        IReadOnlyList<TaskTargetOverride> taskTargetOverrides = Array.Empty<TaskTargetOverride>();
        if (scope.TaskTargets is { Count: > 0 } && basePlanVersionId.HasValue)
        {
            var taskIds = scope.TaskTargets.Select(t => t.TaskId).Distinct().ToList();
            // 分片：`taskIds` = ScopeJson.TaskTargets 的人工指定任务（批量调整可上千）⇒ 同 DueDateOverride，走统一帮手。
            var taskRows = await QueryChunkedInAsync(taskIds, chunk =>
                _connectionManager.QueryAsync<dynamic>(
                    @"SELECT Id, SourceDraftId, OperationCode
                      FROM [Task]
                      WHERE Id IN @Ids AND PlanVersionId = @PvId",
                    new { Ids = chunk, PvId = basePlanVersionId.Value }, db: DatabaseId.APS));
            var taskByKey = taskRows
                .ToDictionary(r => (long)r.Id, r => (
                    DraftId: (string?)r.SourceDraftId,
                    OpCode: (string?)r.OperationCode));

            var list = new List<TaskTargetOverride>(scope.TaskTargets.Count);
            var missCount = 0;
            foreach (var t in scope.TaskTargets)
            {
                if (taskByKey.TryGetValue(t.TaskId, out var row))
                {
                    list.Add(new TaskTargetOverride
                    {
                        DraftId = row.DraftId ?? string.Empty,
                        OperationCode = row.OpCode ?? string.Empty,
                        TargetTime = t.TargetTime
                    });
                }
                else
                {
                    // 3号位 协助项（2026-09-21）：TaskId 未命中 (DraftId, OperationCode) 时不再静默跳过，
                    // 计数告警（TaskId 自增键漂移/归档会导致软目标静默丢失，车间无感）。
                    missCount++;
                }
            }
            if (missCount > 0)
            {
                _logger.LogWarning(
                    "[Pegging][ScopeJsonV2] TaskTargets 投影未命中 {MissCount} 行（TaskId 漂移或已归档，软目标未下达 Solver）",
                    missCount);
            }
            taskTargetOverrides = list;
        }

        return new RunScope
        {
            Trigger = scope.Trigger,
            PriorityMode = scope.PriorityMode,
            InScopeLogicalDemandKeys = Array.Empty<string>(),  // 冻结 v1.7 §69：影响范围由 1号位 从变化 Seed 动态传播推导；2号位 只转 ChangeSeed/TaskTarget/ChangedResourceIds，不预填可移动集合
            DueDateOverrides = dueDateOverrides,
            TaskTargetOverrides = taskTargetOverrides,
            ChangedResourceIds = scope.ChangedResourceIds ?? Array.Empty<int>(),
            // BusinessScope：3号位 生成 → 2号位 原样透传（不判权限）；null 交 1号位 无授权 fail-safe。
            BusinessScope = scope.BusinessScope
        };
    }

    private IReadOnlyList<ResourceDefinition> BuildResourceDefinitions(Core.Models.Scheduling.SchedulingContext? context)
    {
        if (context == null || context.Resources.Count == 0)
            return Array.Empty<ResourceDefinition>();

        var resources = new List<ResourceDefinition>();

        foreach (var res in context.Resources)
        {
            resources.Add(new ResourceDefinition
            {
                ResourceId = int.TryParse(res.ResourceId, out var rid) ? rid : 0,
                ResourceCode = res.ResourceCode,
                FactoryCode = res.FactoryId,
                Capacity = res.CapacityFactor
            });
        }

        return resources;
    }

    private IReadOnlyList<ResourceCalendarSlot> BuildResourceCalendarSlots(Core.Models.Scheduling.SchedulingContext? context)
    {
        if (context == null || context.ResourceCalendars.Count == 0)
            return Array.Empty<ResourceCalendarSlot>();

        var slots = new List<ResourceCalendarSlot>();

        foreach (var (resourceIdStr, timeWindows) in context.ResourceCalendars)
        {
            if (!int.TryParse(resourceIdStr, out var resourceId))
                continue;

            foreach (var window in timeWindows)
            {
                slots.Add(new ResourceCalendarSlot
                {
                    ResourceId = resourceId,
                    Start = window.Start,
                    End = window.End,
                    IsAvailable = true
                });
            }
        }

        return slots;
    }
}
