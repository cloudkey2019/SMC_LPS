using System.Data;
using Dapper;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Models;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Models.Scheduling;
using LPS.APS.Engine.Data;
using LPS.APS.Scheduling.Algorithms;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Shared.Models;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 排程编排器（2号位职责 — §2.5.1 排程发令枪）
/// 
/// 每日02:00由Hangfire触发，完整编排流程：
///   阶段1: 装载排程沙盘（从APS库读取订单/BOM/物料/设备/库存 → SchedulingContext）
///   阶段2: Pegging（2号位PeggingOrchestrator生成Task + Allocation）
///   阶段3: 调用 FiniteCapacitySolver.Solve()（纯内存计算）
///   阶段4: 排程结果落盘
///   阶段5: PlanVersion 状态更新
///   阶段6: 快照封存（§2.6 SchedulingContext → .json.gz）
///
/// 架构位置：Application 层（桥接 Engine 数据层 + Scheduling 算法层）
/// </summary>
public class SchedulingOrchestrator : ISchedulingOrchestrator
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly FiniteCapacitySolver _solver;
    private readonly ISnapshotService _snapshotService;
    private readonly IBatchSplitter _batchSplitter;
    private readonly IPeggingOrchestrator _peggingOrchestrator;
    private readonly IScheduleRunService _scheduleRunService;
    private readonly ILogger<SchedulingOrchestrator> _logger;

    public SchedulingOrchestrator(
        DatabaseConnectionManager connectionManager,
        FiniteCapacitySolver solver,
        ISnapshotService snapshotService,
        IBatchSplitter batchSplitter,
        IPeggingOrchestrator peggingOrchestrator,
        IScheduleRunService scheduleRunService,
        ILogger<SchedulingOrchestrator> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _solver = solver ?? throw new ArgumentNullException(nameof(solver));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _batchSplitter = batchSplitter ?? throw new ArgumentNullException(nameof(batchSplitter));
        _peggingOrchestrator = peggingOrchestrator ?? throw new ArgumentNullException(nameof(peggingOrchestrator));
        _scheduleRunService = scheduleRunService ?? throw new ArgumentNullException(nameof(scheduleRunService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SchedulingRunResult> RunSchedulingAutoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("排程发令枪：自动查找待排计划版本");

        // 原子领取：用CTE + UPDLOCK + READPAST + OUTPUT 确保一个PlanVersion只被一个Worker领取
        var claimedResult = await _connectionManager.ExecuteInTransactionAsync<(int? PlanVersionId, string? VersionCode, int? ScheduleRunId)>(
            async (conn, tx) =>
            {
                // 原子操作：SELECT + UPDATE 在一个语句内完成
                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    @";WITH Target AS (
                        SELECT TOP 1
                            Id,
                            VersionCode,
                            SourceScheduleRunId,
                            Status
                        FROM PlanVersion WITH (UPDLOCK, READPAST, ROWLOCK)
                        WHERE Status = 'Created' AND SourceScheduleRunId > 0
                        ORDER BY CreatedAt DESC
                    )
                    UPDATE Target
                    SET Status = 'Computing'
                    OUTPUT
                        inserted.Id AS PlanVersionId,
                        inserted.VersionCode,
                        inserted.SourceScheduleRunId AS ScheduleRunId",
                    transaction: tx);

                if (result == null)
                {
                    _logger.LogInformation("未找到待排计划版本（Status='Created'），跳过本次触发");
                    return ((int?)null, (string?)null, (int?)null);
                }

                return ((int?)result.PlanVersionId, (string?)result.VersionCode, (int?)result.ScheduleRunId);
            },
            db: DatabaseId.APS);

        // 如果没有领取到PlanVersion，直接返回
        if (claimedResult.PlanVersionId == null || claimedResult.ScheduleRunId == null)
        {
            return new SchedulingRunResult { IsSuccess = true, ErrorMessage = "无待排计划版本" };
        }

        // 读取ScheduleRun完整信息
        var scheduleRun = await _connectionManager.QueryFirstOrDefaultAsync<ScheduleRunQueryDto>(
            "SELECT Id, DataCutoffTime, StrategyProfileVersionId FROM ScheduleRun WHERE Id = @Id",
            new { Id = claimedResult.ScheduleRunId.Value },
            db: DatabaseId.APS);

        if (scheduleRun == null)
        {
            _logger.LogWarning("未找到 ScheduleRun: Id={Id}", claimedResult.ScheduleRunId);
            return new SchedulingRunResult { IsSuccess = false, ErrorMessage = $"ScheduleRun {claimedResult.ScheduleRunId} 不存在" };
        }

        _logger.LogInformation(
            "成功领取计划版本: PlanVersionId={PlanVersionId}, VersionCode={VersionCode}, ScheduleRunId={RunId}",
            claimedResult.PlanVersionId, claimedResult.VersionCode, scheduleRun.Id);

        return await RunSchedulingAsync(claimedResult.PlanVersionId.Value, scheduleRun.Id, scheduleRun.DataCutoffTime, scheduleRun.StrategyProfileVersionId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SchedulingRunResult> RunSchedulingAsync(int planVersionId, CancellationToken cancellationToken = default)
        => await RunSchedulingAsync(planVersionId, scheduleRunId: 0, dataCutoffTime: null, strategyProfileVersionId: null, cancellationToken);

    private async Task<SchedulingRunResult> RunSchedulingAsync(
        int planVersionId,
        int scheduleRunId,
        DateTime? dataCutoffTime,
        long? strategyProfileVersionId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("排程开始: PlanVersionId={PlanVersionId}, ScheduleRunId={RunId}",
            planVersionId, scheduleRunId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 获取计划版本信息
            var planVersion = await _connectionManager.QueryFirstOrDefaultAsync<PlanVersionInfoDto>(
                "SELECT Id, VersionCode, PlanHorizonStart, PlanHorizonEnd FROM PlanVersion WHERE Id = @Id",
                new { Id = planVersionId },
                db: DatabaseId.APS);

            if (planVersion == null)
                throw new InvalidOperationException($"计划版本不存在: PlanVersionId={planVersionId}");

            // 更新状态为 Computing
            await _connectionManager.ExecuteAsync(
                "UPDATE PlanVersion SET Status = 'Computing' WHERE Id = @Id",
                new { Id = planVersionId },
                db: DatabaseId.APS);

            // 幂等清理：删除上次运行留下的脏数据（按 FK 依赖顺序）
            await _connectionManager.ExecuteAsync(
                @"DELETE FROM PeggingSupplyAllocation WHERE PlanVersionId = @Id;
                  DELETE FROM [Pegging]               WHERE PlanVersionId = @Id;
                  DELETE FROM [Task]                  WHERE PlanVersionId = @Id;",
                new { Id = planVersionId },
                db: DatabaseId.APS);

            // ═══════════════════════════════════════════
            // 阶段1: 装载排程沙盘
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{PlanVersionId}] 阶段1: 装载排程沙盘", planVersionId);
            var context = await LoadSchedulingContextAsync(planVersion, scheduleRunId, cancellationToken);
            context.StrategyProfileVersionId = strategyProfileVersionId;
            if (strategyProfileVersionId.HasValue)
                await LoadStrategyConfigAsync(context, strategyProfileVersionId.Value, cancellationToken);
            _logger.LogInformation(
                "[{PlanVersionId}] 沙盘装载完成: Tasks={TaskCount}, Resources={ResourceCount}, MESKeys={MESCount}",
                planVersionId, context.Tasks.Count, context.Resources.Count, context.MESRemainingQty.Count);

            // ═══════════════════════════════════════════
            // 阶段2: Pegging — 供需挂钩 + 冻结区保护
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{PlanVersionId}] 阶段2: Pegging 供需挂钩", planVersionId);
            var topoOrder = await LoadTopologicalOrderAsync(context, cancellationToken);
            _logger.LogInformation("[{PlanVersionId}] 拓扑排序完成: {Count} 个产品族域", planVersionId, topoOrder.Count);
            var allOrderIds = (await _connectionManager.QueryAsync<long>(
                "SELECT Id FROM [Order] WHERE PlanVersionId = @PlanVersionId",
                new { PlanVersionId = planVersionId },
                db: DatabaseId.APS)).ToList();
            var peggingRequest = BuildPeggingRequest(planVersionId, allOrderIds, context, topoOrder);
            var peggingResults = (await _peggingOrchestrator.ExecuteBatchPeggingWorkflowAsync(
                peggingRequest, cancellationToken)).ToList();

            var peggingFailed = peggingResults.Where(r => !r.IsSuccess).ToList();
            if (peggingFailed.Count > 0)
            {
                _logger.LogWarning(
                    "[{PlanVersionId}] Pegging 部分失败: {FailCount}/{Total}，继续排程",
                    planVersionId, peggingFailed.Count, peggingResults.Count);
                foreach (var f in peggingFailed)
                    _logger.LogWarning("[{PlanVersionId}] Pegging 失败 OrderId={OrderId}: {Err}",
                        planVersionId, f.OrderId, f.ErrorMessage);
            }

            var totalTasks   = peggingResults.Sum(r => r.GeneratedTasks.Count);
            var totalAlloc   = peggingResults.Sum(r => r.SupplyAllocationCount);
            _logger.LogInformation(
                "[{PlanVersionId}] Pegging 完成: 生成Task={Tasks}, 分配={Alloc}",
                planVersionId, totalTasks, totalAlloc);

            // V1.2：Pegging已完成Task生成并持久化，无需回填context或再次调用Solver
            // 阶段3已在PeggingOrchestrator内部完成：LogicalProductionDemands → TaskDrafts → SolveAsync → FinalTasks
            _logger.LogInformation("[{PlanVersionId}] 阶段3: 有限产能排程已在Pegging阶段完成", planVersionId);
            _logger.LogInformation("[{PlanVersionId}] 阶段4: 排程结果已在Pegging阶段持久化", planVersionId);

            // ═══════════════════════════════════════════
            // 阶段5: 更新PlanVersion状态 + 更新 ScheduleRun
            // ═══════════════════════════════════════════
            var isSuccess = peggingFailed.Count == 0;
            var finalStatus = isSuccess ? "Computed" : "ComputeFailed";
            await _connectionManager.ExecuteAsync(
                "UPDATE PlanVersion SET Status = @Status, ComputedAt = GETDATE() WHERE Id = @Id",
                new { Id = planVersionId, Status = finalStatus },
                db: DatabaseId.APS);

            if (scheduleRunId > 0)
            {
                stopwatch.Stop();
                await _scheduleRunService.CompleteAsync(scheduleRunId, (int)(stopwatch.ElapsedMilliseconds / 1000), cancellationToken);
                stopwatch.Start();
            }

            // ═══════════════════════════════════════════
            // 阶段6: 快照封存（§2.6）
            // ═══════════════════════════════════════════
            _logger.LogInformation("[{PlanVersionId}] 阶段6: 快照封存", planVersionId);
            try
            {
                var snapshotInfo = await _snapshotService.SaveAsync(context, planVersionId, cancellationToken);
                _logger.LogInformation(
                    "[{PlanVersionId}] 快照封存完成: 压缩后={CompressedMB:F1}MB, SHA256={Hash}",
                    planVersionId, snapshotInfo.CompressedSize / 1048576.0, snapshotInfo.FileHash[..12] + "...");
            }
            catch (Exception snapshotEx)
            {
                _logger.LogWarning(snapshotEx, "[{PlanVersionId}] 快照封存失败（非致命，不影响排程结果）", planVersionId);
            }

            stopwatch.Stop();

            await LogETLAsync(planVersion.VersionCode, "Scheduling",
                $"排程完成 | 已排:{totalTasks} | 失败:{peggingFailed.Count} | 耗时:{stopwatch.ElapsedMilliseconds}ms",
                isSuccess ? "SUCCESS" : "PARTIAL");

            var result = new SchedulingRunResult
            {
                PlanVersionId    = planVersionId,
                VersionCode      = planVersion.VersionCode,
                IsSuccess        = isSuccess,
                ScheduledCount   = totalTasks,
                UnscheduledCount = peggingFailed.Count,
                ElapsedMs        = stopwatch.ElapsedMilliseconds
            };

            _logger.LogInformation(
                "排程完成: PlanVersionId={PlanVersionId}, 已排={Scheduled}, 未排={Unscheduled}, 耗时={Elapsed}ms",
                planVersionId, totalTasks, peggingFailed.Count, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "排程失败: PlanVersionId={PlanVersionId}", planVersionId);

            try
            {
                await _connectionManager.ExecuteAsync(
                    "UPDATE PlanVersion SET Status = 'ComputeFailed' WHERE Id = @Id",
                    new { Id = planVersionId },
                    db: DatabaseId.APS);

                if (scheduleRunId > 0)
                {
                    await _scheduleRunService.FailAsync(
                        scheduleRunId,
                        (int)(stopwatch.ElapsedMilliseconds / 1000),
                        ex.Message,
                        cancellationToken);
                }

                await LogETLAsync($"PV-{planVersionId}", "Scheduling",
                    $"排程失败: {ex.Message}", "FAILED");
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "排程失败后回写状态异常（非致命）");
            }

            throw;
        }
    }

    /// <summary>
    /// 阶段1: 装载排程沙盘（从APS库读取数据组装 SchedulingContext）
    /// 
    /// 子步骤（2号位端到端通路）：
    ///   1.1 订单加载          → context.Orders（供2号位Pegging使用）
    ///   1.2 BOM 加载          → 保留接口（V1 不真装载 BOM，由 5号位 Pegging 真正消费）
    ///   1.3 物料属性           → 订单加载时 JOIN Material 取得 ProductFamilyId/LLC
    ///   1.4 资源 + 日历        → context.Resources / context.ResourceCalendars（日历 V1 默认 7x24）
    ///   1.5 库存装载           → context.InventorySupplies（从 InventoryBalance）
    ///   1.6 Task 拆批          → 按 RoutingOperation 把每个 Order 拆为 N 个 SchedulingTask
    ///                           + 批量 INSERT 到 [Task] 表（阶段4 回写需要真实 TaskId）
    /// 
    /// V1 保守策略（5号位 Pegging 接入后可替换）：
    ///   - 无真 Pegging：Order 数量直接分配到 Task（一 Order 一 Task 链）
    ///   - 无 BOM 下钻：只处理订单物料自身的工艺路线
    ///   - 无批量切分：整订单数量 × StandardDuration 作为 Task 工时
    ///   - 资源指派：按 OperationResourceEligibility 取默认资源
    /// </summary>
    private async Task<SchedulingContext> LoadSchedulingContextAsync(
        PlanVersionInfoDto planVersion,
        int scheduleRunId,
        CancellationToken cancellationToken)
    {
        var context = new SchedulingContext
        {
            PlanVersionId    = planVersion.Id.ToString(),
            ScheduleRunId    = scheduleRunId,
            PlanHorizonStart = planVersion.PlanHorizonStart,
            PlanHorizonEnd   = planVersion.PlanHorizonEnd
        };

        // 1.1 + 1.3：订单 + 物料属性（一次 JOIN 取全）
        var orders = await LoadOrdersAsync(planVersion.Id, cancellationToken);
        _logger.LogInformation("[{PlanVersionId}] 1.1 订单加载: {Count}", planVersion.Id, orders.Count);

        // 1.4 资源 + 日历
        await LoadResourcesAndCalendarAsync(context, cancellationToken);
        _logger.LogInformation("[{PlanVersionId}] 1.4 资源加载: {Count}", planVersion.Id, context.Resources.Count);

        // 1.5 库存双源汇聚（§2.5.2）
        await LoadInventoryAsync(context, cancellationToken);
        _logger.LogInformation("[{PlanVersionId}] 1.5 库存加载: {Count} 个(物料+产品族+工厂) 组合",
            planVersion.Id, context.InventorySupplies.Count);

        // 1.6 Task 拆批 → V1.2 已废弃：Task由2号位Pegging在供需挂钩后生成，不再预拆批
        // v5.1.2冻结设计（§3.1）：DefaultBatchSplitter调用次数=0，批次拆分由1号位IFiniteCapacityScheduler执行
        // await GenerateAndPersistTasksAsync(planVersion.Id, orders, context, cancellationToken);
        _logger.LogInformation("[{PlanVersionId}] 1.6 跳过预拆批（Task将由Pegging生成）", planVersion.Id);

        // 1.7 MES 进度快照装载（按 ScheduleRunId 从 StageProgressSnapshot 读 RemainingQty）
        if (scheduleRunId > 0)
        {
            await LoadMESProgressAsync(context, scheduleRunId, cancellationToken);
            _logger.LogInformation("[{PlanVersionId}] 1.7 MES进度装载: {Count} 条",
                planVersion.Id, context.MESRemainingQty.Count);
        }

        return context;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 阶段1 各子步骤实现
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 1.7 从 StageProgressSnapshot 装载 MES 进度（按 ScheduleRunId 分区）
    /// 1号位用 MESRemainingQty 决定每道工序实际还需排多少量
    /// </summary>
    private async Task LoadMESProgressAsync(SchedulingContext context, int scheduleRunId, CancellationToken ct)
    {
        var rows = await _connectionManager.QueryAsync<MESProgressLoadDto>(
            @"SELECT ProductionInstructionNo, MaterialCode, StageCode, RemainingQty
              FROM StageProgressSnapshot
              WHERE ScheduleRunId = @ScheduleRunId
                AND RemainingQty  > 0",
            new { ScheduleRunId = scheduleRunId },
            db: DatabaseId.APS);

        foreach (var r in rows)
        {
            var key = SchedulingContext.BuildMESKey(r.ProductionInstructionNo, r.MaterialCode, r.StageCode);
            context.MESRemainingQty[key] = r.RemainingQty;
        }
    }


    private async Task<List<OrderLoadDto>> LoadOrdersAsync(int planVersionId, CancellationToken ct)
    {
        var rows = await _connectionManager.QueryAsync<OrderLoadDto>(
            @"SELECT
                o.Id                AS OrderId,
                o.OrderNo,
                o.MaterialId,
                o.MaterialCode,
                o.ProductFamilyId,
                o.FactoryId,
                o.Quantity,
                o.UOM,
                o.CustomerDueDate,
                o.Priority,
                o.PriorityScore,
                o.CustomerTier,
                m.LowLevelCode      AS LLC
              FROM [Order] o
              INNER JOIN Material m ON m.Id = o.MaterialId
              WHERE o.PlanVersionId = @PlanVersionId
                AND o.Status IN ('Open', 'Released')
              ORDER BY o.Priority DESC, o.CustomerDueDate ASC",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        return rows.ToList();
    }

    /// <summary>
    /// 1.4 加载资源 + 日历
    /// V1 日历策略：7x24 连续可用（计划期全覆盖）；待专门的 ResourceCalendar 装载服务落地后替换
    /// </summary>
    private async Task LoadResourcesAndCalendarAsync(SchedulingContext context, CancellationToken ct)
    {
        var resources = await _connectionManager.QueryAsync<ResourceLoadDto>(
            @"SELECT
                r.Id              AS ResourceId,
                r.ResourceCode,
                r.ResourceName,
                r.FactoryId,
                r.ProductionDepartmentId,
                r.CapacityFactor,
                ISNULL(rpc.DispatchPriority, 100) AS DispatchPriority,
                ISNULL(rpc.LocalDisableFlag, 0)   AS LocalDisableFlag
              FROM Resource r
              LEFT JOIN ResourcePlanningContext rpc
                     ON rpc.ResourceId = r.Id
                    AND (rpc.EffectiveTo IS NULL OR rpc.EffectiveTo >= CAST(GETDATE() AS DATE))
                    AND rpc.EffectiveFrom <= CAST(GETDATE() AS DATE)
              WHERE r.IsActive = 1
                AND r.Status = 'AVAILABLE'",
            db: DatabaseId.APS);

        foreach (var r in resources)
        {
            if (r.LocalDisableFlag) continue;

            var resIdStr = r.ResourceId.ToString();
            context.Resources.Add(new SchedulingResource
            {
                ResourceId       = resIdStr,
                ResourceName     = r.ResourceName,
                FactoryId        = r.FactoryId.ToString(),
                ProductionDepartmentId = r.ProductionDepartmentId,
                CapacityFactor   = r.CapacityFactor,
                DispatchPriority = r.DispatchPriority,
                IsAvailable      = true
            });

            // V1 日历：计划期内 7x24 连续可用
            context.ResourceCalendars[resIdStr] = new List<TimeWindow>
            {
                new TimeWindow(context.PlanHorizonStart, context.PlanHorizonEnd)
            };
        }
    }

    /// <summary>
    /// 1.5 从 InventoryBalance + InTransitInventoryFact 全量装载库存到 SchedulingContext.InventorySupplies
    ///
    /// 装载内容（2号位职责）：
    ///   1. INVENTORY：从 InventoryBalance 读取现有库存（ERP + MES 合并后的可用量）
    ///   2. PIPELINE：从 InTransitInventoryFact 读取在途库存（同域跨厂 IN_TRANSIT 状态）
    ///
    /// ⚠️ 【待对齐 5号位 — B 项越界】
    ///   当前 sp_SyncInventorySnapshot 里硬编码了"双源互斥判定 + InventoryAvailabilityRule 筛选"，
    ///   这部分是 5号位业务规则引擎的职责。后续应改为：
    ///     - 2号位 SP 只做 L2→L3→L4 管道流转（读规则表 + JOIN 应用）
    ///     - 5号位负责 InventoryAvailabilityRule 表内容维护 + Pegging 时的运行时判定
    ///   V1.2: 5号位接入前，SP 里的规则逻辑保持不动。
    /// </summary>
    private async Task LoadInventoryAsync(SchedulingContext context, CancellationToken ct)
    {
        // ── 1. 装载现有库存（INVENTORY）──
        var balances = await _connectionManager.QueryAsync<InventoryLoadDto>(
            @"SELECT MaterialCode, ProductFamilyId, FactoryId, AvailableQty
              FROM InventoryBalance
              WHERE AvailableQty > 0",
            db: DatabaseId.APS);

        foreach (var b in balances)
        {
            var key = SchedulingContext.BuildInventoryKey(b.MaterialCode, b.ProductFamilyId, b.FactoryId);
            context.InventorySupplies[key] = b.AvailableQty;
        }

        // ── 2. 装载管道供给（PIPELINE）——来源：SupplyFact_Pipeline ──
        // AvailableTime = ETA + LeadTimeOffset（sp_SyncPipelineSupply 装载时落库）
        // 只取 AvailableTime 在计划期结束前的记录，超期在途不计入排程供给池
        var inTransits = await _connectionManager.QueryAsync<InTransitLoadDto>(
            @"SELECT
                  sfp.MaterialCode,
                  sfp.ProductFamilyId,
                  sfp.FactoryId,
                  sfp.Quantity       AS AvailableQty,
                  sfp.AvailableTime  AS EstimatedArrivalTime
              FROM SupplyFact_Pipeline sfp
              WHERE sfp.IsActive = 1
                AND sfp.Quantity > 0
                AND (sfp.AvailableTime IS NULL OR sfp.AvailableTime <= @PlanHorizonEnd)",
            new { PlanHorizonEnd = context.PlanHorizonEnd },
            db: DatabaseId.APS);

        foreach (var it in inTransits)
        {
            var key = SchedulingContext.BuildInventoryKey(it.MaterialCode, it.ProductFamilyId, it.FactoryId);

            if (context.InventorySupplies.ContainsKey(key))
                context.InventorySupplies[key] += it.AvailableQty;
            else
                context.InventorySupplies[key] = it.AvailableQty;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // v5.1.2 架构变更说明：
    // GenerateAndPersistTasksAsync 和 BulkInsertTasksAsync 方法已废弃
    //
    // 原因：Task 表的 INSERT 职责已转移至 PeggingOrchestrator（2号位）
    //       - PeggingOrchestrator 在供需匹配阶段生成 TaskDraft 并落库
    //       - SchedulingOrchestrator 只负责 UPDATE Task 表（填充时间/资源分配结果）
    //       - 这样避免了重复落库，保证了 Task 与 PeggingSupplyAllocation 的事务一致性
    //
    // 详见：PeggingOrchestrator.PersistDomainAndPeggingInTransactionAsync (line 238-280)
    // ═══════════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════════
    // 阶段4 结果落盘
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 阶段4: 将 SchedulingContext.Tasks 中 1号位填充好的 PlannedStartTime/EndTime/ResourceId
    /// 批量 UPDATE 回 [Task] 表
    /// 策略：临时表 + MERGE（Dapper 对 SqlServer 的 TVP 支持较弱，用 #Temp）
    /// </summary>
    private async System.Threading.Tasks.Task PersistSchedulingResultAsync(
        int planVersionId,
        SchedulingContext context,
        CancellationToken cancellationToken)
    {
        var scheduledTasks = context.Tasks
            .Where(t => t.PlannedStartTime.HasValue && t.PlannedEndTime.HasValue)
            .Select(t => new
            {
                Id               = long.Parse(t.TaskId),
                PlannedStartTime = t.PlannedStartTime!.Value,
                PlannedEndTime   = t.PlannedEndTime!.Value,
                ResourceId       = string.IsNullOrEmpty(t.ResourceId) ? (int?)null : int.Parse(t.ResourceId),
                IsLocked         = t.IsLocked
            })
            .ToList();

        if (scheduledTasks.Count == 0)
        {
            _logger.LogWarning("[{PlanVersionId}] 无已排程任务需要落盘", planVersionId);
            return;
        }

        // 批量 UPDATE（Dapper 对 List 参数展开，SqlServer 对单条 UPDATE 逐行执行）
        // 规模超 1 万时建议改 SqlBulkCopy → #Temp → MERGE；V1 先走朴素版
        var updateSql = $@"
            UPDATE [Task]
            SET PlannedStartTime = @PlannedStartTime,
                PlannedEndTime   = @PlannedEndTime,
                ResourceId       = @ResourceId,
                IsLocked         = @IsLocked,
                Status           = 'Scheduled',
                UpdatedAt        = GETDATE()
            WHERE Id = @Id AND PlanVersionId = {planVersionId}";

        var affected = await _connectionManager.ExecuteAsync(updateSql, scheduledTasks, db: DatabaseId.APS);

        _logger.LogInformation(
            "[{PlanVersionId}] 落盘完成: 已排 {Scheduled}/{Total}, 影响行数={Affected}",
            planVersionId, scheduledTasks.Count, context.Tasks.Count, affected);
    }

    private async Task LoadStrategyConfigAsync(SchedulingContext context, long strategyProfileVersionId, CancellationToken ct)
    {
        var rows = (await _connectionManager.QueryAsync<StrategyConfigLoadDto>(
            @"SELECT rs.RuleSetCode, rs.RuleSetName, rsv.VersionCode AS RuleSetVersionCode,
                     ps.ParameterSetCode, ps.ParameterSetName, psv.VersionCode AS ParameterSetVersionCode,
                     spv.RuleSetVersionId, spv.ParameterSetVersionId
              FROM StrategyProfileVersion spv
              JOIN RuleSetVersion     rsv ON rsv.Id = spv.RuleSetVersionId
              JOIN RuleSet            rs  ON rs.Id  = rsv.RuleSetId
              JOIN ParameterSetVersion psv ON psv.Id = spv.ParameterSetVersionId
              JOIN ParameterSet        ps  ON ps.Id  = psv.ParameterSetId
              WHERE spv.Id = @Id",
            new { Id = strategyProfileVersionId },
            db: DatabaseId.APS)).ToList();

        if (rows.Count == 0) return;

        context.RuleConfigs = rows.Select(r => new RuleConfig
        {
            RuleSetVersionId = r.RuleSetVersionId,
            RuleSetCode      = r.RuleSetCode,
            RuleSetName      = r.RuleSetName,
            VersionCode      = r.RuleSetVersionCode
        }).ToList();

        context.SchedulingParamsList = rows.Select(r => new SchedulingParamConfig
        {
            ParameterSetVersionId = r.ParameterSetVersionId,
            ParameterSetCode      = r.ParameterSetCode,
            ParameterSetName      = r.ParameterSetName,
            VersionCode           = r.ParameterSetVersionCode
        }).ToList();

        _logger.LogInformation("策略配置加载完成: StrategyProfileVersionId={Id}, RuleConfigs={RC}, Params={PC}",
            strategyProfileVersionId, context.RuleConfigs.Count, context.SchedulingParamsList.Count);
    }

    /// <summary>
    /// 记录 ETL 日志
    /// </summary>
    private async Task LogETLAsync(string batchNo, string step, string message, string status)
    {
        try
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
                  VALUES (@BatchNo, @Step, @Message, @Status, GETDATE())",
                new { BatchNo = batchNo, Step = step, Message = message, Status = status },
                db: DatabaseId.APS);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入ETL日志失败（非致命）");
        }
    }

    /// <summary>
    /// 构建 PeggingExecutionRequest（从 SchedulingContext 提取必要字段）
    ///
    /// 冻结区规则（文档 §2.3）：
    ///   当前时间起的 2 小时为滑动冻结窗口；MES 已下发任务不可重排
    ///
    /// 虚拟库存：
    ///   从 context.InventorySupplies 里提取（当前 V1 不含跨域供给，由后续3号位扫描补充）
    ///
    /// 拓扑序：
    ///   由 3号位（01:50 静态扫描）提供；V1 默认空字典，Pegging 内部降级为 FIFO
    /// </summary>
    private static PeggingExecutionRequest BuildPeggingRequest(
        int planVersionId,
        List<long> allOrderIds,
        SchedulingContext context,
        Dictionary<int, int> topologicalOrder)
    {
        var now = DateTime.Now;
        var orderIds = allOrderIds;

        return new PeggingExecutionRequest
        {
            PlanVersionId     = planVersionId,
            OrderIds          = orderIds,
            SnapshotAt        = now,
            FrozenWindowStart = now,
            FrozenWindowEnd   = now.AddHours(2),   // §2.3 滑动冻结窗口
            AllowCrossFactory = false,
            DefaultStrategy   = Core.Enum.PeggingStrategyType.FIFO,
            ProductFamilyIds  = context.Tasks
                .Select(t => int.TryParse(t.MaterialId, out var mid) ? mid : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList(),
            TopologicalOrder  = topologicalOrder,
            VirtualInventory  = new List<Core.Dto.VirtualInventoryItem>(),  // INTEGRATION TODO: V1验收前需接入3号位跨域传递
            MaxBomDepth       = 10,
            TimeoutSeconds    = 300,
            ExecutionMode     = "FULL_RUN",
            SchedulingContext = context  // V1.2：传递完整沙盘上下文供1号位使用
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 拓扑排序（0.5.3 — 读 Domain_Dependency 表，Kahn 算法）
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从 Domain_Dependency 表读取跨域血缘，执行 Kahn 拓扑排序。
    /// 返回 ProductFamilyId → 层级序号（0=最上游，数字越大越靠下游）。
    /// 若 Domain_Dependency 为空（V1 无跨族依赖），返回空字典，Pegging 降级为 FIFO。
    /// </summary>
    private async Task<Dictionary<int, int>> LoadTopologicalOrderAsync(
        SchedulingContext context,
        CancellationToken cancellationToken)
    {
        var edges = (await _connectionManager.QueryAsync<DomainDependencyRow>(
            @"SELECT dd.UpstreamDomainCode, dd.DownstreamDomainCode,
                     pf_up.Id AS UpstreamProductFamilyId,
                     pf_dn.Id AS DownstreamProductFamilyId
              FROM Domain_Dependency dd
              INNER JOIN ProductFamily pf_up ON pf_up.Code = dd.UpstreamDomainCode
              INNER JOIN ProductFamily pf_dn ON pf_dn.Code = dd.DownstreamDomainCode",
            db: DatabaseId.APS)).ToList();

        if (edges.Count == 0)
            return new Dictionary<int, int>();

        // 取 context 中出现的所有 ProductFamilyId 作为节点集
        var nodes = context.Tasks
            .Select(t => int.TryParse(t.MaterialId, out var mid) ? mid : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        // 补入边里出现但 context 里没有的节点（纯上游域，当前计划无其订单但依赖结构存在）
        foreach (var e in edges)
        {
            if (!nodes.Contains(e.UpstreamProductFamilyId))   nodes.Add(e.UpstreamProductFamilyId);
            if (!nodes.Contains(e.DownstreamProductFamilyId)) nodes.Add(e.DownstreamProductFamilyId);
        }

        var edgePairs = edges
            .Select(e => (e.UpstreamProductFamilyId, e.DownstreamProductFamilyId))
            .ToList();

        // Kahn 分层排序
        List<List<int>> layers;
        try
        {
            layers = TopologicalSort.SortByLayers<int>(nodes, edgePairs);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Domain_Dependency 存在循环依赖，拓扑排序失败，降级为 FIFO");
            return new Dictionary<int, int>();
        }

        // 展平为 ProductFamilyId → layerIndex
        var result = new Dictionary<int, int>();
        for (int layer = 0; layer < layers.Count; layer++)
            foreach (var pfId in layers[layer])
                result[pfId] = layer;

        return result;
    }

    public Task<SchedulingRunResult> RunDomainSchedulingAutoAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 内部 DTO（仅本类 Dapper 投影使用，不对外暴露）
    // ═══════════════════════════════════════════════════════════════════════════

    private class OrderLoadDto
    {
        public long OrderId { get; set; }
        public string OrderNo { get; set; } = string.Empty;
        public int MaterialId { get; set; }
        public string MaterialCode { get; set; } = string.Empty;
        public int ProductFamilyId { get; set; }
        public int FactoryId { get; set; }
        public decimal Quantity { get; set; }
        public string UOM { get; set; } = string.Empty;
        public DateTime CustomerDueDate { get; set; }
        public int Priority { get; set; }
        public decimal? PriorityScore { get; set; }
        public string? CustomerTier { get; set; }
        public int? LLC { get; set; }
    }

    private class ResourceLoadDto
    {
        public int ResourceId { get; set; }
        public string ResourceCode { get; set; } = string.Empty;
        public string ResourceName { get; set; } = string.Empty;
        public int FactoryId { get; set; }
        public int ProductionDepartmentId { get; set; }
        public decimal CapacityFactor { get; set; }
        public int DispatchPriority { get; set; }
        public bool LocalDisableFlag { get; set; }
    }

    private class InventoryLoadDto
    {
        public string MaterialCode { get; set; } = string.Empty;
        public int ProductFamilyId { get; set; }
        public int FactoryId { get; set; }
        public decimal AvailableQty { get; set; }
    }

    private class InTransitLoadDto
    {
        public string MaterialCode { get; set; } = string.Empty;
        public int ProductFamilyId { get; set; }
        public int FactoryId { get; set; }
        public decimal AvailableQty { get; set; }
        public DateTime? EstimatedArrivalTime { get; set; }
    }

    private class RoutingOperationDto
    {
        public int MaterialId { get; set; }
        public string OperationCode { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public decimal StandardDuration { get; set; }
        public decimal SetupTime { get; set; }
        public int OperationSeq { get; set; }
    }

    private class OperationEligibilityDto
    {
        public int MaterialId { get; set; }
        public string OperationCode { get; set; } = string.Empty;
        public int ResourceId { get; set; }
        public int Priority { get; set; }
    }

    private class TaskInsertDto
    {
        public int PlanVersionId { get; set; }
        public string TaskNo { get; set; } = string.Empty;
        public long OrderId { get; set; }
        public int MaterialId { get; set; }
        public int OperationSeq { get; set; }
        public string OperationCode { get; set; } = string.Empty;
        public int? ResourceId { get; set; }
        public string RouteCode { get; set; } = "DEFAULT";
        public int PathId { get; set; } = 1;
        public decimal Quantity { get; set; }
        public string UOM { get; set; } = string.Empty;
        public decimal? Duration { get; set; }
        public string Status { get; set; } = "Pending";
        public string TaskType { get; set; } = "PRODUCTION";
    }

    private class ScheduleRunQueryDto
    {
        public int Id { get; set; }
        public DateTime DataCutoffTime { get; set; }
        public long? StrategyProfileVersionId { get; set; }
    }

    private class DomainDependencyRow
    {
        public int UpstreamProductFamilyId { get; set; }
        public int DownstreamProductFamilyId { get; set; }
    }

    private class MESProgressLoadDto
    {
        public string ProductionInstructionNo { get; set; } = string.Empty;
        public string MaterialCode { get; set; } = string.Empty;
        public string StageCode { get; set; } = string.Empty;
        public decimal RemainingQty { get; set; }
    }

    private class StrategyConfigLoadDto
    {
        public long RuleSetVersionId { get; set; }
        public string RuleSetCode { get; set; } = string.Empty;
        public string RuleSetName { get; set; } = string.Empty;
        public string RuleSetVersionCode { get; set; } = string.Empty;
        public long ParameterSetVersionId { get; set; }
        public string ParameterSetCode { get; set; } = string.Empty;
        public string ParameterSetName { get; set; } = string.Empty;
        public string ParameterSetVersionCode { get; set; } = string.Empty;
    }
}
