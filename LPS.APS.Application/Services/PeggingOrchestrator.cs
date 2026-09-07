using System.Data;
using System.Diagnostics;
using Dapper;
using LPS.APS.Core.Dto;
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
    private readonly CrossFactoryPeggingHandler _crossFactoryPeggingHandler;

    public PeggingOrchestrator(
        IDemandSupplyHardLockRepository lockRepo,
        DatabaseConnectionManager connectionManager,
        ILogger<PeggingOrchestrator> logger,
        ILoggerFactory loggerFactory,
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
        _crossFactoryPeggingHandler = new CrossFactoryPeggingHandler(
            loggerFactory?.CreateLogger<CrossFactoryPeggingHandler>()
            ?? throw new ArgumentNullException(nameof(loggerFactory)));
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

            var supplyPool = await LoadSupplyPoolAsync(request, frozenSnapshot, cancellationToken);
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

            // v5.1.2架构整改：不再预先生成TaskDrafts，改为传递LogicalProductionDemands给1号位
            // 1号位基于LogicalProductionDemands生成FinalTasks（含拆批/合批决策）
            _logger.LogInformation("[Pegging] 准备传递LogicalProductionDemands给Solver: {Count} 个",
                voucher.LogicalProductionDemands.Count);

            // ── 装载 Routing 三件套 + 部门归属上下文（PM 裁定：最小 B）──
            // 2号位裁剪当前 Domain 所需的 (MaterialId, StageCode) Context 与 Routing 三件套一并传入 1号位；
            // 1号位按 (MaterialId, StageCode) → ProductionDepartmentId 锁定部门后过滤三件套（不得重新推导部门）。
            var demandMaterialIds = voucher.LogicalProductionDemands
                .Select(d => d.MaterialId)
                .Distinct()
                .ToList();

            var (routingOperations, routingDependencies, operationResourceEligibility) =
                await LoadRoutingContextAsync(demandMaterialIds, cancellationToken);

            var materialStageDeptContexts =
                await LoadMaterialStageDeptContextAsync(demandMaterialIds, cancellationToken);

            if (demandMaterialIds.Count > 0 && routingOperations.Count == 0)
            {
                _logger.LogWarning(
                    "[Pegging] Routing 三件套为空（需求物料数={MaterialCount}），1号位将把所有新增生产需求判定为 Unscheduled；请确认 routing-sync（00:25）已灌入数据",
                    demandMaterialIds.Count);
            }

            // ── 求解参数：由冻结 SolverStrategy 块投影，不再硬编码（Q9 修复，2026-09-04）──
            // Mode → Direction 1:1；AllowSplit 由 Split.MaxOptimizationSplitCount>1 推；
            // AllowMerge / MaxIterations 由 3号位 补源字段直接投影（2026-09-04 回执认可口径，零行为变化）。
            var solverStrategy = frozenSnapshot.SolverStrategy;
            var schedulingDirection = solverStrategy.Mode switch
            {
                SolverStrategyMode.Forward  => "FORWARD",
                SolverStrategyMode.Backward => "BACKWARD",
                SolverStrategyMode.Mixed    => "MIXED",
                _                            => "BACKWARD"
            };

            var solveRequest = new DomainSolveRequest
            {
                ScheduleRunId = request.SchedulingContext?.ScheduleRunId,
                PlanVersionId = request.PlanVersionId,
                DomainKey     = request.DomainKey,
                DataCutoffTime = request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt,
                PlanningStart = request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt,
                PlanningEnd   = request.FrozenWindowEnd == default ? DateTime.Now.AddDays(90) : request.FrozenWindowEnd,

                LogicalProductionDemands = voucher.LogicalProductionDemands,
                AllocationLineage = BuildAllocationLineage(voucher),

                RoutingOperations = routingOperations,
                RoutingDependencies = routingDependencies,
                OperationResourceEligibility = operationResourceEligibility,
                MaterialStageDepartmentContexts = materialStageDeptContexts,

                MaterialConstraints = BuildMaterialConstraints(voucher),

                Resources     = BuildResourceDefinitions(request.SchedulingContext),
                CalendarSlots = BuildResourceCalendarSlots(request.SchedulingContext),
                ResourceEligibility = Array.Empty<ResourceEligibilityDefinition>(),
                ExecutionConstraints = Array.Empty<ExecutionConstraint>(),

                StrategySnapshot = new SolverStrategySnapshot
                {
                    StrategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId,
                    ParameterSetVersionId = frozenSnapshot.ParameterSetVersionId,
                    Parameters = new FiniteCapacityParameters
                    {
                        AllowSplit = solverStrategy.Split.MaxOptimizationSplitCount > 1,
                        AllowMerge = solverStrategy.AllowMerge,
                        MaxIterations = solverStrategy.MaxIterations,
                        SchedulingDirection = schedulingDirection
                    }
                },

                CandidateContext = null,

                // FULL §9：前序 Domain 成功后的共享 Resource 占用块 → 1号位 作为不可用时间窗
                UpstreamDomainResourceBlocks = request.UpstreamResourceBlocks ?? Array.Empty<ResourceBlock>()
            };
            // 阶段3前：Pegging 阶段耗时（供给装载 + BOM 遍历扣减 + Routing 装载 + 请求构建）
            var peggingMs = sw.ElapsedMilliseconds;

            var solverSw = System.Diagnostics.Stopwatch.StartNew();
            var solveResult = await _scheduler.SolveAsync(solveRequest, cancellationToken);
            solverSw.Stop();
            Console.WriteLine($"[PeggingOrchestrator] IFiniteCapacityScheduler.SolveAsync完成: FinalTasks={solveResult.FinalTasks?.Count ?? 0}, Success={solveResult.Success}");

            var persistSw = System.Diagnostics.Stopwatch.StartNew();
            (result.GeneratedTasks, result.PhysicalPeggingCount, result.SupplyAllocationCount) =
                await PersistDomainAndPeggingInTransactionAsync(
                    request.PlanVersionId, voucher, solveResult, cancellationToken);
            persistSw.Stop();
            _logger.LogInformation(
                "[Pegging] 统一事务落库: Task={Tasks}, Pegging={Pegging}, SupplyAllocation={Alloc}",
                result.GeneratedTasks.Count, result.PhysicalPeggingCount, result.SupplyAllocationCount);

            sw.Stop();
            result.IsSuccess      = true;
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
    public async System.Threading.Tasks.Task<IEnumerable<PeggingOrchestrationResult>> ExecuteBatchPeggingWorkflowAsync(
        PeggingExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        const int batchSize = 20;
        var results = new List<PeggingOrchestrationResult>();

        foreach (var batch in request.OrderIds.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchRequest = new PeggingExecutionRequest
            {
                PlanVersionId     = request.PlanVersionId,
                DomainKey         = request.DomainKey,
                OrderIds          = batch.ToList(),
                SnapshotAt        = request.SnapshotAt,
                FrozenWindowStart = request.FrozenWindowStart,
                FrozenWindowEnd   = request.FrozenWindowEnd,
                AllowCrossFactory = request.AllowCrossFactory,
                CrossFactoryMode  = request.CrossFactoryMode,
                DefaultStrategy   = request.DefaultStrategy,
                ProductFamilyIds  = request.ProductFamilyIds,
                UpstreamResourceBlocks = request.UpstreamResourceBlocks,
                MaxBomDepth       = request.MaxBomDepth,
                TimeoutSeconds    = request.TimeoutSeconds,
                ExecutionMode     = request.ExecutionMode,
                // V1.2：完整传递沙盘上下文（含 StrategyProfileVersionId），供下游 DemandPriority 守卫/1号位使用；
                // 缺失会被 BuildDemandSequenceMapAsync 判定为"策略上下文不完整"而抛异常（P0-05 联调回归）
                SchedulingContext = request.SchedulingContext
            };

            results.Add(await ExecutePeggingWorkflowAsync(batchRequest, cancellationToken));
        }

        return results;
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

                foreach (var final in solveResult.FinalTasks)
                {
                    ct.ThrowIfCancellationRequested();

                    var taskNo = $"PEGG-{planVersionId}-{final.FinalDraftId[..8]}";
                    var ids = await conn.QueryAsync<long>(
                        @"INSERT INTO [Task] (
                              PlanVersionId, TaskNo, OrderId, MaterialId,
                              OperationSeq, OperationCode,
                              Quantity, UOM, PlannedStartTime, PlannedEndTime,
                              Status, IsLocked, IsCriticalPath, TaskType,
                              CreatedAt, UpdatedAt
                          )
                          OUTPUT INSERTED.Id
                          VALUES (
                              @PlanVersionId, @TaskNo, @OrderId, @MaterialId,
                              @OperationSeq, @OperationCode,
                              @Quantity, @UOM, @PlannedStartTime, @PlannedEndTime,
                              @Status, @IsLocked, @IsCriticalPath, @TaskType,
                              @CreatedAt, @UpdatedAt
                          )",
                        new
                        {
                            PlanVersionId    = planVersionId,
                            TaskNo           = taskNo,
                            OrderId          = voucher.OrderId,
                            MaterialId       = final.MaterialId,
                            OperationSeq     = 0,
                            OperationCode    = final.OperationCode,
                            Quantity         = final.Quantity,
                            UOM              = final.UOM,
                            PlannedStartTime = final.PlannedStartTime,
                            PlannedEndTime   = final.PlannedEndTime,
                            Status           = "PLANNED",
                            IsLocked         = false,
                            IsCriticalPath   = false,
                            TaskType         = final.TaskType,
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
                        OperationSeq     = 0,
                        OperationCode    = final.OperationCode,
                        RouteCode        = "DEFAULT",
                        PathId           = 1,
                        Quantity         = final.Quantity,
                        UOM              = final.UOM,
                        PlannedStartTime = final.PlannedStartTime,
                        PlannedEndTime   = final.PlannedEndTime,
                        Status           = "PLANNED",
                        IsLocked         = false,
                        IsCriticalPath   = false,
                        TaskType         = final.TaskType,
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
                var seqToShareId = new Dictionary<long, long>();
                if (solveResult.AllocationShares.Count > 0)
                {
                    var orderCanonicalMap = (await conn.QueryAsync(
                        "SELECT DISTINCT OrderId, OrderCanonicalId FROM OrderBomRequestLink WHERE PlanVersionId = @PlanVersionId",
                        new { PlanVersionId = planVersionId },
                        transaction: tx))
                        .ToDictionary(r => (long)r.OrderId, r => (long)r.OrderCanonicalId);

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

                        orderCanonicalMap.TryGetValue(voucher.OrderId, out var rootOrderId);

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
                                DemandKey = rootOrderId.ToString(),
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

                    var orderCanonicalMap = (await conn.QueryAsync(
                        "SELECT DISTINCT OrderId, OrderCanonicalId FROM OrderBomRequestLink WHERE PlanVersionId = @PlanVersionId",
                        new { PlanVersionId = planVersionId },
                        transaction: tx))
                        .ToDictionary(r => (long)r.OrderId, r => (long)r.OrderCanonicalId);

                    var materialMap = (await conn.QueryAsync(
                        "SELECT Id, MaterialCode FROM Material WHERE Id IN @Ids",
                        new { Ids = nonTaskAllocations.Select(a => a.SupplyMaterialId).Distinct() },
                        transaction: tx))
                        .ToDictionary(r => (int)r.Id, r => (string)r.MaterialCode);

                    orderCanonicalMap.TryGetValue(voucher.OrderId, out var rootOrderId);

                    var supplyRows = nonTaskAllocations
                        .Select(a =>
                        {
                            materialMap.TryGetValue(a.SupplyMaterialId, out var materialCode);
                            return new
                            {
                                PlanVersionId          = planVersionId,
                                ScheduleRunId          = scheduleRunId,
                                AllocationSequence     = a.AllocationSequence,
                                RootOrderId            = rootOrderId,
                                MaterialId             = a.SupplyMaterialId,
                                MaterialCode           = materialCode ?? string.Empty,
                                DemandFactoryCode      = a.FactoryCode,
                                DemandQty              = voucher.DemandQuantity,
                                AllocatedQty           = a.AllocatedQuantity,
                                SupplyType             = a.SourceType.ToString(),
                                SupplyFactoryCode      = a.FactoryCode,
                                KnownAvailableTime     = a.AvailableAt,
                                SupplyDocumentNo       = a.SourceReference,
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
                              KnownAvailableTime, SupplyDocumentNo, CreatedAt
                          ) VALUES (
                              @PlanVersionId, @ScheduleRunId, @AllocationSequence,
                              @RootOrderId, @MaterialId, @MaterialCode,
                              @DemandFactoryCode, @DemandQty, @AllocatedQty,
                              @SupplyType, @SupplyFactoryCode,
                              @KnownAvailableTime, @SupplyDocumentNo, @CreatedAt
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
    /// ⑤⑥ SH 读 ShippingInstructionNo（INTER_FACTORY_ORDER 分配落 SH No）+ 池内 SH 段 OriginalQty。
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

        // 5/6. SH 同 SH 匹配不串 SH / 份额不重复计量：读 ShippingInstructionNo（SH 分配落 SH No）+ 池内 SH 段总量
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
    /// ⑥ 同一 SH 分配合计不得超过其已实际发生总量（Transit+Received，shipmentTotals）。
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
        int EdgeCount);

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
        public IReadOnlyList<SupplyLedgerEntry> GetEntries(string materialCode, int factoryId, bool isPurchased, bool includeInventory)
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
                ordered.AddRange(SortPi(list));
                // 跨 Domain：上游域生产输出作为分段虚拟供给（§8/D12），按可用时间升序
                ordered.AddRange(SortUpstreamDomainProduction(list));
            }
            return ordered;
        }

        /// <summary>
        /// INTER_FACTORY_ORDER（厂间出荷指示，SH级）供给：按 AvailableAt 升序（Received 先于 Transit）。
        /// 不进入 GetEntries 三类通用排序（PM 裁决：跨厂 Transit/Received 绑定消费，走独立消费路径）。
        /// 每个 SH 是单一供给身份（PhysicalSourceKey=SH No），Received/Transit 是其履行状态的两段。
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
        public decimal RemainingQty           { get; set; }
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

    /// <summary>
    /// 强事实（Received）行：ext_ERP_Received_ByDocument_View。
    /// DocumentType 为 varchar(6)，ODS 会把 'UNKNOWN' 截断成 'UNKNOW'，装载层归一回 'UNKNOWN'。
    /// </summary>
    private sealed class ReceivedLoadRow
    {
        public string MaterialCode  { get; set; } = string.Empty;
        public string FactoryCode   { get; set; } = string.Empty;
        public string WarehouseCode { get; set; } = string.Empty;
        public string DocumentType  { get; set; } = string.Empty;
        public string DocumentNo    { get; set; } = string.Empty;
        public decimal ReceivedQty  { get; set; }
        public DateTime LastReceivedAt { get; set; }
        public string? StageCode    { get; set; }
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
    }

    /// <summary>厂间出荷指示（SH）级 Transit 行：SourceDocumentNo 以 O 前缀标识出荷指示级在途。</summary>
    private sealed class InterFactoryShipmentTransitRow
    {
        public string  ShipmentNo        { get; set; } = string.Empty; // = SourceDocumentNo（O前缀）
        public string  MaterialCode      { get; set; } = string.Empty;
        public int     MaterialId        { get; set; }
        public string  TargetFactoryCode { get; set; } = string.Empty; // 到货厂
        public int     TargetFactoryId   { get; set; }
        public string  SourceFactoryCode { get; set; } = string.Empty; // 发货厂
        public decimal Quantity          { get; set; }
        public DateTime? Eta             { get; set; }
    }

    /// <summary>厂间出荷指示（SH）级 Received 行：DocumentType=SHIPPING_INSTRUCTION 的到货单。</summary>
    private sealed class InterFactoryShipmentReceivedRow
    {
        public string  ShipmentNo        { get; set; } = string.Empty; // = DocumentNo
        public string  MaterialCode      { get; set; } = string.Empty;
        public int     MaterialId        { get; set; }
        public string  TargetFactoryCode { get; set; } = string.Empty;
        public int     TargetFactoryId   { get; set; }
        public decimal Quantity          { get; set; }
        public DateTime ReceivedAt       { get; set; }
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

    // ─────────────────────────────────────────────────────────────────────────
    // 供给池装载
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 装载供给池（INVENTORY + PIPELINE + WIP）
    /// </summary>
    private async Task<SupplyPool> LoadSupplyPoolAsync(
        PeggingExecutionRequest request,
        FrozenStrategySnapshot frozenSnapshot,
        CancellationToken ct)
    {
        var pool    = new SupplyPool(frozenSnapshot);
        var cutoff  = request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt;

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

        // 采购/定时供给：切 5号位 ITimedSupplyFactLoader 读原始事实（Eta/ReleaseDate，AvailableTime 留 2号位算），
        // 再由 2号位用 AvailableTimeCalculator（EtaInvariant 三级链 + ArrivalToUsableOffset）内存计算 AvailableTime（阶段 3，
        // 不再直读 sfp.AvailableTime）。
        // 注意：ITimedSupplyFactLoader 白名单已排除 INTERPLANT_IN_TRANSIT（仅 PURCHASE_IN_TRANSIT /
        // OPEN_PO_REMAINING / ARRIVED_NOT_RECEIVED），厂间在途不从此装载入池。
        // 厂间在途分两类（PM 2026-09-01）：PI 级 Transit = PI Position（LoadTransitFactsAsync →
        // ProductionInstructionPositionCalculator）；SH 级 Transit/Received 走 CrossFactoryPeggingHandler
        // .ConsumeInterFactoryShipment（SH 履行闭合）。两类均待 5号位真实事实就绪后正式启用。
        var rawFacts = await _timedSupplyFactLoader.LoadRawFactsAsync(
            new SupplyFactScope { DataCutoffTime = cutoff }, ct);

        Dictionary<(string, int, int, string), DateTime> manualEtaMap = new();
        if (rawFacts.Count > 0)
        {
            var materialIds = rawFacts.Select(f => f.MaterialId).Distinct().ToList();
            var poNos = rawFacts.Select(f => f.SourceDocumentNo)
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .Distinct()
                                .ToList();
            var manualEtaOverrides = await _procurementManualEtaRepo.QueryAsync(
                materialIds: materialIds,
                poNos: poNos,
                activeOnly: true,
                ct: ct);
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

        // ── WIP（生产指示）供给：PI Position 两级装载（§8）──
        // PM 2026-08-28 最终裁决（STAGE_HANDOFF 场景）：先选 PI → 再消费该 PI 内部 PI Position。
        // 旧实现把 StageProgressSnapshot 逐 Stage 行当独立 WIP 供给（同一 PI 跨 Stage 被重复入池）；改为：
        //   1) 读 Stage 明细（StageCode/GoodCompletedQty/RemainingQty），按 PI 聚合取 PI 级 RemainingQty；
        //   2) 调 5号位 IProductionInstructionPositionCalculator 把 RemainingQty 拆成
        //      located（STAGE/XC/TRANSIT/WAITING，可承接供给）与 unlocated（位置不明，须走新增生产）；
        //   3) 仅 located 入池；unlocated 不入池，由 TraverseBomNode 自然落入 NEW_REQUIREMENT 缺口。
        var wipStageRows = (await _connectionManager.QueryAsync<WipStageLoadRow>(
            @"SELECT sp.ProductionInstructionNo,
                     sp.MaterialCode,
                     m.Id             AS MaterialId,
                     t.FactoryId      AS FactoryId,
                     f.Code           AS FactoryCode,
                     sp.StageCode,
                     sp.GoodCompletedQty,
                     sp.RemainingQty
              FROM StageProgressSnapshot sp
              INNER JOIN Material m ON m.MaterialCode = sp.MaterialCode
              INNER JOIN (
                  SELECT DISTINCT t.MTS_InstructionNo, o.FactoryId
                  FROM [Task] t
                  INNER JOIN [Order] o ON o.Id = t.OrderId
                  WHERE t.PlanVersionId = @PlanVersionId
                    AND t.MTS_InstructionNo IS NOT NULL
              ) t ON t.MTS_InstructionNo = sp.ProductionInstructionNo
              INNER JOIN Factory f ON f.Id = t.FactoryId
              WHERE sp.ScheduleRunId = (
                  SELECT TOP 1 pv.SourceScheduleRunId
                  FROM PlanVersion pv
                  WHERE pv.Id = @PlanVersionId
              )
              AND sp.RemainingQty > 0",
            new { request.PlanVersionId },
            db: DatabaseId.APS)).ToList();

        var piPositions = await LoadPiPositionsAsync(wipStageRows, frozenSnapshot, request.PlanVersionId, ct);

        foreach (var group in wipStageRows.GroupBy(r => r.ProductionInstructionNo))
        {
            var first = group.First();
            var erpRemainingQty = group.Max(r => r.RemainingQty); // PI 级 RemainingQty（Stage 行冗余，取 MAX 去重）
            var availableQty = erpRemainingQty;

            // 有成功 Position 结果时按 located 拆分：仅已定位份额承接供给，未定位份额落新增生产。
            if (piPositions.TryGetValue(first.ProductionInstructionNo, out var pos) && pos.IsSuccess)
            {
                var locatedQty = pos.Positions.Where(p => !p.IsUnlocated).Sum(p => p.Quantity);
                if (locatedQty <= 0m)
                    continue; // 全部 unlocated → 无可用供给，整体走新增生产
                availableQty = Math.Min(erpRemainingQty, locatedQty);
            }

            pool.Add(first.MaterialCode, first.MaterialId, first.FactoryId, availableQty,
                     null, Core.Enum.SupplySourceType.WIP,
                     first.ProductionInstructionNo, first.FactoryCode,
                     sort: new SupplySortFacts(PiNo: first.ProductionInstructionNo),
                     physicalSourceKey: first.ProductionInstructionNo);
        }

        // ── INTER_FACTORY_ORDER（厂间出荷指示，SH级）供给：SH 单一身份，Transit/Received 两段 ──
        // PM 裁决（§七.2）：SH 保持单一 Supply 身份，Transit/Received 是履行状态；禁止拆成独立 Transit/Received Supply。
        // 每 SH 拆两段入池（Received@到货时间、Transit@ETA），PhysicalSourceKey 统一 = SH No，由红线③⑥ 防重复计量。
        // V1 口径：SH 主档（出荷指示总量）未接，shipmentRemainingQty 暂取 Transit+Received 已实际发生部分 → unproduced=0；
        // 接入主档后 unproduced>0 即触发源厂生产需求（下一步，见台账）。
        var shipments = await LoadInterFactoryShipmentsAsync(ct);
        foreach (var s in shipments)
        {
            var transit = s.TransitQty > 0m
                ? new[] { new SupplyFact { SupplyType = "INTERPLANT_IN_TRANSIT", SourceKey = s.ShipmentNo, AvailableQuantity = s.TransitQty } }
                : Array.Empty<SupplyFact>();
            var received = s.ReceivedQty > 0m
                ? new[] { new SupplyFact { SupplyType = "INTER_FACTORY_RECEIVED", SourceKey = s.ShipmentNo, AvailableQuantity = s.ReceivedQty } }
                : Array.Empty<SupplyFact>();

            var consumption = _crossFactoryPeggingHandler.ConsumeInterFactoryShipment(
                s.ShipmentNo, s.TransitQty + s.ReceivedQty, transit, received);

            if (consumption.ConsumedReceivedQty > 0m)
                pool.Add(s.MaterialCode, s.MaterialId, s.TargetFactoryId, consumption.ConsumedReceivedQty,
                         s.ReceivedAt, Core.Enum.SupplySourceType.INTER_FACTORY_ORDER,
                         $"{s.ShipmentNo}#RECEIVED", s.TargetFactoryCode,
                         physicalSourceKey: s.ShipmentNo);

            if (consumption.ConsumedTransitQty > 0m)
                pool.Add(s.MaterialCode, s.MaterialId, s.TargetFactoryId, consumption.ConsumedTransitQty,
                         s.TransitEta, Core.Enum.SupplySourceType.INTER_FACTORY_ORDER,
                         $"{s.ShipmentNo}#TRANSIT", s.TargetFactoryCode,
                         physicalSourceKey: s.ShipmentNo);
        }

        _logger.LogDebug(
            "[Pegging] 供给池明细: INVENTORY={Inv}, WIP(PI)={Wip}, PIPELINE={Pipe}, PI Position={PiPos}, SH={Sh}",
            inventoryRows.Count(), wipStageRows.Select(r => r.ProductionInstructionNo).Distinct().Count(),
            rawFacts.Count, piPositions.Count, shipments.Count);

        // 跨 Domain Quantity-Time（§8/D12）：注入上游域生产输出为分段虚拟供给
        await LoadUpstreamDomainSupplyAsync(pool, request, ct);

        await LoadActiveLockDataAsync(pool, ct);

        return pool;
    }

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

    /// <summary>
    /// 装载 PI Position（消费 5号位 IProductionInstructionPositionCalculator）。返回 ProductionInstructionNo → 结果。
    /// 事实范围（V1 最小集）：Stage 进度（StageProgressSnapshot）→ StageProgressFact；
    /// Stage 顺序取 APS_BOM_STAGE_PATH_RAW（ChildMaterialCode+StageCode → StageSeq，多批次/BOMNO/Scope 取 MIN，近似）；
    /// PiInventory 已绑定（ext_ERP_Inventory_View × ext_MES_ProcessCode_View）。
    /// PiInventory / XC / CrossFactoryEdge / Received / Transit 已绑定（ext_ 同义词 Loader，Transit 实测 0 行）；
    /// 计算器按 UNLOCATED 兜底闭合，2号位据此把未定位份额路由到新增生产。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ProductionInstructionPositionResult>> LoadPiPositionsAsync(
        IReadOnlyList<WipStageLoadRow> wipStageRows,
        FrozenStrategySnapshot frozenSnapshot,
        int planVersionId,
        CancellationToken ct)
    {
        if (wipStageRows.Count == 0)
            return new Dictionary<string, ProductionInstructionPositionResult>();

        // 1) Stage 顺序映射：ChildMaterialCode+StageCode → StageSeq（MIN 去重多 BOMNO/Scope）
        var materialCodes = wipStageRows.Select(r => r.MaterialCode).Distinct().ToList();
        var stagePathRows = await _connectionManager.QueryAsync<StagePathLoadRow>(
            @"SELECT ChildMaterialCode, StageCode, MIN(StageSeq) AS StageSeq
              FROM APS_BOM_STAGE_PATH_RAW
              WHERE ChildMaterialCode IN @MaterialCodes
              GROUP BY ChildMaterialCode, StageCode",
            new { MaterialCodes = materialCodes },
            db: DatabaseId.APS);

        var stageSeqMap = new Dictionary<(string MaterialCode, string StageCode), int>();
        foreach (var p in stagePathRows)
            stageSeqMap[(p.ChildMaterialCode, p.StageCode)] = p.StageSeq;

        // 1.5) PI 级库存事实（ext_ 同义词：ERP_Inventory_View.WarehouseCode=6位工序码 → MES_ProcessCode_View.StageCode）
        var piInventoryMap = await LoadPiInventoryFactsAsync(wipStageRows, ct);

        // 1.6) XC（线边仓）事实（ERPProperty='XC' × 库存）
        var xcMap = await LoadXcFactsAsync(wipStageRows, ct);

        // 1.7) 跨厂边事实（ext_MES_APS_BOM_Workset_CrossFactoryEdge，按 ChildMaterialCode 归组）
        var crossFactoryEdgeMap = await LoadCrossFactoryEdgesAsync(wipStageRows, ct);

        // 1.8) 强事实（Received，ext_ERP_Received_ByDocument_View）
        var strongFactMap = await LoadReceivedFactsAsync(wipStageRows, ct);

        // 1.9) 厂间在途事实（Transit，ext_ERP_InterplantInTransit_View；0 行，待 5号位 ODS 数据）
        var transitMap = await LoadTransitFactsAsync(wipStageRows, ct);

        // 2) 按 PI 分组构建输入
        var inputs = new List<ProductionInstructionPositionInput>();
        foreach (var group in wipStageRows.GroupBy(r => r.ProductionInstructionNo))
        {
            var first = group.First();
            var stageProgress = new List<StageProgressFact>(group.Count());
            foreach (var s in group)
            {
                stageProgress.Add(new StageProgressFact
                {
                    StageCode = s.StageCode,
                    CumulativeCompletedQty = s.GoodCompletedQty,
                    StageSequence = stageSeqMap.TryGetValue((s.MaterialCode, s.StageCode), out var seq) ? seq : 0,
                    SnapshotId = null
                });
            }

            var key = (first.MaterialCode, first.FactoryCode);
            piInventoryMap.TryGetValue(key, out var piInventories);
            xcMap.TryGetValue(key, out var xcFacts);
            crossFactoryEdgeMap.TryGetValue(first.MaterialCode, out var crossFactoryEdges);
            strongFactMap.TryGetValue(key, out var strongFacts);
            transitMap.TryGetValue(key, out var transitFacts);

            inputs.Add(new ProductionInstructionPositionInput
            {
                ProductionInstructionNo = first.ProductionInstructionNo,
                MaterialId = first.MaterialId,
                FactoryId = first.FactoryId,
                ErpRemainingQty = group.Max(r => r.RemainingQty),
                StageProgress = stageProgress,
                PiInventories = piInventories ?? (IReadOnlyList<PiInventoryFact>)Array.Empty<PiInventoryFact>(),
                XcFacts = xcFacts ?? (IReadOnlyList<XcFact>)Array.Empty<XcFact>(),
                CrossFactoryEdges = crossFactoryEdges ?? (IReadOnlyList<CrossFactoryEdgeFact>)Array.Empty<CrossFactoryEdgeFact>(),
                StrongFacts = strongFacts ?? (IReadOnlyList<ReceivedFact>)Array.Empty<ReceivedFact>(),
                TransitFacts = transitFacts ?? (IReadOnlyList<InterplantTransitFact>)Array.Empty<InterplantTransitFact>()
            });
        }

        // 3) 调 5号位计算器（纯计算，无 I/O）
        var parameters = BuildFrozenFactParameters(frozenSnapshot);
        var results = await _piPositionCalculator.CalculateProductionInstructionPositionsAsync(inputs, parameters, ct);

        // 4) 保存 PI Position 快照 + 数量闭环校验（2号位职责，不修正 5号位 事实）
        await SavePiPositionSnapshotsAsync(planVersionId, inputs, results, wipStageRows, ct);

        return results.ToDictionary(r => r.ProductionInstructionNo, StringComparer.Ordinal);
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
        IReadOnlyList<WipStageLoadRow> wipStageRows,
        CancellationToken ct)
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
        var materialCodeByPi = wipStageRows
            .GroupBy(r => r.ProductionInstructionNo)
            .ToDictionary(g => g.Key, g => g.First().MaterialCode, StringComparer.Ordinal);

        var (rows, issues) = MapPositionSnapshots(scheduleRunId, planVersionId, inputs, resultsByPi, materialCodeByPi);

        foreach (var issue in issues)
            _logger.LogWarning("[Pegging] PI Position 闭环异常: {Issue}", issue);

        if (rows.Count > 0)
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

        var rows = (await _connectionManager.QueryAsync<PiInventoryLoadRow>(
            @"SELECT inv.MaterialCode,
                     inv.FactoryCode,
                     inv.WarehouseCode,
                     inv.Quantity,
                     pc.StageCode
              FROM ext_ERP_Inventory_View inv
              LEFT JOIN ext_MES_ProcessCode_View pc
                ON pc.ProcessCode = inv.WarehouseCode
              WHERE inv.IsActive = 1
                AND inv.Quantity > 0
                AND inv.MaterialCode IN @MaterialCodes
                AND inv.FactoryCode  IN @FactoryCodes",
            new { MaterialCodes = materialCodes, FactoryCodes = factoryCodes },
            db: DatabaseId.APS)).ToList();

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

        var rows = (await _connectionManager.QueryAsync<XcLoadRow>(
            @"SELECT inv.MaterialCode,
                     inv.FactoryCode,
                     inv.WarehouseCode,
                     inv.Quantity,
                     pc.StageCode
              FROM ext_ERP_Inventory_View inv
              INNER JOIN ext_MES_ProcessCode_View pc
                ON pc.ProcessCode = inv.WarehouseCode
               AND pc.ERPProperty = 'XC'
              WHERE inv.IsActive = 1
                AND inv.Quantity > 0
                AND inv.MaterialCode IN @MaterialCodes
                AND inv.FactoryCode  IN @FactoryCodes",
            new { MaterialCodes = materialCodes, FactoryCodes = factoryCodes },
            db: DatabaseId.APS)).ToList();

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

        var rows = (await _connectionManager.QueryAsync<CrossFactoryEdgeLoadRow>(
            @"SELECT ChildMaterialCode,
                     FromStageCode,
                     FromFactoryCode,
                     ToStageCode,
                     ToFactoryCode
              FROM ext_MES_APS_BOM_Workset_CrossFactoryEdge
              WHERE ChildMaterialCode IN @MaterialCodes
              ORDER BY ChildMaterialCode, BatchNo, WorksetId, Id",
            new { MaterialCodes = materialCodes },
            db: DatabaseId.APS)).ToList();

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
    /// 装载强事实（Received）。
    /// 链：ext_ERP_Received_ByDocument_View（53748 行）LEFT JOIN MES_ProcessCode_View（WarehouseCode→StageCode）。
    /// 防腐：DocumentType 为 varchar(6)，ODS 把 'UNKNOWN' 截断成 'UNKNOW'，装载层归一回 'UNKNOWN'，
    ///       使计算器走 UNKNOWN→WARN 跳过（不误扣 Stage），待 5号位把分类接对后再真正参与扣减。
    /// 返回 (MaterialCode, FactoryCode) → 事实列表，供按 PI 归属装载。
    /// </summary>
    private async Task<IReadOnlyDictionary<(string MaterialCode, string FactoryCode), List<ReceivedFact>>>
        LoadReceivedFactsAsync(IReadOnlyList<WipStageLoadRow> wipStageRows, CancellationToken ct)
    {
        var materialCodes = wipStageRows.Select(r => r.MaterialCode).Distinct().ToList();
        var factoryCodes  = wipStageRows.Select(r => r.FactoryCode).Distinct().ToList();

        var rows = (await _connectionManager.QueryAsync<ReceivedLoadRow>(
            @"SELECT rv.MaterialCode,
                     rv.FactoryCode,
                     rv.WarehouseCode,
                     rv.DocumentType,
                     rv.DocumentNo,
                     rv.ReceivedQty,
                     rv.LastReceivedAt,
                     pc.StageCode
              FROM ext_ERP_Received_ByDocument_View rv
              LEFT JOIN ext_MES_ProcessCode_View pc
                ON pc.ProcessCode = rv.WarehouseCode
              WHERE rv.IsActive = 1
                AND rv.ReceivedQty > 0
                AND rv.MaterialCode IN @MaterialCodes
                AND rv.FactoryCode  IN @FactoryCodes",
            new { MaterialCodes = materialCodes, FactoryCodes = factoryCodes },
            db: DatabaseId.APS)).ToList();

        var map = new Dictionary<(string MaterialCode, string FactoryCode), List<ReceivedFact>>();
        foreach (var row in rows)
        {
            var key = (row.MaterialCode, row.FactoryCode);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<ReceivedFact>();
                map[key] = list;
            }

            list.Add(new ReceivedFact
            {
                DocumentNo = row.DocumentNo,
                // 5号位已把 'UNKNOW' 修正为 'SHIPPING_INSTRUCTION'（列宽 varchar 20），归一不再需要；
                // 当前 DocumentType 恒为 SHIPPING_INSTRUCTION（业务源无 PI 区分字段，见台账 C 组）。
                DocumentType = row.DocumentType,
                Quantity     = row.ReceivedQty,
                ReceivedAt   = row.LastReceivedAt,
                WarehouseCode = row.WarehouseCode,
                RelatedStageCode = row.StageCode
            });
        }

        return map;
    }

    /// <summary>
    /// 装载厂间在途事实（Transit）。链：ext_ERP_InterplantInTransit_View（实测 0 行，待 5号位 ODS 数据）。
    /// 返回 (MaterialCode, 目标FactoryCode) → 事实列表，供按 PI 归属装载。
    /// 注：P 前缀（生产指示级 Transit）属 PI Position 计算范围；O 前缀（出荷指示级 Transit）属
    /// INTER_FACTORY_ORDER 跨厂订单链，待该链落地时拆分，当前一并装入（0 行无实际影响）。
    /// </summary>
    private async Task<IReadOnlyDictionary<(string MaterialCode, string FactoryCode), List<InterplantTransitFact>>>
        LoadTransitFactsAsync(IReadOnlyList<WipStageLoadRow> wipStageRows, CancellationToken ct)
    {
        var materialCodes = wipStageRows.Select(r => r.MaterialCode).Distinct().ToList();
        var factoryCodes  = wipStageRows.Select(r => r.FactoryCode).Distinct().ToList();

        var rows = (await _connectionManager.QueryAsync<TransitLoadRow>(
            @"SELECT MaterialCode,
                     FactoryCode,
                     SourceFactoryCode,
                     Quantity,
                     ETA,
                     ReleaseDate,
                     SourceDocumentNo
              FROM ext_ERP_InterplantInTransit_View
              WHERE Quantity > 0
                AND MaterialCode IN @MaterialCodes
                AND FactoryCode  IN @FactoryCodes",
            new { MaterialCodes = materialCodes, FactoryCodes = factoryCodes },
            db: DatabaseId.APS)).ToList();

        var map = new Dictionary<(string MaterialCode, string FactoryCode), List<InterplantTransitFact>>();
        foreach (var row in rows)
        {
            var key = (row.MaterialCode, row.FactoryCode);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<InterplantTransitFact>();
                map[key] = list;
            }

            list.Add(new InterplantTransitFact
            {
                TransitDocumentNo    = row.SourceDocumentNo,
                SourceFactoryCode    = row.SourceFactoryCode,
                TargetFactoryCode    = row.FactoryCode,
                Quantity             = row.Quantity,
                EstimatedArrivalTime = row.Eta,
                SourceDocument       = row.SourceDocumentNo,
                ShippedAt            = row.ReleaseDate
            });
        }

        return map;
    }

    /// <summary>厂间出荷指示（SH）聚合事实：Transit（O前缀）+ Received（SHIPPING_INSTRUCTION）按 SH No 归并。</summary>
    private sealed record InterFactoryShipmentFact(
        string ShipmentNo,
        string MaterialCode,
        int MaterialId,
        string TargetFactoryCode,
        int TargetFactoryId,
        string SourceFactoryCode,
        decimal TransitQty,
        DateTime? TransitEta,
        decimal ReceivedQty,
        DateTime? ReceivedAt);

    /// <summary>
    /// 装载厂间出荷指示（INTER_FACTORY_ORDER，SH级）事实。
    /// SH No 契约：Transit = SourceDocumentNo 以 O 前缀（出荷指示级）；Received = DocumentType=SHIPPING_INSTRUCTION 的 DocumentNo。
    /// 同一 SH 的 Transit + Received 是履行状态的两段，归并成一条 ShipmentFact（SH 单一供给身份，§七.2）。
    /// </summary>
    private async Task<IReadOnlyList<InterFactoryShipmentFact>> LoadInterFactoryShipmentsAsync(CancellationToken ct)
    {
        var transitRows = (await _connectionManager.QueryAsync<InterFactoryShipmentTransitRow>(
            @"SELECT t.SourceDocumentNo AS ShipmentNo,
                     t.MaterialCode,
                     m.Id            AS MaterialId,
                     t.FactoryCode   AS TargetFactoryCode,
                     f.Id            AS TargetFactoryId,
                     t.SourceFactoryCode,
                     t.Quantity,
                     t.ETA           AS Eta
              FROM ext_ERP_InterplantInTransit_View t
              INNER JOIN Material m ON m.MaterialCode = t.MaterialCode
              INNER JOIN Factory  f ON f.Code = t.FactoryCode
              WHERE t.Quantity > 0
                AND t.SourceDocumentNo LIKE 'O%'",
            db: DatabaseId.APS)).ToList();

        var receivedRows = (await _connectionManager.QueryAsync<InterFactoryShipmentReceivedRow>(
            @"SELECT r.DocumentNo     AS ShipmentNo,
                     r.MaterialCode,
                     m.Id             AS MaterialId,
                     r.FactoryCode    AS TargetFactoryCode,
                     f.Id             AS TargetFactoryId,
                     r.ReceivedQty    AS Quantity,
                     r.LastReceivedAt AS ReceivedAt
              FROM ext_ERP_Received_ByDocument_View r
              INNER JOIN Material m ON m.MaterialCode = r.MaterialCode
              INNER JOIN Factory  f ON f.Code = r.FactoryCode
              WHERE r.ReceivedQty > 0
                AND r.DocumentType = 'SHIPPING_INSTRUCTION'",
            db: DatabaseId.APS)).ToList();

        var shipments = new Dictionary<string, InterFactoryShipmentFact>(StringComparer.Ordinal);

        foreach (var r in transitRows)
        {
            if (!shipments.TryGetValue(r.ShipmentNo, out var s))
                shipments[r.ShipmentNo] = s = new InterFactoryShipmentFact(
                    r.ShipmentNo, r.MaterialCode, r.MaterialId, r.TargetFactoryCode,
                    r.TargetFactoryId, r.SourceFactoryCode, 0m, null, 0m, null);
            // Transit 同 SH 多行：数量累加，ETA 取首个非空（ETA 语义按单一致，V1 不细化）
            shipments[r.ShipmentNo] = s with { TransitQty = s.TransitQty + r.Quantity, TransitEta = s.TransitEta ?? r.Eta };
        }

        foreach (var r in receivedRows)
        {
            if (!shipments.TryGetValue(r.ShipmentNo, out var s))
                shipments[r.ShipmentNo] = s = new InterFactoryShipmentFact(
                    r.ShipmentNo, r.MaterialCode, r.MaterialId, r.TargetFactoryCode,
                    r.TargetFactoryId, string.Empty, 0m, null, 0m, null);
            shipments[r.ShipmentNo] = s with { ReceivedQty = s.ReceivedQty + r.Quantity, ReceivedAt = s.ReceivedAt ?? r.ReceivedAt };
        }

        return shipments.Values.ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Routing 三件套 + 部门归属上下文装载（PM 裁定：最小 B）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 按当前 Domain 涉及的 MaterialId 完整装载 Routing 三件套（不过滤部门——
    /// 部门锁定由 1号位消费 MaterialStageDepartmentContexts 时执行，见 ProductionDepartment回复.md）。
    /// 三件套缺失时 1号位将把对应新增生产需求判定为 Unscheduled。
    /// </summary>
    private async Task<(List<RoutingOperation> Operations, List<RoutingDependency> Dependencies, List<OperationResourceEligibility> Eligibility)>
        LoadRoutingContextAsync(IReadOnlyList<int> materialIds, CancellationToken ct)
    {
        if (materialIds.Count == 0)
            return (new List<RoutingOperation>(), new List<RoutingDependency>(), new List<OperationResourceEligibility>());

        // TransferBatchSize 冻结口径（DDL v5.1.4）：批量/规划参数从原 Routing 拆出到 RoutingPlanningParam，
        // 不污染工艺事实层 RoutingOperation。故此处 LEFT JOIN RoutingPlanningParam 取 TransferBatchSize，
        // 键 = (MaterialId, RouteCode, PathId, OperationCode)——RoutingPlanningParam 非部门维度（无 ProductionDepartmentId），
        // 同一工序跨部门共享同一 TransferBatchSize。无参数行时 TransferBatchSize=NULL，1号位按无阈值跳过。
        var operations = (await _connectionManager.QueryAsync<RoutingOperation>(
            @"SELECT ro.MaterialId, ro.ProductionDepartmentId, ro.RouteCode, ro.PathId, ro.OperationCode,
                     ro.OperationName, ro.ProcessType, ro.StageCode, ro.StandardDuration, ro.SetupTime,
                     rpp.TransferBatchSize, ro.IsActive
              FROM RoutingOperation ro
              LEFT JOIN RoutingPlanningParam rpp
                ON rpp.MaterialId    = ro.MaterialId
               AND rpp.RouteCode     = ro.RouteCode
               AND rpp.PathId        = ro.PathId
               AND rpp.OperationCode = ro.OperationCode
              WHERE ro.IsActive = 1 AND ro.MaterialId IN @MaterialIds",
            new { MaterialIds = materialIds },
            db: DatabaseId.APS)).ToList();

        var dependencies = (await _connectionManager.QueryAsync<RoutingDependency>(
            @"SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId,
                     FromOperationCode, ToOperationCode, DependencyType, LagTime, IsActive
              FROM RoutingDependency
              WHERE IsActive = 1 AND MaterialId IN @MaterialIds",
            new { MaterialIds = materialIds },
            db: DatabaseId.APS)).ToList();

        var eligibility = (await _connectionManager.QueryAsync<OperationResourceEligibility>(
            @"SELECT MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode,
                     ResourceId, Priority, CapacityFactor, IsPrimary, IsActive
              FROM OperationResourceEligibility
              WHERE IsActive = 1 AND MaterialId IN @MaterialIds",
            new { MaterialIds = materialIds },
            db: DatabaseId.APS)).ToList();

        return (operations, dependencies, eligibility);
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

        var rows = (await _connectionManager.QueryAsync<MaterialStageDeptContextLoadRow>(
            @"SELECT MaterialId, StageCode, DefaultProductionDepartmentId
              FROM MaterialStageDeptContext
              WHERE IsCurrent = 1 AND MaterialId IN @MaterialIds",
            new { MaterialIds = materialIds },
            db: DatabaseId.APS)).ToList();

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
    private async Task<BomSnapshot> LoadBomSnapshotAsync(
        int planVersionId,
        CancellationToken ct)
    {
        var rows = (await _connectionManager.QueryAsync<BomRawRow>(
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
              )",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS)).ToList();

        if (rows.Count == 0)
        {
            _logger.LogWarning(
                "[Pegging] APS_BOM_RAW 无数据（PlanVersionId={PlanVersionId}），BOM 快照为空",
                planVersionId);
            return new BomSnapshot(
                Enumerable.Empty<BomEdge>().ToLookup(e => e.ParentCode),
                new Dictionary<string, int>(),
                new Dictionary<string, bool>(),
                0);
        }

        var edges = rows.Select(r => new BomEdge(
            r.ParentMaterialCode,
            r.ChildMaterialCode,
            r.ChildMaterialId,
            r.Quantity,
            r.Level,
            r.IsLeaf,
            r.IsPurchased,
            r.ChildRequiredStageCode)).ToList();

        // LLC 取各物料在所有 BOM 路径中出现的最小值
        var llcByMaterial = rows
            .Where(r => r.LLC.HasValue)
            .GroupBy(r => r.ChildMaterialCode)
            .ToDictionary(g => g.Key, g => g.Min(r => r.LLC!.Value));

        // IsPurchased 按物料编码分组（每个物料只有一个IsPurchased值）
        var isPurchasedByMaterial = rows
            .GroupBy(r => r.ChildMaterialCode)
            .ToDictionary(g => g.Key, g => g.First().IsPurchased);

        return new BomSnapshot(edges.ToLookup(e => e.ParentCode), llcByMaterial, isPurchasedByMaterial, edges.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 订单装载（PeggingLoop 前置步骤）
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class OrderPeggingRow
    {
        public long     OrderId         { get; set; }
        public int      MaterialId      { get; set; }
        public string   MaterialCode    { get; set; } = string.Empty;
        public int      FactoryId       { get; set; }
        public string   FactoryCode     { get; set; } = string.Empty;
        public string?  OrderType       { get; set; }
        public string?  CustomerTier    { get; set; }
        public DateTime? IssueDate      { get; set; }
        public decimal  DemandQty       { get; set; }
        public DateTime DueDate         { get; set; }
        public string   UOM             { get; set; } = string.Empty;
        public int?     ProductFamilyId { get; set; }
    }

    private async Task<IReadOnlyList<OrderPeggingRow>> LoadOrdersForPeggingAsync(
        PeggingExecutionRequest request,
        CancellationToken ct)
    {
        var rows = await _connectionManager.QueryAsync<OrderPeggingRow>(
            @"SELECT o.Id          AS OrderId,
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
                     m.ProductFamilyId
              FROM [Order] o
              INNER JOIN Material m ON m.Id = o.MaterialId
              INNER JOIN Factory  f ON f.Id = o.FactoryId
              WHERE o.Id IN @OrderIds",
            new { request.OrderIds },
            db: DatabaseId.APS);

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

        var firstOrder = orders.FirstOrDefault();
        var voucher = new PeggingResultVoucher
        {
            PlanVersionId    = request.PlanVersionId,
            DomainKey        = request.DomainKey,
            OrderId          = firstOrder?.OrderId ?? request.OrderIds.FirstOrDefault(),
            DemandMaterialId = firstOrder?.MaterialId ?? 0,
            UOM              = firstOrder?.UOM ?? string.Empty,
            IsSuccess        = true,
            ExecutedAt       = DateTime.Now
        };

        // 按 DemandSequence 升序遍历：优先级高的订单先抢供给
        var orderedOrders = orders
            .OrderBy(o => demandSequenceByOrder.GetValueOrDefault(o.OrderId, int.MaxValue))
            .ToList();

        foreach (var order in orderedOrders)
        {
            ct.ThrowIfCancellationRequested();

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
                visited);
        }

        voucher.IsFullyAllocated = voucher.ShortageQuantity == 0;
        return voucher;
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

        // 方案A：外部按 CalculationLayer 调用 Executor —— 只对「第一层：顶层独立需求（订单）」取当前层 Segments
        const int currentCalculationLayer = 1;
        var layerConfig = new DemandPriorityConfig
        {
            Segments = config.Segments
                .Where(s => s.CalculationLayer == currentCalculationLayer)
                .ToList()
        };

        var sorted = _demandPriorityExecutor.ExecutePrioritySort(demands, layerConfig);

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
        // INTEGRATION TODO: 联调占位，V1验收前必须接入5号位规则引擎
        // 未来在此处调用：if (!ValidateEligibility(supply, demand)) return Failed(...)

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
            AllocatedAt = DateTime.UtcNow,
            RequiresProduction = supply.SourceType == Core.Enum.SupplySourceType.NEW_REQUIREMENT
        };

        supply.Allocations.Add(allocationRecord);

        // 添加到凭证的SupplyAllocations（用于持久化）
        voucher.SupplyAllocations.Add(new Core.Dto.SupplyAllocationItem
        {
            AllocationSequence = allocationSeq,
            DemandKey = demand.DemandKey,
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
            AvailableAt = supply.AvailableAt ?? DateTime.UtcNow
        });

        return AllocationResult.Succeeded(allocQty, allocationSeq, allocationRecord);
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
        int materialId,
        int factoryId,
        DateTime requiredTime,
        int demandSequence,
        PeggingResultVoucher voucher)
    {
        // LogicalDemandKey格式：PlanVersion_AllocationSeq
        var logicalDemandKey = $"{voucher.PlanVersionId}_{allocation.AllocationSequence}";

        // INTEGRATION TODO：StartStageCode从工艺路由第一道工序获取（当前简化为空，V1验收前需接入工艺路由）
        var startStageCode = string.Empty;

        // 数量双口径：
        // - NetOutputQty：净产出数量（扣除损耗后的有效产出）
        // - PlannedProcessQty：计划加工数量（含损耗的实际投入）
        // V1.2当前版本：暂不计算损耗率，两者相等，待工艺路由接入后补充
        var netOutputQty = allocation.AllocatedQty;
        var plannedProcessQty = allocation.AllocatedQty;

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
            ProductionInstructionNo = null, // 非PI类需求
            IsUnlocated = false // INTEGRATION TODO: 联调占位，V1验收前必须接入5号位PI Position实际计算结果
        };
    }

    /// <summary>
    /// 递归 BOM 节点供给扣减。
    /// 贪婪扣减成功 → 记录 SupplyAllocationItem。
    /// 有短缺 → 在当前节点创建 TaskDraft，然后：
    ///   有 BOM 子节点 → 递归子节点，子件 DraftId 填入本节点 UpstreamDraftIds。
    ///   叶子节点     → 计入 voucher.ShortageQuantity（真正无法拆解的缺口）。
    /// 返回本节点创建的 DraftId，供父节点写入 UpstreamDraftIds；供给完全满足时返回 null。
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
        HashSet<string> visited)
    {
        var nodeKey = SupplyPool.BuildKey(materialCode, factoryId);
        if (!visited.Add(nodeKey)) return null;

        try
        {
            var demandKey = $"ORDER_{order.OrderId}_{materialCode}_{factoryId}";

            // 红线① Demand 闭合：累加本节点需求量，供 ValidatePeggingResult 校验
            // 「Σ(已分配) + 短缺 ≤ 需求总量」。跨层级 BOM 展开时总量严格大于叶级分配，属有效上界校验。
            voucher.DemandQuantity += demandQty;

            // §5.2 DemandBalance：构建需求侧内存账本
            var demand = new DemandBalance
            {
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
            //  - 自制件（isPurchased=false）：合资格库存 → PI → 生产缺口。
            //  - 采购件（isPurchased=true）：合资格库存 → 正式采购/在途 → 规划采购占位。
            var isPurchased = bom.IsPurchasedByMaterial.TryGetValue(materialCode, out var purchased) && purchased;
            var includeInventory = bomLevel > 0;

            // 贪婪扣减：使用原子Allocation机制，确保供需扣减、Lock校验、AllocationSequence生成的原子性
            foreach (var entry in supplyPool.GetEntries(materialCode, factoryId, isPurchased, includeInventory))
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
                        materialId: materialId,
                        factoryId: factoryId,
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
                    availableAt: order.DueDate.AddDays(-7),
                    sourceType: Core.Enum.SupplySourceType.PLANNING_PURCHASE_PLACEHOLDER,
                    sourceRef: $"PLANNING_PLACEHOLDER_{voucher.PlanVersionId}_{materialCode}_{Guid.NewGuid():N}",
                    factoryCode: factoryCode,
                    confidence: SupplyConfidence.ESTIMATED,
                    commitment: SupplyCommitment.NOT_COMMITTED);

                voucher.ShortageQuantity += demand.RemainingQty;
                return null;
            }
            else
            {
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
                var result = TryAtomicAllocation(
                    supply: virtualSupply,
                    demand: demand,
                    bomLevel: bomLevel,
                    voucher: voucher,
                    requestedQty: demand.RemainingQty);

                if (!result.Success)
                {
                    voucher.ShortageQuantity += demand.RemainingQty;
                    return null;
                }

                if (result.Record != null && result.Record.RequiresProduction)
                {
                    voucher.LogicalProductionDemands.Add(BuildLogicalProductionDemand(
                        allocation: result.Record,
                        demandKey: demand.DemandKey,
                        orderId: order.OrderId,
                        materialId: materialId,
                        factoryId: factoryId,
                        requiredTime: order.DueDate,
                        demandSequence: demandSequence,
                        voucher: voucher));
                }

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
                            result.AllocatedQty * edge.Qty,
                            bomLevel + 1,
                            demandSequence,
                            bom,
                            supplyPool,
                            voucher,
                            visited);
                    }
                }

                return null;
            }
        }
        finally
        {
            visited.Remove(nodeKey);
        }
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
                ResourceCode = res.ResourceName,
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
