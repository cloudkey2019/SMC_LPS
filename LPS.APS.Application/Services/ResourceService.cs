using System.Data;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;

namespace LPS.APS.Application.Services;

/// <summary>
/// 资源主表查询服务（5号位；G1 设备下拉数据源）
/// 数据源：dbo.Resource（只读，不改资源主档）
/// </summary>
public interface IResourceService
{
    /// <summary>资源主表列表（可按部门/类型过滤；含 hasCalendar 日历状态）</summary>
    Task<List<ResourceListItemDto>> GetListAsync(int? departmentId = null, string? resourceType = null, bool includeInactive = false, CancellationToken ct = default);
}

public class ResourceService : IResourceService
{
    private readonly DatabaseConnectionManager _connectionManager;

    public ResourceService(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<ResourceListItemDto>> GetListAsync(int? departmentId = null, string? resourceType = null, bool includeInactive = false, CancellationToken ct = default)
    {
        var sql = @"
SELECT r.Id AS ResourceId, r.ResourceCode, r.ResourceName, r.ResourceType,
       r.ProductionDepartmentId, pd.DeptName AS ProductionDepartmentName, r.IsActive,
       CASE WHEN EXISTS(SELECT 1 FROM dbo.ResourceCalendarSlot rc WHERE rc.ResourceId = r.Id)
            THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasCalendar
FROM dbo.Resource r
LEFT JOIN dbo.ProductionDepartment pd ON pd.Id = r.ProductionDepartmentId
WHERE (@Dept IS NULL OR r.ProductionDepartmentId = @Dept)
  AND (@Type IS NULL OR r.ResourceType = @Type)
  AND (@IncludeInactive = 1 OR r.IsActive = 1)
ORDER BY r.ResourceCode;";

        var rows = await _connectionManager.QueryAsync<ResourceListItemDto>(
            sql,
            new { Dept = departmentId, Type = resourceType, IncludeInactive = includeInactive ? 1 : 0 },
            CommandType.Text, DatabaseId.APS, 60);

        return rows.ToList();
    }
}