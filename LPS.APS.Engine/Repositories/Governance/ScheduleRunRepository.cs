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

    /// <summary>Run 列表查询上限（G4 防全表；与治理审计日志 take 上限一致）</summary>
    private const int MaxTake = 200;

    public async Task<ScheduleRunGov?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT [Id], [RunType], [Status], [TriggeredBy], [DataCutoffTime],
                   [StrategyProfileVersionId], [BasePlanVersionId], [ExpectedDomainKeysJson], [ScopeJson],
                   [StartedAt], [CompletedAt], [ErrorMessage]
            FROM [dbo].[ScheduleRun]
            WHERE [Id] = @Id";

        return await _connectionManager.QueryFirstOrDefaultAsync<ScheduleRunGov>(
            sql, new { Id = id }, db: DatabaseId.APS);
    }

    public async Task<IReadOnlyList<ScheduleRunGov>> GetListAsync(
        int? take = null,
        string? status = null,
        string? runType = null,
        CancellationToken ct = default)
    {
        // 缺省 100、上限 200（G4：运行历史列表分页展示，防无界查询）
        var takeClamped = Math.Clamp(take ?? 100, 1, MaxTake);

        const string sql = @"
            SELECT [Id], [RunType], [Status], [TriggeredBy], [DataCutoffTime],
                   [StrategyProfileVersionId], [BasePlanVersionId], [ExpectedDomainKeysJson], [ScopeJson],
                   [StartedAt], [CompletedAt], [ErrorMessage]
            FROM [dbo].[ScheduleRun]
            WHERE (@Status IS NULL OR [Status] = @Status)
              AND (@RunType IS NULL OR [RunType] = @RunType)
            ORDER BY [Id] DESC
            OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY";

        var rows = await _connectionManager.QueryAsync<ScheduleRunGov>(
            sql,
            new
            {
                Take = takeClamped,
                Status = string.IsNullOrWhiteSpace(status) ? null : status!.Trim(),
                RunType = string.IsNullOrWhiteSpace(runType) ? null : runType!.Trim(),
            },
            db: DatabaseId.APS);

        return rows.ToList();
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

    public async Task<CandidateRunCreatedResult> CreateCandidateRunAsync(
        CandidateRunCreateSpec spec,
        long strategyProfileVersionId,
        string triggeredBy,
        CancellationToken ct = default)
    {
        // B-1 白天候选运行创建：ScheduleRun + Candidate 壳 单事务原子写（沿用 ReplaceActiveAsync 事务模式）。
        // 任一失败整体回滚，不产生孤立 RUNNING 运行；触发 2号位 主流程不在本方法内（契约接缝，IRunLifecycleService）。
        var now = DateTime.UtcNow;
        var expectedDomainKeysJson = System.Text.Json.JsonSerializer.Serialize(new[] { spec.DomainKey });
        var versionCode = $"CANDIDATE_{spec.DomainKey}_{now:yyyyMMddHHmmss}";

        return await _connectionManager.ExecuteInTransactionAsync(async (connection, transaction) =>
        {
            const string insertRunSql = @"
                INSERT INTO [dbo].[ScheduleRun]
                    ([RunType], [Status], [TriggeredBy], [DataCutoffTime],
                     [BasePlanVersionId], [StrategyProfileVersionId], [ExpectedDomainKeysJson],
                     [StartedAt], [CreatedAt])
                OUTPUT INSERTED.[Id]
                VALUES (@RunType, 'RUNNING', @TriggeredBy, @DataCutoffTime,
                        @BasePlanVersionId, @StrategyProfileVersionId, @ExpectedDomainKeysJson,
                        @StartedAt, @StartedAt)";

            var runId = await connection.QueryFirstOrDefaultAsync<int>(insertRunSql,
                new
                {
                    RunType = spec.RunType,
                    TriggeredBy = triggeredBy,
                    DataCutoffTime = spec.DataCutoffTime ?? now,
                    BasePlanVersionId = spec.BasePlanVersionId,
                    StrategyProfileVersionId = strategyProfileVersionId,
                    ExpectedDomainKeysJson = expectedDomainKeysJson,
                    StartedAt = now,
                },
                transaction);

            if (runId <= 0)
            {
                throw new InvalidOperationException(
                    $"白天候选运行创建失败：ScheduleRun 写入未返回 Id（RunType={spec.RunType}, Domain={spec.DomainKey}）");
            }

            const string insertShellSql = @"
                INSERT INTO [dbo].[PlanVersion]
                    ([VersionCode], [VersionCategory], [DomainKey],
                     [PlanHorizonStart], [PlanHorizonEnd], [ComputeMode], [Status],
                     [SourceScheduleRunId], [CreatedBy], [CreatedAt])
                OUTPUT INSERTED.[Id]
                VALUES (@VersionCode, 'CANDIDATE', @DomainKey,
                        @PlanHorizonStart, @PlanHorizonEnd, 'FULL', 'Created',
                        @SourceScheduleRunId, @CreatedBy, @StartedAt)";

            var shellId = await connection.QueryFirstOrDefaultAsync<int>(insertShellSql,
                new
                {
                    VersionCode = versionCode,
                    DomainKey = spec.DomainKey,
                    PlanHorizonStart = spec.PlanHorizonStart ?? DateTime.Today,
                    PlanHorizonEnd = spec.PlanHorizonEnd ?? DateTime.Today.AddDays(90),
                    SourceScheduleRunId = runId,
                    CreatedBy = triggeredBy,
                    StartedAt = now,
                },
                transaction);

            if (shellId <= 0)
            {
                throw new InvalidOperationException(
                    $"白天候选运行创建失败：Candidate 壳写入未返回 Id（RunId={runId}, Domain={spec.DomainKey}）");
            }

            return new CandidateRunCreatedResult
            {
                NewScheduleRunId = runId,
                NewPlanVersionId = shellId,
            };
        }, db: DatabaseId.APS);
    }
}
