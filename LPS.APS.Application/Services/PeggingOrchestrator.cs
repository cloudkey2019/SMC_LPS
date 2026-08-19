using System.Data;
using System.Diagnostics;
using Dapper;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;
using ApsTask = LPS.APS.Core.Entities.APS.Task;

namespace LPS.APS.Application.Services;

/// <summary>
/// Pegging 编排器（2号位职责）
/// </summary>
public class PeggingOrchestrator : IPeggingOrchestrator
{
    private readonly IPeggingRuleService _peggingRuleService;
    private readonly IPeggingSupplyAllocationRepository _allocationRepo;
    private readonly IDemandSupplyHardLockRepository _lockRepo;
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<PeggingOrchestrator> _logger;
    private readonly IFiniteCapacityScheduler _scheduler;

    public PeggingOrchestrator(
        IPeggingRuleService peggingRuleService,
        IPeggingSupplyAllocationRepository allocationRepo,
        IDemandSupplyHardLockRepository lockRepo,
        DatabaseConnectionManager connectionManager,
        ILogger<PeggingOrchestrator> logger,
        IFiniteCapacityScheduler scheduler)
    {
        _peggingRuleService = peggingRuleService ?? throw new ArgumentNullException(nameof(peggingRuleService));
        _allocationRepo     = allocationRepo     ?? throw new ArgumentNullException(nameof(allocationRepo));
        _lockRepo           = lockRepo           ?? throw new ArgumentNullException(nameof(lockRepo));
        _connectionManager  = connectionManager  ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger             = logger             ?? throw new ArgumentNullException(nameof(logger));
        _scheduler          = scheduler          ?? throw new ArgumentNullException(nameof(scheduler));
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

            var supplyPool = await LoadSupplyPoolAsync(request, cancellationToken);
            _logger.LogInformation(
                "[Pegging] 供给池装载完成: PlanVersionId={PlanVersionId}, 条目={EntryCount}",
                request.PlanVersionId, supplyPool.TotalEntries);

            // ── 步骤3：PeggingLoop BOM 遍历 + 供给扣减 ──
            var voucher = await ExecutePeggingLoopAsync(request, bomSnapshot, supplyPool, cancellationToken);
            result.Voucher = voucher;

            var ruleVoucher = await _peggingRuleService.BuildPeggingVoucherAsync(
                request.PlanVersionId,
                result.OrderId,
                new List<SupplyCandidate>(),
                cancellationToken);

            var (ruleValid, ruleErrors) = await _peggingRuleService.ValidateBusinessRuleResultAsync(
                ruleVoucher, cancellationToken);

            voucher.RuleVoucher = ruleVoucher;

            if (!ruleValid)
            {
                foreach (var e in ruleErrors)
                    _logger.LogError("[Pegging] 业务规则红线: {Error}", e);
                result.IsSuccess   = false;
                result.ErrorMessage = string.Join("; ", ruleErrors);
                return result;
            }

            foreach (var w in ruleVoucher.Warnings)
            {
                _logger.LogWarning("[Pegging] 规则警告: {Warning}", w);
                result.Warnings.Add(w);
            }

            // v5.1.2架构整改：不再预先生成TaskDrafts，改为传递LogicalProductionDemands给1号位
            // 1号位基于LogicalProductionDemands生成FinalTasks（含拆批/合批决策）
            _logger.LogInformation("[Pegging] 准备传递LogicalProductionDemands给Solver: {Count} 个",
                voucher.LogicalProductionDemands.Count);

            var solveRequest = new DomainSolveRequest
            {
                ScheduleRunId = request.SchedulingContext?.ScheduleRunId,
                PlanVersionId = request.PlanVersionId,
                DomainKey     = request.PlanVersionId.ToString(),
                DataCutoffTime = request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt,
                PlanningStart = request.SnapshotAt == default ? DateTime.Now : request.SnapshotAt,
                PlanningEnd   = request.FrozenWindowEnd == default ? DateTime.Now.AddDays(90) : request.FrozenWindowEnd,

                LogicalProductionDemands = voucher.LogicalProductionDemands,
                AllocationLineage = BuildAllocationLineage(voucher),

                RoutingOperations = Array.Empty<RoutingOperation>(),
                RoutingDependencies = Array.Empty<RoutingDependency>(),
                OperationResourceEligibility = Array.Empty<OperationResourceEligibility>(),

                MaterialConstraints = BuildMaterialConstraints(voucher),

                Resources     = BuildResourceDefinitions(request.SchedulingContext),
                CalendarSlots = BuildResourceCalendarSlots(request.SchedulingContext),
                ResourceEligibility = Array.Empty<ResourceEligibilityDefinition>(),
                ExecutionConstraints = Array.Empty<ExecutionConstraint>(),

                StrategySnapshot = new SolverStrategySnapshot
                {
                    StrategyProfileVersionId = request.SchedulingContext?.StrategyProfileVersionId,
                    ParameterSetVersionId = null,
                    Parameters = new FiniteCapacityParameters
                    {
                        AllowSplit = false,
                        AllowMerge = false,
                        MaxIterations = 1000,
                        SchedulingDirection = "BACKWARD"
                    }
                },

                CandidateContext = null
            };
            var solveResult = await _scheduler.SolveAsync(solveRequest, cancellationToken);
            Console.WriteLine($"[PeggingOrchestrator] IFiniteCapacityScheduler.SolveAsync完成: FinalTasks={solveResult.FinalTasks?.Count ?? 0}, Success={solveResult.Success}");

            (result.GeneratedTasks, result.PhysicalPeggingCount) =
                await PersistDomainAndPeggingInTransactionAsync(
                    request.PlanVersionId, voucher, solveResult, cancellationToken);
            _logger.LogInformation(
                "[Pegging] 统一事务落库: Task={Tasks}, Pegging={Pegging}",
                result.GeneratedTasks.Count, result.PhysicalPeggingCount);

            result.SupplyAllocationCount = await PersistSupplyAllocationAsync(voucher, cancellationToken);
            _logger.LogInformation(
                "[Pegging] PeggingSupplyAllocation 写入: {Count} 条", result.SupplyAllocationCount);

            sw.Stop();
            result.IsSuccess      = true;
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;

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
                OrderIds          = batch.ToList(),
                SnapshotAt        = request.SnapshotAt,
                FrozenWindowStart = request.FrozenWindowStart,
                FrozenWindowEnd   = request.FrozenWindowEnd,
                AllowCrossFactory = request.AllowCrossFactory,
                CrossFactoryMode  = request.CrossFactoryMode,
                DefaultStrategy   = request.DefaultStrategy,
                ProductFamilyIds  = request.ProductFamilyIds,
                TopologicalOrder  = request.TopologicalOrder,
                VirtualInventory  = request.VirtualInventory,
                MaxBomDepth       = request.MaxBomDepth,
                TimeoutSeconds    = request.TimeoutSeconds,
                ExecutionMode     = request.ExecutionMode
            };

