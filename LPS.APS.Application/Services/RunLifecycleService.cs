using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Interfaces;
using GovernanceAuditLog = LPS.APS.Core.Entities.Auth.GovernanceAuditLog;
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
    /// <summary>计划版本状态：ACTIVE（每域单一正式采用版本）</summary>
    private const string PlanVersionActiveStatus = "ACTIVE";
    /// <summary>INSERT_ORDER_WHATIF RunType：仅组合 CTP / INSERT_IMPACT_ANALYSIS，永远不得激活（实施包十九）</summary>
    private const string InsertOrderWhatifRunType = "INSERT_ORDER_WHATIF";
    /// <summary>最小人工确认审计操作类型（P0-04 激活硬前置）</summary>
    private const string ConfirmCandidateOperation = "ConfirmCandidate";

    private readonly IScheduleRunRepository _scheduleRunRepo;
    private readonly IPlanVersionRepository _planVersionRepo;
    private readonly IStrategyProfileVersionRepository _strategyProfileVersionRepo;
    private readonly IRuleSetVersionRepository _ruleSetVersionRepo;
    private readonly IParameterSetVersionRepository _parameterSetVersionRepo;
    private readonly IGovernanceAuditLogRepository _auditLogRepository;

    public RunLifecycleService(
        IScheduleRunRepository scheduleRunRepo,
        IPlanVersionRepository planVersionRepo,
        IStrategyProfileVersionRepository strategyProfileVersionRepo,
        IRuleSetVersionRepository ruleSetVersionRepo,
        IParameterSetVersionRepository parameterSetVersionRepo,
        IGovernanceAuditLogRepository auditLogRepository)
    {
        _scheduleRunRepo = scheduleRunRepo;
        _planVersionRepo = planVersionRepo;
        _strategyProfileVersionRepo = strategyProfileVersionRepo;
        _ruleSetVersionRepo = ruleSetVersionRepo;
        _parameterSetVersionRepo = parameterSetVersionRepo;
        _auditLogRepository = auditLogRepository;
    }

    /// <summary>校验 ScheduleRun.ExpectedDomainKeysJson 冻结规则（P0-08；配置错误抛异常，不静默降级）</summary>
    public async Task ValidateExpectedDomainKeysAsync(int scheduleRunId, CancellationToken ct = default)
    {
        var run = await _scheduleRunRepo.GetByIdAsync(scheduleRunId, ct)
            ?? throw new InvalidOperationException($"ScheduleRun 不存在：{scheduleRunId}");

        ValidateDomainKeys(run.RunType, run.ExpectedDomainKeysJson, $"ScheduleRun {scheduleRunId}");
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
    public async Task ConfirmCandidateAsync(int planVersionId, string actor, string? remark, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new InvalidOperationException("确认人（Actor）不能为空");
        }

        var version = await _planVersionRepo.GetByIdAsync(planVersionId, ct)
            ?? throw new InvalidOperationException($"计划版本不存在：{planVersionId}");

        EnsureCandidateConfirmable(version);

        // 仅记审计：Actor / ConfirmedAt / CandidatePlanVersionId(=planVersionId) / 必要 Remark
        await _auditLogRepository.AddAsync(new GovernanceAuditLog
        {
            OperationType = ConfirmCandidateOperation,
            EntityType = "PlanVersion",
            EntityId = planVersionId,
            BeforeStatus = PlanVersionCandidateStatus,
            AfterStatus = PlanVersionCandidateStatus,
            OperatedBy = actor,
            OperatedAt = DateTime.UtcNow,
            Remarks = $"确认候选版本（CandidatePlanVersionId={planVersionId}）"
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
    public async Task ActivateCandidateAsync(int planVersionId, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new InvalidOperationException("激活人（Actor）不能为空");
        }

        var version = await _planVersionRepo.GetByIdAsync(planVersionId, ct)
            ?? throw new InvalidOperationException($"计划版本不存在：{planVersionId}");

        EnsureCandidateConfirmable(version);

        // P0-04：已完成最小人工确认 —— 校验存在 ConfirmCandidate 审计记录
        await EnsureConfirmedAsync(planVersionId, ct);

        // P0-03：来源 Run 可激活 —— SourceScheduleRunId 非空 + RunType != INSERT_ORDER_WHATIF
        await EnsureSourceRunActivatableAsync(version, ct);

        var activatedAt = DateTime.UtcNow;
        version.Status = PlanVersionActiveStatus;
        version.ActivatedAt = activatedAt;
        version.ActivatedBy = actor;

        // P0-06：原子替换（同域既有 ACTIVE 归档 + 本版本置 ACTIVE，单事务）
        await _planVersionRepo.ReplaceActiveAsync(version, actor, activatedAt, ct);

        await _auditLogRepository.AddAsync(new GovernanceAuditLog
        {
            OperationType = "ActivateCandidate",
            EntityType = "PlanVersion",
            EntityId = planVersionId,
            BeforeStatus = PlanVersionCandidateStatus,
            AfterStatus = PlanVersionActiveStatus,
            OperatedBy = actor,
            OperatedAt = activatedAt,
            Remarks = $"候选版本正式采用（CANDIDATE → ACTIVE，原子替换同域旧 ACTIVE）：{planVersionId}",
        }, ct);
    }

    /// <summary>P0-04：校验该 Candidate 已完成最小人工确认（存在 ConfirmCandidate 审计记录）</summary>
    private async Task EnsureConfirmedAsync(int planVersionId, CancellationToken ct)
    {
        var logs = await _auditLogRepository.GetByEntityAsync("PlanVersion", planVersionId, ct);
        var confirmed = logs.Any(l => l.OperationType == ConfirmCandidateOperation);
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

    /// <summary>候选确认/激活前置校验：状态必须 CANDIDATE 且 DomainKey 非空（V1 必填语义）</summary>
    private static void EnsureCandidateConfirmable(PlanVersion version)
    {
        if (version.Status != PlanVersionCandidateStatus)
        {
            throw new InvalidOperationException($"计划版本 {version.Id} 状态为 {version.Status}，仅 CANDIDATE 可确认/激活");
        }

        if (string.IsNullOrWhiteSpace(version.DomainKey))
        {
            throw new InvalidOperationException($"计划版本 {version.Id} 的 DomainKey 为空（V1 必填语义，无法按域确认/激活）");
        }
    }

    /// <summary>FAILED 恢复（P0-08）：为 FAILED ScheduleRun 新建一条 RUNNING 重跑，继承策略包版本与 Domain 基线；绝不动旧记录</summary>
    public async Task<int> RecoverFailedRunAsync(int failedScheduleRunId, CancellationToken ct = default)
    {
        var failed = await _scheduleRunRepo.GetByIdAsync(failedScheduleRunId, ct)
            ?? throw new InvalidOperationException($"ScheduleRun 不存在：{failedScheduleRunId}");

        if (failed.Status != ScheduleRunFailedStatus)
        {
            throw new InvalidOperationException($"ScheduleRun {failedScheduleRunId} 状态为 {failed.Status}，仅 FAILED 可恢复（旧记录不可回改 RUNNING）");
        }

        // 新建前先校验继承基线合法性（避免插入后再因基线不合法产生孤立 RUNNING 记录）
        ValidateDomainKeys(failed.RunType, failed.ExpectedDomainKeysJson, $"ScheduleRun {failedScheduleRunId} 继承基线");

        var newRunId = await _scheduleRunRepo.InsertForRecoveryAsync(failed, "Recover", ct);

        await _auditLogRepository.AddAsync(new GovernanceAuditLog
        {
            OperationType = "RecoverFailedRun",
            EntityType = "ScheduleRun",
            EntityId = failedScheduleRunId,
            BeforeStatus = ScheduleRunFailedStatus,
            AfterStatus = ScheduleRunRunningStatus,
            OperatedAt = DateTime.UtcNow,
            Remarks = $"由 FAILED 运行 {failedScheduleRunId} 恢复，新建 RUNNING 运行 {newRunId}（继承 StrategyProfileVersionId 与 ExpectedDomainKeysJson 基线）",
        }, ct);

        return newRunId;
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
}
