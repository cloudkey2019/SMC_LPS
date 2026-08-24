using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using StrategyProfileVersion = LPS.APS.Core.Entities.APS.StrategyProfileVersion;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;

namespace LPS.APS.Application.Services;

/// <summary>
/// 治理版本发布服务（阶段 A-4/A-5：3号位 Application 编排）
/// 六态状态机发布流程实现（R01/R02 验收）。
/// A-7 扩展：发布时记录审计日志到 Auth 库。
/// 红线：
/// - 已 PUBLISHED 版本不可再次发布（历史不可覆盖，R01）；
/// - 发布产生新 PUBLISHED 记录，旧版本记录不变（新 Run 可引用新版本、旧 Run 引用不变，R02）；
/// - 仅 DRAFT/SUBMITTED/APPROVED 为合法发布前驱；DISABLED/ARCHIVED 不可发布。
/// 完整发布前校验（引用有效、参数越界、Guardrail 为正、On-time 0~100 等）由阶段 A-5 扩展。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class GovernanceVersionService : IGovernanceVersionService
{
    private readonly IRuleSetVersionRepository _ruleSetVersionRepository;
    private readonly IParameterSetVersionRepository _parameterSetVersionRepository;
    private readonly IStrategyProfileRepository _strategyProfileRepository;
    private readonly IStrategyProfileVersionRepository _strategyProfileVersionRepository;
    private readonly IGovernanceAuditLogRepository _auditLogRepository;
    /// <summary>Demand Priority 业务校验器（无状态纯校验，P0-05 强制接入发布前校验）</summary>
    private readonly DemandPriorityValidator _demandPriorityValidator = new();

    /// <summary>Solver Strategy 业务校验器（无状态纯校验，E-4；P0-02b 接入参数集发布前校验）</summary>
    private readonly SolverStrategyValidator _solverStrategyValidator = new();

    /// <summary>Candidate Guardrail 业务校验器（无状态纯校验，E-4；P0-02b 接入参数集发布前校验）</summary>
    private readonly CandidateGuardrailValidator _candidateGuardrailValidator = new();

    public GovernanceVersionService(
        IRuleSetVersionRepository ruleSetVersionRepository,
        IParameterSetVersionRepository parameterSetVersionRepository,
        IStrategyProfileRepository strategyProfileRepository,
        IStrategyProfileVersionRepository strategyProfileVersionRepository,
        IGovernanceAuditLogRepository auditLogRepository)
    {
        _ruleSetVersionRepository = ruleSetVersionRepository;
        _parameterSetVersionRepository = parameterSetVersionRepository;
        _strategyProfileRepository = strategyProfileRepository;
        _strategyProfileVersionRepository = strategyProfileVersionRepository;
        _auditLogRepository = auditLogRepository;
    }

    /// <summary>合法发布前驱状态（其余状态发布一律拒绝）</summary>
    private static readonly string[] PublishableStatuses = [GovernanceVersionStatus.Draft, GovernanceVersionStatus.Submitted, GovernanceVersionStatus.Approved];

    public async Task PublishRuleSetVersionAsync(long ruleSetVersionId, string? publishedBy, CancellationToken ct = default)
    {
        var version = await _ruleSetVersionRepository.GetByIdAsync(ruleSetVersionId, ct)
            ?? throw new InvalidOperationException($"规则集版本不存在：{ruleSetVersionId}");

        // P0-05：正式 Publish 强制发布前完整校验，无绕过路径（Validate → 有 Error 拒绝 → Publish）
        var validation = await ValidateRuleSetVersionForPublishAsync(ruleSetVersionId, ct);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"规则集版本发布前校验失败：{validation.GetErrorMessage()}");
        }

        EnsurePublishable(version.Status, ruleSetVersionId);

        var beforeStatus = version.Status;

        // P0-02b：发布时聚合 DemandPriority 子块 → ContentSnapshotJson（契约 §6.10.5，Run 装载重放载体）
        version.ContentSnapshotJson = BuildRuleSetContentSnapshot(version);

        version.Status = GovernanceVersionStatus.Published;
        version.PublishedAt = DateTime.UtcNow;
        version.PublishedBy = publishedBy;

        await _ruleSetVersionRepository.UpdateAsync(version, ct);

        // A-7 审计日志：记录发布操作
        await _auditLogRepository.AddAsync(new Core.Entities.Auth.GovernanceAuditLog
        {
            OperationType = "Publish",
            EntityType = "RuleSetVersion",
            EntityId = ruleSetVersionId,
            VersionCode = version.VersionCode,
            BeforeStatus = beforeStatus,
            AfterStatus = GovernanceVersionStatus.Published,
            OperatedBy = publishedBy,
            OperatedAt = DateTime.UtcNow,
            Remarks = "规则集版本发布"
        }, ct);
    }

    public async Task PublishParameterSetVersionAsync(long parameterSetVersionId, string? publishedBy, CancellationToken ct = default)
    {
        var version = await _parameterSetVersionRepository.GetByIdAsync(parameterSetVersionId, ct)
            ?? throw new InvalidOperationException($"参数集版本不存在：{parameterSetVersionId}");

        // P0-05：正式 Publish 强制发布前完整校验，无绕过路径（Validate → 有 Error 拒绝 → Publish）
        var validation = await ValidateParameterSetVersionForPublishAsync(parameterSetVersionId, ct);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"参数集版本发布前校验失败：{validation.GetErrorMessage()}");
        }

        EnsurePublishable(version.Status, parameterSetVersionId);

        var beforeStatus = version.Status;

        // P0-02b：发布时聚合五子块（Lock/Supply/Procurement/SolverStrategy/CandidateGuardrail）→ ContentSnapshotJson（契约 §6.10.5）
        version.ContentSnapshotJson = BuildParameterSetContentSnapshot(version);

        version.Status = GovernanceVersionStatus.Published;
        version.PublishedAt = DateTime.UtcNow;
        version.PublishedBy = publishedBy;

        await _parameterSetVersionRepository.UpdateAsync(version, ct);

        // A-7 审计日志：记录发布操作
        await _auditLogRepository.AddAsync(new Core.Entities.Auth.GovernanceAuditLog
        {
            OperationType = "Publish",
            EntityType = "ParameterSetVersion",
            EntityId = parameterSetVersionId,
            VersionCode = version.VersionCode,
            BeforeStatus = beforeStatus,
            AfterStatus = GovernanceVersionStatus.Published,
            OperatedBy = publishedBy,
            OperatedAt = DateTime.UtcNow,
            Remarks = "参数集版本发布"
        }, ct);
    }

    /// <summary>
    /// 校验状态是否可发布：仅 DRAFT/SUBMITTED/APPROVED 合法；
    /// PUBLISHED 拒绝（历史不可覆盖，R01）；DISABLED/ARCHIVED 拒绝（失效/归档不可发布）。
    /// </summary>
    private static void EnsurePublishable(string status, long versionId)
    {
        if (PublishableStatuses.Contains(status))
        {
            return;
        }

        throw status switch
        {
            GovernanceVersionStatus.Published => new InvalidOperationException($"版本已发布，历史不可覆盖：{versionId}"),
            _ => new InvalidOperationException($"版本状态不可发布（当前 {status}）：{versionId}"),
        };
    }

    /// <summary>开发者：3号位</summary>
    public async Task<VersionDiffResult> CompareRuleSetVersionsAsync(long sourceVersionId, long targetVersionId, CancellationToken ct = default)
    {
        var sourceVersion = await _ruleSetVersionRepository.GetByIdAsync(sourceVersionId, ct)
            ?? throw new InvalidOperationException($"源规则集版本不存在：{sourceVersionId}");

        var targetVersion = await _ruleSetVersionRepository.GetByIdAsync(targetVersionId, ct)
            ?? throw new InvalidOperationException($"目标规则集版本不存在：{targetVersionId}");

        // P1-02：Diff 基于 ContentSnapshotJson 真实子块（兼容仅传主题 JSON 的装配路径，EnsureNormalized 幂等）
        EnsureRuleSetNormalized(sourceVersion);
        EnsureRuleSetNormalized(targetVersion);

        var diffs = new List<FieldDiff>
        {
            CompareField("VersionCode", "版本编码", sourceVersion.VersionCode, targetVersion.VersionCode),
            CompareField("Status", "状态", sourceVersion.Status, targetVersion.Status),
            CompareField("DemandPriority", "需求优先级配置",
                ExtractBlockJson(sourceVersion.ContentSnapshotJson, "DemandPriority"),
                ExtractBlockJson(targetVersion.ContentSnapshotJson, "DemandPriority")),
            CompareField("EffectiveFrom", "生效起始", sourceVersion.EffectiveFrom?.ToString("yyyy-MM-dd HH:mm:ss"), targetVersion.EffectiveFrom?.ToString("yyyy-MM-dd HH:mm:ss")),
            CompareField("EffectiveTo", "生效截止", sourceVersion.EffectiveTo?.ToString("yyyy-MM-dd HH:mm:ss"), targetVersion.EffectiveTo?.ToString("yyyy-MM-dd HH:mm:ss")),
            CompareField("PublishedAt", "发布时间", sourceVersion.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss"), targetVersion.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss")),
            CompareField("PublishedBy", "发布人", sourceVersion.PublishedBy, targetVersion.PublishedBy),
            CompareField("Remarks", "备注", sourceVersion.Remarks, targetVersion.Remarks)
        };

        return new VersionDiffResult
        {
            SourceVersionId = sourceVersionId,
            TargetVersionId = targetVersionId,
            SourceVersionCode = sourceVersion.VersionCode,
            TargetVersionCode = targetVersion.VersionCode,
            EntityType = "RuleSetVersion",
            FieldDiffs = diffs,
            ComparedAt = DateTime.UtcNow
        };
    }

    /// <summary>开发者：3号位</summary>
    public async Task<VersionDiffResult> CompareParameterSetVersionsAsync(long sourceVersionId, long targetVersionId, CancellationToken ct = default)
    {
        var sourceVersion = await _parameterSetVersionRepository.GetByIdAsync(sourceVersionId, ct)
            ?? throw new InvalidOperationException($"源参数集版本不存在：{sourceVersionId}");

        var targetVersion = await _parameterSetVersionRepository.GetByIdAsync(targetVersionId, ct)
            ?? throw new InvalidOperationException($"目标参数集版本不存在：{targetVersionId}");

        // P1-02：Diff 基于 ContentSnapshotJson 真实子块（兼容仅传主题 JSON 的装配路径，EnsureNormalized 幂等）
        EnsureParameterSetNormalized(sourceVersion);
        EnsureParameterSetNormalized(targetVersion);

        var diffs = new List<FieldDiff>
        {
            CompareField("VersionCode", "版本编码", sourceVersion.VersionCode, targetVersion.VersionCode),
            CompareField("Status", "状态", sourceVersion.Status, targetVersion.Status),
            CompareField("Lock", "锁定配置",
                ExtractBlockJson(sourceVersion.ContentSnapshotJson, "Lock"),
                ExtractBlockJson(targetVersion.ContentSnapshotJson, "Lock")),
            CompareField("Supply", "供给配置",
                ExtractBlockJson(sourceVersion.ContentSnapshotJson, "Supply"),
                ExtractBlockJson(targetVersion.ContentSnapshotJson, "Supply")),
            CompareField("Procurement", "采购配置",
                ExtractBlockJson(sourceVersion.ContentSnapshotJson, "Procurement"),
                ExtractBlockJson(targetVersion.ContentSnapshotJson, "Procurement")),
            CompareField("SolverStrategy", "Solver策略配置",
                ExtractBlockJson(sourceVersion.ContentSnapshotJson, "SolverStrategy"),
                ExtractBlockJson(targetVersion.ContentSnapshotJson, "SolverStrategy")),
            CompareField("CandidateGuardrail", "Candidate技术Guardrail配置",
                ExtractBlockJson(sourceVersion.ContentSnapshotJson, "CandidateGuardrail"),
                ExtractBlockJson(targetVersion.ContentSnapshotJson, "CandidateGuardrail")),
            CompareField("EffectiveFrom", "生效起始", sourceVersion.EffectiveFrom?.ToString("yyyy-MM-dd HH:mm:ss"), targetVersion.EffectiveFrom?.ToString("yyyy-MM-dd HH:mm:ss")),
            CompareField("EffectiveTo", "生效截止", sourceVersion.EffectiveTo?.ToString("yyyy-MM-dd HH:mm:ss"), targetVersion.EffectiveTo?.ToString("yyyy-MM-dd HH:mm:ss")),
            CompareField("PublishedAt", "发布时间", sourceVersion.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss"), targetVersion.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss")),
            CompareField("PublishedBy", "发布人", sourceVersion.PublishedBy, targetVersion.PublishedBy),
            CompareField("Remarks", "备注", sourceVersion.Remarks, targetVersion.Remarks)
        };

        return new VersionDiffResult
        {
            SourceVersionId = sourceVersionId,
            TargetVersionId = targetVersionId,
            SourceVersionCode = sourceVersion.VersionCode,
            TargetVersionCode = targetVersion.VersionCode,
            EntityType = "ParameterSetVersion",
            FieldDiffs = diffs,
            ComparedAt = DateTime.UtcNow
        };
    }

    /// <summary>字段对比辅助方法</summary>
    /// <remarks>开发者：3号位</remarks>
    private static FieldDiff CompareField(string fieldName, string displayName, string? sourceValue, string? targetValue)
    {
        return new FieldDiff
        {
            FieldName = fieldName,
            FieldDisplayName = displayName,
            SourceValue = sourceValue,
            TargetValue = targetValue,
            IsChanged = sourceValue != targetValue
        };
    }

    /// <summary>开发者：3号位</summary>
    public async Task<PublishValidationResult> ValidateRuleSetVersionForPublishAsync(long ruleSetVersionId, CancellationToken ct = default)
    {
        var result = new PublishValidationResult
        {
            ValidatedAt = DateTime.UtcNow
        };

        var version = await _ruleSetVersionRepository.GetByIdAsync(ruleSetVersionId, ct);
        if (version == null)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "NOT_FOUND",
                Message = $"规则集版本不存在：{ruleSetVersionId}"
            });
            result.IsValid = false;
            return result;
        }

        // 校验状态是否可发布
        if (!PublishableStatuses.Contains(version.Status))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "INVALID_STATUS",
                Message = $"版本状态不可发布（当前 {version.Status}）",
                FieldName = "Status"
            });
        }

        // 校验版本编码非空
        if (string.IsNullOrWhiteSpace(version.VersionCode))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_VERSION_CODE",
                Message = "版本编码不能为空",
                FieldName = "VersionCode"
            });
        }

        // 校验生效时间范围
        if (version.EffectiveFrom.HasValue && version.EffectiveTo.HasValue)
        {
            if (version.EffectiveFrom.Value >= version.EffectiveTo.Value)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_DATE_RANGE",
                    Message = "生效起始时间必须早于截止时间",
                    FieldName = "EffectiveFrom,EffectiveTo",
                    Details = $"起始: {version.EffectiveFrom:yyyy-MM-dd}, 截止: {version.EffectiveTo:yyyy-MM-dd}"
                });
            }
        }

        // 校验 DemandPriority 子块（P0-01/P1-02：基于 ContentSnapshotJson 真实持久化内容——
        // 不再读临时主题 JSON；兼容仅传主题 JSON 的装配路径，EnsureRuleSetNormalized 自动归一化到快照）
        EnsureRuleSetNormalized(version);
        var demandPriorityJson = ExtractBlockJson(version.ContentSnapshotJson, "DemandPriority");
        if (string.IsNullOrWhiteSpace(demandPriorityJson))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_DEMAND_PRIORITY",
                Message = "DemandPriorityJson 不能为空（规则集版本须含完整排序配置）",
                FieldName = "DemandPriorityJson"
            });
        }
        else
        {
            try
            {
                var block = System.Text.Json.JsonSerializer.Deserialize<DemandPriorityBlock>(
                    demandPriorityJson,
                    JsonOptions);

                if (block == null)
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = "INVALID_JSON",
                        Message = "DemandPriorityJson 反序列化结果为空",
                        FieldName = "DemandPriorityJson"
                    });
                }
                else
                {
                    var dpResult = _demandPriorityValidator.Validate(block);
                    foreach (var err in dpResult.Errors)
                    {
                        result.Errors.Add(new ValidationError
                        {
                            Code = "INVALID_DEMAND_PRIORITY",
                            Message = err,
                            FieldName = "DemandPriorityJson"
                        });
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_JSON",
                    Message = "DemandPriorityJson 格式无效",
                    FieldName = "DemandPriorityJson",
                    Details = ex.Message
                });
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    /// <summary>开发者：3号位</summary>
    public async Task<PublishValidationResult> ValidateParameterSetVersionForPublishAsync(long parameterSetVersionId, CancellationToken ct = default)
    {
        var result = new PublishValidationResult
        {
            ValidatedAt = DateTime.UtcNow
        };

        var version = await _parameterSetVersionRepository.GetByIdAsync(parameterSetVersionId, ct);
        if (version == null)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "NOT_FOUND",
                Message = $"参数集版本不存在：{parameterSetVersionId}"
            });
            result.IsValid = false;
            return result;
        }

        // 校验状态是否可发布
        if (!PublishableStatuses.Contains(version.Status))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "INVALID_STATUS",
                Message = $"版本状态不可发布（当前 {version.Status}）",
                FieldName = "Status"
            });
        }

        // 校验版本编码非空
        if (string.IsNullOrWhiteSpace(version.VersionCode))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_VERSION_CODE",
                Message = "版本编码不能为空",
                FieldName = "VersionCode"
            });
        }

        // 校验生效时间范围
        if (version.EffectiveFrom.HasValue && version.EffectiveTo.HasValue)
        {
            if (version.EffectiveFrom.Value >= version.EffectiveTo.Value)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_DATE_RANGE",
                    Message = "生效起始时间必须早于截止时间",
                    FieldName = "EffectiveFrom,EffectiveTo",
                    Details = $"起始: {version.EffectiveFrom:yyyy-MM-dd}, 截止: {version.EffectiveTo:yyyy-MM-dd}"
                });
            }
        }

        // 校验五子块（P0-01/P1-02：基于 ContentSnapshotJson 真实持久化内容，不再读临时主题 JSON；
        // 兼容仅传主题 JSON 的装配路径，EnsureParameterSetNormalized 自动归一化到快照）
        EnsureParameterSetNormalized(version);
        ValidateLockJson(ExtractBlockJson(version.ContentSnapshotJson, "Lock"), result);
        ValidateSupplyJson(ExtractBlockJson(version.ContentSnapshotJson, "Supply"), result);
        ValidateProcurementJson(ExtractBlockJson(version.ContentSnapshotJson, "Procurement"), result);

        // P0-02b：SolverStrategy / CandidateGuardrail 两 Validator 接入发布链（E-4，契约 §6.10.5 校验器接线）
        ValidateSolverStrategyJson(ExtractBlockJson(version.ContentSnapshotJson, "SolverStrategy"), result);
        ValidateCandidateGuardrailJson(ExtractBlockJson(version.ContentSnapshotJson, "CandidateGuardrail"), result);

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    /// <summary>校验 SolverStrategyJson：缺失/损坏/业务约束（E-4 SolverStrategyValidator 接线，P0-02b）</summary>
    private void ValidateSolverStrategyJson(string? json, PublishValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_SOLVER_STRATEGY",
                Message = "SolverStrategyJson 不能为空（参数集版本须含 Solver 策略配置）",
                FieldName = "SolverStrategyJson"
            });
            return;
        }

        try
        {
            var block = System.Text.Json.JsonSerializer.Deserialize<SolverStrategyBlock>(json, JsonOptions);
            if (block == null)
            {
                result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "SolverStrategyJson 反序列化结果为空", FieldName = "SolverStrategyJson" });
                return;
            }

            var ssResult = _solverStrategyValidator.Validate(block);
            foreach (var err in ssResult.Errors)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_SOLVER_STRATEGY",
                    Message = err,
                    FieldName = "SolverStrategyJson"
                });
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "SolverStrategyJson 格式无效", FieldName = "SolverStrategyJson", Details = ex.Message });
        }
    }

    /// <summary>校验 CandidateGuardrailJson：缺失/损坏/业务约束（E-4 CandidateGuardrailValidator 接线，P0-02b）</summary>
    private void ValidateCandidateGuardrailJson(string? json, PublishValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_CANDIDATE_GUARDRAIL",
                Message = "CandidateGuardrailJson 不能为空（参数集版本须含 Candidate 技术 Guardrail 配置）",
                FieldName = "CandidateGuardrailJson"
            });
            return;
        }

        try
        {
            var block = System.Text.Json.JsonSerializer.Deserialize<CandidateGuardrailBlock>(json, JsonOptions);
            if (block == null)
            {
                result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "CandidateGuardrailJson 反序列化结果为空", FieldName = "CandidateGuardrailJson" });
                return;
            }

            var cgResult = _candidateGuardrailValidator.Validate(block);
            foreach (var err in cgResult.Errors)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_CANDIDATE_GUARDRAIL",
                    Message = err,
                    FieldName = "CandidateGuardrailJson"
                });
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "CandidateGuardrailJson 格式无效", FieldName = "CandidateGuardrailJson", Details = ex.Message });
        }
    }

    /// <summary>校验 LockJson：缺失/损坏/业务约束（P0-05）</summary>
    private void ValidateLockJson(string? json, PublishValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_PARAMETER",
                Message = "LockJson 不能为空（参数集版本须含完整参数配置）",
                FieldName = "LockJson"
            });
            return;
        }

        try
        {
            var block = System.Text.Json.JsonSerializer.Deserialize<LockBlock>(json, JsonOptions);
            if (block == null)
            {
                result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "LockJson 反序列化结果为空", FieldName = "LockJson" });
                return;
            }

            // 触发阈值：启用 RemainingTime 阈值时其值必须为正
            if (block.Trigger.UseRemainingTimeThreshold && block.Trigger.RemainingTimeThresholdHours <= 0)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_LOCK_TRIGGER",
                    Message = "LockJson 启用 RemainingTime 阈值但阈值非正（RemainingTimeThresholdHours > 0 必须）",
                    FieldName = "LockJson"
                });
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "LockJson 格式无效", FieldName = "LockJson", Details = ex.Message });
        }
    }

    /// <summary>校验 SupplyJson：缺失/损坏/业务约束（P0-05）</summary>
    private void ValidateSupplyJson(string? json, PublishValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_PARAMETER",
                Message = "SupplyJson 不能为空（参数集版本须含完整参数配置）",
                FieldName = "SupplyJson"
            });
            return;
        }

        try
        {
            var block = System.Text.Json.JsonSerializer.Deserialize<SupplyBlock>(json, JsonOptions);
            if (block == null)
            {
                result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "SupplyJson 反序列化结果为空", FieldName = "SupplyJson" });
                return;
            }

            // Warehouse 优先级顺序隐含优先级，重复项会导致歧义
            if (block.Inventory.WarehousePriority.Count != block.Inventory.WarehousePriority.Distinct().Count())
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_WAREHOUSE_PRIORITY",
                    Message = "SupplyJson 的 WarehousePriority 存在重复 Warehouse，优先级歧义",
                    FieldName = "SupplyJson"
                });
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "SupplyJson 格式无效", FieldName = "SupplyJson", Details = ex.Message });
        }
    }

    /// <summary>校验 ProcurementJson：缺失/损坏/业务约束（P0-05：Planning Yield / 采购 LT / Offset / OverdueMargin）</summary>
    private void ValidateProcurementJson(string? json, PublishValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_PARAMETER",
                Message = "ProcurementJson 不能为空（参数集版本须含完整参数配置）",
                FieldName = "ProcurementJson"
            });
            return;
        }

        try
        {
            var block = System.Text.Json.JsonSerializer.Deserialize<ProcurementBlock>(json, JsonOptions);
            if (block == null)
            {
                result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "ProcurementJson 反序列化结果为空", FieldName = "ProcurementJson" });
                return;
            }

            foreach (var rule in block.PlanningYields)
            {
                if (rule.YieldPercent <= 0 || rule.YieldPercent > 100)
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = "INVALID_YIELD",
                        Message = $"PlanningYield 越界（0 < YieldPercent <= 100）：物料 {rule.MaterialId} = {rule.YieldPercent}",
                        FieldName = "ProcurementJson"
                    });
                }
            }

            foreach (var rule in block.DefaultPurchaseLt)
            {
                if (rule.DefaultLtDays <= 0)
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = "INVALID_PURCHASE_LT",
                        Message = $"DefaultPurchaseLt 越界（DefaultLtDays > 0）：Warehouse {rule.WarehouseCode} = {rule.DefaultLtDays}",
                        FieldName = "ProcurementJson"
                    });
                }
            }

            foreach (var offset in block.ArrivalToUsableOffsets)
            {
                if (offset.OffsetHours < 0)
                {
                    result.Errors.Add(new ValidationError
                    {
                        Code = "INVALID_OFFSET",
                        Message = $"ArrivalToUsableOffsets 越界（OffsetHours >= 0）：Warehouse {offset.WarehouseCode} = {offset.OffsetHours}",
                        FieldName = "ProcurementJson"
                    });
                }
            }

            if (block.OverdueMargin.MarginPercent is < 0 or > 100)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_OVERDUE_MARGIN",
                    Message = $"OverdueMargin.MarginPercent 越界（0~100）：{block.OverdueMargin.MarginPercent}",
                    FieldName = "ProcurementJson"
                });
            }

            if (block.OverdueMargin.MinimumExtraDays < 0)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_OVERDUE_MARGIN",
                    Message = $"OverdueMargin.MinimumExtraDays 越界（>= 0）：{block.OverdueMargin.MinimumExtraDays}",
                    FieldName = "ProcurementJson"
                });
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            result.Errors.Add(new ValidationError { Code = "INVALID_JSON", Message = "ProcurementJson 格式无效", FieldName = "ProcurementJson", Details = ex.Message });
        }
    }

    /// <summary>JSON 反序列化选项（大小写不敏感）</summary>
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 发布时取 RuleSet 侧 ContentSnapshotJson（契约 §6.10.5 内容归属：DemandPriority 子块）。
    /// P0-01：快照为唯一内容真相；兼容仅传主题 JSON 的装配路径（单测）——先归一化再取快照，幂等；
    /// 缺失时防御性抛出（发布链 Validate 已保证非空，不应走到）。
    /// </summary>
    private static string BuildRuleSetContentSnapshot(RuleSetVersion version)
    {
        EnsureRuleSetNormalized(version);
        return version.ContentSnapshotJson
            ?? throw new InvalidOperationException($"规则集版本 {version.Id} 无内容快照，无法聚合发布");
    }

    /// <summary>
    /// 发布时取 ParameterSet 侧 ContentSnapshotJson（契约 §6.10.5 内容归属：Lock/Supply/Procurement/SolverStrategy/CandidateGuardrail 五子块）。
    /// P0-01：快照为唯一内容真相（同 RuleSet 语义，幂等）。
    /// </summary>
    private static string BuildParameterSetContentSnapshot(ParameterSetVersion version)
    {
        EnsureParameterSetNormalized(version);
        return version.ContentSnapshotJson
            ?? throw new InvalidOperationException($"参数集版本 {version.Id} 无内容快照，无法聚合发布");
    }

    // ==================== P0-01：内容持久化链辅助（ContentSnapshotJson 唯一真相，方案 A2） ====================

    /// <summary>
    /// 归一化：把 RuleSet 主题 JSON（内存/API 字段）→ ContentSnapshotJson 子块（真实持久化载体）。
    /// 语义：任一主题 JSON 非空即全量重建快照（主题 JSON 为编辑态真相）；全部为空则保持现有快照（真实 DB 重读场景，避免覆盖已落库内容）。
    /// </summary>
    private static void EnsureRuleSetNormalized(RuleSetVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.DemandPriorityJson))
        {
            return;
        }

        var blocks = new Dictionary<string, object>
        {
            ["DemandPriority"] = System.Text.Json.JsonSerializer.Deserialize<DemandPriorityBlock>(version.DemandPriorityJson, JsonOptions)
                ?? throw new InvalidOperationException($"规则集版本 {version.Id} 的 DemandPriorityJson 反序列化失败，无法归一化内容快照")
        };

        version.ContentSnapshotJson = System.Text.Json.JsonSerializer.Serialize(blocks);
    }

    /// <summary>归一化：把 ParameterSet 五主题 JSON（内存/API 字段）→ ContentSnapshotJson 五子块；全部为空则保持现有快照。</summary>
    private static void EnsureParameterSetNormalized(ParameterSetVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.LockJson)
            && string.IsNullOrWhiteSpace(version.SupplyJson)
            && string.IsNullOrWhiteSpace(version.ProcurementJson)
            && string.IsNullOrWhiteSpace(version.SolverStrategyJson)
            && string.IsNullOrWhiteSpace(version.CandidateGuardrailJson))
        {
            return;
        }

        var blocks = new Dictionary<string, object>();
        TryAddBlock(blocks, "Lock", version.LockJson, version.Id);
        TryAddBlock(blocks, "Supply", version.SupplyJson, version.Id);
        TryAddBlock(blocks, "Procurement", version.ProcurementJson, version.Id);
        TryAddBlock(blocks, "SolverStrategy", version.SolverStrategyJson, version.Id);
        TryAddBlock(blocks, "CandidateGuardrail", version.CandidateGuardrailJson, version.Id);

        version.ContentSnapshotJson = System.Text.Json.JsonSerializer.Serialize(blocks);
    }

    /// <summary>归一化辅助：非空主题 JSON 反序列化为 JsonElement 后加入快照字典；空则跳过（该子块在快照中缺键，发布前校验将报 EMPTY）。</summary>
    private static void TryAddBlock(Dictionary<string, object> blocks, string blockName, string? json, long versionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        blocks[blockName] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json, JsonOptions);
    }

    /// <summary>
    /// 从 ContentSnapshotJson 提取指定子块的原始 JSON 字符串；快照为空、子块缺失或为 null 返回 null。
    /// 损坏快照视为子块缺失（由发布前校验报 EMPTY/INVALID，不静默吞错）。
    /// </summary>
    private static string? ExtractBlockJson(string? contentSnapshotJson, string blockName)
    {
        if (string.IsNullOrWhiteSpace(contentSnapshotJson))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(contentSnapshotJson);
            if (!doc.RootElement.TryGetProperty(blockName, out var block))
            {
                return null;
            }

            return block.ValueKind == System.Text.Json.JsonValueKind.Null ? null : block.GetRawText();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>投影：ContentSnapshotJson 的 DemandPriority 子块 → 主题 JSON 内存字段（GET 详情前端兼容）</summary>
    private static void ProjectRuleSetContent(RuleSetVersion version)
    {
        version.DemandPriorityJson = ExtractBlockJson(version.ContentSnapshotJson, "DemandPriority");
    }

    /// <summary>投影：ContentSnapshotJson 五子块 → 五主题 JSON 内存字段（GET 详情前端兼容）</summary>
    private static void ProjectParameterSetContent(ParameterSetVersion version)
    {
        version.LockJson = ExtractBlockJson(version.ContentSnapshotJson, "Lock");
        version.SupplyJson = ExtractBlockJson(version.ContentSnapshotJson, "Supply");
        version.ProcurementJson = ExtractBlockJson(version.ContentSnapshotJson, "Procurement");
        version.SolverStrategyJson = ExtractBlockJson(version.ContentSnapshotJson, "SolverStrategy");
        version.CandidateGuardrailJson = ExtractBlockJson(version.ContentSnapshotJson, "CandidateGuardrail");
    }

    /// <summary>P0-02：状态机约束——已发布不可原地修改；失效/归档不可改；仅 DRAFT/SUBMITTED/APPROVED 可编辑。</summary>
    private static void EnsureUpdatable(string status, long versionId)
    {
        if (status == GovernanceVersionStatus.Published)
        {
            throw new InvalidOperationException($"版本已发布，不可原地修改（历史不可变，须创建新版本）：{versionId}");
        }

        if (status is GovernanceVersionStatus.Disabled or GovernanceVersionStatus.Archived)
        {
            throw new InvalidOperationException($"版本状态不可修改（当前 {status}）：{versionId}");
        }
    }

    // ==================== P0-06：StrategyProfileVersion 治理完整闭环 ====================

    public async Task<PublishValidationResult> ValidateStrategyProfileVersionForPublishAsync(long strategyProfileVersionId, CancellationToken ct = default)
    {
        var result = new PublishValidationResult
        {
            ValidatedAt = DateTime.UtcNow
        };

        var version = await _strategyProfileVersionRepository.GetByIdAsync(strategyProfileVersionId, ct);
        if (version == null)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "NOT_FOUND",
                Message = $"策略包版本不存在：{strategyProfileVersionId}"
            });
            result.IsValid = false;
            return result;
        }

        // 状态是否可发布
        if (!PublishableStatuses.Contains(version.Status))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "INVALID_STATUS",
                Message = $"版本状态不可发布（当前 {version.Status}）",
                FieldName = "Status"
            });
        }

        // 版本编码非空
        if (string.IsNullOrWhiteSpace(version.VersionCode))
        {
            result.Errors.Add(new ValidationError
            {
                Code = "EMPTY_VERSION_CODE",
                Message = "版本编码不能为空",
                FieldName = "VersionCode"
            });
        }

        // 引用合法性：RuleSetVersion / ParameterSetVersion 存在且 PUBLISHED（P0-06 引用合法性检查）
        await ValidateReferencedVersionAsync(
            _ruleSetVersionRepository.GetByIdAsync(version.RuleSetVersionId, ct),
            "RuleSetVersion", version.RuleSetVersionId, "规则集版本",
            v => v.Status, result, ct);
        await ValidateReferencedVersionAsync(
            _parameterSetVersionRepository.GetByIdAsync(version.ParameterSetVersionId, ct),
            "ParameterSetVersion", version.ParameterSetVersionId, "参数集版本",
            v => v.Status, result, ct);

        // 生效窗口
        if (version.EffectiveFrom.HasValue && version.EffectiveTo.HasValue
            && version.EffectiveFrom.Value >= version.EffectiveTo.Value)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "INVALID_DATE_RANGE",
                Message = "生效起始时间必须早于截止时间",
                FieldName = "EffectiveFrom,EffectiveTo",
                Details = $"起始: {version.EffectiveFrom:yyyy-MM-dd}, 截止: {version.EffectiveTo:yyyy-MM-dd}"
            });
        }

        // 默认歧义：本版本 IsDefault=1 且同 Profile 已存在另一 PUBLISHED 默认 → 报配置错误
        if (version.IsDefault)
        {
            var existingDefault = await _strategyProfileVersionRepository.GetDefaultByStrategyProfileIdAsync(version.StrategyProfileId, ct);
            if (existingDefault != null && existingDefault.Id != version.Id)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "DEFAULT_CONFLICT",
                    Message = $"策略包 {version.StrategyProfileId} 已存在 PUBLISHED 默认版本 {existingDefault.Id}（{existingDefault.VersionCode}），发布将违反 UQ_StrategyProfileVersion_DefaultPublished",
                    FieldName = "IsDefault"
                });
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    /// <summary>引用版本合法性：存在且 PUBLISHED（P0-06；泛型兼容 RuleSet/ParameterSet 不同实体类型，statusGetter 提取状态）</summary>
    private async Task ValidateReferencedVersionAsync<T>(
        Task<T?> task, string entityType, long versionId, string displayName,
        Func<T, string> statusGetter, PublishValidationResult result, CancellationToken ct)
        where T : class
    {
        var referenced = await task;
        if (referenced == null)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "REF_NOT_FOUND",
                Message = $"引用的{displayName}不存在：{versionId}",
                FieldName = entityType
            });
        }
        else if (statusGetter(referenced) != GovernanceVersionStatus.Published)
        {
            result.Errors.Add(new ValidationError
            {
                Code = "REF_NOT_PUBLISHED",
                Message = $"引用的{displayName}未发布（当前 {statusGetter(referenced)}）：{versionId}",
                FieldName = entityType
            });
        }
    }

    public async Task PublishStrategyProfileVersionAsync(long strategyProfileVersionId, string? publishedBy, CancellationToken ct = default)
    {
        var version = await _strategyProfileVersionRepository.GetByIdAsync(strategyProfileVersionId, ct)
            ?? throw new InvalidOperationException($"策略包版本不存在：{strategyProfileVersionId}");

        // P0-06：正式 Publish 强制发布前完整校验，无绕过路径（与 P0-05 一致）
        var validation = await ValidateStrategyProfileVersionForPublishAsync(strategyProfileVersionId, ct);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"策略包版本发布前校验失败：{validation.GetErrorMessage()}");
        }

        EnsurePublishable(version.Status, strategyProfileVersionId);

        // IsDefault=1：先清同 Profile 其他默认再置位，避免 UQ_StrategyProfileVersion_DefaultPublished 冲突
        if (version.IsDefault)
        {
            await _strategyProfileVersionRepository.ClearDefaultFlagAsync(version.StrategyProfileId, strategyProfileVersionId, ct);
        }

        var beforeStatus = version.Status;
        version.Status = GovernanceVersionStatus.Published;
        version.PublishedAt = DateTime.UtcNow;
        version.PublishedBy = publishedBy;

        await _strategyProfileVersionRepository.UpdateAsync(version, ct);

        // A-7 审计日志
        await _auditLogRepository.AddAsync(new Core.Entities.Auth.GovernanceAuditLog
        {
            OperationType = "Publish",
            EntityType = "StrategyProfileVersion",
            EntityId = strategyProfileVersionId,
            VersionCode = version.VersionCode,
            BeforeStatus = beforeStatus,
            AfterStatus = GovernanceVersionStatus.Published,
            OperatedBy = publishedBy,
            OperatedAt = DateTime.UtcNow,
            Remarks = "策略包版本发布"
        }, ct);
    }

    public async Task<StrategyProfileVersion?> ResolveDefaultStrategyProfileVersionAsync(string? runType, DateTime? asOf = null, CancellationToken ct = default)
    {
        // P0-06 跨号位冻结语义：无显式 StrategyProfileVersionId 时，必须得到当前有效、无歧义的 PUBLISHED 策略包
        if (string.IsNullOrWhiteSpace(runType))
        {
            throw new InvalidOperationException("解析默认策略包需要 RunType（StrategyProfile.RunType 匹配）");
        }

        var candidates = await _strategyProfileVersionRepository.GetDefaultByRunTypeAsync(runType, ct);
        var effective = asOf ?? DateTime.UtcNow;

        // 过滤生效窗口：EffectiveFrom <= now（有值才校验）、EffectiveTo >= now（有值才校验）
        var inWindow = candidates
            .Where(v => (!v.EffectiveFrom.HasValue || v.EffectiveFrom.Value <= effective)
                     && (!v.EffectiveTo.HasValue || v.EffectiveTo.Value >= effective))
            .ToList();

        return inWindow.Count switch
        {
            0 => null,
            1 => inWindow[0],
            _ => throw new InvalidOperationException(
                $"RunType={runType} 的默认 PUBLISHED 策略包存在歧义：{inWindow.Count} 个候选（Id: {string.Join(", ", inWindow.Select(v => v.Id))}），须收敛为 1 个再执行"),
        };
    }

    public async Task<RunStrategyProfileTrace> GetRunStrategyProfileTraceAsync(long strategyProfileVersionId, CancellationToken ct = default)
    {
        var version = await _strategyProfileVersionRepository.GetByIdAsync(strategyProfileVersionId, ct)
            ?? throw new InvalidOperationException($"策略包版本不存在：{strategyProfileVersionId}");

        var profile = await _strategyProfileRepository.GetByIdAsync(version.StrategyProfileId, ct);
        var ruleSet = await _ruleSetVersionRepository.GetByIdAsync(version.RuleSetVersionId, ct);
        var parameterSet = await _parameterSetVersionRepository.GetByIdAsync(version.ParameterSetVersionId, ct);

        return new RunStrategyProfileTrace
        {
            StrategyProfileVersionId = version.Id,
            VersionCode = version.VersionCode,
            StrategyProfileId = version.StrategyProfileId,
            StrategyProfileCode = profile?.StrategyProfileCode,
            RunType = profile?.RunType,
            RuleSetVersionId = version.RuleSetVersionId,
            RuleSetVersionCode = ruleSet?.VersionCode,
            ParameterSetVersionId = version.ParameterSetVersionId,
            ParameterSetVersionCode = parameterSet?.VersionCode,
            Status = version.Status,
            EffectiveFrom = version.EffectiveFrom,
            EffectiveTo = version.EffectiveTo,
            IsDefault = version.IsDefault,
            PublishedAt = version.PublishedAt,
            PublishedBy = version.PublishedBy,
        };
    }

    // ==================== P0-01/P0-02：CRUD 状态机不可绕过（ContentSnapshotJson 唯一真相） ====================

    /// <summary>
    /// 创建规则集版本（P0-02：强制 DRAFT——入参 Status 一律忽略覆盖，状态只能经 Submit/Approve/Publish 流转，Create 不可直提 PUBLISHED；
    /// 治理字段一律置空，由后续流转写入）。
    /// P0-01：DRAFT 编辑的 DemandPriorityJson（内存/API 字段）归一化写入 ContentSnapshotJson 后持久化。
    /// </summary>
    public async Task<RuleSetVersion> CreateRuleSetVersionAsync(RuleSetVersion version, string? createdBy, CancellationToken ct = default)
    {
        version.Status = GovernanceVersionStatus.Draft;
        version.CreatedAt = DateTime.UtcNow;
        version.CreatedBy = createdBy;
        version.PublishedAt = null;
        version.PublishedBy = null;
        version.ApprovedAt = null;
        version.ApprovedBy = null;

        EnsureRuleSetNormalized(version);

        return await _ruleSetVersionRepository.AddAsync(version, ct);
    }

    /// <summary>
    /// 更新规则集版本（P0-02：已发布不可原地修改、失效/归档不可改；入参 Status/治理字段/主键一律以现有记录为准，禁止越权改状态）。
    /// P0-01：DRAFT 编辑内容归一化到 ContentSnapshotJson 后持久化（任一主题 JSON 非空则重建，全空保持现有快照）。
    /// </summary>
    public async Task UpdateRuleSetVersionAsync(long ruleSetVersionId, RuleSetVersion version, CancellationToken ct = default)
    {
        var existing = await _ruleSetVersionRepository.GetByIdAsync(ruleSetVersionId, ct)
            ?? throw new InvalidOperationException($"规则集版本不存在：{ruleSetVersionId}");

        EnsureUpdatable(existing.Status, ruleSetVersionId);

        version.Id = existing.Id;
        version.RuleSetId = existing.RuleSetId;
        version.Status = existing.Status;
        version.CreatedAt = existing.CreatedAt;
        version.CreatedBy = existing.CreatedBy;
        version.PublishedAt = existing.PublishedAt;
        version.PublishedBy = existing.PublishedBy;
        version.ApprovedAt = existing.ApprovedAt;
        version.ApprovedBy = existing.ApprovedBy;

        EnsureRuleSetNormalized(version);

        await _ruleSetVersionRepository.UpdateAsync(version, ct);
    }

    /// <summary>获取规则集版本详情（P0-01：ContentSnapshotJson → DemandPriorityJson 投影，前端 API 兼容）</summary>
    public async Task<RuleSetVersion?> GetRuleSetVersionAsync(long ruleSetVersionId, CancellationToken ct = default)
    {
        var version = await _ruleSetVersionRepository.GetByIdAsync(ruleSetVersionId, ct);
        if (version != null)
        {
            ProjectRuleSetContent(version);
        }
        return version;
    }

    /// <summary>创建参数集版本（P0-02：强制 DRAFT；治理字段置空；P0-01：五主题 JSON 归一化到 ContentSnapshotJson 持久化）</summary>
    public async Task<ParameterSetVersion> CreateParameterSetVersionAsync(ParameterSetVersion version, string? createdBy, CancellationToken ct = default)
    {
        version.Status = GovernanceVersionStatus.Draft;
        version.CreatedAt = DateTime.UtcNow;
        version.CreatedBy = createdBy;
        version.PublishedAt = null;
        version.PublishedBy = null;
        version.ApprovedAt = null;
        version.ApprovedBy = null;

        EnsureParameterSetNormalized(version);

        return await _parameterSetVersionRepository.AddAsync(version, ct);
    }

    /// <summary>更新参数集版本（P0-02：已发布/失效/归档不可改；Status/治理字段冻结；P0-01：内容归一化到快照）</summary>
    public async Task UpdateParameterSetVersionAsync(long parameterSetVersionId, ParameterSetVersion version, CancellationToken ct = default)
    {
        var existing = await _parameterSetVersionRepository.GetByIdAsync(parameterSetVersionId, ct)
            ?? throw new InvalidOperationException($"参数集版本不存在：{parameterSetVersionId}");

        EnsureUpdatable(existing.Status, parameterSetVersionId);

        version.Id = existing.Id;
        version.ParameterSetId = existing.ParameterSetId;
        version.Status = existing.Status;
        version.CreatedAt = existing.CreatedAt;
        version.CreatedBy = existing.CreatedBy;
        version.PublishedAt = existing.PublishedAt;
        version.PublishedBy = existing.PublishedBy;
        version.ApprovedAt = existing.ApprovedAt;
        version.ApprovedBy = existing.ApprovedBy;

        EnsureParameterSetNormalized(version);

        await _parameterSetVersionRepository.UpdateAsync(version, ct);
    }

    /// <summary>获取参数集版本详情（P0-01：ContentSnapshotJson 五子块 → 五主题 JSON 投影，前端 API 兼容）</summary>
    public async Task<ParameterSetVersion?> GetParameterSetVersionAsync(long parameterSetVersionId, CancellationToken ct = default)
    {
        var version = await _parameterSetVersionRepository.GetByIdAsync(parameterSetVersionId, ct);
        if (version != null)
        {
            ProjectParameterSetContent(version);
        }
        return version;
    }

    /// <summary>创建策略包版本（P0-02：强制 DRAFT；治理字段置空）</summary>
    public async Task<StrategyProfileVersion> CreateStrategyProfileVersionAsync(StrategyProfileVersion version, string? createdBy, CancellationToken ct = default)
    {
        version.Status = GovernanceVersionStatus.Draft;
        version.CreatedAt = DateTime.UtcNow;
        version.CreatedBy = createdBy;
        version.PublishedAt = null;
        version.PublishedBy = null;
        version.ApprovedAt = null;
        version.ApprovedBy = null;

        return await _strategyProfileVersionRepository.AddAsync(version, ct);
    }

    /// <summary>更新策略包版本（P0-02：已发布/失效/归档不可改；Status/治理字段/引用字段冻结）</summary>
    public async Task UpdateStrategyProfileVersionAsync(long strategyProfileVersionId, StrategyProfileVersion version, CancellationToken ct = default)
    {
        var existing = await _strategyProfileVersionRepository.GetByIdAsync(strategyProfileVersionId, ct)
            ?? throw new InvalidOperationException($"策略包版本不存在：{strategyProfileVersionId}");

        EnsureUpdatable(existing.Status, strategyProfileVersionId);

        version.Id = existing.Id;
        version.StrategyProfileId = existing.StrategyProfileId;
        version.RuleSetVersionId = existing.RuleSetVersionId;
        version.ParameterSetVersionId = existing.ParameterSetVersionId;
        version.IsDefault = existing.IsDefault;
        version.Status = existing.Status;
        version.CreatedAt = existing.CreatedAt;
        version.CreatedBy = existing.CreatedBy;
        version.PublishedAt = existing.PublishedAt;
        version.PublishedBy = existing.PublishedBy;
        version.ApprovedAt = existing.ApprovedAt;
        version.ApprovedBy = existing.ApprovedBy;

        await _strategyProfileVersionRepository.UpdateAsync(version, ct);
    }
}
