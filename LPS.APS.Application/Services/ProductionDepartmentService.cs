using System.Data;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;

namespace LPS.APS.Application.Services;

/// <summary>
/// 生产部门查询服务（5号位；G2 部门下拉数据源）
/// 数据源：dbo.ProductionDepartment（只读）
/// </summary>
public interface IProductionDepartmentService
{
    /// <summary>生产部门列表（可选仅排程部门；默认不含软删）</summary>
    Task<List<ProductionDepartmentDto>> GetListAsync(bool? schedulingOnly = null, bool includeInactive = false, CancellationToken ct = default);
}

public class ProductionDepartmentService : IProductionDepartmentService
{
    private readonly DatabaseConnectionManager _connectionManager;

    public ProductionDepartmentService(DatabaseConnectionManager connectionManager)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public async Task<List<ProductionDepartmentDto>> GetListAsync(bool? schedulingOnly = null, bool includeInactive = false, CancellationToken ct = default)
    {
        var sql = @"
SELECT Id, DeptCode, DeptName, IsActive
FROM dbo.ProductionDepartment
WHERE (@IncludeInactive = 1 OR IsActive = 1)
  AND (@Sched IS NULL OR IsSchedulingDept = CASE WHEN @Sched = 1 THEN 1 ELSE 0 END)
ORDER BY DeptCode;";

        var rows = await _connectionManager.QueryAsync<ProductionDepartmentDto>(
            sql,
            new { IncludeInactive = includeInactive ? 1 : 0, Sched = schedulingOnly },
            CommandType.Text, DatabaseId.APS, 60);

        return rows.ToList();
    }
}