            results.Add(await ExecutePeggingWorkflowAsync(batchRequest, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// ⚠️ v5.1.2架构整改后已废弃
    /// 原因：不再预先构建TaskDrafts，改为直接传递LogicalProductionDemands给1号位Solver
    /// FinalTask由1号位生成（含拆批/合批决策），2号位负责持久化
    /// </summary>
    [Obsolete("v5.1.2架构整改：不再预先构建TaskDrafts，改为传递LogicalProductionDemands")]
    public IReadOnlyList<Core.Dto.TaskDraft> BuildTaskDraftsFromVoucher(PeggingResultVoucher voucher)
    {
        if (voucher.LogicalProductionDemands.Count == 0)
        {
            _logger.LogDebug("[Pegging] BuildTaskDraftsFromVoucher: LogicalProductionDemands 为空");
            return Array.Empty<Core.Dto.TaskDraft>();
        }

        var drafts = voucher.LogicalProductionDemands
            .Select(lpd => new Core.Dto.TaskDraft
            {
                DraftId = lpd.LogicalDemandKey,
                MaterialId = lpd.MaterialId,
                StageCode = lpd.StartStageCode ?? string.Empty,
                OperationCode = string.Empty,
                RouteKey = string.Empty,
                ProductionInstructionNo = lpd.ProductionInstructionNo,
                Quantity = lpd.NetOutputQty,
                UOM = string.Empty,
                FactoryCode = string.Empty,
                Department = null,
                ProductFamilyId = 0,
                EarliestAvailableTime = lpd.RequiredAvailableTime,
                DueTime = lpd.RequiredAvailableTime,
                TaskPlanningMode = "OPERATION_FINITE",
                Priority = lpd.DemandSequence,
                UpstreamDraftIds = new List<string>(),
                IsVirtual = false,
                ExistingMESPlanReleaseId = null,
                ExecutionLockId = null
            })
            .ToList();

        _logger.LogInformation(
            "[Pegging] 从LogicalProductionDemands构建TaskDrafts: {Count} 个",
            drafts.Count);

        return drafts;
    }

    /// <summary>
    /// 统一事务：DELETE 占位 Task → INSERT Task → INSERT Pegging 血缘 → INSERT AllocationLedger。
    /// 四步在同一 SqlTransaction 内，任一失败全部回滚。
    /// </summary>
    private async System.Threading.Tasks.Task<(List<ApsTask> tasks, int peggingCount)>
        PersistDomainAndPeggingInTransactionAsync(
            int planVersionId,
            PeggingResultVoucher voucher,
            DomainSolveResult solveResult,
            CancellationToken ct)
    {
        return await _connectionManager.ExecuteInTransactionAsync<(List<ApsTask>, int)>(
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

                return (tasks, peggingRows.Count);
            },
            db: DatabaseId.APS);
    }

    /// <summary>
    /// 按 UpstreamDraftIds 做 DFS 后序拓扑排序，确保上游草稿排在下游之前。
    /// </summary>
    private static IEnumerable<Core.Dto.TaskDraft> TopologicalSortDrafts(
        IReadOnlyList<Core.Dto.TaskDraft> drafts)
    {
        var byId    = drafts.ToDictionary(d => d.DraftId, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var result  = new List<Core.Dto.TaskDraft>(drafts.Count);

        void Visit(Core.Dto.TaskDraft d)
        {
            if (!visited.Add(d.DraftId)) return;
            foreach (var upId in d.UpstreamDraftIds)
                if (byId.TryGetValue(upId, out var upstream))
                    Visit(upstream);
            result.Add(d);
        }

        foreach (var d in drafts) Visit(d);
        return result;
    }


    /// <inheritdoc />
    public async System.Threading.Tasks.Task<int> PersistSupplyAllocationAsync(
        PeggingResultVoucher voucher,
        CancellationToken cancellationToken = default)
    {
        var allocations = MapVoucherToSupplyAllocations(voucher);
        if (allocations.Count == 0) return 0;

        var count = await _allocationRepo.BulkInsertAsync(allocations, cancellationToken);

        return count;
    }

    [Obsolete("V1.2 退出主链：不再实现 FrozenZoneSnapshot 平台", false)]
    public async System.Threading.Tasks.Task<int> UpdateFrozenZoneSnapshotAsync(
        int planVersionId,
        DateTime frozenWindowStart,
        DateTime frozenWindowEnd,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Pegging] UpdateFrozenZoneSnapshotAsync 已废弃，跳过执行");
        return 0;
    }

    /// <inheritdoc />
    [Obsolete("V1.2 退出主链：VirtualInventoryBalance 不再实现", false)]
    public async System.Threading.Tasks.Task<int> PropagateVirtualInventoryAsync(
        int planVersionId,
        int sourceProductFamilyId,
        int targetProductFamilyId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Pegging] PropagateVirtualInventoryAsync 已废弃，跳过执行");
        await System.Threading.Tasks.Task.CompletedTask;
        return 0;
    }

