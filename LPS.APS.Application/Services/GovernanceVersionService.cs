using RuleSetVersion = LPS.APS.Core.Entities.APS.RuleSetVersion;
using ParameterSetVersion = LPS.APS.Core.Entities.APS.ParameterSetVersion;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
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
    private readonly IGovernanceAuditLogRepository _auditLogRepository;

    public GovernanceVersionService(
        IRuleSetVersionRepository ruleSetVersionRepository,
        IParameterSetVersionRepository parameterSetVersionRepository,
        IGovernanceAuditLogRepository auditLogRepository)
    {
        _ruleSetVersionRepository = ruleSetVersionRepository;
        _parameterSetVersionRepository = parameterSetVersionRepository;
        _auditLogRepository = auditLogRepository;
    }

    /// <summary>合法发布前驱状态（其余状态发布一律拒绝）</summary>
    private static readonly string[] PublishableStatuses = [GovernanceVersionStatus.Draft, GovernanceVersionStatus.Submitted, GovernanceVersionStatus.Approved];

    public async Task PublishRuleSetVersionAsync(long ruleSetVersionId, string? publishedBy, CancellationToken ct = default)
    {
        var version = await _ruleSetVersionRepository.GetByIdAsync(ruleSetVersionId, ct)
            ?? throw new InvalidOperationException($"规则集版本不存在：{ruleSetVersionId}");

        EnsurePublishable(version.Status, ruleSetVersionId);

        var beforeStatus = version.Status;
        version.Status = GovernanceVersionStatus.Published;
        version.PublishedAt = DateTime.UtcNow;
        version.PublishedBy = publishedBy;

        // A-6 不变量：若发布为默认版本，清除同 RuleSet 内其他版本的 IsDefault
        if (version.IsDefault)
        {
            await _ruleSetVersionRepository.ClearDefaultFlagAsync(version.RuleSetId, ruleSetVersionId, ct);
        }

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

        EnsurePublishable(version.Status, parameterSetVersionId);

        var beforeStatus = version.Status;
        version.Status = GovernanceVersionStatus.Published;
        version.PublishedAt = DateTime.UtcNow;
        version.PublishedBy = publishedBy;

        // A-6 不变量：若发布为默认版本，清除同 ParameterSet 内其他版本的 IsDefault
        if (version.IsDefault)
        {
            await _parameterSetVersionRepository.ClearDefaultFlagAsync(version.ParameterSetId, parameterSetVersionId, ct);
        }

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

        var diffs = new List<FieldDiff>
        {
            CompareField("VersionCode", "版本编码", sourceVersion.VersionCode, targetVersion.VersionCode),
            CompareField("Status", "状态", sourceVersion.Status, targetVersion.Status),
            CompareField("IsDefault", "是否默认", sourceVersion.IsDefault.ToString(), targetVersion.IsDefault.ToString()),
            CompareField("DemandPriorityJson", "需求优先级配置", sourceVersion.DemandPriorityJson, targetVersion.DemandPriorityJson),
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

        var diffs = new List<FieldDiff>
        {
            CompareField("VersionCode", "版本编码", sourceVersion.VersionCode, targetVersion.VersionCode),
            CompareField("Status", "状态", sourceVersion.Status, targetVersion.Status),
            CompareField("IsDefault", "是否默认", sourceVersion.IsDefault.ToString(), targetVersion.IsDefault.ToString()),
            CompareField("LockJson", "锁定配置", sourceVersion.LockJson, targetVersion.LockJson),
            CompareField("SupplyJson", "供给配置", sourceVersion.SupplyJson, targetVersion.SupplyJson),
            CompareField("ProcurementJson", "采购配置", sourceVersion.ProcurementJson, targetVersion.ProcurementJson),
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

        // 校验 DemandPriorityJson 格式（如果非空）
        if (!string.IsNullOrWhiteSpace(version.DemandPriorityJson))
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(version.DemandPriorityJson);
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

        // 校验 JSON 配置字段格式
        if (!string.IsNullOrWhiteSpace(version.LockJson))
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(version.LockJson);
            }
            catch (System.Text.Json.JsonException ex)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_JSON",
                    Message = "LockJson 格式无效",
                    FieldName = "LockJson",
                    Details = ex.Message
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(version.SupplyJson))
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(version.SupplyJson);
            }
            catch (System.Text.Json.JsonException ex)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_JSON",
                    Message = "SupplyJson 格式无效",
                    FieldName = "SupplyJson",
                    Details = ex.Message
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(version.ProcurementJson))
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(version.ProcurementJson);
            }
            catch (System.Text.Json.JsonException ex)
            {
                result.Errors.Add(new ValidationError
                {
                    Code = "INVALID_JSON",
                    Message = "ProcurementJson 格式无效",
                    FieldName = "ProcurementJson",
                    Details = ex.Message
                });
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }
}
