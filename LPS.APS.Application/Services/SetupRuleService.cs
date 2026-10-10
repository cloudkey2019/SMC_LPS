using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Setup;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Exceptions;
using LPS.APS.Core.Interfaces;
using LPS.APS.Application.Models;
// 消歧：APS 命名空间存在实体 Task，别名定向 System.Threading.Tasks.Task（using 别名优先级高于命名空间成员）
using Task = System.Threading.Tasks.Task;

namespace LPS.APS.Application.Services;

/// <summary>
/// Setup 换型规则治理编排服务（4号位 Setup 契约 §11.1 #1-13）。
/// 组合：<see cref="IRuleSetVersionRepository"/>（版本态检查 + Status 派生 + 内容承载读写）+
///       <see cref="IMasterDataLookupRepository"/>（Code 回带）+
///       <see cref="IAuditLogRepository"/>（统一审计落库）+
///       <see cref="SetupTransitionRuleConflictValidator"/>（预校验 → 422，触底前拦截）。
/// 承载（重构方案 S-1）：Setup 规则不再存独立物理表，改存 RuleSetVersion.ContentSnapshotJson 的
///       "SetupTransitionRules" 子块（<see cref="SetupTransitionRuleProjector"/> 编解码；与 DemandPriority 子块同轨，零 DDL）。
/// 错误语义：唯一键冲突/数据红线 → <see cref="SetupRuleDataRedLineException"/>（422）；
///          版本/规则不存在 → <see cref="ResourceNotFoundException"/>（404）；
///          非 DRAFT 写操作/参数缺失 → <see cref="InvalidOperationException"/>（400）。
/// 注意：冻结校验器（39 测试绿）仅做同类内判重（EXACT 七元组 / DEFAULT 五元组分别），不判 EXACT×DEFAULT 跨类冲突——
///       与求解器设计一致（DEFAULT 为 EXACT 无命中时的兜底，二者并存合理）；契约 §8.3 跨类冲突条款将作为偏差项向 4号位 标注。
/// </summary>
public sealed class SetupRuleService : ISetupRuleService
{
    /// <summary>前端未传产品（EXACT 必填）哨兵值（Material.Id 为 IDENTITY，恒 &gt; 0）</summary>
    private const int MissingMaterialId = 0;

    /// <summary>统一审计实体类型常量（AuditLog.EntityType）</summary>
    public const string EntityTypeSetupTransitionRule = "SetupTransitionRule";

    private const string ActionCreate = "Create";
    private const string ActionUpdate = "Update";
    private const string ActionDelete = "Delete";

    private static readonly System.Text.Json.JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    private readonly IRuleSetVersionRepository _versionRepository;
    private readonly IMasterDataLookupRepository _masterDataLookup;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IGovernanceVersionService _governanceVersionService;
    private readonly ISetupUncoveredStatRepository _setupUncoveredStatRepository;
    private readonly IDataScopeService _dataScopeService;
    private readonly SetupTransitionRuleConflictValidator _conflictValidator = new();

    public SetupRuleService(
        IRuleSetVersionRepository versionRepository,
        IMasterDataLookupRepository masterDataLookup,
        IAuditLogRepository auditLogRepository,
        IGovernanceVersionService governanceVersionService,
        ISetupUncoveredStatRepository setupUncoveredStatRepository,
        IDataScopeService dataScopeService)
    {
        _versionRepository = versionRepository;
        _masterDataLookup = masterDataLookup;
        _auditLogRepository = auditLogRepository;
        _governanceVersionService = governanceVersionService;
        _setupUncoveredStatRepository = setupUncoveredStatRepository;
        _dataScopeService = dataScopeService;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SetupRuleDto>> ListAsync(long ruleSetVersionId, string ruleType, CancellationToken ct = default)
    {
        var version = await GetVersionOrThrowAsync(ruleSetVersionId, ct);
        var rules = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson);
        var codes = new MasterDataCodeCache(_masterDataLookup, ct);

        var result = new List<SetupRuleDto>();
        foreach (var rule in rules.Where(r => r.RuleType == ruleType))
        {
            result.Add(await ProjectAsync(rule, version.Status, codes));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SetupRuleDto?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var (ruleSetVersionId, _) = SetupTransitionRuleProjector.ParseRuleId(id);
        var version = await GetVersionOrThrowAsync(ruleSetVersionId, ct);
        var rule = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson).FirstOrDefault(r => r.Id == id);
        if (rule is null)
        {
            return null;
        }

        return await ProjectAsync(rule, version.Status, new MasterDataCodeCache(_masterDataLookup, ct));
    }