    /// <inheritdoc />
    [Obsolete("V1.2 退出主链：FrozenZoneSnapshot 和 VirtualInventoryBalance 不再实现", false)]
    public async System.Threading.Tasks.Task RollbackPeggingWorkflowAsync(
        int planVersionId,
        long orderId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[Pegging] 回滚: PlanVersionId={PlanVersionId}, OrderId={OrderId}",
            planVersionId, orderId);

        // V1.2 退出主链：只回滚 Allocation，不再回滚 FrozenZoneSnapshot 和 VirtualInventoryBalance
        await _allocationRepo.DeleteByPlanVersionIdAsync(planVersionId, cancellationToken);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<(bool IsValid, List<string> ValidationErrors)> ValidateWorkflowConsistencyAsync(
        PeggingOrchestrationResult result,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (!result.IsSuccess)
            errors.Add($"Pegging 执行失败: {result.ErrorMessage}");

        if (result.Voucher?.ShortageQuantity > 0)
            errors.Add($"供应短缺数量: {result.Voucher.ShortageQuantity}");

        if (result.Voucher?.RuleVoucher is { PassedBusinessRules: false })
            errors.AddRange(result.Voucher.RuleVoucher.BusinessRuleErrors);

        return await System.Threading.Tasks.Task.FromResult((errors.Count == 0, errors));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 私有辅助方法
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 将 Voucher 中的供应分配映射为 PeggingSupplyAllocation 实体
    ///
    /// NEW_REQUIREMENT 不写 PeggingSupplyAllocation：
    ///   该类型对应"需新排产"，最终通过 Task 实例化 + 物理 Pegging 表记录 Task-to-Task 血缘
    /// </summary>
    private static List<Core.Entities.APS.PeggingSupplyAllocation> MapVoucherToSupplyAllocations(
        PeggingResultVoucher voucher)
    {
        var now = DateTime.Now;

        return voucher.SupplyAllocations
            .Where(a => a.SourceType != Core.Enum.SupplySourceType.NEW_REQUIREMENT)
            .Select(alloc => new Core.Entities.APS.PeggingSupplyAllocation
            {
                PlanVersionId        = voucher.PlanVersionId,
                ScheduleRunId        = 0, // 需从PlanVersion查询
                AllocationSequence   = alloc.AllocationSequence,
                MaterialId           = alloc.SupplyMaterialId,
                MaterialCode         = string.Empty, // 需从Material表查询
                DemandQty            = voucher.DemandQuantity,
                AllocatedQty         = alloc.AllocatedQuantity,
                SupplyType           = alloc.SourceType.ToString(),
                DemandFactoryCode    = alloc.FactoryCode,
                SupplyFactoryCode    = alloc.FactoryCode,
                KnownAvailableTime   = alloc.AvailableAt,
                SupplyDocumentNo     = alloc.SourceReference,
                CreatedAt            = now
            }).ToList();
    }

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

        public int TotalEntries { get; private set; }

        public void Add(
            string materialCode, int materialId, int factoryId, decimal qty,
            DateTime? availableAt, Core.Enum.SupplySourceType sourceType,
            string? sourceRef, string factoryCode, long? supplySourceId = null,
            SupplyConfidence confidence = SupplyConfidence.CONFIRMED,
            SupplyCommitment commitment = SupplyCommitment.COMMITTED)
        {
            var key = BuildKey(materialCode, factoryId);
            if (!_ledger.TryGetValue(key, out var list))
            {
                list = new List<SupplyLedgerEntry>();
                _ledger[key] = list;
            }
            list.Add(new SupplyLedgerEntry
            {
                OriginalQty     = qty,
                RemainingQty    = qty,
                MaterialId      = materialId,
                AvailableAt     = availableAt,
                SourceType      = sourceType,
                SourceReference = sourceRef,
                FactoryCode     = factoryCode,
                FactoryId       = factoryId,
                SupplySourceId  = supplySourceId,
                Confidence      = confidence,
                Commitment      = commitment
            });
            TotalEntries++;
        }

        /// <summary>返回指定物料+工厂的所有供给条目（按 AvailableAt 升序排列，现货在前）</summary>
        public IReadOnlyList<SupplyLedgerEntry> GetEntries(string materialCode, int factoryId)
        {
            var key = BuildKey(materialCode, factoryId);
            if (!_ledger.TryGetValue(key, out var list)) return Array.Empty<SupplyLedgerEntry>();
            return list.OrderBy(e => e.AvailableAt ?? DateTime.MinValue).ToList();
        }

        /// <summary>返回所有供给条目（用于加载Lock数据）</summary>
        public IEnumerable<SupplyLedgerEntry> GetAllEntries()
        {
            return _ledger.Values.SelectMany(list => list);
        }

        public static string BuildKey(string materialCode, int factoryId)
            => $"{materialCode}|{factoryId}";
    }

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
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 供给池装载
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 装载供给池（INVENTORY + PIPELINE + WIP）
    /// </summary>
    private async Task<SupplyPool> LoadSupplyPoolAsync(
        PeggingExecutionRequest request,
        CancellationToken ct)
    {
        var pool    = new SupplyPool();
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
                     null, r.FactoryCode, r.SupplySourceId);

