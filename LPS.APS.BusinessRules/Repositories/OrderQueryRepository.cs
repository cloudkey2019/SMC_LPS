using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using System.Data;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// 订单查询Repository实现
/// 直接读取[Order]、PeggingSupplyAllocation、Task表
/// </summary>
public class OrderQueryRepository : IOrderQueryRepository
{
    private readonly DatabaseConnectionManager _connectionManager;

    public OrderQueryRepository(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<OrderListItemDto>> QueryOrdersAsync(
        int planVersionId,
        string? orderNo = null,
        string? materialCode = null,
        string? customerName = null,
        string? factoryCode = null,
        string? domainKey = null,
        string? delayStatus = null,
        string? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken ct = default,
        IReadOnlySet<string>? allowedFactories = null,
        IReadOnlySet<string>? allowedDomains = null)
    {
        // Dapper 列表参数只能出现在 IN 内；@X IS NULL(标量) 会随 @X 一起扩成 (@p1,@p2) → "(@p1,@p2) IS NULL" 非法 SQL 4145。
        // 用 Has 标志替代标量判空；空集 fail-closed 返空，避免空集传入 IN ()。
        if (allowedFactories is { Count: 0 } || allowedDomains is { Count: 0 })
            return new List<OrderListItemDto>();
        var hasFactories = allowedFactories is { Count: > 0 };
        var hasDomains = allowedDomains is { Count: > 0 };

        var sql = @"
SELECT
    o.Id,
    o.PlanVersionId,
    o.OrderCanonicalId,
    o.OrderNo,
    o.OrderType,
    o.MaterialCode,
    o.MaterialId,
    o.CustomerName,
    o.CustomerSegment,
    f.Code AS FactoryCode,
    o.FactoryId,
    pf.Code AS ProductFamilyCode,
    o.DomainKey,
    o.Quantity,
    o.UOM,
    o.CustomerDueDate,
    o.PromisedDate,
    o.Priority,
    o.Status,
    o.DelayStatus,
    o.DemandMaturityStatus,
    o.MTS_InstructionNo,
    o.BOMNO
FROM [Order] o
LEFT JOIN Factory f ON f.Id = o.FactoryId
LEFT JOIN ProductFamily pf ON pf.Id = o.ProductFamilyId
WHERE o.PlanVersionId = @PlanVersionId
    AND (@OrderNo IS NULL OR o.OrderNo LIKE '%' + @OrderNo + '%')
    AND (@MaterialCode IS NULL OR o.MaterialCode LIKE '%' + @MaterialCode + '%')
    AND (@CustomerName IS NULL OR o.CustomerName LIKE '%' + @CustomerName + '%')
    AND (@FactoryCode IS NULL OR f.Code = @FactoryCode)
    AND (@DomainKey IS NULL OR o.DomainKey = @DomainKey)
    AND (@DelayStatus IS NULL OR o.DelayStatus = @DelayStatus)
    AND (@Status IS NULL OR o.Status = @Status)
    AND (@HasFactories = 0 OR f.Code IN @AllowedFactories)
    AND (@HasDomains = 0 OR o.DomainKey IN @AllowedDomains)
ORDER BY o.Priority DESC, o.CustomerDueDate
OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var parameters = new
        {
            PlanVersionId = planVersionId,
            OrderNo = orderNo,
            MaterialCode = materialCode,
            CustomerName = customerName,
            FactoryCode = factoryCode,
            DomainKey = domainKey,
            DelayStatus = delayStatus,
            Status = status,
            HasFactories = hasFactories,
            HasDomains = hasDomains,
            AllowedFactories = allowedFactories,
            AllowedDomains = allowedDomains,
            Skip = skip,
            Take = take
        };

        var results = await _connectionManager.QueryAsync<OrderListItemDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 30);

        return results.ToList();
    }

