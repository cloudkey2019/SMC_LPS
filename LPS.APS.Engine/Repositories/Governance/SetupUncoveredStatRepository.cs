using Dapper;
using LPS.APS.Core.DTOs.Setup;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// Setup 规则缺失（uncovered-stats）只读聚合查询仓储实现（Dapper + APS_Production）。
/// #10 uncovered-stats 端点数据源：<see cref="Task"/>.[SetupSource] = 'SETUP_RULE_MISSING_ZERO_FALLBACK' 命中频次聚合。
/// 只读查询，无任何写操作，禁止查询层反推；列名对齐冻结 DDL v5.1.2（Task / PlanVersion / Resource / ProductionDepartment / Material）。
/// 3号位 依 G4 只读查询归属自写（Setup 治理域只读事实），与 <see cref="MasterDataLookupRepository"/> 同源先例。
/// </summary>
public sealed class SetupUncoveredStatRepository : ISetupUncoveredStatRepository
{
    /// <summary>
    /// 规则缺失兜底值（默认换型缺失，SetupSource 4 态之一）。
    /// Engine 不可引 Scheduling <c>SetupOptimizer</c>（红线 #1 反向引用），故用字面量（与 Task.cs:60 契约注释一致）。
    /// </summary>
    private const string SetupSourceMissingZeroFallback = "SETUP_RULE_MISSING_ZERO_FALLBACK";

    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<SetupUncoveredStatRepository> _logger;

    public SetupUncoveredStatRepository(DatabaseConnectionManager connectionManager, ILogger<SetupUncoveredStatRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SetupUncoveredStatDto>> GetUncoveredAsync(
        int? runId,
        int? departmentId,
        string? operationCode,
        int? resourceId,
        int? materialId,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    pv.[SourceScheduleRunId]                 AS [ScheduleRunId],
    r.[ProductionDepartmentId]               AS [DepartmentId],
    pd.[DeptCode]                            AS [DepartmentCode],
    t.[OperationCode]                        AS [OperationCode],
    t.[ResourceId]                           AS [ResourceId],
    r.[ResourceCode]                         AS [ResourceCode],
    t.[MaterialId]                           AS [MaterialId],
    m.[MaterialCode]                         AS [MaterialCode],
    COUNT(*)                                 AS [HitCount],
    MIN(t.[Id])                              AS [SampleTaskId]
FROM [dbo].[Task] t
INNER JOIN [dbo].[PlanVersion]          pv ON t.[PlanVersionId]      = pv.[Id]
INNER JOIN [dbo].[Resource]              r ON t.[ResourceId]         = r.[Id]
INNER JOIN [dbo].[ProductionDepartment] pd ON r.[ProductionDepartmentId] = pd.[Id]
INNER JOIN [dbo].[Material]              m ON t.[MaterialId]         = m.[Id]
WHERE t.[SetupSource] = @SetupSource
  AND (@RunId         IS NULL OR pv.[SourceScheduleRunId]   = @RunId)
  AND (@DepartmentId  IS NULL OR r.[ProductionDepartmentId] = @DepartmentId)
  AND (@OperationCode IS NULL OR t.[OperationCode]          = @OperationCode)
  AND (@ResourceId    IS NULL OR t.[ResourceId]             = @ResourceId)
  AND (@MaterialId    IS NULL OR t.[MaterialId]             = @MaterialId)
GROUP BY pv.[SourceScheduleRunId], r.[ProductionDepartmentId], pd.[DeptCode],
         t.[OperationCode], t.[ResourceId], r.[ResourceCode], t.[MaterialId], m.[MaterialCode]
ORDER BY [ScheduleRunId], [DepartmentCode], [OperationCode], [ResourceId], [MaterialCode]";

        return (await _connectionManager.QueryAsync<SetupUncoveredStatDto>(
            sql,
            new
            {
                SetupSource = SetupSourceMissingZeroFallback,
                RunId = runId,
                DepartmentId = departmentId,
                OperationCode = operationCode,
                ResourceId = resourceId,
                MaterialId = materialId,
            },
            db: DatabaseId.APS))
            .ToList();
    }
}