        var pipelineRows = await _connectionManager.QueryAsync<SupplyLoadRow>(
            @"SELECT
                  sfp.MaterialCode,
                  sfp.MaterialId,
                  sfp.FactoryId,
                  sfp.FactoryCode,
                  sfp.Quantity                                       AS AvailableQty,
                  sfp.AvailableTime                                  AS AvailableAt,
                  ISNULL(sfp.SourceDocumentNo, sfp.SourceRowKey)     AS SourceReference
              FROM SupplyFact_Pipeline sfp
              WHERE sfp.IsActive = 1
                AND sfp.Quantity > 0
                AND (sfp.AvailableTime IS NULL OR sfp.AvailableTime <= @Cutoff)",
            new { Cutoff = cutoff },
            db: DatabaseId.APS);

        foreach (var r in pipelineRows)
            pool.Add(r.MaterialCode, r.MaterialId, r.FactoryId, r.AvailableQty,
                     r.AvailableAt, Core.Enum.SupplySourceType.PIPELINE,
                     r.SourceReference, r.FactoryCode);

        var wipRows = await _connectionManager.QueryAsync<SupplyLoadRow>(
            @"SELECT sp.MaterialCode,
                     m.Id             AS MaterialId,
                     f.Id             AS FactoryId,
                     f.Code           AS FactoryCode,
                     sp.RemainingQty  AS AvailableQty,
                     NULL             AS AvailableAt,
                     sp.ProductionInstructionNo AS SourceReference
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
            db: DatabaseId.APS);

