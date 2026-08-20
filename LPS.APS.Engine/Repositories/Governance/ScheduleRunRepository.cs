using Dapper;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Repositories.Governance;

/// <summary>
/// ScheduleRun 治理仓储实现（Dapper + APS_Production）
/// 对应表：APS_Production.dbo.ScheduleRun（冻结 DDL v5.1.2 §3.1）
/// 边界：只读取冻结列；仅"FAILED 恢复新建"一条写入路径（IRunLifecycleService.RecoverFailedRunAsync 内部使用）；
///       不重写 2号位运行状态执行流转。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class ScheduleRunRepository : IScheduleRunRepository
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<ScheduleRunRepository> _logger;

    public ScheduleRunRepository(
        DatabaseConnectionManager connectionManager,
        ILogger<ScheduleRunRepository> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task<ScheduleRunGov?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT [Id], [RunType], [Status], [TriggeredBy], [DataCutoffTime],
                   [StrategyProfileVersionId], [ExpectedDomainKeysJson],
                   [StartedAt], [CompletedAt], [ErrorMessage]
            FROM [dbo].[ScheduleRun]
            WHERE [Id] = @Id";

        return await _connectionManager.QueryFirstOrDefaultAsync<ScheduleRunGov>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<int> InsertForRecoveryAsync(ScheduleRunGov source, string triggeredBy, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO [dbo].[ScheduleRun]
                ([RunType], [Status], [TriggeredBy], [DataCutoffTime],
                 [StrategyProfileVersionId], [ExpectedDomainKeysJson], [StartedAt], [CreatedAt])
            OUTPUT INSERTED.[Id]
            VALUES (@RunType, 'RUNNING', @TriggeredBy, GETDATE(),
                    @StrategyProfileVersionId, @ExpectedDomainKeysJson, GETDATE(), GETDATE())";

        var id = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            sql,
            new
            {
                source.RunType,
                TriggeredBy = triggeredBy,
                source.StrategyProfileVersionId,
                source.ExpectedDomainKeysJson,
            },
            db: DatabaseId.APS);

        if (id <= 0)
        {
            throw new InvalidOperationException($"FAILED 恢复新建 ScheduleRun 失败（源运行 {source.Id}）");
        }

        _logger.LogInformation("ScheduleRun 恢复新建成功：NewRunId={NewRunId}, SourceRunId={SourceRunId}, RunType={RunType}",
            id, source.Id, source.RunType);
        return id;
    }
}
