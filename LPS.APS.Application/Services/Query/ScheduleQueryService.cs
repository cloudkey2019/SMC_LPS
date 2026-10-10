using Dapper;
using LPS.APS.Application.Services.Query.Dto;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services.Query;

/// <summary>
/// 排程结果查询服务实现（3号位）
///
/// 架构红线：
///   - 只读 APS_Production 库（已落盘数据），不触碰推演期内存沙盘
///   - 不包含任何排程/重排逻辑，纯查询投影
///   - Dapper 直连，避免 EF 跟踪开销（大结果集场景）
/// </summary>
public class ScheduleQueryService : IScheduleQueryService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<ScheduleQueryService> _logger;

    public ScheduleQueryService(
        DatabaseConnectionManager connectionManager,
        IDataScopeService dataScopeService,
        ILogger<ScheduleQueryService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlanVersionSummaryDto>> GetVersionsAsync(
        int userId,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        var allowed = await ResolveAllowedDomainKeysAsync(userId, cancellationToken);
        if (allowed is { Count: 0 })
            return Array.Empty<PlanVersionSummaryDto>();

        var sql = @"SELECT TOP (@Take)
                pv.Id, pv.VersionCode, pv.VersionCategory,
                pv.DomainKey,
                pv.PlanHorizonStart, pv.PlanHorizonEnd,
                pv.Status, pv.ComputedAt,
                pv.TotalTasks, pv.CreatedAt,
                pv.SourceScheduleRunId, pv.ActivatedAt,
                CASE WHEN pv.VersionCategory = 'CANDIDATE' OR pv.Status = 'ARCHIVED'
                     THEN (
                         SELECT TOP 1 b.Id
                         FROM PlanVersion b
                         WHERE b.DomainKey = pv.DomainKey
                           AND b.Status = 'ARCHIVED'
                           AND b.ActivatedAt <= pv.CreatedAt
                         ORDER BY b.ActivatedAt DESC, b.Id DESC
                     )
                     ELSE NULL
                END AS BasePlanVersionId
              FROM PlanVersion pv
              WHERE pv.ArchivedAt IS NULL";

        object parameters;
        if (allowed == null)
        {
            parameters = new { Take = take };
        }
        else
        {
            sql += " AND pv.DomainKey IN @ScopeDomainKeys";
            parameters = new { Take = take, ScopeDomainKeys = allowed.ToList() };
        }

        sql += " ORDER BY pv.CreatedAt DESC";

        var rows = await _connectionManager.QueryAsync<PlanVersionSummaryDto>(
            sql, parameters, db: DatabaseId.APS);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<GanttDataDto> GetGanttAsync(
        int userId,
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        var allowed = await ResolveAllowedDomainKeysAsync(userId, cancellationToken);

        // 1. 版本信息（含 DomainKey 做范围校验）
        var version = await _connectionManager.QueryFirstOrDefaultAsync<(int Id, string VersionCode, string DomainKey, DateTime PlanHorizonStart, DateTime PlanHorizonEnd)>(
            @"SELECT Id, VersionCode, DomainKey, PlanHorizonStart, PlanHorizonEnd
              FROM PlanVersion WHERE Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        if (version.Id == 0 || !IsDomainAllowed(allowed, version.DomainKey))
        {
            return new GanttDataDto { PlanVersionId = planVersionId };
        }

        // 2. 资源行（仅返回该版本里被 Task 使用到的资源，减少前端噪声）
        //    F-G4：资源 DomainKey 不再以 FACTORY_{id} 命名规则推导，改用 PlanVersion.DomainKey（版本唯一权威域）
        //    factoryName / productionDepartmentName / stage 由域主数据 JOIN 投影
        var resources = await _connectionManager.QueryAsync<GanttResourceDto>(
            @"SELECT DISTINCT
                r.Id AS ResourceId,
                r.ResourceCode,
                r.ResourceName,
                r.FactoryId,
                r.ProductionDepartmentId,
                @DomainKey AS DomainKey,
                f.Name AS FactoryName,
                pd.DeptName AS ProductionDepartmentName,
                pd.StageCode AS Stage
              FROM Resource r
              INNER JOIN [Task] t ON t.ResourceId = r.Id
              LEFT JOIN Factory f ON f.Id = r.FactoryId
              LEFT JOIN ProductionDepartment pd ON pd.Id = r.ProductionDepartmentId
              WHERE t.PlanVersionId = @Id
              ORDER BY r.FactoryId, r.ProductionDepartmentId, r.ResourceCode",
            new { Id = planVersionId, DomainKey = version.DomainKey },
            db: DatabaseId.APS);

        // 3. 任务条
        //    G2-b 档①：DomainKey = COALESCE(Order.DomainKey, PlanVersion.DomainKey)
        //              netQty = Task.Quantity（落盘即净合格产出 NetOutputQty）
        //              plannedProcessQty / mesEligible / crossDomainBlocked 无落盘来源，返回 null / 保守默认
        //              （详见 v1.2 §二 2.3 与 §七 事实来源待对齐清单）
        var tasks = await _connectionManager.QueryAsync<GanttTaskDto>(
            @"SELECT
                t.Id                AS TaskId,
                t.TaskNo,
                t.OrderId,
                o.OrderNo,
                t.MaterialId,
                m.MaterialCode,
                m.MaterialName,
                t.ResourceId,
                t.OperationCode,
                t.OperationSeq,
                t.Quantity,
                t.UOM,
                t.PlannedStartTime,
                t.PlannedEndTime,
                t.Status,
                t.SetupSource                 AS SetupSource,
                CAST(CASE
                    WHEN t.PlannedEndTime IS NOT NULL
                     AND o.CustomerDueDate IS NOT NULL
                     AND t.PlannedEndTime > o.CustomerDueDate
                    THEN 1 ELSE 0
                END AS BIT) AS IsDelayed,
                COALESCE(o.DomainKey, pv.DomainKey) AS DomainKey,
                t.Quantity          AS NetQty,
                CAST(NULL AS DECIMAL(18,4)) AS PlannedProcessQty,
                CAST(0 AS BIT)      AS MesEligible,
                CAST(NULL AS NVARCHAR(4000)) AS MesIneligibleReasons,
                CAST(0 AS BIT)      AS CrossDomainBlocked,
                CAST(NULL AS NVARCHAR(4000)) AS CrossDomainBlockReason
              FROM [Task] t
              LEFT JOIN [Order] o ON o.Id = t.OrderId
              LEFT JOIN Material m ON m.Id = t.MaterialId
              LEFT JOIN PlanVersion pv ON pv.Id = @Id
              WHERE t.PlanVersionId = @Id
              ORDER BY t.ResourceId, t.PlannedStartTime",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        return new GanttDataDto
        {
            PlanVersionId    = version.Id,
            VersionCode      = version.VersionCode,
            PlanHorizonStart = version.PlanHorizonStart,
            PlanHorizonEnd   = version.PlanHorizonEnd,
            Resources        = resources.ToList(),
            Tasks            = tasks.ToList()
        };
    }

    /// <inheritdoc />
    public async Task<ScheduleSummaryDto> GetSummaryAsync(
        int userId,
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        var allowed = await ResolveAllowedDomainKeysAsync(userId, cancellationToken);

        var versionKey = await _connectionManager.QueryFirstOrDefaultAsync<(int Id, string DomainKey)>(
            "SELECT Id, DomainKey FROM PlanVersion WHERE Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        if (versionKey.Id == 0 || !IsDomainAllowed(allowed, versionKey.DomainKey))
        {
            return new ScheduleSummaryDto { PlanVersionId = planVersionId };
        }

        var summary = await _connectionManager.QueryFirstOrDefaultAsync<ScheduleSummaryDto>(
            @"SELECT
                pv.Id                AS PlanVersionId,
                pv.VersionCode,
                pv.Status,
                pv.DurationSeconds   AS ComputeDurationSeconds,

                (SELECT COUNT(*) FROM [Task] WHERE PlanVersionId = @Id)
                    AS TotalTasks,
                (SELECT COUNT(*) FROM [Task]
                    WHERE PlanVersionId = @Id AND PlannedStartTime IS NOT NULL)
                    AS ScheduledTasks,
                (SELECT COUNT(*) FROM [Task]
                    WHERE PlanVersionId = @Id AND PlannedStartTime IS NULL)
                    AS UnscheduledTasks,
                (SELECT COUNT(*) FROM [Task] t
                    INNER JOIN [Order] o ON o.Id = t.OrderId
                    WHERE t.PlanVersionId = @Id
                      AND t.PlannedEndTime IS NOT NULL
                      AND o.CustomerDueDate IS NOT NULL
                      AND t.PlannedEndTime > o.CustomerDueDate)
                    AS DelayedTasks,

                (SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @Id)
                    AS TotalOrders,
                (SELECT COUNT(DISTINCT o.Id) FROM [Order] o
                    INNER JOIN [Task] t ON t.OrderId = o.Id
                    WHERE o.PlanVersionId = @Id
                      AND t.PlannedEndTime IS NOT NULL
                      AND o.CustomerDueDate IS NOT NULL
                      AND t.PlannedEndTime > o.CustomerDueDate)
                    AS DelayedOrders,

                (SELECT MIN(PlannedStartTime) FROM [Task] WHERE PlanVersionId = @Id)
                    AS FirstTaskStart,
                (SELECT MAX(PlannedEndTime) FROM [Task] WHERE PlanVersionId = @Id)
                    AS LastTaskEnd
              FROM PlanVersion pv
              WHERE pv.Id = @Id",
            new { Id = planVersionId },
            db: DatabaseId.APS);

        return summary ?? new ScheduleSummaryDto { PlanVersionId = planVersionId };
    }

    /// <inheritdoc />
    public async Task<CandidateComparisonDto> GetCandidateComparisonAsync(
        int userId,
        int candidatePlanVersionId,
        int basePlanVersionId,
        CancellationToken cancellationToken = default)
    {
        var allowed = await ResolveAllowedDomainKeysAsync(userId, cancellationToken);

        // 1. 双版本概要 + 存在性/状态校验（缺失 → 404；候选非 CANDIDATE → 400）
        var versions = (await _connectionManager.QueryAsync<PlanVersionBriefDto>(
            @"SELECT Id AS PlanVersionId, VersionCode, VersionCategory, Status, ComputedAt
              FROM PlanVersion
              WHERE Id IN (@CandidateId, @BaseId)",
            new { CandidateId = candidatePlanVersionId, BaseId = basePlanVersionId },
            db: DatabaseId.APS)).ToList();

        var candidateSummary = versions.FirstOrDefault(v => v.PlanVersionId == candidatePlanVersionId)
            ?? throw new KeyNotFoundException($"候选计划版本不存在：{candidatePlanVersionId}");
        var baseSummary = versions.FirstOrDefault(v => v.PlanVersionId == basePlanVersionId)
            ?? throw new KeyNotFoundException($"基础计划版本不存在：{basePlanVersionId}");

        if (!string.Equals(candidateSummary.VersionCategory, "CANDIDATE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"候选版本身份非 CANDIDATE（当前：{candidateSummary.VersionCategory}）");
        }

        // F-G4：业务范围校验（越权视为不存在 → 404，不泄露版本存在性）
        var domainKeys = (await _connectionManager.QueryAsync<(int Id, string DomainKey)>(
            "SELECT Id, DomainKey FROM PlanVersion WHERE Id IN (@CandidateId, @BaseId)",
            new { CandidateId = candidatePlanVersionId, BaseId = basePlanVersionId },
            db: DatabaseId.APS)).ToList();
        if (domainKeys.Count < 2 || !domainKeys.All(d => IsDomainAllowed(allowed, d.DomainKey)))
        {
            throw new KeyNotFoundException("候选或基础计划版本不存在或超出当前用户业务范围");
        }

        var parameters = new { CandidateId = candidatePlanVersionId, BaseId = basePlanVersionId };

        // 2. 任务变化汇总（主键差集：OrderId+MaterialId+OperationSeq+OperationCode+RouteCode+PathId）
        var added = await CountScalarAsync(
            $@"SELECT COUNT(*)
               FROM [Task] t_c
               WHERE t_c.PlanVersionId = @CandidateId
                 AND NOT EXISTS (
                   SELECT 1 FROM [Task] t_b
                   WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment})",
            parameters);

        var removed = await CountScalarAsync(
            $@"SELECT COUNT(*)
               FROM [Task] t_b
               WHERE t_b.PlanVersionId = @BaseId
                 AND NOT EXISTS (
                   SELECT 1 FROM [Task] t_c
                   WHERE t_c.PlanVersionId = @CandidateId{TaskKeyMatchFragment})",
            parameters);

        var timeShifted = await CountScalarAsync(
            $@"SELECT COUNT(*)
               FROM [Task] t_c
               WHERE t_c.PlanVersionId = @CandidateId
                 AND EXISTS (
                   SELECT 1 FROM [Task] t_b
                   WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment}
                     AND (
                       (t_c.PlannedStartTime IS NULL) <> (t_b.PlannedStartTime IS NULL)
                       OR t_c.PlannedStartTime <> t_b.PlannedStartTime
                       OR (t_c.PlannedEndTime IS NULL) <> (t_b.PlannedEndTime IS NULL)
                       OR t_c.PlannedEndTime <> t_b.PlannedEndTime))",
            parameters);

        var resourceChanged = await CountScalarAsync(
            $@"SELECT COUNT(*)
               FROM [Task] t_c
               WHERE t_c.PlanVersionId = @CandidateId
                 AND EXISTS (
                   SELECT 1 FROM [Task] t_b
                   WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment}
                     AND (
                       (t_c.ResourceId IS NULL) <> (t_b.ResourceId IS NULL)
                       OR t_c.ResourceId <> t_b.ResourceId))",
            parameters);

        var impactedOrders = await CountScalarAsync(
            $@"SELECT COUNT(*)
               FROM (
                 SELECT DISTINCT OrderId FROM (
                   SELECT t_c.OrderId
                   FROM [Task] t_c
                   WHERE t_c.PlanVersionId = @CandidateId
                     AND (
                       NOT EXISTS (
                         SELECT 1 FROM [Task] t_b
                         WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment})
                       OR EXISTS (
                         SELECT 1 FROM [Task] t_b
                         WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment}
                           AND (
                             (t_c.PlannedStartTime IS NULL) <> (t_b.PlannedStartTime IS NULL)
                             OR t_c.PlannedStartTime <> t_b.PlannedStartTime
                             OR (t_c.PlannedEndTime IS NULL) <> (t_b.PlannedEndTime IS NULL)
                             OR t_c.PlannedEndTime <> t_b.PlannedEndTime
                             OR (t_c.ResourceId IS NULL) <> (t_b.ResourceId IS NULL)
                             OR t_c.ResourceId <> t_b.ResourceId))
                     )
                   UNION
                   SELECT t_b.OrderId
                   FROM [Task] t_b
                   WHERE t_b.PlanVersionId = @BaseId
                     AND NOT EXISTS (
                       SELECT 1 FROM [Task] t_c
                       WHERE t_c.PlanVersionId = @CandidateId{TaskKeyMatchFragment})
                 ) changed
               ) cnt",
            parameters);

        var newDelays = await CountScalarAsync(
            $@"SELECT COUNT(*)
               FROM [Task] t_c
               INNER JOIN [Order] o ON o.Id = t_c.OrderId
               WHERE t_c.PlanVersionId = @CandidateId
                 AND t_c.PlannedEndTime IS NOT NULL
                 AND o.CustomerDueDate IS NOT NULL
                 AND t_c.PlannedEndTime > o.CustomerDueDate
                 AND NOT EXISTS (
                   SELECT 1
                   FROM [Task] t_b
                   INNER JOIN [Order] ob ON ob.Id = t_b.OrderId
                   WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment}
                     AND t_b.PlannedEndTime IS NOT NULL
                     AND ob.CustomerDueDate IS NOT NULL
                     AND t_b.PlannedEndTime > ob.CustomerDueDate)",
            parameters);

        // 3. 汇总组装（crossDomain 计数未落盘，保守返 0；estimatedOnlyCount 归 2号位 返 null —— v1.2 §三 3.3 / §九-b）
        return new CandidateComparisonDto
        {
            TaskChangeSummary = new TaskChangeSummaryDto
            {
                Added = added,
                Removed = removed,
                TimeShifted = timeShifted,
                ResourceChanged = resourceChanged,
                CrossDomainBlocked = 0
            },
            ImpactSummary = new ImpactSummaryDto
            {
                ImpactedOrderCount = impactedOrders,
                NewDelayCount = newDelays,
                CrossDomainImpacted = 0,
                EstimatedOnlyCount = null
            },
            BaseSummary = baseSummary,
            CandidateSummary = candidateSummary
        };
    }

    /// <inheritdoc />
    public async Task<CandidateSummaryDto> GetCandidateSummaryAsync(
        int userId,
        int candidateRunId,
        CancellationToken cancellationToken = default)
    {
        var allowed = await ResolveAllowedDomainKeysAsync(userId, cancellationToken);

        var run = await _connectionManager.QueryFirstOrDefaultAsync<(int Id, string Status)>(
            "SELECT Id, Status FROM ScheduleRun WHERE Id = @Id",
            new { Id = candidateRunId },
            db: DatabaseId.APS);

        if (run.Id == 0)
            throw new KeyNotFoundException($"候选运行不存在：{candidateRunId}");

        var pv = await _connectionManager.QueryFirstOrDefaultAsync<(int Id, string DomainKey, string CreatedBy, DateTime CreatedAt)>(
            @"SELECT Id, DomainKey, CreatedBy, CreatedAt
              FROM PlanVersion
              WHERE SourceScheduleRunId = @RunId AND VersionCategory = 'CANDIDATE'",
            new { RunId = candidateRunId },
            db: DatabaseId.APS);

        if (pv.Id == 0 || !IsDomainAllowed(allowed, pv.DomainKey))
            throw new KeyNotFoundException($"候选运行不存在或超出当前用户业务范围：{candidateRunId}");

        var counts = await _connectionManager.QueryFirstOrDefaultAsync<(int OrderCount, int TaskCount)>(
            @"SELECT
                (SELECT COUNT(*) FROM [Order] WHERE PlanVersionId = @PvId) AS OrderCount,
                (SELECT COUNT(*) FROM [Task] WHERE PlanVersionId = @PvId) AS TaskCount",
            new { PvId = pv.Id },
            db: DatabaseId.APS);

        return new CandidateSummaryDto
        {
            CandidateId = candidateRunId,
            RunStatus = run.Status,
            OrderCount = counts.OrderCount,
            TaskCount = counts.TaskCount,
            CreatedBy = pv.CreatedBy,
            CreatedAt = pv.CreatedAt
        };
    }

    /// <inheritdoc />
    public async Task<CandidateDiffDto> GetCandidateDiffAsync(
        int userId,
        int candidateRunId,
        CancellationToken cancellationToken = default)
    {
        var allowed = await ResolveAllowedDomainKeysAsync(userId, cancellationToken);

        var run = await _connectionManager.QueryFirstOrDefaultAsync<(int Id, int? BasePlanVersionId)>(
            "SELECT Id, BasePlanVersionId FROM ScheduleRun WHERE Id = @Id",
            new { Id = candidateRunId },
            db: DatabaseId.APS);

        if (run.Id == 0)
            throw new KeyNotFoundException($"候选运行不存在：{candidateRunId}");

        var pv = await _connectionManager.QueryFirstOrDefaultAsync<(int Id, string DomainKey)>(
            "SELECT Id, DomainKey FROM PlanVersion WHERE SourceScheduleRunId = @RunId AND VersionCategory = 'CANDIDATE'",
            new { RunId = candidateRunId },
            db: DatabaseId.APS);

        if (pv.Id == 0 || !IsDomainAllowed(allowed, pv.DomainKey))
            throw new KeyNotFoundException($"候选运行不存在或超出当前用户业务范围：{candidateRunId}");

        if (run.BasePlanVersionId is not int baseId)
            throw new InvalidOperationException($"候选运行 {candidateRunId} 无基础计划版本（BasePlanVersionId 为空）");

        var parameters = new { CandidateId = pv.Id, BaseId = baseId };

        var addedOrders = (await _connectionManager.QueryAsync<int?>(
            $@"SELECT DISTINCT t_c.OrderId
               FROM [Task] t_c
               WHERE t_c.PlanVersionId = @CandidateId
                 AND NOT EXISTS (
                   SELECT 1 FROM [Task] t_b
                   WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment})",
            parameters, db: DatabaseId.APS))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();

        var removedOrders = (await _connectionManager.QueryAsync<int?>(
            $@"SELECT DISTINCT t_b.OrderId
               FROM [Task] t_b
               WHERE t_b.PlanVersionId = @BaseId
                 AND NOT EXISTS (
                   SELECT 1 FROM [Task] t_c
                   WHERE t_c.PlanVersionId = @CandidateId{TaskKeyMatchFragment})",
            parameters, db: DatabaseId.APS))
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();

        var changedTasks = (await _connectionManager.QueryAsync<ChangedTaskDto>(
            $@"SELECT
                 t_c.OrderId, t_c.MaterialId, t_c.OperationSeq, t_c.OperationCode,
                 t_c.RouteCode, t_c.PathId, t_c.ResourceId,
                 t_c.PlannedStartTime, t_c.PlannedEndTime
               FROM [Task] t_c
               WHERE t_c.PlanVersionId = @CandidateId
                 AND EXISTS (
                   SELECT 1 FROM [Task] t_b
                   WHERE t_b.PlanVersionId = @BaseId{TaskKeyMatchFragment}
                     AND (
                       (t_c.PlannedStartTime IS NULL) <> (t_b.PlannedStartTime IS NULL)
                       OR t_c.PlannedStartTime <> t_b.PlannedStartTime
                       OR (t_c.PlannedEndTime IS NULL) <> (t_b.PlannedEndTime IS NULL)
                       OR t_c.PlannedEndTime <> t_b.PlannedEndTime
                       OR (t_c.ResourceId IS NULL) <> (t_b.ResourceId IS NULL)
                       OR t_c.ResourceId <> t_b.ResourceId))",
            parameters, db: DatabaseId.APS)).ToList();

        return new CandidateDiffDto
        {
            BasePlanVersionId = baseId,
            CandidatePlanVersionId = pv.Id,
            AddedOrders = addedOrders,
            RemovedOrders = removedOrders,
            ChangedTasks = changedTasks
        };
    }

    /// <summary>跨版本 Task 自然主键匹配片段（H1 差集判定：OrderId+MaterialId+OperationSeq+OperationCode+RouteCode+PathId）</summary>
    private const string TaskKeyMatchFragment = @"
        AND t_b.OrderId       = t_c.OrderId
        AND t_b.MaterialId    = t_c.MaterialId
        AND t_b.OperationSeq  = t_c.OperationSeq
        AND t_b.OperationCode = t_c.OperationCode
        AND t_b.RouteCode     = t_c.RouteCode
        AND t_b.PathId        = t_c.PathId";

    /// <summary>
    /// 解析当前用户有效范围并映射为可落 SQL 的 DomainKey 约束（F-G4）。
    /// 返回 null=全局放行；空集合=拒绝全部；否则 DomainKey ∈ 集合。
    /// 仅 Domain 维度可无损映射到 PlanVersion.DomainKey（DomainDefinition 为唯一权威映射源）。
    /// Factory / ProductFamily / Department 等维度不得靠命名规则（如 FACTORY_{id}）推导权威 DomainKey
    /// （0号位 2026-09-07 F-G4 裁决：Business Scope ≠ Domain Scope 别名）。
    /// 仅授权这些维度而无 Domain 授权时按拒绝处理（fail-closed 安全默认）；
    /// 其正式查询闭环随普通查询迁 5号位 后由 5→2 依据 DomainDefinition / ProductionDepartment 真值落地。
    /// </summary>
    private async Task<IReadOnlySet<string>?> ResolveAllowedDomainKeysAsync(int userId, CancellationToken cancellationToken)
        => ResolveAllowedDomainKeys(await _dataScopeService.ResolveScopeAsync(userId, cancellationToken));

    private static IReadOnlySet<string>? ResolveAllowedDomainKeys(DataScopeContext scope)
    {
        if (scope.IsGlobal)
            return null;
        if (scope.IsEmpty)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var domain = scope.GetValues(DataScopeTypes.Domain);
        if (domain is { Count: > 0 })
            return new HashSet<string>(domain, StringComparer.OrdinalIgnoreCase);

        // Factory / ProductFamily / Department 无法直接映射为权威 DomainKey（F-G4）——拒绝全部（fail-closed）
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDomainAllowed(IReadOnlySet<string>? allowed, string domainKey)
        => allowed == null || allowed.Contains(domainKey);

    /// <summary>标量 COUNT 查询（只读 APS_Production；T=int 无约束时 T? 仅为注解，须显式 int? 类型参数才能 ?? 0 兜底）</summary>
    private async Task<int> CountScalarAsync(string sql, object parameters)
        => await _connectionManager.QueryFirstOrDefaultAsync<int?>(sql, parameters, db: DatabaseId.APS) ?? 0;
}