    /// <inheritdoc />
    public async Task<SetupRuleDto> CreateExactAsync(SetupRuleExactInput input, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        ValidateExactInput(input);
        // P1-04：SetupTransitionRule.ProductionDepartmentId 即业务范围维度——写路径经部门码 EnsureInScopeAsync(Department)，越界 403。
        await EnsureDepartmentScopeAsync(input.ProductionDepartmentId, actorUserId, ct);
        var version = await GetVersionForWriteAsync(input.RuleSetVersionId, ct);

        var existing = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson);

        var rule = new SetupTransitionRule
        {
            RuleSetVersionId = input.RuleSetVersionId,
            ProductionDepartmentId = input.ProductionDepartmentId,
            StageCode = input.StageCode.Trim(),
            OperationCode = input.OperationCode.Trim(),
            ResourceId = input.ResourceId,
            FromMaterialId = input.FromMaterialId,
            ToMaterialId = input.ToMaterialId,
            RuleType = SetupTransitionRuleType.Exact,
            SetupMinutes = input.SetupMinutes,
            IsActive = true,
            CreatedBy = actorUserCode,
            // CreatedAt 须显式打戳（非空 DateTime，缺省会带 0001-01-01 → 序列化空值异常）
            CreatedAt = DateTime.Now,
        };

        EnsureNoConflict(existing, rule, excludingId: 0);

        // 子块 Id 合成：版本内最大序号 + 1（跨版本由高位隔离，全局唯一）
        var maxSeq = existing.Count == 0 ? 0 : existing.Max(r => SetupTransitionRuleProjector.ParseRuleId(r.Id).Seq);
        rule.Id = SetupTransitionRuleProjector.BuildRuleId(input.RuleSetVersionId, maxSeq + 1);

        var rules = new List<SetupTransitionRule>(existing) { rule };
        // P1-03：审计前置预检（跨库无 2PC）——审计库不可写则整操作失败、业务零变更。
        await _auditLogRepository.EnsureWritableAsync(ct);
        await PersistRulesAsync(version, rules, ActionCreate, rule.Id, rule, null, Describe(rule), actorUserId, actorUserCode, ct);

