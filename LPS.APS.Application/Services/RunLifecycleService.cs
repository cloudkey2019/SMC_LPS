using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.DTOs.Scope;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using AuditLog = LPS.APS.Core.Entities.Auth.AuditLog;
using PlanVersion = LPS.APS.Core.Entities.APS.PlanVersion;

namespace LPS.APS.Application.Services;

/// <summary>
/// ScheduleRun 运行生命周期治理服务（3号位，P0-08）
/// 边界：仅 3号位生命周期治理（ExpectedDomainKeysJson 冻结规则 / Candidate 最小确认与激活 / FAILED 恢复新建 / Run 引用追溯）；
///       不重写 2号位已冻结的运行状态执行逻辑（SchedulingOrchestrator / ScheduleRunService / DomainSchedulingJob 不动）。
/// DDL 依据：冻结 DDL v5.1.2（ScheduleRun §3.1 / PlanVersion §3.2 / UQ_PlanVersion_OneActivePerDomain）。
/// 语义：配置/状态错误一律抛 InvalidOperationException，不静默降级；旧 FAILED 记录绝不动（不回改 RUNNING）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class RunLifecycleService : IRunLifecycleService
{
    /// <summary>全量排程 RunType（Domain 数 ≥ 1）</summary>
    private const string FullScheduleRunType = "FULL_SCHEDULE";
    /// <summary>运行状态：FAILED</summary>
    private const string ScheduleRunFailedStatus = "FAILED";
    /// <summary>运行状态：RUNNING</summary>
    private const string ScheduleRunRunningStatus = "RUNNING";
    /// <summary>计划版本状态：CANDIDATE</summary>
    private const string PlanVersionCandidateStatus = "CANDIDATE";
    private const string PlanVersionCreatedStatus = "Created";      // D7：候选壳初始状态（替代旧 BUILDING，2号位 执行词表）
    private const string PlanVersionComputedStatus = "Computed";    // D7：候选执行完成终态（2号位 ExecuteDomainAsync 写）
    /// <summary>计划版本状态：ACTIVE（每域单一正式采用版本）</summary>
    private const string PlanVersionActiveStatus = "ACTIVE";
    /// <summary>INSERT_ORDER_WHATIF RunType：仅组合 CTP / INSERT_IMPACT_ANALYSIS，永远不得激活（实施包十九）</summary>
    private const string InsertOrderWhatifRunType = "INSERT_ORDER_WHATIF";
    /// <summary>最小人工确认审计操作类型（P0-04 激活硬前置）</summary>
    private const string ConfirmCandidateOperation = "ConfirmCandidate";
    /// <summary>计划版本状态：FAILED（G8 域级状态判定）</summary>
    private const string PlanVersionFailedStatus = "FAILED";
    /// <summary>计划版本状态：BUILDING（G8 域级状态判定）</summary>
    // D7 废弃：候选壳初始 Status 改用 Created（PlanVersionCreatedStatus）；BUILDING 词表不再使用
    /// <summary>运行状态：COMPLETED（G8 终态判定）</summary>
    private const string ScheduleRunCompletedStatus = "COMPLETED";
    /// <summary>运行状态：PARTIAL_SUCCESS（G8 终态判定）</summary>
    private const string ScheduleRunPartialSuccessStatus = "PARTIAL_SUCCESS";
    /// <summary>域级状态：COMPLETED（成功）</summary>
    private const string RunDomainCompletedStatus = "COMPLETED";
    /// <summary>域级状态：CANDIDATE（待人工确认）</summary>
    private const string RunDomainCandidateStatus = "CANDIDATE";
    /// <summary>域级状态：RUNNING（计算中）</summary>
    private const string RunDomainRunningStatus = "RUNNING";
    /// <summary>域级状态：FAILED（失败根因域）</summary>
    private const string RunDomainFailedStatus = "FAILED";
    /// <summary>域级状态：BLOCKED（因上游失败被阻断）</summary>
    private const string RunDomainBlockedStatus = "BLOCKED";
    /// <summary>域级状态：NOT_STARTED（未参与本次）</summary>
    private const string RunDomainNotStartedStatus = "NOT_STARTED";
    /// <summary>白天候选运行类型（B-1：3号位 运行治理创建入口，0号位 2026-08-29 裁决3）</summary>
    private static readonly string[] DaytimeCandidateRunTypes =
    [
        StrategyProfileRunType.ManualReschedule,
        StrategyProfileRunType.LocalReschedule,
        StrategyProfileRunType.InsertOrderWhatIf,
    ];
    /// <summary>RunType×Purpose 冻结合法组合（实施包 §十九；CTP/INSERT_IMPACT_ANALYSIS 仅组合 INSERT_ORDER_WHATIF 且永远不得激活）</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> LegalPurposeByRunType =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            [StrategyProfileRunType.InsertOrderWhatIf] = [StrategyProfilePurpose.Ctp, StrategyProfilePurpose.InsertImpactAnalysis],
            [StrategyProfileRunType.LocalReschedule] = [StrategyProfilePurpose.InsertReschedule, StrategyProfilePurpose.ManualAdjustment],
            [StrategyProfileRunType.ManualReschedule] = [StrategyProfilePurpose.ManualAdjustment],
        };
    /// <summary>白天候选运行创建审计操作类型（B-1）</summary>
    private const string CreateCandidateRunOperation = "CreateCandidateRun";

    private readonly IScheduleRunRepository _scheduleRunRepo;
    private readonly IPlanVersionRepository _planVersionRepo;
    private readonly IStrategyProfileVersionRepository _strategyProfileVersionRepo;
    private readonly IRuleSetVersionRepository _ruleSetVersionRepo;
    private readonly IParameterSetVersionRepository _parameterSetVersionRepo;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IDataScopeService _dataScopeService;
    private readonly ISchedulingOrchestrator _schedulingOrchestrator;
    private readonly IScheduleRunService _scheduleRunService;
    private readonly ILogger<RunLifecycleService> _logger;

    public RunLifecycleService(
        IScheduleRunRepository scheduleRunRepo,
        IPlanVersionRepository planVersionRepo,
        IStrategyProfileVersionRepository strategyProfileVersionRepo,
        IRuleSetVersionRepository ruleSetVersionRepo,
        IParameterSetVersionRepository parameterSetVersionRepo,
        IAuditLogRepository auditLogRepository,
        IDataScopeService dataScopeService,
        ISchedulingOrchestrator schedulingOrchestrator,
        IScheduleRunService scheduleRunService,
        ILogger<RunLifecycleService> logger)
    {
        _scheduleRunRepo = scheduleRunRepo;
        _planVersionRepo = planVersionRepo;
        _strategyProfileVersionRepo = strategyProfileVersionRepo;
        _ruleSetVersionRepo = ruleSetVersionRepo;
        _parameterSetVersionRepo = parameterSetVersionRepo;
        _auditLogRepository = auditLogRepository;
        _dataScopeService = dataScopeService;
        _schedulingOrchestrator = schedulingOrchestrator;
        _scheduleRunService = scheduleRunService;
        _logger = logger;
    }

    /// <summary>校验 ScheduleRun.ExpectedDomainKeysJson 冻结规则（P0-08；配置错误抛异常，不静默降级）</summary>
    public async Task ValidateExpectedDomainKeysAsync(int scheduleRunId, int actorUserId, CancellationToken ct = default)
    {
        var run = await _scheduleRunRepo.GetByIdAsync(scheduleRunId, ct)
            ?? throw new InvalidOperationException($"ScheduleRun 不存在：{scheduleRunId}");

        ValidateDomainKeys(run.RunType, run.ExpectedDomainKeysJson, $"ScheduleRun {scheduleRunId}");

        // B3：业务范围硬校验（F-G4 Domain 维度）—— 校验人仅可操作其授权 Domain 的 Run
        await EnsureDomainsInScopeAsync(actorUserId, ParseExpectedDomainKeys(run.ExpectedDomainKeysJson, scheduleRunId), ct);
    }

    /// <summary>
    /// ExpectedDomainKeysJson 冻结规则（FULL_SCHEDULE → Domain 数 ≥ 1；RESCHEDULE 类 → 恰 1 Domain）。
    /// JSON 数组格式由 DDL CHECK ISJSON 兜底，此处做语义校验：空/缺失/非数组/含空 DomainKey/数量越界一律抛异常。
    /// </summary>
    private static void ValidateDomainKeys(string runType, string? expectedDomainKeysJson, string displayName)
    {
        if (string.IsNullOrWhiteSpace(expectedDomainKeysJson))
        {
            throw new InvalidOperationException($"{displayName} 的 ExpectedDomainKeysJson 为空/缺失（运行启动须冻结预期 Domain 集合）");
        }

        List<string>? domains;
        try
        {
            domains = System.Text.Json.JsonSerializer.Deserialize<List<string>>(expectedDomainKeysJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"{displayName} 的 ExpectedDomainKeysJson 不是合法 JSON 数组：{ex.Message}", ex);
        }

        if (domains == null)
        {
            throw new InvalidOperationException($"{displayName} 的 ExpectedDomainKeysJson 反序列化结果为空");
        }

        if (domains.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"{displayName} 的 ExpectedDomainKeysJson 含空 DomainKey（预期 Domain 不可为空）");
        }

        if (runType == FullScheduleRunType)
        {
            if (domains.Count < 1)
            {
                throw new InvalidOperationException($"{displayName} 为 FULL_SCHEDULE，预期 Domain 数须 ≥ 1（当前 {domains.Count}）");
            }

            // P1-01：FULL 场景重复 DomainKey 拒绝（预期 Domain 集合不可重复）
            if (domains.Distinct().Count() != domains.Count)
            {
                throw new InvalidOperationException($"{displayName} 为 FULL_SCHEDULE，预期 Domain 集合含重复 DomainKey（须去重后唯一）");
            }
        }
        else
        {
            // RESCHEDULE 类（Candidate）：恰 1 Domain
            if (domains.Count != 1)
            {
                throw new InvalidOperationException($"{displayName} 为 {runType}（RESCHEDULE 类/Candidate），预期 Domain 数须恰为 1（当前 {domains.Count}）");
            }
        }
    }

    /// <summary>
    /// Candidate 最小人工确认（P0-08 / 二轮复审 P0-05 / P0-06）。
    /// 语义：确认**仅记录确认事实，不写 ActivatedAt/ActivatedBy、不转 ACTIVE、不预检同域 ACTIVE**；
    ///       Base ACTIVE 存在时仍可正常确认。确认事实唯一落点是 ConfirmCandidate 审计记录，
    ///       ActivateCandidateAsync 以该审计作为"已完成最小人工确认"的硬前置。
    /// </summary>
    public async Task ConfirmCandidateAsync(int planVersionId, int actorUserId, string actor, string? remark, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new InvalidOperationException("确认人（Actor）不能为空");
        }

        var version = await _planVersionRepo.GetByIdAsync(planVersionId, ct)
            ?? throw new InvalidOperationException($"计划版本不存在：{planVersionId}");

        EnsureCandidateConfirmable(version);

        // 5e：业务范围校验（F-G4 Domain 维度）—— 确认人仅可操作其 DataScopePolicy 授权 Domain
        await EnsureDomainInScopeAsync(actorUserId, version.DomainKey!, ct);

        // 仅记审计：Actor / ConfirmedAt / CandidatePlanVersionId(=planVersionId) / 必要 Remark
        // D7：审计状态记真实 Status（候选排程完成态 = Computed），不记旧 CANDIDATE 词表
        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = ConfirmCandidateOperation,
            EntityType = "PlanVersion",
            EntityId = planVersionId.ToString(),
            OldValue = PlanVersionComputedStatus,
            NewValue = PlanVersionComputedStatus,
            UserId = actorUserId,
            UserCode = actor,
            OccurredAt = DateTime.Now,
            Remark = $"确认候选版本（CandidatePlanVersionId={planVersionId}）"
                + (string.IsNullOrWhiteSpace(remark) ? string.Empty : $"：{remark}"),
        }, ct);
    }

    /// <summary>
    /// 激活 Candidate（确认后正式采用：CANDIDATE → ACTIVE，原子替换同域旧 ACTIVE）。
    /// 前置（硬校验，缺失/不满足一律抛 InvalidOperationException）：
    ///   a) DomainKey 非空（V1 必填）；
    ///   b) 已完成最小人工确认（存在 ConfirmCandidate 审计记录，二轮复审 P0-04）；
    ///   c) 来源 Run 可激活（SourceScheduleRunId 非空且 RunType != INSERT_ORDER_WHATIF，二轮复审 P0-03：
    ///      INSERT_ORDER_WHATIF 仅组合 CTP / INSERT_IMPACT_ANALYSIS，二者永远不得激活）。
    /// 采用边界（二轮复审 P0-06）：原子替换——单事务内归档同域既有 ACTIVE（→ARCHIVED + ArchivedAt）
    ///       再将本 Candidate 置 ACTIVE；UQ_PlanVersion_OneActivePerDomain 红线保留，不删除。
    /// </summary>
    public async Task ActivateCandidateAsync(int planVersionId, int actorUserId, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new InvalidOperationException("激活人（Actor）不能为空");
        }

        var version = await _planVersionRepo.GetByIdAsync(planVersionId, ct)
            ?? throw new InvalidOperationException($"计划版本不存在：{planVersionId}");

        EnsureCandidateConfirmable(version);

        // 5e：业务范围校验（F-G4 Domain 维度）—— 激活人仅可操作其 DataScopePolicy 授权 Domain
        await EnsureDomainInScopeAsync(actorUserId, version.DomainKey!, ct);

        // P0-04：已完成最小人工确认 —— 校验存在 ConfirmCandidate 审计记录
        await EnsureConfirmedAsync(planVersionId, ct);

        // P0-03：来源 Run 可激活 —— SourceScheduleRunId 非空 + RunType != INSERT_ORDER_WHATIF
        await EnsureSourceRunActivatableAsync(version, ct);

        var activatedAt = DateTime.Now;
        version.Status = PlanVersionActiveStatus;
        version.ActivatedAt = activatedAt;
        version.ActivatedBy = actor;

        // 预检（F-G4 跨库兜底）：状态写前先确认审计库可写，失败不等状态落库即抛
        await _auditLogRepository.EnsureWritableAsync(ct);

        // P0-06：原子替换（同域既有 ACTIVE 归档 + 本版本置 ACTIVE，单事务）
        await _planVersionRepo.ReplaceActiveAsync(version, actor, activatedAt, ct);

        try
        {
            await _auditLogRepository.AddAsync(new AuditLog
            {
                ActionCode = "ActivateCandidate",
                EntityType = "PlanVersion",
                EntityId = planVersionId.ToString(),
                OldValue = PlanVersionComputedStatus,
                NewValue = PlanVersionActiveStatus,
                UserId = actorUserId,
                UserCode = actor,
                OccurredAt = activatedAt,
                Remark = $"候选版本正式采用（CANDIDATE → ACTIVE，原子替换同域旧 ACTIVE）：{planVersionId}",
            }, ct);
        }
        catch (Exception ex)
        {
            // 兜底（fail-closed，P1-05）：状态已落库而审计失败，保留对账键供后续对账，绝不静默丢审计
            _logger.LogError(ex,
                "候选激活审计写入失败（状态已落库、审计缺、需对账）：EntityType=PlanVersion EntityId={PlanVersionId} Before={OldStatus} After={NewStatus} Actor={Actor}",
                planVersionId, PlanVersionComputedStatus, PlanVersionActiveStatus, actor);
            throw;
        }
    }

    /// <summary>P0-04：校验该 Candidate 已完成最小人工确认（存在 ConfirmCandidate 审计记录）</summary>
    private async Task EnsureConfirmedAsync(int planVersionId, CancellationToken ct)
    {
        var logs = await _auditLogRepository.GetByEntityAsync("PlanVersion", planVersionId.ToString(), ct);
        var confirmed = logs.Any(l => l.ActionCode == ConfirmCandidateOperation);
        if (!confirmed)
        {
            throw new InvalidOperationException($"计划版本 {planVersionId} 未完成最小人工确认（缺 ConfirmCandidate 审计），不可激活");
        }
    }

    /// <summary>P0-03：来源 Run 可激活校验（SourceScheduleRunId 非空 + RunType != INSERT_ORDER_WHATIF）</summary>
    private async Task EnsureSourceRunActivatableAsync(PlanVersion version, CancellationToken ct)
    {
        if (!version.SourceScheduleRunId.HasValue)
        {
            throw new InvalidOperationException($"计划版本 {version.Id} 的 SourceScheduleRunId 为空，无法证明来源 Run 可激活（拒绝激活）");
        }

        var run = await _scheduleRunRepo.GetByIdAsync(version.SourceScheduleRunId.Value, ct);
        if (run == null)
        {
            throw new InvalidOperationException($"计划版本 {version.Id} 的来源 ScheduleRun {version.SourceScheduleRunId.Value} 不存在，拒绝激活");
        }

        if (run.RunType == InsertOrderWhatifRunType)
        {
            throw new InvalidOperationException(
                $"计划版本 {version.Id} 的来源 Run（{run.Id}）为 {InsertOrderWhatifRunType}"
                + "（实施包十九：INSERT_ORDER_WHATIF 仅组合 CTP / INSERT_IMPACT_ANALYSIS，永远不得激活）");
        }
    }

    /// <summary>
    /// 候选确认/激活前置校验（D7 词表：候选身份在 VersionCategory，Status 走 2号位 执行词表）。
    /// 门禁：VersionCategory 必须 CANDIDATE，且 Status 必须排程完成（Computed）才可确认/激活；DomainKey 非空（V1 必填语义）。
    /// </summary>
    private static void EnsureCandidateConfirmable(PlanVersion version)
    {
        if (version.VersionCategory != PlanVersionCandidateStatus)
        {
            throw new InvalidOperationException(
                $"计划版本 {version.Id} 的 VersionCategory 为 {version.VersionCategory}，仅 CANDIDATE 可确认/激活");
        }

        if (version.Status != PlanVersionComputedStatus)
        {
            throw new InvalidOperationException(
                $"计划版本 {version.Id} 状态为 {version.Status}（D7 执行词表），仅排程完成（Computed）可确认/激活");
        }

        if (string.IsNullOrWhiteSpace(version.DomainKey))
        {
            throw new InvalidOperationException($"计划版本 {version.Id} 的 DomainKey 为空（V1 必填语义，无法按域确认/激活）");
        }
    }

    /// <summary>
    /// 业务范围校验（5e：F-G4 Domain 维度）。写入人仅可操作其 DataScopePolicy 授权 Domain；
    /// 未授权一律抛 <see cref="ScopeViolationException"/>（安全默认，失败关闭，Web 层映射 403）。
    /// </summary>
    private Task EnsureDomainInScopeAsync(int actorUserId, string domainKey, CancellationToken ct)
        => _dataScopeService.EnsureInScopeAsync(actorUserId, DataScopeTypes.Domain, domainKey, ct);

    /// <summary>B3：多 Domain 业务范围硬校验（F-G4 Domain 维度，fail-closed）。用于校验/恢复等多 Domain 场景。</summary>
    private async Task EnsureDomainsInScopeAsync(int actorUserId, IReadOnlyList<string> domainKeys, CancellationToken ct)
    {
        foreach (var domainKey in domainKeys)
        {
            await _dataScopeService.EnsureInScopeAsync(actorUserId, DataScopeTypes.Domain, domainKey, ct);
        }
    }

    /// <summary>
    /// FAILED 恢复（P0-08 + P1-03）：为 FAILED FULL_SCHEDULE Run 新建一条 RUNNING 重跑，同一事务为每个预期 Domain
    /// 各建一个 RECOVERY 壳（一域一壳），随后内联全量执行并一次性收口；继承策略包版本与 Domain 基线；绝不动旧记录。
    /// </summary>
    public async Task<int> RecoverFailedRunAsync(int failedScheduleRunId, int actorUserId, CancellationToken ct = default)
    {
        var failed = await _scheduleRunRepo.GetByIdAsync(failedScheduleRunId, ct)
            ?? throw new InvalidOperationException($"ScheduleRun 不存在：{failedScheduleRunId}");

        if (failed.Status != ScheduleRunFailedStatus)
        {
            throw new InvalidOperationException($"ScheduleRun {failedScheduleRunId} 状态为 {failed.Status}，仅 FAILED 可恢复（旧记录不可回改 RUNNING）");
        }

        // P1-03 §5.1：仅 FULL_SCHEDULE 可恢复。非 FULL（候选/局部重排）恢复会丢 BasePlanVersionId 基线
        //（恢复 INSERT 不继承该列），需求侧订单不钉基线、退化为按本 PV 装，恢复语义不正确。fail-closed 拒绝。
        if (failed.RunType != FullScheduleRunType)
        {
            throw new InvalidOperationException(
                $"ScheduleRun {failedScheduleRunId} 的 RunType 为 {failed.RunType}，仅 {FullScheduleRunType} 可恢复（非全量运行无法继承 Base 基线）");
        }

        // 新建前先校验继承基线合法性（避免插入后再因基线不合法产生孤立 RUNNING 记录）
        ValidateDomainKeys(failed.RunType, failed.ExpectedDomainKeysJson, $"ScheduleRun {failedScheduleRunId} 继承基线");

        // B3：业务范围硬校验（F-G4 Domain 维度）—— 恢复人仅可操作其授权 Domain 的 Run（fail-closed）
        var domainKeys = ParseExpectedDomainKeys(failed.ExpectedDomainKeysJson, failedScheduleRunId);
        await EnsureDomainsInScopeAsync(actorUserId, domainKeys, ct);

        // P1-03 §3.2/§3.3：一域一壳（N 个），窗口继承失败 Run 既有 PlanVersion，缺失兜底今天 ~ +90 天（不得静默平移）。
        var failedPlanVersions = await _planVersionRepo.GetByScheduleRunIdAsync(failedScheduleRunId, ct);
        var shells = new List<RecoveryShellSpec>(domainKeys.Count);
        foreach (var domainKey in domainKeys)
        {
            var pv = failedPlanVersions.FirstOrDefault(p => string.Equals(p.DomainKey, domainKey, StringComparison.Ordinal));
            shells.Add(new RecoveryShellSpec
            {
                DomainKey = domainKey,
                PlanHorizonStart = pv is not null && pv.PlanHorizonStart != default ? pv.PlanHorizonStart : DateTime.Today,
                PlanHorizonEnd = pv is not null && pv.PlanHorizonEnd != default ? pv.PlanHorizonEnd : DateTime.Today.AddDays(90),
            });
        }

        // 同一事务原子写 Run + N 壳（任一失败整体回滚，不产生孤立 RUNNING）
        var newRunId = await _scheduleRunRepo.InsertForRecoveryWithShellsAsync(failed, shells, "Recover", ct);

        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = "RecoverFailedRun",
            EntityType = "ScheduleRun",
            EntityId = failedScheduleRunId.ToString(),
            OldValue = ScheduleRunFailedStatus,
            NewValue = ScheduleRunRunningStatus,
            UserId = actorUserId,
            OccurredAt = DateTime.Now,
            Remark = $"由 FAILED 运行 {failedScheduleRunId} 恢复，新建 RUNNING 运行 {newRunId}（一域一壳 {shells.Count} 个，继承 StrategyProfileVersionId 与 ExpectedDomainKeysJson 基线）",
        }, ct);

        // P1-03 §2：内联全量执行并一次性收口（与候选路径一致，恢复 = 交互式立即重算）。
        // 注意超时预算：多域耗时 ≈ N × 单域（FULL 单域 ≈ 5.4 分钟），端点须放宽超时；排队/巡检兜底另行立项（发令枪/巡检在 2号位侧）。
        var runResult = await _schedulingOrchestrator.ExecuteRunAsync(newRunId, ct);
        if (!runResult.IsSuccess)
        {
            // ExecuteRunAsync 不抛业务异常，失败以返回结果表达（Run 已收口 PARTIAL_SUCCESS / FAILED）；
            // 此处仅记录，供运维/追溯定位，不改变 Run 终态。
            _logger.LogWarning(
                "FAILED 恢复执行未全部成功：NewRunId={NewRunId} IsSuccess={IsSuccess} ScheduledCount={ScheduledCount} Error={Error}",
                newRunId, runResult.IsSuccess, runResult.ScheduledCount, runResult.ErrorMessage);
        }

        return newRunId;
    }

    /// <summary>
    /// 白天候选运行创建（B-1：0号位 2026-08-29 裁决3——白天候选 ScheduleRun 创建归 3号位 运行治理侧；
    /// 冻结 运行类型 × 用途 × 策略版本，交 2号位 主流程执行收口）。
    /// 校验顺序（任一失败抛 InvalidOperationException，不静默降级）：见契约草案 §三。
    /// </summary>
    public async Task<CandidateRunCreatedResult> CreateCandidateRunAsync(CandidateRunCreateSpec spec, int actorUserId, CancellationToken ct = default)
    {
        // 1. Actor 必填
        if (string.IsNullOrWhiteSpace(spec.Actor))
        {
            throw new InvalidOperationException("操作人（Actor）不能为空");
        }

        // 2. RunType ∈ 白天候选类（FULL_SCHEDULE / SIMULATION 拒绝）
        if (!DaytimeCandidateRunTypes.Contains(spec.RunType))
        {
            throw new InvalidOperationException(
                $"RunType={spec.RunType} 不属于白天候选运行类（{string.Join("/", DaytimeCandidateRunTypes)}），拒绝创建");
        }

        // 3. Purpose ∈ RunType 冻结合法组合（实施包 §十九）
        if (!LegalPurposeByRunType.TryGetValue(spec.RunType, out var legalPurposes))
        {
            throw new InvalidOperationException($"RunType={spec.RunType} 未配置冻结合法用途组合，拒绝创建");
        }

        if (!legalPurposes.Contains(spec.Purpose))
        {
            throw new InvalidOperationException(
                $"RunType={spec.RunType} 的用途 {spec.Purpose} 不在冻结合法组合（{string.Join("/", legalPurposes)}），拒绝创建（实施包 §十九）");
        }

        // 3a. ScopeJsonV2 范围载荷校验（M1：八码映射 + PriorityMode 轴 + 单一真相；GANTT/EQUIPMENT/RESOURCE_CALENDAR + EXPEDITE 拒绝）
        ScopeJsonV2Validator.Validate(spec.Scope, spec.RunType, spec.Purpose);

        // 3a'. BusinessScope 装配（真空④契约定案 §三/§四：3号位 生成权）。
        // 服务端权威解析 actor 业务范围（经 IDataScopeService.ResolveScopeAsync），依 Trigger 判定维度装配进 ScopeJsonV2，
        // **覆盖** 前端/调用方可能自传的 BusinessScope 值（范围=授权边界，不得由调用方自定）。
        // 中断级（EQUIPMENT_FAILURE / RESOURCE_CALENDAR_CHANGE）= Global；订单类=ProductFamily；Task 局部=ResourceOrgGroup（DataScopeTypes 常量）。
        if (spec.Scope is not null)
        {
            spec.Scope.BusinessScope = await BuildBusinessScopeAsync(actorUserId, spec.Scope.Trigger, ct);
        }

        // 4. DomainKey 必填（白天候选严格单 Domain）
        if (string.IsNullOrWhiteSpace(spec.DomainKey))
        {
            throw new InvalidOperationException("目标 DomainKey 不能为空（白天候选运行严格单 Domain）");
        }

        // 4a. B3：业务范围硬校验（F-G4 Domain 维度）—— 创建人仅可操作其授权 Domain（fail-closed）
        await EnsureDomainInScopeAsync(actorUserId, spec.DomainKey, ct);

        // 5. Base ACTIVE 解析与校验（Base ACTIVE 只读锚定：白天 Candidate 必须基于当前 ACTIVE，不修改 Base）
        PlanVersion? baseVersion;
        if (spec.BasePlanVersionId.HasValue)
        {
            baseVersion = await _planVersionRepo.GetByIdAsync(spec.BasePlanVersionId.Value, ct);
            if (baseVersion == null)
            {
                throw new InvalidOperationException($"Base 计划版本不存在：{spec.BasePlanVersionId.Value}");
            }

            if (baseVersion.Status != PlanVersionActiveStatus)
            {
                throw new InvalidOperationException(
                    $"Base 计划版本 {baseVersion.Id} 状态为 {baseVersion.Status}，白天候选必须基于 ACTIVE（Base 只读）");
            }

            if (!string.Equals(baseVersion.DomainKey, spec.DomainKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Base 计划版本 {baseVersion.Id} 的 DomainKey={baseVersion.DomainKey} 与目标 {spec.DomainKey} 不一致");
            }
        }
        else
        {
            baseVersion = await _planVersionRepo.GetActiveByDomainKeyAsync(spec.DomainKey, ct);
            if (baseVersion == null)
            {
                throw new InvalidOperationException(
                    $"Domain={spec.DomainKey} 无当前 ACTIVE 计划版本，白天候选运行必须基于 Base ACTIVE（拒绝创建）");
            }
        }

        // 6. 默认策略版本解析（有效窗口 0 缺失 / 多歧义拒绝——红线 #4：禁止盲目取第一个）
        var now = spec.DataCutoffTime ?? DateTime.Now;
        var defaultCandidates = await _strategyProfileVersionRepo.GetDefaultByRunTypeAsync(spec.RunType, ct);
        var effectiveDefaults = defaultCandidates
            .Where(v => (!v.EffectiveFrom.HasValue || v.EffectiveFrom.Value <= now)
                     && (!v.EffectiveTo.HasValue || v.EffectiveTo.Value >= now))
            .ToList();

        if (effectiveDefaults.Count == 0)
        {
            throw new InvalidOperationException($"RunType={spec.RunType} 无当前有效默认 PUBLISHED 策略包版本（拒绝创建）");
        }

        if (effectiveDefaults.Count > 1)
        {
            throw new InvalidOperationException(
                $"RunType={spec.RunType} 存在 {effectiveDefaults.Count} 个当前有效默认 PUBLISHED 策略包版本，歧义拒绝创建");
        }

        var strategyProfileVersionId = effectiveDefaults[0].Id;

        // 7. Candidate 壳计划窗口：复制 Base 的（不修改 Base），缺省 今天~+90 天
        var createSpec = new CandidateRunCreateSpec
        {
            RunType = spec.RunType,
            Purpose = spec.Purpose,
            DomainKey = spec.DomainKey,
            BasePlanVersionId = spec.BasePlanVersionId ?? baseVersion.Id,
            DataCutoffTime = spec.DataCutoffTime,
            Scope = spec.Scope,
            Actor = spec.Actor,
            PlanHorizonStart = baseVersion.PlanHorizonStart != default ? baseVersion.PlanHorizonStart : DateTime.Today,
            PlanHorizonEnd = baseVersion.PlanHorizonEnd != default ? baseVersion.PlanHorizonEnd : DateTime.Today.AddDays(90),
        };

        // 8. 单事务原子写 Run + Candidate 壳（任一失败整体回滚，不产生孤立 RUNNING 运行）
        var result = await _scheduleRunRepo.CreateCandidateRunAsync(createSpec, strategyProfileVersionId, spec.Actor, ct);

        // 9. 审计（契约点 P1：Purpose 本轮仅审计不落库）
        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = CreateCandidateRunOperation,
            EntityType = "ScheduleRun",
            EntityId = result.NewScheduleRunId.ToString(),
            OldValue = "-",
            NewValue = ScheduleRunRunningStatus,
            UserId = actorUserId,
            UserCode = spec.Actor,
            OccurredAt = DateTime.Now,
            Remark = $"白天候选运行创建：RunType={spec.RunType}, Purpose={spec.Purpose}, Domain={spec.DomainKey}, "
                + $"BasePlanVersionId={createSpec.BasePlanVersionId}, StrategyProfileVersionId={strategyProfileVersionId}, "
                + $"NewPlanVersionId={result.NewPlanVersionId}",
        }, ct);

        // 10. 触发接缝（P1-02）：建 Run + 候选壳后正式接通 3→2 Application Service 契约，
        //     调 2号位 RunSchedulingAndFinalizeAsync 执行并收口 Run（RUNNING → COMPLETED / FAILED）。
        // P1-03 §5.4 兜底：同步耗时（单域≈5.4min）期间应用池回收/发版/OOM/客户端断连 → 异常直接穿出、
        //     Run 永久卡 RUNNING（发令枪只领 FULL_SCHEDULE、恢复端点只收 FAILED，两条路都接不着）。
        //     此处落 FAILED 即被既有恢复通道接住。用无取消令牌绕过已取消的请求 ct（客户端断连也能落库）。
        try
        {
            await _schedulingOrchestrator.RunSchedulingAndFinalizeAsync(
                result.NewPlanVersionId, result.NewScheduleRunId, strategyProfileVersionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "候选运行执行中断，落 FAILED 供恢复：ScheduleRunId={RunId} PlanVersionId={PlanVersionId}",
                result.NewScheduleRunId, result.NewPlanVersionId);
            await _scheduleRunService.FailAsync(result.NewScheduleRunId, 0, ex.Message, CancellationToken.None);
            throw;
        }

        return result;
    }

    /// <summary>
    /// 装配 BusinessScopeDto（真空④契约定案 §1.3 维度映射，3号位 生成权）。
    /// 经 <see cref="IDataScopeService.ResolveScopeAsync"/> 解析 actor 有效业务范围，依 Trigger 判定操作类型维度：
    /// 订单类（NEW_ORDER_CTP / NEW_ORDER_IMPACT / NEW_ORDER_INSERT / EXISTING_ORDER_ADVANCE）= ProductFamily（产品族 PMC）；
    /// Task 局部（GANTT_ADJUSTMENT / DOMAIN_MANUAL_RESCHEDULE）= ResourceOrgGroup（一般计划员）；
    /// 中断级（EQUIPMENT_FAILURE / RESOURCE_CALENDAR_CHANGE）= Global（不涉用户业务范围）。
    /// Global 放行 → IsGlobal=true；全局范围扩展解析出的维度键集合 → ScopeType/ScopeValues（业务键字符串，与 EnsureInScopeAsync 的 value 同口径）；
    /// 无任何授权（DataScopeContext.Empty）→ IsGlobal=false + 空集（按无授权 fail-safe，交 1号位 范围不足返回）。
    /// </summary>
    private async Task<BusinessScopeDto> BuildBusinessScopeAsync(int actorUserId, BusinessTriggerType trigger, CancellationToken ct)
    {
        var scopeType = trigger switch
        {
            BusinessTriggerType.NewOrderCtp or BusinessTriggerType.NewOrderImpact
                or BusinessTriggerType.NewOrderInsert or BusinessTriggerType.ExistingOrderAdvance => DataScopeTypes.ProductFamily,
            BusinessTriggerType.GanttAdjustment or BusinessTriggerType.DomainManualReschedule => DataScopeTypes.ResourceOrgGroup,
            _ => DataScopeTypes.Global,
        };

        var ctx = await _dataScopeService.ResolveScopeAsync(actorUserId, ct);

        if (scopeType == DataScopeTypes.Global || ctx.IsGlobal)
        {
            return new BusinessScopeDto { IsGlobal = true, ScopeType = scopeType };
        }

        var values = ctx.GetValues(scopeType);
        return new BusinessScopeDto
        {
            IsGlobal = false,
            ScopeType = scopeType,
            ScopeValues = values is null ? Array.Empty<string>() : values.ToArray(),
        };
    }

    /// <summary>Run 引用追溯（P0-08）：ScheduleRun → 策略包版本 → 规则集/参数集版本 + 关联 PlanVersion 状态与结果</summary>
    public async Task<RunReferenceTrace> GetRunReferenceTraceAsync(int scheduleRunId, CancellationToken ct = default)
    {
        var run = await _scheduleRunRepo.GetByIdAsync(scheduleRunId, ct)
            ?? throw new InvalidOperationException($"ScheduleRun 不存在：{scheduleRunId}");

        long? strategyProfileVersionId = run.StrategyProfileVersionId;
        string? strategyProfileVersionCode = null;
        long? ruleSetVersionId = null;
        string? ruleSetVersionCode = null;
        long? parameterSetVersionId = null;
        string? parameterSetVersionCode = null;

        if (strategyProfileVersionId.HasValue)
        {
            var spv = await _strategyProfileVersionRepo.GetByIdAsync(strategyProfileVersionId.Value, ct);
            if (spv != null)
            {
                strategyProfileVersionCode = spv.VersionCode;
                ruleSetVersionId = spv.RuleSetVersionId;
                parameterSetVersionId = spv.ParameterSetVersionId;

                if (ruleSetVersionId.HasValue)
                {
                    var ruleSet = await _ruleSetVersionRepo.GetByIdAsync(ruleSetVersionId.Value, ct);
                    ruleSetVersionCode = ruleSet?.VersionCode;
                }

                if (parameterSetVersionId.HasValue)
                {
                    var parameterSet = await _parameterSetVersionRepo.GetByIdAsync(parameterSetVersionId.Value, ct);
                    parameterSetVersionCode = parameterSet?.VersionCode;
                }
            }
        }

        var planVersion = await _planVersionRepo.GetLatestByScheduleRunIdAsync(scheduleRunId, ct);

        return new RunReferenceTrace
        {
            ScheduleRunId = run.Id,
            RunType = run.RunType,
            Status = run.Status,
            StrategyProfileVersionId = strategyProfileVersionId,
            StrategyProfileVersionCode = strategyProfileVersionCode,
            RuleSetVersionId = ruleSetVersionId,
            RuleSetVersionCode = ruleSetVersionCode,
            ParameterSetVersionId = parameterSetVersionId,
            ParameterSetVersionCode = parameterSetVersionCode,
            ExpectedDomainKeysJson = run.ExpectedDomainKeysJson,
            PlanVersionId = planVersion?.Id ?? 0,
            PlanVersionStatus = planVersion?.Status,
            DataCutoffTime = run.DataCutoffTime,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            ErrorMessage = run.ErrorMessage,
        };
    }

    /// <summary>
    /// Run 域级状态汇总（G8：3号位文档 §十六 FULL 失败链）。
    /// 判定依据：ExpectedDomainKeysJson（预期 Domain 集） + 该 Run 的 PlanVersion 集合。
    /// 被阻断语义：Run 已终态且存在 FAILED 域时，无 PlanVersion 的预期域标记 BLOCKED（非根因），
    /// 满足"上游失败 → 下游本次不得发布新 ACTIVE → 展示被阻断原因所需元数据"。
    /// </summary>
    public async Task<IReadOnlyList<RunDomainStatusDto>> GetRunDomainStatusAsync(int scheduleRunId, CancellationToken ct = default)
    {
        var run = await _scheduleRunRepo.GetByIdAsync(scheduleRunId, ct)
            ?? throw new InvalidOperationException($"ScheduleRun 不存在：{scheduleRunId}");

        var domainKeys = ParseExpectedDomainKeys(run.ExpectedDomainKeysJson, scheduleRunId);
        var planVersions = await _planVersionRepo.GetByScheduleRunIdAsync(scheduleRunId, ct);

        var hasFailedDomain = planVersions.Any(pv => pv.Status == PlanVersionFailedStatus);
        var isTerminal = run.Status is ScheduleRunCompletedStatus
            or ScheduleRunPartialSuccessStatus
            or ScheduleRunFailedStatus;

        var result = new List<RunDomainStatusDto>(domainKeys.Count);
        foreach (var domainKey in domainKeys)
        {
            var pv = planVersions.FirstOrDefault(p => string.Equals(p.DomainKey, domainKey, StringComparison.Ordinal));

            var dto = new RunDomainStatusDto
            {
                DomainKey = domainKey,
                StartedAt = run.StartedAt,
            };

            if (pv == null)
            {
                if (isTerminal && hasFailedDomain)
                {
                    dto.Status = RunDomainBlockedStatus;
                    dto.Reason = "上游 Domain 失败，本次未生成 PlanVersion（被阻断）";
                }
                else if (isTerminal)
                {
                    dto.Status = RunDomainNotStartedStatus;
                }
                else
                {
                    dto.Status = RunDomainRunningStatus;
                }

                result.Add(dto);
                continue;
            }

            dto.PlanVersionId = pv.Id;
            dto.PlanVersionCode = pv.VersionCode;
            dto.ComputedAt = pv.ComputedAt;
            dto.ActivatedAt = pv.ActivatedAt;

            // D7：候选身份在 VersionCategory（执行词表 Status 不写 CANDIDATE），域级候选待确认按 VersionCategory 判定
            dto.Status = pv.VersionCategory == PlanVersionCandidateStatus
                ? RunDomainCandidateStatus
                : pv.Status switch
                {
                    PlanVersionActiveStatus => RunDomainCompletedStatus,
                    PlanVersionFailedStatus => RunDomainFailedStatus,
                    _ => RunDomainRunningStatus,
                };

            if (pv.Status == PlanVersionFailedStatus)
            {
                dto.Reason = string.IsNullOrWhiteSpace(run.ErrorMessage)
                    ? "域级计算失败"
                    : run.ErrorMessage;
            }

            result.Add(dto);
        }

        return result;
    }

    /// <summary>解析 ExpectedDomainKeysJson 为 DomainKey 有序列表（非空元素；损坏/缺失抛异常，不静默降级）</summary>
    private static List<string> ParseExpectedDomainKeys(string? json, int scheduleRunId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"ScheduleRun {scheduleRunId} 的 ExpectedDomainKeysJson 为空/缺失");
        }

        try
        {
            var domains = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return domains.Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException(
                $"ScheduleRun {scheduleRunId} 的 ExpectedDomainKeysJson 不是合法 JSON 数组：{ex.Message}", ex);
        }
    }
}
