using System.Data;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;

namespace LPS.APS.Application.Services;

/// <summary>
/// 人工能力槽业务服务（5号位；主档软删，窗口物理删）
/// 数据源：dbo.ManualCapacitySlot（主档）+ dbo.ManualCapacitySlotCalendar（窗口）
/// </summary>
public interface IManualCapacityService
{
    /// <summary>新增人工槽主档（唯一键 Dept+OperationName+SlotCode）</summary>
    Task<ManualCapacitySlotDto> CreateSlotAsync(ManualCapacitySlotDto slot, CancellationToken ct = default);

    /// <summary>查询人工槽主档（可按部门/工序过滤，支持含软删）</summary>
    Task<List<ManualCapacitySlotDto>> GetSlotsAsync(int? departmentId = null, string? operationName = null, bool includeInactive = false, CancellationToken ct = default);

    /// <summary>软删人工槽主档（IsActive=0）</summary>
    Task<bool> SoftDeleteSlotAsync(int manualSlotId, CancellationToken ct = default);

    /// <summary>批量铺人工槽窗口（连续多天；Days=1 覆盖逐条）</summary>
    Task<int> BulkCreateCalendarAsync(ManualSlotCalendarBulkRequest request, CancellationToken ct = default);

    /// <summary>查询某人工槽所有窗口</summary>
    Task<List<ManualSlotCalendarDto>> GetCalendarBySlotAsync(int manualSlotId, CancellationToken ct = default);

    /// <summary>物理删除某人工槽窗口（DELETE）</summary>
    Task<bool> DeleteCalendarAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// 人工能力槽业务服务实现（5号位）
/// </summary>
public class ManualCapacityService : IManualCapacityService
{
    private readonly DatabaseConnectionManager _connectionManager;

    public ManualCapacityService(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<ManualCapacitySlotDto> CreateSlotAsync(ManualCapacitySlotDto slot, CancellationToken ct = default)
    {
        if (slot.ProductionDepartmentId <= 0)
            throw new ArgumentException("ProductionDepartmentId must be positive", nameof(slot));
        if (string.IsNullOrWhiteSpace(slot.OperationName))
            throw new ArgumentException("OperationName is required", nameof(slot));
        if (string.IsNullOrWhiteSpace(slot.SlotCode))
            throw new ArgumentException("SlotCode is required", nameof(slot));

        // 唯一键校验：同一 (Dept, OperationName, SlotCode) 不得重复
        var dup = await _connectionManager.QueryFirstOrDefaultAsync<long?>(
            @"SELECT Id FROM dbo.ManualCapacitySlot
              WHERE ProductionDepartmentId=@Dept AND OperationName=@Op AND SlotCode=@Slot
                AND IsActive=1;",
            new { Dept = slot.ProductionDepartmentId, Op = slot.OperationName, Slot = slot.SlotCode },
            CommandType.Text, DatabaseId.APS, 30);
        if (dup != null)
            throw new InvalidOperationException($"人工槽已存在：(部门 {slot.ProductionDepartmentId}, 工序 {slot.OperationName}, 槽 {slot.SlotCode})");

        var newId = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            @"INSERT INTO dbo.ManualCapacitySlot (ProductionDepartmentId, OperationName, SlotCode, IsActive, CreatedAt, UpdatedAt)
              VALUES (@Dept, @Op, @Slot, 1, SYSDATETIME(), SYSDATETIME());
              SELECT SCOPE_IDENTITY();",
            new { Dept = slot.ProductionDepartmentId, Op = slot.OperationName, Slot = slot.SlotCode },
            CommandType.Text, DatabaseId.APS, 30);

        return new ManualCapacitySlotDto
        {
            ManualSlotId = newId,
            ProductionDepartmentId = slot.ProductionDepartmentId,
            OperationName = slot.OperationName,
            SlotCode = slot.SlotCode,
            IsActive = true
        };
    }

    public async Task<List<ManualCapacitySlotDto>> GetSlotsAsync(int? departmentId = null, string? operationName = null, bool includeInactive = false, CancellationToken ct = default)
    {
        var sql = @"
SELECT ms.ManualSlotId, ms.ProductionDepartmentId, pd.DeptName AS DepartmentName,
       ms.OperationName, ms.SlotCode, ms.IsActive,
       CASE WHEN EXISTS(SELECT 1 FROM dbo.ManualCapacitySlotCalendar mc WHERE mc.ManualSlotId = ms.ManualSlotId)
            THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasCalendar
FROM dbo.ManualCapacitySlot ms
LEFT JOIN dbo.ProductionDepartment pd ON pd.Id = ms.ProductionDepartmentId
WHERE (@Dept IS NULL OR ms.ProductionDepartmentId = @Dept)
  AND (@Op IS NULL OR ms.OperationName = @Op)
  AND (@IncludeInactive = 1 OR ms.IsActive = 1)
ORDER BY ms.ProductionDepartmentId, ms.OperationName, ms.SlotCode;";

        var rows = await _connectionManager.QueryAsync<ManualCapacitySlotDto>(
            sql,
            new { Dept = departmentId, Op = operationName, IncludeInactive = includeInactive ? 1 : 0 },
            CommandType.Text, DatabaseId.APS, 60);

        return rows.ToList();
    }

