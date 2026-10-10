using System.Data;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;

namespace LPS.APS.Application.Services;

/// <summary>
/// 资源日历业务服务（5号位，设备日历维护）
/// 数据源：dbo.ResourceCalendarSlot
/// </summary>
public interface IResourceCalendarService
{
    /// <summary>批量铺窗（一次生成连续多天窗口；Days=1 覆盖逐条）</summary>
    Task<int> BulkCreateAsync(ResourceCalendarBulkRequest request, CancellationToken ct = default);

    /// <summary>查询某设备所有窗口</summary>
    Task<List<ResourceCalendarEntryDto>> GetByResourceAsync(int resourceId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);

    /// <summary>物理删除某窗口（DELETE）</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// 资源日历业务服务实现（5号位；只读+写窗，不重算排程）
/// </summary>
public class ResourceCalendarService : IResourceCalendarService
{
    private readonly DatabaseConnectionManager _connectionManager;

    public ResourceCalendarService(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<int> BulkCreateAsync(ResourceCalendarBulkRequest request, CancellationToken ct = default)
    {
        if (request.ResourceId <= 0)
            throw new ArgumentException("ResourceId must be positive", nameof(request));
        if (request.Days <= 0 || request.Days > 370)
            throw new ArgumentException("Days must be in 1..370", nameof(request));
        if (request.EndTime <= request.StartTime)
            throw new ArgumentException("EndTime must be after StartTime", nameof(request));

        // 校验设备存在
        var exists = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
            "SELECT 1 FROM dbo.Resource WHERE Id = @Id AND IsActive = 1",
            new { Id = request.ResourceId }, CommandType.Text, DatabaseId.APS, 30);
        if (exists == null)
            throw new KeyNotFoundException($"设备不存在或未启用：{request.ResourceId}");

        // 覆盖式铺窗：先物理删该资源在 [StartDate, StartDate+Days) 区间的全部旧窗，再生成新窗，保证无重叠且重复铺窗幂等（D2 回执口径）
        var rangeEnd = request.StartDate.Date.AddDays(request.Days);
        await _connectionManager.ExecuteAsync(@"
DELETE FROM dbo.ResourceCalendarSlot
WHERE ResourceId = @ResourceId
  AND StartTime >= @Start AND StartTime < @End;",
            new { ResourceId = request.ResourceId, Start = request.StartDate.Date, End = rangeEnd },
            CommandType.Text, DatabaseId.APS, 30);

        // 逐天生成新窗口
        var inserted = 0;
        for (var i = 0; i < request.Days; i++)
        {
            var day = request.StartDate.Date.AddDays(i);
            var start = day + request.StartTime;
            var end = day + request.EndTime;

            inserted += await _connectionManager.ExecuteAsync(@"
INSERT INTO dbo.ResourceCalendarSlot (ResourceId, StartTime, EndTime, AvailableFlag, Remark, CreatedAt, UpdatedAt)
VALUES (@ResourceId, @StartTime, @EndTime, @AvailableFlag, @Remark, SYSDATETIME(), SYSDATETIME());",
                new
                {
                    ResourceId = request.ResourceId,
                    StartTime = start,
                    EndTime = end,
                    AvailableFlag = request.AvailableFlag ? 1 : 0,
                    Remark = request.Remark
                }, CommandType.Text, DatabaseId.APS, 30);
        }

        return inserted;
    }

    public async Task<List<ResourceCalendarEntryDto>> GetByResourceAsync(int resourceId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var sql = @"
SELECT rc.Id, rc.ResourceId, r.ResourceCode, r.ResourceName,
       rc.StartTime, rc.EndTime, rc.AvailableFlag, rc.Remark
FROM dbo.ResourceCalendarSlot rc
JOIN dbo.Resource r ON r.Id = rc.ResourceId
WHERE rc.ResourceId = @ResourceId
  AND (@From IS NULL OR rc.StartTime >= @From)
  AND (@To IS NULL OR rc.EndTime <= @To)
ORDER BY rc.StartTime;";

        var rows = await _connectionManager.QueryAsync<ResourceCalendarEntryDto>(
            sql,
            new { ResourceId = resourceId, From = from, To = to },
            CommandType.Text, DatabaseId.APS, 60);

        return rows.ToList();
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var affected = await _connectionManager.ExecuteAsync(
            "DELETE FROM dbo.ResourceCalendarSlot WHERE Id = @Id;",
            new { Id = id }, CommandType.Text, DatabaseId.APS, 30);
        return affected > 0;
    }
}