        return await ProjectAsync(rule, version.Status, new MasterDataCodeCache(_masterDataLookup, ct));
    }

    /// <inheritdoc />
    public async Task<SetupRuleDto> CreateDefaultAsync(SetupRuleDefaultInput input, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        ValidateDefaultInput(input);
        // P1-04：同 CreateExact——部门业务范围校验，越界 403。
        await EnsureDepartmentScopeAsync(input.ProductionDepartmentId, actorUserId, ct);
        var version = await GetVersionForWriteAsync(input.RuleSetVersionId, ct);

        var existing = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson);

        var rule = new SetupTransitionRule
        {
            RuleSetVersionId = input.RuleSetVersionId,
            ProductionDepartmentId = input.ProductionDepartmentId,
            StageCode = input.StageCode.Trim(),
            OperationCode = input.OperationCode.Trim(),
            ResourceId = input.ResourceId,
            RuleType = SetupTransitionRuleType.Default,
            SetupMinutes = input.SetupMinutes,
            IsActive = true,
            CreatedBy = actorUserCode,
            // CreatedAt 同病（见 CreateExactAsync 注释）：DEFAULT 创建亦须显式打戳
            CreatedAt = DateTime.Now,
        };

        EnsureNoConflict(existing, rule, excludingId: 0);

        // 子块 Id 合成：版本内最大序号 + 1（跨版本由高位隔离，全局唯一）
        var maxSeq = existing.Count == 0 ? 0 : existing.Max(r => SetupTransitionRuleProjector.ParseRuleId(r.Id).Seq);
        rule.Id = SetupTransitionRuleProjector.BuildRuleId(input.RuleSetVersionId, maxSeq + 1);

        var rules = new List<SetupTransitionRule>(existing) { rule };
        // P1-03：审计前置预检（跨库无 2PC）——审计库不可写则整操作失败、业务零变更。
        await _auditLogRepository.EnsureWritableAsync(ct);
        await PersistRulesAsync(version, rules, ActionCreate, rule.Id, rule, null, Describe(rule), actorUserId, actorUserCode, ct);

        return await ProjectAsync(rule, version.Status, new MasterDataCodeCache(_masterDataLookup, ct));
    }

    /// <inheritdoc />
    public async Task<SetupRuleDto> UpdateExactAsync(long id, SetupRuleExactInput input, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        ValidateExactInput(input);

        var existing = await GetRuleByIdAsync(id, ct)
            ?? throw new ResourceNotFoundException($"换型规则不存在（Id={id}）。");
        EnsureSameTypeAndVersion(existing, SetupTransitionRuleType.Exact, input.RuleSetVersionId);

        // P1-04：老归属 + 新归属分别校验（防 IDOR 式跨部门记录迁移——仅授权 A 的用户不得把 B 部门规则改到 A）。
        await EnsureDepartmentScopeAsync(existing.ProductionDepartmentId, actorUserId, ct);
        await EnsureDepartmentScopeAsync(input.ProductionDepartmentId, actorUserId, ct);

        var version = await GetVersionForWriteAsync(existing.RuleSetVersionId, ct);

        var updated = BuildUpdated(existing, input.ProductionDepartmentId, input.StageCode, input.OperationCode,
            input.ResourceId, input.FromMaterialId, input.ToMaterialId, input.SetupMinutes, actorUserCode);

        var all = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson);
        EnsureNoConflict(all, updated, excludingId: id);

        var rules = all.Select(r => r.Id == id ? updated : r).ToList();
        // P1-03：审计前置预检（同 Create）。
        await _auditLogRepository.EnsureWritableAsync(ct);
        await PersistRulesAsync(version, rules, ActionUpdate, id, updated, existing, Describe(updated), actorUserId, actorUserCode, ct);

        return await ProjectAsync(updated, version.Status, new MasterDataCodeCache(_masterDataLookup, ct));
    }

    /// <inheritdoc />
    public async Task<SetupRuleDto> UpdateDefaultAsync(long id, SetupRuleDefaultInput input, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        ValidateDefaultInput(input);

        var existing = await GetRuleByIdAsync(id, ct)
            ?? throw new ResourceNotFoundException($"换型规则不存在（Id={id}）。");
        EnsureSameTypeAndVersion(existing, SetupTransitionRuleType.Default, input.RuleSetVersionId);

        // P1-04：老归属 + 新归属分别校验（同 UpdateExact，防跨部门迁移）。
        await EnsureDepartmentScopeAsync(existing.ProductionDepartmentId, actorUserId, ct);
        await EnsureDepartmentScopeAsync(input.ProductionDepartmentId, actorUserId, ct);

        var version = await GetVersionForWriteAsync(existing.RuleSetVersionId, ct);

        var updated = BuildUpdated(existing, input.ProductionDepartmentId, input.StageCode, input.OperationCode,
            input.ResourceId, fromMaterialId: null, toMaterialId: null, input.SetupMinutes, actorUserCode);

        var all = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson);
        EnsureNoConflict(all, updated, excludingId: id);

        var rules = all.Select(r => r.Id == id ? updated : r).ToList();
        // P1-03：审计前置预检（同 Create）。
        await _auditLogRepository.EnsureWritableAsync(ct);
        await PersistRulesAsync(version, rules, ActionUpdate, id, updated, existing, Describe(updated), actorUserId, actorUserCode, ct);

        return await ProjectAsync(updated, version.Status, new MasterDataCodeCache(_masterDataLookup, ct));
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        var existing = await GetRuleByIdAsync(id, ct)
            ?? throw new ResourceNotFoundException($"换型规则不存在（Id={id}）。");

        // P1-04：删除亦须目标部门在业务 Scope 内（防越权删除他部门规则）。
        await EnsureDepartmentScopeAsync(existing.ProductionDepartmentId, actorUserId, ct);

        var version = await GetVersionForWriteAsync(existing.RuleSetVersionId, ct);

        var rules = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson)
            .Where(r => r.Id != id)
            .ToList();
        // P1-03：审计前置预检（同 Create/Update）。
        await _auditLogRepository.EnsureWritableAsync(ct);
        await PersistRulesAsync(version, rules, ActionDelete, id, null, existing, Describe(existing), actorUserId, actorUserCode, ct);
    }

    /// <inheritdoc />
    public async Task<OperationResourceEligibilityDto> GetEligibilityAsync(string operationCode, int materialId, int? toMaterialId, CancellationToken ct = default)
    {
        var fromIds = await _masterDataLookup.GetEligibleResourceIdsAsync(operationCode, materialId, ct);

        IReadOnlyList<int> ids = fromIds;
        if (toMaterialId is > 0)
        {
            var toIds = await _masterDataLookup.GetEligibleResourceIdsAsync(operationCode, toMaterialId.Value, ct);
            ids = fromIds.Intersect(toIds).ToList();
        }

        var resourceInfos = await _masterDataLookup.GetResourceInfosAsync(ids, ct);
        var resourceById = resourceInfos.ToDictionary(r => r.Id);

        var departmentIds = resourceInfos.Select(r => r.ProductionDepartmentId).Distinct().ToList();
        var departmentInfos = await _masterDataLookup.GetDepartmentInfosAsync(departmentIds, ct);
        var departmentById = departmentInfos.ToDictionary(d => d.Id);

        var resources = new List<OperationResourceInfoDto>(ids.Count);
        foreach (var id in ids)
        {
            if (!resourceById.TryGetValue(id, out var resource))
            {
                continue;
            }

            DepartmentLookupInfo? department = null;
            if (resource.ProductionDepartmentId > 0 && departmentById.TryGetValue(resource.ProductionDepartmentId, out var dept))
            {
                department = dept;
            }

            resources.Add(new OperationResourceInfoDto
            {
                ResourceCode = resource.ResourceCode,
                ResourceName = resource.ResourceName,
                DepartmentCode = department?.DeptCode,
                StageCode = department?.StageCode,
            });
        }

        return new OperationResourceEligibilityDto
        {
            OperationCode = operationCode,
            ResourceCodes = resources.Select(r => r.ResourceCode).ToList(),
            Resources = resources,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SetupRuleSetVersionDto>> ListRuleSetVersionsAsync(long? ruleSetId, string? status, CancellationToken ct = default)
    {
        var versions = await _masterDataLookup.GetAllRuleSetVersionsAsync(ct);

        IEnumerable<RuleSetVersion> filtered = versions;
        if (ruleSetId is > 0)
        {
            filtered = filtered.Where(v => v.RuleSetId == ruleSetId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim();
            filtered = filtered.Where(v => string.Equals(v.Status, normalized, StringComparison.OrdinalIgnoreCase));
        }

        return filtered
            .OrderBy(v => v.VersionCode)
            .Select(v => new SetupRuleSetVersionDto
            {
                Id = v.Id,
                RuleSetId = v.RuleSetId,
                VersionCode = v.VersionCode,
                Status = v.Status,
                EffectiveFrom = v.EffectiveFrom,
                EffectiveTo = v.EffectiveTo,
                PublishedAt = v.PublishedAt,
                PublishedBy = v.PublishedBy,
                CreatedAt = v.CreatedAt,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SetupDiffDto> GetDiffAsync(long versionId, long otherVersionId, CancellationToken ct = default)
    {
        if (versionId == otherVersionId)
        {
            throw new SetupRuleDataRedLineException("对比的两个版本不能相同（id == otherVersionId）。");
        }

        var sourceVersion = await GetVersionOrThrowAsync(versionId, ct);
        var targetVersion = await GetVersionOrThrowAsync(otherVersionId, ct);

        var sourceRules = SetupTransitionRuleProjector.ExtractRules(sourceVersion.ContentSnapshotJson).Where(r => r.IsActive).ToList();
        var targetRules = SetupTransitionRuleProjector.ExtractRules(targetVersion.ContentSnapshotJson).Where(r => r.IsActive).ToList();

        // 冻结校验器保证同版本内有效规则唯一键无重复（EXACT 七元组 / DEFAULT 五元组各自判重），ToDictionary 安全。
        var sourceByKey = sourceRules.ToDictionary(BuildKey);
        var targetByKey = targetRules.ToDictionary(BuildKey);

        var codes = new MasterDataCodeCache(_masterDataLookup, ct);

        var added = new List<SetupRuleChangeDto>();
        var modified = new List<SetupRuleChangeDto>();
        var removed = new List<SetupRuleChangeDto>();

        foreach (var (key, target) in targetByKey)
        {
            if (!sourceByKey.TryGetValue(key, out var source))
            {
                added.Add(await BuildChangeAsync(target, codes));
            }
            else if (source.SetupMinutes != target.SetupMinutes)
            {
                modified.Add(new SetupRuleChangeDto
                {
                    Id = target.Id,
                    RuleType = target.RuleType,
                    OperationCode = target.OperationCode,
                    DepartmentCode = await codes.DepartmentCodeAsync(target.ProductionDepartmentId),
                    StageCode = target.StageCode,
                    ResourceCode = await codes.ResourceCodeAsync(target.ResourceId),
                    FromMaterialCode = target.FromMaterialId.HasValue ? await codes.MaterialCodeAsync(target.FromMaterialId.Value) : null,
                    ToMaterialCode = target.ToMaterialId.HasValue ? await codes.MaterialCodeAsync(target.ToMaterialId.Value) : null,
                    SetupMinutes = target.SetupMinutes,
                    Before = await ProjectAsync(source, sourceVersion.Status, codes),
                    After = await ProjectAsync(target, targetVersion.Status, codes),
                });
            }
        }

        foreach (var (key, source) in sourceByKey)
        {
            if (!targetByKey.ContainsKey(key))
            {
                removed.Add(await BuildChangeAsync(source, codes));
            }
        }

        return new SetupDiffDto
        {
            RuleSetVersionId = versionId,
            OtherVersionId = otherVersionId,
            AddedCount = added.Count,
            ModifiedCount = modified.Count,
            RemovedCount = removed.Count,
            PublishStatus = targetVersion.Status,
            ComparedAt = DateTime.Now,
            SetupRuleChanges = new SetupRuleChangesDto
            {
                Added = added,
                Modified = modified,
                Removed = removed,
            },
        };
    }

    /// <inheritdoc />
    public async Task PublishAsync(long versionId, string? changeReason, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        // 404 前置：版本不存在直接拒（发布语义上属资源缺失）
        var version = await GetVersionOrThrowAsync(versionId, ct);

        // 发布前 Setup 冲突全量预校验（§8.3；补齐旧发布路径 GovernanceController rule-set/version/{versionId}/publish 不校验 Setup 冲突的旁路缺口）
        var rules = SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson);
        var activeRules = rules.Where(r => r.IsActive).ToList();
        var result = _conflictValidator.Validate(activeRules);
        if (!result.IsValid)
        {
            throw new SetupRuleDataRedLineException($"发布前 Setup 换型规则冲突校验未通过：{result.GetErrorMessage()}");
        }

        await _governanceVersionService.PublishRuleSetVersionAsync(versionId, actorUserCode, actorUserId, ct, changeReason);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DepartmentLookupItem>> LookupDepartmentsAsync(string? search, CancellationToken ct = default)
        => _masterDataLookup.LookupDepartmentsAsync(search, cancellationToken: ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<ResourceLookupItem>> LookupResourcesAsync(string? search, CancellationToken ct = default)
        => _masterDataLookup.LookupResourcesAsync(search, cancellationToken: ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<MaterialLookupItem>> LookupMaterialsAsync(string? search, bool activeOnly = false, CancellationToken ct = default)
        => _masterDataLookup.LookupMaterialsAsync(search, activeOnly, cancellationToken: ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<SetupUncoveredStatDto>> GetUncoveredStatsAsync(
        int? runId, int? departmentId, string? operationCode, int? resourceId, int? materialId, CancellationToken ct = default)
        => _setupUncoveredStatRepository.GetUncoveredAsync(runId, departmentId, operationCode, resourceId, materialId, ct);

    /// <summary>目标规则须与端点类型一致且不可变更所属版本（否则 422）</summary>
    private static void EnsureSameTypeAndVersion(SetupTransitionRule existing, string expectedType, long inputRuleSetVersionId)
    {
        if (existing.RuleType != expectedType)
        {
            throw new SetupRuleDataRedLineException($"规则 Id={existing.Id} 为 {existing.RuleType} 类型，请使用对应的更新端点。");
        }

        if (existing.RuleSetVersionId != inputRuleSetVersionId)
        {
            throw new SetupRuleDataRedLineException("不允许变更规则所属规则集版本（随版本冻结）。");
        }
    }

    /// <summary>构建更新实体（保留主键/版本/审计时间戳，刷新业务字段）</summary>
    private static SetupTransitionRule BuildUpdated(
        SetupTransitionRule existing,
        int departmentId, string stageCode, string operationCode, int resourceId,
        int? fromMaterialId, int? toMaterialId, decimal setupMinutes, string actorUserCode)
    {
        return new SetupTransitionRule
        {
            Id = existing.Id,
            RuleSetVersionId = existing.RuleSetVersionId,
            ProductionDepartmentId = departmentId,
            StageCode = stageCode.Trim(),
            OperationCode = operationCode.Trim(),
            ResourceId = resourceId,
            FromMaterialId = fromMaterialId,
            ToMaterialId = toMaterialId,
            RuleType = existing.RuleType,
            SetupMinutes = setupMinutes,
            IsActive = existing.IsActive,
            CreatedAt = existing.CreatedAt,
            CreatedBy = existing.CreatedBy,
            UpdatedAt = existing.UpdatedAt,
            UpdatedBy = actorUserCode,
        };
    }

    /// <summary>EXACT 入参数据红线校验（422）</summary>
    private static void ValidateExactInput(SetupRuleExactInput input)
    {
        if (input.FromMaterialId <= MissingMaterialId || input.ToMaterialId <= MissingMaterialId)
        {
            throw new SetupRuleDataRedLineException("明确换型规则（EXACT）必须携带前产品与后产品（fromMaterialId/toMaterialId）。");
        }

        if (input.FromMaterialId == input.ToMaterialId)
        {
            throw new SetupRuleDataRedLineException("明确换型规则（EXACT）前产品与后产品不能相同（fromMaterialId == toMaterialId）。");
        }

        ValidateCommon(input.ProductionDepartmentId, input.StageCode, input.OperationCode, input.ResourceId, input.SetupMinutes);
    }

    /// <summary>DEFAULT 入参数据红线校验（422）</summary>
    private static void ValidateDefaultInput(SetupRuleDefaultInput input)
    {
        if (input.ExtensionData?.Any(k => k.Key.Contains("material", StringComparison.OrdinalIgnoreCase)) == true)
        {
            throw new SetupRuleDataRedLineException("默认换型规则（DEFAULT）不允许携带产品字段（fromMaterialId/toMaterialId 等）。");
        }

        ValidateCommon(input.ProductionDepartmentId, input.StageCode, input.OperationCode, input.ResourceId, input.SetupMinutes);
    }

    /// <summary>公共维度数据红线校验（422）</summary>
    private static void ValidateCommon(int departmentId, string stageCode, string operationCode, int resourceId, decimal setupMinutes)
    {
        if (departmentId <= 0)
        {
            throw new SetupRuleDataRedLineException("生产部门（productionDepartmentId）不能为空。");
        }

        if (string.IsNullOrWhiteSpace(stageCode))
        {
            throw new SetupRuleDataRedLineException("大工艺阶段（stageCode）不能为空。");
        }

        if (string.IsNullOrWhiteSpace(operationCode))
        {
            throw new SetupRuleDataRedLineException("工序（operationCode）不能为空。");
        }

        if (resourceId <= 0)
        {
            throw new SetupRuleDataRedLineException("设备（resourceId）不能为空。");
        }

        if (setupMinutes <= 0)
        {
            throw new SetupRuleDataRedLineException("SetupMinutes 必须大于 0（契约 &gt; 0，严于 DDL CK ≥ 0）。");
        }
    }

    /// <summary>版本存在性 + DRAFT 状态检查（非 DRAFT → 400 状态红线）</summary>
    private async Task<RuleSetVersion> GetVersionForWriteAsync(long ruleSetVersionId, CancellationToken ct)
    {
        var version = await GetVersionOrThrowAsync(ruleSetVersionId, ct);
        if (version.Status != GovernanceVersionStatus.Draft)
        {
            throw new InvalidOperationException($"规则集版本状态为 {version.Status}，仅 DRAFT 状态允许维护 Setup 换型规则。");
        }

        return version;
    }

    /// <summary>版本存在性检查（不存在 → 404）</summary>
    private async Task<RuleSetVersion> GetVersionOrThrowAsync(long ruleSetVersionId, CancellationToken ct)
    {
        var version = await _versionRepository.GetByIdAsync(ruleSetVersionId, ct);
        if (version is null)
        {
            throw new ResourceNotFoundException($"规则集版本不存在（Id={ruleSetVersionId}）。");
        }

        return version;
    }

    /// <summary>
    /// 发布前唯一键冲突预校验（§十）：候选并入当前版本「有效规则」后整体校验，冲突 → 422。
    /// 输入 <paramref name="all"/> 为当前版本子块全量规则（含候选外全部），<paramref name="candidate"/> 为候选；
    /// 复用 <see cref="SetupTransitionRuleConflictValidator"/>（纯计算）。映射 422 而非 400。
    /// </summary>
    private void EnsureNoConflict(IReadOnlyList<SetupTransitionRule> all, SetupTransitionRule candidate, long excludingId)
    {
        var combined = all
            .Where(r => r.IsActive && r.Id != excludingId)
            .Append(candidate)
            .ToList();

        var result = _conflictValidator.Validate(combined);
        if (!result.IsValid)
        {
            throw new SetupRuleDataRedLineException(result.GetErrorMessage());
        }
    }

    /// <summary>按 Id 查单条规则（子块承载；Id 含版本高位，ParseRuleId 定位版本）</summary>
    private async Task<SetupTransitionRule?> GetRuleByIdAsync(long id, CancellationToken ct)
    {
        var (ruleSetVersionId, _) = SetupTransitionRuleProjector.ParseRuleId(id);
        var version = await GetVersionOrThrowAsync(ruleSetVersionId, ct);
        return SetupTransitionRuleProjector.ExtractRules(version.ContentSnapshotJson).FirstOrDefault(r => r.Id == id);
    }

    /// <summary>
    /// A2/P1-04 Department Scope：写路径经部门码（IMasterDataLookupRepository 回带，无直连 DB）调
    /// EnsureInScopeAsync(Department)。部门不存在 → 404（ResourceNotFoundException）；越界 → ScopeViolationException（Controller 映射 403）。
    /// 依据：SetupTransitionRule.ProductionDepartmentId 即业务范围维度（实体注释），冻结 Auth §9.4「目标对象须在业务 Scope 内」。
    /// </summary>
    private async Task EnsureDepartmentScopeAsync(int departmentId, int actorUserId, CancellationToken ct)
    {
        var deptCode = await _masterDataLookup.GetDepartmentCodeAsync(departmentId, ct);
        if (string.IsNullOrWhiteSpace(deptCode))
        {
            throw new ResourceNotFoundException($"生产部门（ProductionDepartment.Id={departmentId}）不存在。");
        }

        await _dataScopeService.EnsureInScopeAsync(actorUserId, DataScopeTypes.Department, deptCode, ct);
    }

    /// <summary>
    /// 将规则列表写回版本子块并落库 + 统一审计（fail-closed：写库或审计异常即抛，可追溯性不降级）。
    /// <paramref name="newRule"/> 为写入后的规则（Create/Update 用，Delete 为 null）；<paramref name="oldRule"/> 为写入前规则（Create 为 null）。
    /// </summary>
    private async Task PersistRulesAsync(
        RuleSetVersion version,
        IReadOnlyList<SetupTransitionRule> rules,
        string actionCode,
        long entityId,
        SetupTransitionRule? newRule,
        SetupTransitionRule? oldRule,
        string remark,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct)
    {
        version.ContentSnapshotJson = SetupTransitionRuleProjector.SetRules(version.ContentSnapshotJson, rules);
        await _versionRepository.UpdateAsync(version, ct);

        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = actionCode,
            EntityType = EntityTypeSetupTransitionRule,
            EntityId = entityId.ToString(),
            VersionCode = version.Id.ToString(),
            OldValue = oldRule is null ? null : Serialize(oldRule),
            NewValue = newRule is null ? null : Serialize(newRule),
            UserId = actorUserId,
            UserCode = actorUserCode,
            OccurredAt = DateTime.Now,
            Remark = remark,
        }, ct);
    }

    /// <summary>业务字段快照（不含主键/审计时间戳，EntityId 由审计列承载）</summary>
    private static string Serialize(SetupTransitionRule rule) => System.Text.Json.JsonSerializer.Serialize(new
    {
        rule.RuleSetVersionId,
        rule.ProductionDepartmentId,
        rule.StageCode,
        rule.OperationCode,
        rule.ResourceId,
        rule.FromMaterialId,
        rule.ToMaterialId,
        rule.RuleType,
        rule.SetupMinutes,
        rule.IsActive,
    }, AuditJsonOptions);

    /// <summary>规则业务键描述（审计 Remark 用）</summary>
    private static string Describe(SetupTransitionRule rule) =>
        rule.RuleType == SetupTransitionRuleType.Exact
            ? $"换型规则 工序[{rule.OperationCode}] 设备[{rule.ResourceId}] {rule.FromMaterialId}→{rule.ToMaterialId}"
            : $"换型规则 工序[{rule.OperationCode}] 设备[{rule.ResourceId}] 默认";

    /// <summary>Setup 换型规则唯一键（EXACT 七元组 / DEFAULT 五元组；前缀区分类型，杜绝跨类误并）</summary>
    private static string BuildKey(SetupTransitionRule rule)
    {
        if (rule.RuleType == SetupTransitionRuleType.Exact)
        {
            return $"E|{rule.ProductionDepartmentId}|{rule.StageCode}|{rule.OperationCode}|{rule.ResourceId}|{rule.FromMaterialId}|{rule.ToMaterialId}";
        }

        return $"D|{rule.ProductionDepartmentId}|{rule.StageCode}|{rule.OperationCode}|{rule.ResourceId}";
    }

    /// <summary>规则 → 变更行（added/removed 维度；Code 回带，不含 id/before/after——契约仅 modified 行携带）</summary>
    private static async Task<SetupRuleChangeDto> BuildChangeAsync(SetupTransitionRule rule, MasterDataCodeCache codes)
    {
        return new SetupRuleChangeDto
        {
            RuleType = rule.RuleType,
            OperationCode = rule.OperationCode,
            DepartmentCode = await codes.DepartmentCodeAsync(rule.ProductionDepartmentId),
            StageCode = rule.StageCode,
            ResourceCode = await codes.ResourceCodeAsync(rule.ResourceId),
            FromMaterialCode = rule.FromMaterialId.HasValue ? await codes.MaterialCodeAsync(rule.FromMaterialId.Value) : null,
            ToMaterialCode = rule.ToMaterialId.HasValue ? await codes.MaterialCodeAsync(rule.ToMaterialId.Value) : null,
            SetupMinutes = rule.SetupMinutes,
        };
    }

    /// <summary>版本六态 → DTO 三态（Status 派生版本态）</summary>
    private static string DeriveStatus(string versionStatus) => versionStatus switch
    {
        GovernanceVersionStatus.Draft or GovernanceVersionStatus.Submitted or GovernanceVersionStatus.Approved => "DRAFT",
        GovernanceVersionStatus.Published => "ACTIVE",
        _ => "DEPRECATED", // DISABLED / ARCHIVED
    };

    /// <summary>实体 + 版本态 + Code 回带 → 读模型 DTO</summary>
    private static async Task<SetupRuleDto> ProjectAsync(SetupTransitionRule rule, string versionStatus, MasterDataCodeCache codes)
    {
        var departmentCode = await codes.DepartmentCodeAsync(rule.ProductionDepartmentId);
        var resourceCode = await codes.ResourceCodeAsync(rule.ResourceId);
        var fromCode = rule.FromMaterialId.HasValue ? await codes.MaterialCodeAsync(rule.FromMaterialId.Value) : null;
        var toCode = rule.ToMaterialId.HasValue ? await codes.MaterialCodeAsync(rule.ToMaterialId.Value) : null;

        return new SetupRuleDto
        {
            Id = rule.Id,
            RuleSetVersionId = rule.RuleSetVersionId,
            RuleType = rule.RuleType,
            ProductionDepartmentId = rule.ProductionDepartmentId,
            DepartmentCode = departmentCode,
            StageCode = rule.StageCode,
            OperationCode = rule.OperationCode,
            ResourceId = rule.ResourceId,
            ResourceCode = resourceCode,
            FromMaterialId = rule.FromMaterialId,
            FromMaterialCode = fromCode,
            ToMaterialId = rule.ToMaterialId,
            ToMaterialCode = toCode,
            SetupMinutes = rule.SetupMinutes,
            Status = DeriveStatus(versionStatus),
            CreatedBy = rule.CreatedBy,
            CreatedAt = rule.CreatedAt,
            UpdatedBy = rule.UpdatedBy,
            UpdatedAt = rule.UpdatedAt,
        };
    }

    /// <summary>主数据 Code 回带缓存（列表场景避免 N+1：同一 Id 仅查一次）</summary>
    private sealed class MasterDataCodeCache
    {
        private readonly IMasterDataLookupRepository _lookup;
        private readonly CancellationToken _ct;
        private readonly Dictionary<int, string?> _department = new();
        private readonly Dictionary<int, string?> _resource = new();
        private readonly Dictionary<int, string?> _material = new();

        public MasterDataCodeCache(IMasterDataLookupRepository lookup, CancellationToken ct)
        {
            _lookup = lookup;
            _ct = ct;
        }

        public Task<string?> DepartmentCodeAsync(int id) => GetOrLoadAsync(_department, id, _lookup.GetDepartmentCodeAsync);

        public Task<string?> ResourceCodeAsync(int id) => GetOrLoadAsync(_resource, id, _lookup.GetResourceCodeAsync);

        public Task<string?> MaterialCodeAsync(int id) => GetOrLoadAsync(_material, id, _lookup.GetMaterialCodeAsync);

        private async Task<string?> GetOrLoadAsync(Dictionary<int, string?> cache, int id, Func<int, CancellationToken, Task<string?>> loader)
        {
            if (cache.TryGetValue(id, out var code))
            {
                return code;
            }

            var value = await loader(id, _ct);
            cache[id] = value;
            return value;
        }
    }
}