    public async Task<bool> SoftDeleteSlotAsync(int manualSlotId, CancellationToken ct = default)
    {
        var affected = await _connectionManager.ExecuteAsync(
            "UPDATE dbo.ManualCapacitySlot SET IsActive=0, UpdatedAt=SYSDATETIME() WHERE ManualSlotId=@Id AND IsActive=1;",
            new { Id = manualSlotId }, CommandType.Text, DatabaseId.APS, 30);
        return affected > 0;
    }

    public async Task<int> BulkCreateCalendarAsync(ManualSlotCalendarBulkRequest request, CancellationToken ct = default)
    {
        if (request.ManualSlotId <= 0)
            throw new ArgumentException("ManualSlotId must be positive", nameof(request));
        if (request.Days <= 0 || request.Days > 370)
            throw new ArgumentException("Days must be in 1..370", nameof(request));
        if (request.EndTime <= request.StartTime)
            throw new ArgumentException("EndTime must be after StartTime", nameof(request));

        // 校验人工槽存在且未软删
        var exists = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM dbo.ManualCapacitySlot WHERE ManualSlotId=@Id AND IsActive=1;",
            new { Id = request.ManualSlotId }, CommandType.Text, DatabaseId.APS, 30);
        if (exists == null)
            throw new KeyNotFoundException($"人工槽不存在或未启用：{request.ManualSlotId}");

        // 覆盖式铺窗：先物理删该人工槽在 [StartDate, StartDate+Days) 区间的全部旧窗，再生成新窗，保证无重叠且重复铺窗幂等（D2 回执口径）
        var rangeEnd = request.StartDate.Date.AddDays(request.Days);
        await _connectionManager.ExecuteAsync(@"
DELETE FROM dbo.ManualCapacitySlotCalendar
WHERE ManualSlotId = @ManualSlotId
  AND StartTime >= @Start AND StartTime < @End;",
            new { ManualSlotId = request.ManualSlotId, Start = request.StartDate.Date, End = rangeEnd },
            CommandType.Text, DatabaseId.APS, 30);

        // 逐天生成新窗口
        var inserted = 0;
        for (var i = 0; i < request.Days; i++)
        {
            var day = request.StartDate.Date.AddDays(i);
            var start = day + request.StartTime;
            var end = day + request.EndTime;

            inserted += await _connectionManager.ExecuteAsync(@"
INSERT INTO dbo.ManualCapacitySlotCalendar (ManualSlotId, StartTime, EndTime, AvailableFlag, Remark, CreatedAt, UpdatedAt)
VALUES (@ManualSlotId, @StartTime, @EndTime, @AvailableFlag, @Remark, SYSDATETIME(), SYSDATETIME());",
                new
                {
                    ManualSlotId = request.ManualSlotId,
                    StartTime = start,
                    EndTime = end,
                    AvailableFlag = request.AvailableFlag ? 1 : 0,
                    Remark = request.Remark
                }, CommandType.Text, DatabaseId.APS, 30);
        }

        return inserted;
    }

    public async Task<List<ManualSlotCalendarDto>> GetCalendarBySlotAsync(int manualSlotId, CancellationToken ct = default)
    {
        var sql = @"
SELECT mc.Id, mc.ManualSlotId, mc.StartTime, mc.EndTime, mc.AvailableFlag, mc.Remark
FROM dbo.ManualCapacitySlotCalendar mc
WHERE mc.ManualSlotId = @ManualSlotId
ORDER BY mc.StartTime;";

        var rows = await _connectionManager.QueryAsync<ManualSlotCalendarDto>(
            sql, new { ManualSlotId = manualSlotId }, CommandType.Text, DatabaseId.APS, 60);

        return rows.ToList();
    }

    public async Task<bool> DeleteCalendarAsync(long id, CancellationToken ct = default)
    {
        var affected = await _connectionManager.ExecuteAsync(
            "DELETE FROM dbo.ManualCapacitySlotCalendar WHERE Id = @Id;",
            new { Id = id }, CommandType.Text, DatabaseId.APS, 30);
        return affected > 0;
    }
}