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

    /// <summary>Candidate 最小人工确认（P0-08：仅记录 Actor / ConfirmedAt / CandidatePlanVersionId / Remark；不转 ACTIVE）</summary>
    public async Task ConfirmCandidateAsync(int planVersionId, string actor, string? remark, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new InvalidOperationException("确认人（Actor）不能为空");
        }

        var version = await _planVersionRepo.GetByIdAsync(planVersionId, ct)
            ?? throw new InvalidOperationException($"计划版本不存在：{planVersionId}");

        EnsureCandidateConfirmable(version);

        // 同域唯一预检（UQ_PlanVersion_OneActivePerDomain 应用层预检；排除自身）
        await EnsureNoActiveInSameDomainAsync(version, planVersionId, ct);

        version.ActivatedAt = DateTime.UtcNow;
        version.ActivatedBy = actor;

        await _planVersionRepo.UpdateAsync(version, ct);

        // 审计：仅记录 Actor / ConfirmedAt / CandidatePlanVersionId(=planVersionId) / 必要 Remark
        await _auditLogRepository.AddAsync(new GovernanceAuditLog
        {
            OperationType = "ConfirmCandidate",
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

    /// <summary>激活 Candidate（确认后正式采用：CANDIDATE → ACTIVE + 写 ActivatedAt/ActivatedBy）</summary>
    public async Task ActivateCandidateAsync(int planVersionId, string actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new InvalidOperationException("激活人（Actor）不能为空");
        }

        var version = await _planVersionRepo.GetByIdAsync(planVersionId, ct)
            ?? throw new InvalidOperationException($"计划版本不存在：{planVersionId}");

        EnsureCandidateConfirmable(version);

        // 同域唯一预检（UQ_PlanVersion_OneActivePerDomain 应用层预检；排除自身）
        await EnsureNoActiveInSameDomainAsync(version, planVersionId, ct);

        var beforeStatus = version.Status;
        version.Status = PlanVersionActiveStatus;
        version.ActivatedAt = DateTime.UtcNow;
        version.ActivatedBy = actor;

        await _planVersionRepo.UpdateAsync(version, ct);

        await _auditLogRepository.AddAsync(new GovernanceAuditLog
        {
            OperationType = "ActivateCandidate",
            EntityType = "PlanVersion",
            EntityId = planVersionId,
            BeforeStatus = beforeStatus,
            AfterStatus = PlanVersionActiveStatus,
            OperatedBy = actor,
            OperatedAt = DateTime.UtcNow,
            Remarks = $"候选版本正式采用（CANDIDATE → ACTIVE）：{planVersionId}",
        }, ct);
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

    /// <summary>同域已有 ACTIVE 版本则拒绝（每域单一正式采用版本）</summary>
    private async Task EnsureNoActiveInSameDomainAsync(PlanVersion version, int planVersionId, CancellationToken ct)
    {
        var existing = await _planVersionRepo.GetActiveByDomainKeyAsync(version.DomainKey!, planVersionId, ct);
        if (existing != null)
        {
            throw new InvalidOperationException($"Domain {version.DomainKey} 已存在 ACTIVE 版本 {existing.Id}（UQ_PlanVersion_OneActivePerDomain，每域单一正式采用版本）");
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