    public async Task<OrderDetailDto?> GetOrderDetailAsync(
        int planVersionId,
        long orderId,
        CancellationToken ct = default)
    {
        // 1. 查询订单基本信息
        var orderSql = @"
SELECT
    o.Id,
    o.PlanVersionId,
    o.OrderCanonicalId,
    o.OrderNo,
    o.OrderType,
    o.MaterialCode,
    o.MaterialId,
    o.CustomerName,
    o.CustomerSegment,
    f.Code AS FactoryCode,
    o.FactoryId,
    pf.Code AS ProductFamilyCode,
    o.DomainKey,
    o.Quantity,
    o.UOM,
    o.CustomerDueDate,
    o.PromisedDate,
    o.Priority,
    o.Status,
    o.DelayStatus,
    o.DemandMaturityStatus,
    o.MTS_InstructionNo,
    o.BOMNO
FROM [Order] o
LEFT JOIN Factory f ON f.Id = o.FactoryId
LEFT JOIN ProductFamily pf ON pf.Id = o.ProductFamilyId
WHERE o.PlanVersionId = @PlanVersionId AND o.Id = @OrderId";

        var orderParams = new { PlanVersionId = planVersionId, OrderId = orderId };
        var order = (await _connectionManager.QueryAsync<OrderListItemDto>(
            orderSql, orderParams, CommandType.Text, DatabaseId.APS, commandTimeout: 10))
            .FirstOrDefault();

        if (order == null) return null;

        // 2. 查询Pegging承接
        var peggingSql = @"
SELECT
    AllocationSequence,
    MaterialCode,
    AllocatedQty,
    SupplyType,
    SupplyFactoryCode,
    SupplyWarehouseCode,
    ERPProperty,
    SupplyDocumentNo,
    SupplyDocumentType,
    ETA,
    KnownAvailableTime,
    CommitmentStatus,
    SupplyMode
FROM PeggingSupplyAllocation
WHERE PlanVersionId = @PlanVersionId
    AND (RootOrderId = @OrderId OR CurrentOrderId = @OrderId)
ORDER BY AllocationSequence";

        var peggingParams = new { PlanVersionId = planVersionId, OrderId = orderId };
        var pegging = (await _connectionManager.QueryAsync<OrderPeggingDto>(
            peggingSql, peggingParams, CommandType.Text, DatabaseId.APS, commandTimeout: 30))
            .ToList();

        // 3. 查询生产计划（FinalTask）
        var taskSql = @"
SELECT
    t.Id AS TaskId,
    t.TaskNo,
    t.OperationCode,
    t.OperationSeq,
    r.ResourceCode,
    r.ResourceName,
    t.Quantity,
    t.PlannedProcessQty,
    t.PlannedStartTime,
    t.PlannedEndTime,
    t.Duration,
    t.Status,
    t.IsCriticalPath,
    t.IsLocked,
    t.MTS_InstructionNo
FROM Task t
LEFT JOIN Resource r ON r.Id = t.ResourceId
WHERE t.PlanVersionId = @PlanVersionId AND t.OrderId = @OrderId
ORDER BY t.OperationSeq";

        var taskParams = new { PlanVersionId = planVersionId, OrderId = orderId };
        var tasks = (await _connectionManager.QueryAsync<OrderTaskDto>(
            taskSql, taskParams, CommandType.Text, DatabaseId.APS, commandTimeout: 30))
            .ToList();

        return new OrderDetailDto
        {
            Order = order,
            Pegging = pegging,
            Tasks = tasks
        };
    }

    public async Task<OrderSummaryDto> GetSummaryAsync(
        int planVersionId,
        IReadOnlySet<string>? allowedFactories = null,
        IReadOnlySet<string>? allowedDomains = null,
        CancellationToken ct = default)
    {
        // Dapper 列表参数只进 IN；空集 fail-closed 返空摘要。
        if (allowedFactories is { Count: 0 } || allowedDomains is { Count: 0 })
            return new OrderSummaryDto { PlanVersionId = planVersionId };
        var hasFactories = allowedFactories is { Count: > 0 };
        var hasDomains = allowedDomains is { Count: > 0 };

        var sql = @"
SELECT
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN o.DelayStatus IS NULL OR o.DelayStatus = 'ON_TIME' THEN 1 ELSE 0 END) AS OnTimeCount,
    SUM(CASE WHEN o.DelayStatus IN ('FIRST_DELAY', 'REPEATED_DELAY') THEN 1 ELSE 0 END) AS DelayedCount,
    SUM(CASE WHEN o.DelayStatus = 'RISK' THEN 1 ELSE 0 END) AS RiskCount,
    SUM(CASE WHEN o.Status = 'UNSCHEDULED' THEN 1 ELSE 0 END) AS UnscheduledCount
FROM [Order] o
LEFT JOIN Factory f ON f.Id = o.FactoryId
WHERE o.PlanVersionId = @PlanVersionId
    AND (@HasFactories = 0 OR f.Code IN @AllowedFactories)
    AND (@HasDomains = 0 OR o.DomainKey IN @AllowedDomains)";

        var parameters = new
        {
            PlanVersionId = planVersionId,
            HasFactories = hasFactories,
            HasDomains = hasDomains,
            AllowedFactories = allowedFactories,
            AllowedDomains = allowedDomains
        };

        var result = await _connectionManager.QueryFirstOrDefaultAsync<OrderSummaryDto>(
            sql, parameters, CommandType.Text, DatabaseId.APS, commandTimeout: 10);

        return result ?? new OrderSummaryDto { PlanVersionId = planVersionId };
    }
}
