using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Enum;
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
        // D7：审计状态记真实 Status（候选排程完成态 = Computed），不记旧 CANDIDATE 词表
        await _auditLogRepository.AddAsync(new GovernanceAuditLog
        {
            OperationType = ConfirmCandidateOperation,
            EntityType = "PlanVersion",
            EntityId = planVersionId,
            BeforeStatus = PlanVersionComputedStatus,
            AfterStatus = PlanVersionComputedStatus,
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
            BeforeStatus = PlanVersionComputedStatus,
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

    /// <summary>
    /// 白天候选运行创建（B-1：0号位 2026-08-29 裁决3——白天候选 ScheduleRun 创建归 3号位 运行治理侧；
    /// 冻结 运行类型 × 用途 × 策略版本，交 2号位 主流程执行收口）。
    /// 校验顺序（任一失败抛 InvalidOperationException，不静默降级）：见契约草案 §三。
    /// </summary>
    public async Task<CandidateRunCreatedResult> CreateCandidateRunAsync(CandidateRunCreateSpec spec, CancellationToken ct = default)
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

        // 4. DomainKey 必填（白天候选严格单 Domain）
        if (string.IsNullOrWhiteSpace(spec.DomainKey))
        {
            throw new InvalidOperationException("目标 DomainKey 不能为空（白天候选运行严格单 Domain）");
        }

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
        var now = spec.DataCutoffTime ?? DateTime.UtcNow;
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
            Actor = spec.Actor,
            PlanHorizonStart = baseVersion.PlanHorizonStart != default ? baseVersion.PlanHorizonStart : DateTime.Today,
            PlanHorizonEnd = baseVersion.PlanHorizonEnd != default ? baseVersion.PlanHorizonEnd : DateTime.Today.AddDays(90),
        };

        // 8. 单事务原子写 Run + Candidate 壳（任一失败整体回滚，不产生孤立 RUNNING 运行）
        var result = await _scheduleRunRepo.CreateCandidateRunAsync(createSpec, strategyProfileVersionId, spec.Actor, ct);

        // 9. 审计（契约点 P1：Purpose 本轮仅审计不落库）
        await _auditLogRepository.AddAsync(new GovernanceAuditLog
        {
            OperationType = CreateCandidateRunOperation,
            EntityType = "ScheduleRun",
            EntityId = result.NewScheduleRunId,
            BeforeStatus = "-",
            AfterStatus = ScheduleRunRunningStatus,
            OperatedBy = spec.Actor,
            OperatedAt = DateTime.UtcNow,
            Remarks = $"白天候选运行创建：RunType={spec.RunType}, Purpose={spec.Purpose}, Domain={spec.DomainKey}, "
                + $"BasePlanVersionId={createSpec.BasePlanVersionId}, StrategyProfileVersionId={strategyProfileVersionId}, "
                + $"NewPlanVersionId={result.NewPlanVersionId}",
        }, ct);

        // 10. 触发接缝（B-1 契约草案 §四）：建 Run+壳后按方案 A/B/C 调 2号位 主流程执行并收口 Run；
        //     本轮未接线，待 2号位/0号位 裁定后由 3号位 补接线（不越位调用 2号位 内部方法）。
        return result;
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