        foreach (var r in wipRows)
            pool.Add(r.MaterialCode, r.MaterialId, r.FactoryId, r.AvailableQty,
                     null, Core.Enum.SupplySourceType.WIP,
                     r.SourceReference, r.FactoryCode);

        _logger.LogDebug(
            "[Pegging] 供给池明细: INVENTORY={Inv}, WIP={Wip}, PIPELINE={Pipe}",
            inventoryRows.Count(), wipRows.Count(), pipelineRows.Count());

        await LoadActiveLockDataAsync(pool, ct);

        return pool;
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

        var firstOrder = orders.FirstOrDefault();
        var voucher = new PeggingResultVoucher
        {
            PlanVersionId    = request.PlanVersionId,
            OrderId          = firstOrder?.OrderId ?? request.OrderIds.FirstOrDefault(),
            DemandMaterialId = firstOrder?.MaterialId ?? 0,
            UOM              = firstOrder?.UOM ?? string.Empty,
            IsSuccess        = true,
            ExecutedAt       = DateTime.Now
        };

        foreach (var order in orders)
        {
            ct.ThrowIfCancellationRequested();

            var visited = new HashSet<string>(StringComparer.Ordinal);
            _ = TraverseBomNode(
                order,
                order.MaterialCode,
                order.MaterialId,
                order.FactoryId,
                order.FactoryCode,
                order.DemandQty,
                bomLevel: 0,
                bom,
                supplyPool,
                voucher,
                visited);
        }

        voucher.IsFullyAllocated = voucher.ShortageQuantity == 0;
        return voucher;
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
            DomainKey = $"FACTORY_{factoryId}", // V1.2：单工厂Domain
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
                Priority = bomLevel, // TEST FIXTURE ONLY：临时使用bomLevel，V1验收前必须接入§6 Priority Segment排序
                ProductFamilyId = 0, // V1.2：暂不使用产品族
                IsInFrozenZone = false,
                WorksetId = null
            };

            // V1.2 贪婪扣减：AvailableAt 升序（INVENTORY null → MinValue 排最前）
            // 使用原子Allocation机制，确保供需扣减、Lock校验、AllocationSequence生成的原子性
            foreach (var entry in supplyPool.GetEntries(materialCode, factoryId))
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
                        demandSequence: bomLevel,
                        voucher: voucher));
                }
            }

            if (demand.RemainingQty <= 0m) return null;

            // V1.2：缺口处理 - 根据IsPurchased区分采购件/自制件
            var isPurchased = bom.IsPurchasedByMaterial.TryGetValue(materialCode, out var purchased) && purchased;

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

                // 添加虚拟PLANNED_PRODUCTION供给到SupplyPool
                supplyPool.Add(
                    materialCode: materialCode,
                    materialId: materialId,
                    factoryId: factoryId,
                    qty: demand.RemainingQty,
                    availableAt: order.DueDate,
                    sourceType: Core.Enum.SupplySourceType.NEW_REQUIREMENT,
                    sourceRef: $"NEW_REQ_{voucher.PlanVersionId}_{materialCode}_{Guid.NewGuid():N}",
                    factoryCode: factoryCode);

                // 通过标准原子Allocation流程分配（经过完整的Lock校验和余额扣减）
                var virtualSupply = supplyPool.GetEntries(materialCode, factoryId).Last();
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
                        demandSequence: bomLevel,
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
