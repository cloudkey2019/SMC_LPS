using System.Text.Json;
using Dapper;
using Hangfire;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Models.Scheduling;
using LPS.APS.Engine.Data;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Shared.Models;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 单域排程 Job 实现（B2 分域并行方案）
///
/// 执行流程：
///   1. 更新 DomainScheduleStatus → RUNNING
///   2. 装载本域 SchedulingContext（按 ProductFamilyId 过滤）
///   3. 若非第0层，从 VirtualInventoryBalance 读上游虚拟库存
///   4. Pegging + Solve
///   5. 落盘本域排程结果
///   6. 写 VirtualInventoryBalance（供下游域消费）
///   7. 更新 DomainScheduleStatus → COMPLETED / FAILED
/// </summary>
public class DomainSchedulingJob : IDomainSchedulingJob
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly FiniteCapacitySolver _solver;
    private readonly IPeggingOrchestrator _peggingOrchestrator;
    private readonly ILogger<DomainSchedulingJob> _logger;

    public DomainSchedulingJob(
        DatabaseConnectionManager connectionManager,
        FiniteCapacitySolver solver,
        IPeggingOrchestrator peggingOrchestrator,
        ILogger<DomainSchedulingJob> logger)
    {
        _connectionManager   = connectionManager   ?? throw new ArgumentNullException(nameof(connectionManager));
        _solver              = solver              ?? throw new ArgumentNullException(nameof(solver));
        _peggingOrchestrator = peggingOrchestrator ?? throw new ArgumentNullException(nameof(peggingOrchestrator));
        _logger              = logger              ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 60, 120 })]
    public async Task ExecuteAsync(
        int planVersionId,
        int scheduleRunId,
        string domainKey,
        int productFamilyId,
        int layerIndex,
        long strategyProfileVersionId)
    {
        var cancellationToken = CancellationToken.None;
        long? strategyProfileVersionIdNullable = strategyProfileVersionId == 0 ? null : strategyProfileVersionId;
        _logger.LogInformation("[{PlanVersionId}] 域排程开始: domainKey={DomainKey}, layer={Layer}",
            planVersionId, domainKey, layerIndex);

        await UpdateDomainStatusAsync(planVersionId, productFamilyId, "RUNNING", null);

        try
        {
            var context = await LoadDomainContextAsync(
                planVersionId, scheduleRunId, productFamilyId, strategyProfileVersionIdNullable, cancellationToken);

            if (layerIndex > 0)
                await LoadVirtualInventoryAsync(context, planVersionId, cancellationToken);

            var peggingRequest  = BuildDomainPeggingRequest(planVersionId, productFamilyId, context);
            var peggingResults  = (await _peggingOrchestrator.ExecuteBatchPeggingWorkflowAsync(
                peggingRequest, cancellationToken)).ToList();

            var failCount = peggingResults.Count(r => !r.IsSuccess);
            if (failCount > 0)
                _logger.LogWarning("[{PlanVersionId}] 域 {DomainKey} Pegging 部分失败: {Fail}/{Total}",
                    planVersionId, domainKey, failCount, peggingResults.Count);

            var solveResult = _solver.Solve(context, new SchedulingOptions());
            _logger.LogInformation("[{PlanVersionId}] 域 {DomainKey} 求解完成: {Summary}",
                planVersionId, domainKey, solveResult.Summary);

            await PersistDomainResultAsync(planVersionId, productFamilyId, context, cancellationToken);
            await WriteVirtualInventoryAsync(context, planVersionId, productFamilyId, cancellationToken);
            await UpdateDomainStatusAsync(planVersionId, productFamilyId, "COMPLETED", null);

            _logger.LogInformation("[{PlanVersionId}] 域 {DomainKey} 排程完成: 已排={Scheduled}, 未排={Unscheduled}",
                planVersionId, domainKey, solveResult.ScheduledCount, solveResult.UnscheduledCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{PlanVersionId}] 域 {DomainKey} 排程失败", planVersionId, domainKey);
            await UpdateDomainStatusAsync(planVersionId, productFamilyId, "FAILED", ex.Message);
            throw;
        }
    }

    // ── 装载本域 SchedulingContext ────────────────────────────────────────────

    private async Task<SchedulingContext> LoadDomainContextAsync(
        int planVersionId,
        int scheduleRunId,
        int productFamilyId,
        long? strategyProfileVersionId,
        CancellationToken cancellationToken)
    {
        var planVersion = await _connectionManager.QueryFirstOrDefaultAsync<PlanVersionInfoDto>(
            "SELECT Id, VersionCode, PlanHorizonStart, PlanHorizonEnd FROM PlanVersion WHERE Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS)
            ?? throw new InvalidOperationException($"计划版本不存在: {planVersionId}");

        var context = new SchedulingContext
        {
            PlanVersionId            = planVersionId.ToString(),
            ScheduleRunId            = scheduleRunId,
            DomainKey                = productFamilyId.ToString(),
            PlanHorizonStart         = planVersion.PlanHorizonStart,
            PlanHorizonEnd           = planVersion.PlanHorizonEnd,
            StrategyProfileVersionId = strategyProfileVersionId
        };

        await LoadResourcesAsync(context, cancellationToken);
        await LoadInventoryAsync(context, cancellationToken);
        await LoadDomainTasksAsync(context, planVersionId, productFamilyId, cancellationToken);

        return context;
    }

    private async Task LoadDomainTasksAsync(
        SchedulingContext context, int planVersionId, int productFamilyId, CancellationToken cancellationToken)
    {
        var rows = await _connectionManager.QueryAsync<DomainTaskDto>(
            @"SELECT t.Id AS TaskId, t.OrderId, t.MaterialCode, t.ResourceId,
                     t.OperationCode, t.RouteCode, t.PathId, t.OperationSeq,
                     t.Priority, t.Quantity, t.DurationMinutes,
                     t.SetupAttribute, t.CustomerDueDate, t.IsLocked, t.IsFrozen,
                     t.PredecessorTaskIds
              FROM [Task] t
              INNER JOIN [Order] o ON o.Id = t.OrderId
              WHERE t.PlanVersionId   = @PlanVersionId
                AND o.ProductFamilyId = @ProductFamilyId",
            new { PlanVersionId = planVersionId, ProductFamilyId = productFamilyId },
            db: DatabaseId.APS);

        foreach (var row in rows)
        {
            context.Tasks.Add(new SchedulingTask
            {
                TaskId             = row.TaskId.ToString(),
                OrderId            = row.OrderId.ToString(),
                MaterialId         = row.MaterialCode,
                ResourceId         = row.ResourceId,
                OperationCode      = row.OperationCode,
                RouteCode          = row.RouteCode,
                PathId             = row.PathId,
                OperationSeq       = row.OperationSeq,
                Priority           = row.Priority,
                Quantity           = row.Quantity,
                DurationMinutes    = row.DurationMinutes,
                SetupAttribute     = row.SetupAttribute,
                CustomerDueDate    = row.CustomerDueDate,
                IsLocked           = row.IsLocked,
                IsFrozen           = row.IsFrozen,
                PredecessorTaskIds = string.IsNullOrEmpty(row.PredecessorTaskIds)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(row.PredecessorTaskIds) ?? new List<string>()
            });
        }
    }

    private async Task LoadResourcesAsync(SchedulingContext context, CancellationToken cancellationToken)
    {
        var rows = await _connectionManager.QueryAsync<ResourceDto>(
            @"SELECT r.Id, r.ResourceName, r.FactoryId,
                     r.ProductionDepartmentId, r.CapacityFactor, r.DispatchPriority
              FROM Resource r
              WHERE r.IsActive = 1",
            db: DatabaseId.APS);

        foreach (var r in rows)
        {
            context.Resources.Add(new SchedulingResource
            {
                ResourceId             = r.Id.ToString(),
                ResourceName           = r.ResourceName,
                FactoryId              = r.FactoryId.ToString(),
                ProductionDepartmentId = r.ProductionDepartmentId,
                CapacityFactor         = r.CapacityFactor,
                DispatchPriority       = r.DispatchPriority,
                IsAvailable            = true
            });
        }
    }

    private async Task LoadInventoryAsync(SchedulingContext context, CancellationToken cancellationToken)
    {
        var rows = await _connectionManager.QueryAsync<InventoryDto>(
            @"SELECT ib.MaterialCode, ib.ProductFamilyId, ib.FactoryId,
                     (ib.OnHandQty - ISNULL(ib.AllocatedQty, 0)) AS AvailableQty
              FROM InventoryBalance ib
              WHERE (ib.OnHandQty - ISNULL(ib.AllocatedQty, 0)) > 0",
            db: DatabaseId.APS);

        foreach (var row in rows)
        {
            var key = SchedulingContext.BuildInventoryKey(row.MaterialCode, row.ProductFamilyId, row.FactoryId);
            context.InventorySupplies[key] = row.AvailableQty;
        }
    }

    // ── 虚拟库存（跨域传递）────────────────────────────────────────────────────

    private async Task LoadVirtualInventoryAsync(
        SchedulingContext context, int planVersionId, CancellationToken cancellationToken)
    {
        var rows = await _connectionManager.QueryAsync<VirtualInventoryRow>(
            @"SELECT MaterialCode, FactoryCode, VirtualQty, AvailableAt
              FROM VirtualInventoryBalance
              WHERE PlanVersionId = @PlanVersionId",
            new { PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        foreach (var row in rows)
            context.VirtualInventory[$"{row.MaterialCode}|{row.FactoryCode}"] = row.AvailableAt;
    }

    private async Task WriteVirtualInventoryAsync(
        SchedulingContext context, int planVersionId, int productFamilyId, CancellationToken cancellationToken)
    {
        var materialMaxEnd = context.Tasks
            .Where(t => t.PlannedEndTime.HasValue)
            .GroupBy(t => t.MaterialId)
            .Select(g => new
            {
                MaterialCode = g.Key,
                AvailableAt  = g.Max(t => t.PlannedEndTime!.Value)
            })
            .ToList();

        if (materialMaxEnd.Count == 0) return;

        var factoryCode = context.Resources.FirstOrDefault()?.FactoryId ?? "DEFAULT";

        var rows = materialMaxEnd.Select(m => new
        {
            PlanVersionId         = planVersionId,
            SourceProductFamilyId = productFamilyId,
            MaterialCode          = m.MaterialCode,
            FactoryCode           = factoryCode,
            VirtualQty            = 1m,
            AvailableAt           = m.AvailableAt
        }).ToList();

        await _connectionManager.ExecuteAsync(
            @"INSERT INTO VirtualInventoryBalance
                (PlanVersionId, SourceProductFamilyId, MaterialCode, FactoryCode, VirtualQty, AvailableAt, CreatedAt)
              VALUES
                (@PlanVersionId, @SourceProductFamilyId, @MaterialCode, @FactoryCode, @VirtualQty, @AvailableAt, GETDATE())",
            rows,
            db: DatabaseId.APS);

        _logger.LogInformation("[{PlanVersionId}] 域 {PfId} 写入虚拟库存: {Count} 条",
            planVersionId, productFamilyId, rows.Count);
    }

    // ── 落盘排程结果 ──────────────────────────────────────────────────────────

    private async Task PersistDomainResultAsync(
        int planVersionId, int productFamilyId, SchedulingContext context, CancellationToken cancellationToken)
    {
        var scheduledTasks = context.Tasks
            .Where(t => t.PlannedStartTime.HasValue && t.PlannedEndTime.HasValue)
            .Select(t => new
            {
                TaskId           = long.Parse(t.TaskId),
                PlannedStartTime = t.PlannedStartTime!.Value,
                PlannedEndTime   = t.PlannedEndTime!.Value
            })
            .ToList();

        if (scheduledTasks.Count == 0) return;

        await _connectionManager.ExecuteAsync(
            @"UPDATE [Task]
              SET PlannedStartTime = @PlannedStartTime,
                  PlannedEndTime   = @PlannedEndTime,
                  UpdatedAt        = GETDATE()
              WHERE Id = @TaskId",
            scheduledTasks,
            db: DatabaseId.APS);

        _logger.LogInformation("[{PlanVersionId}] 域 {PfId} 落盘: {Count} 个 Task",
            planVersionId, productFamilyId, scheduledTasks.Count);
    }

    // ── DomainScheduleStatus 辅助 ─────────────────────────────────────────────

    private async Task UpdateDomainStatusAsync(
        int planVersionId, int productFamilyId, string status, string? errorMessage)
    {
        if (status == "RUNNING")
        {
            await _connectionManager.ExecuteAsync(
                @"UPDATE DomainScheduleStatus
                  SET Status = @Status, StartedAt = GETDATE()
                  WHERE PlanVersionId = @PlanVersionId AND ProductFamilyId = @ProductFamilyId",
                new { Status = status, PlanVersionId = planVersionId, ProductFamilyId = productFamilyId },
                db: DatabaseId.APS);
        }
        else
        {
            await _connectionManager.ExecuteAsync(
                @"UPDATE DomainScheduleStatus
                  SET Status = @Status, CompletedAt = GETDATE(), ErrorMessage = @ErrorMessage
                  WHERE PlanVersionId = @PlanVersionId AND ProductFamilyId = @ProductFamilyId",
                new { Status = status, ErrorMessage = errorMessage,
                      PlanVersionId = planVersionId, ProductFamilyId = productFamilyId },
                db: DatabaseId.APS);
        }
    }

    // ── Pegging 请求构建 ───────────────────────────────────────────────────────

    private static PeggingExecutionRequest BuildDomainPeggingRequest(
        int planVersionId, int productFamilyId, SchedulingContext context)
    {
        var now      = DateTime.Now;
        var orderIds = context.Tasks
            .Select(t => long.TryParse(t.OrderId, out var id) ? id : 0L)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        return new PeggingExecutionRequest
        {
            PlanVersionId     = planVersionId,
            OrderIds          = orderIds,
            SnapshotAt        = now,
            FrozenWindowStart = now,
            FrozenWindowEnd   = now.AddHours(2),
            AllowCrossFactory = false,
            DefaultStrategy   = Core.Enum.PeggingStrategyType.FIFO,
            ProductFamilyIds  = new List<int> { productFamilyId },
            TopologicalOrder  = new Dictionary<int, int>(),
            VirtualInventory  = new List<Core.Dto.VirtualInventoryItem>(),
            MaxBomDepth    = 10,
            TimeoutSeconds = 300,
            ExecutionMode  = "DOMAIN_RUN"
        };
    }

    // ── 内部 DTO ──────────────────────────────────────────────────────────────

    private class PlanVersionInfoDto
    {
        public int      Id               { get; set; }
        public string   VersionCode      { get; set; } = string.Empty;
        public DateTime PlanHorizonStart { get; set; }
        public DateTime PlanHorizonEnd   { get; set; }
    }

    private class DomainTaskDto
    {
        public long     TaskId             { get; set; }
        public long     OrderId            { get; set; }
        public string   MaterialCode       { get; set; } = string.Empty;
        public string   ResourceId         { get; set; } = string.Empty;
        public string   OperationCode      { get; set; } = string.Empty;
        public string   RouteCode          { get; set; } = "DEFAULT";
        public int      PathId             { get; set; } = 1;
        public int      OperationSeq       { get; set; }
        public int      Priority           { get; set; }
        public decimal  Quantity           { get; set; }
        public double   DurationMinutes    { get; set; }
        public string?  SetupAttribute     { get; set; }
        public DateTime? CustomerDueDate   { get; set; }
        public bool     IsLocked           { get; set; }
        public bool     IsFrozen           { get; set; }
        public string?  PredecessorTaskIds { get; set; }
    }

    private class ResourceDto
    {
        public int     Id                    { get; set; }
        public string  ResourceName          { get; set; } = string.Empty;
        public int     FactoryId             { get; set; }
        public int     ProductionDepartmentId { get; set; }
        public decimal CapacityFactor        { get; set; } = 1m;
        public int     DispatchPriority      { get; set; } = 100;
    }

    private class InventoryDto
    {
        public string  MaterialCode    { get; set; } = string.Empty;
        public int     ProductFamilyId { get; set; }
        public int     FactoryId       { get; set; }
        public decimal AvailableQty    { get; set; }
    }

    private class VirtualInventoryRow
    {
        public string   MaterialCode { get; set; } = string.Empty;
        public string   FactoryCode  { get; set; } = string.Empty;
        public decimal  VirtualQty   { get; set; }
        public DateTime AvailableAt  { get; set; }
    }
}
