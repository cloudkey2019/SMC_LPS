using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using ProductFamily = LPS.APS.Core.Entities.APS.ProductFamily;
using Factory = LPS.APS.Core.Entities.APS.Factory;
using AuditLog = LPS.APS.Core.Entities.Auth.AuditLog;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 域定义治理服务（E-1 DomainDefinition 治理，3号位应用编排）
/// 校验规则（冻结 DDL v5.1.4 §2.4aa + Domain 专项 §3/§4）：
///   - DomainKey 必填 / 唯一 / 一经创建不可变更；
///   - DomainName 必填；
///   - ScopeType 仅 FAMILY / FACTORY_FAMILY；
///   - ProductFamilyId 必须引用存在的 ProductFamily；
///   - FACTORY_FAMILY 必须指定存在的 Factory；FAMILY 不得指定 Factory。
/// 每次 Create / Update / Enable / Disable 落一条 AuditLog（APS_Auth）。
/// 写入前执行业务范围校验（F-G4）：操作用户需在 Domain 维度授权该 DomainKey，fail-closed。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class DomainDefinitionGovernanceService : IDomainDefinitionGovernanceService
{
    private const string ScopeTypeFamily = "FAMILY";
    private const string ScopeTypeFactoryFamily = "FACTORY_FAMILY";
    private const string EntityTypeDomainDefinition = "DomainDefinition";
    private const int DefaultSortOrder = 100;
    private const int MinSortOrder = 0;
    private const int MaxSortOrder = 9999;
    private const int MinDomainNameLength = 2;
    private const int MaxDomainNameLength = 64;

    /// <summary>DomainKey 契约（4号位 字段校验缺口 F2）：首字符大写字母，4-50 字符，仅含 [A-Z0-9_-]。</summary>
    private static readonly System.Text.RegularExpressions.Regex DomainKeyPattern =
        new(@"^[A-Z][A-Z0-9_-]{3,49}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly IDomainDefinitionRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<DomainDefinitionGovernanceService> _logger;

    public DomainDefinitionGovernanceService(
        IDomainDefinitionRepository repository,
        IAuditLogRepository auditLogRepository,
        IDataScopeService dataScopeService,
        ILogger<DomainDefinitionGovernanceService> logger)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _dataScopeService = dataScopeService;
        _logger = logger;
    }

    public Task<DomainDefinition?> GetByIdAsync(int id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<DomainDefinition>> GetAllAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(null, ct);

    public Task<IReadOnlyList<DomainDefinition>> GetActiveAsync(CancellationToken ct = default)
        => _repository.GetActiveAsync(ct);

    public Task<IReadOnlyList<ProductFamily>> GetProductFamiliesAsync(CancellationToken ct = default)
        => _repository.GetProductFamiliesAsync(ct);

    public Task<IReadOnlyList<Factory>> GetFactoriesAsync(CancellationToken ct = default)
        => _repository.GetFactoriesAsync(ct);

    public async Task<DomainDefinition> CreateAsync(DomainDefinition input, int actorUserId, string? operatedBy, CancellationToken ct = default)
    {
        await ValidateCoreAsync(input, ct);
        await EnsureDomainAllowsAsync(actorUserId, input.DomainKey.Trim(), ct);
        await EnsureKeyUniqueAsync(input.DomainKey, excludeId: null, ct);

        var now = DateTime.Now;
        var entity = new DomainDefinition
        {
            DomainKey = input.DomainKey.Trim(),
            DomainName = input.DomainName.Trim(),
            ScopeType = input.ScopeType.Trim().ToUpperInvariant(),
            ProductFamilyId = input.ProductFamilyId,
            FactoryId = input.FactoryId,
            IsActive = true, // 新建默认启用；停用走显式停用接口
            SortOrder = input.SortOrder > 0 ? input.SortOrder : DefaultSortOrder,
            CreatedBy = operatedBy,
            CreatedAt = now,
            UpdatedBy = operatedBy,
            UpdatedAt = now
        };

        await _auditLogRepository.EnsureWritableAsync(ct);

        var created = await _repository.CreateAsync(entity, ct);
        await AuditAsync("Create", created, beforeStatus: null, afterStatus: StatusOf(created.IsActive), actorUserId, operatedBy, ct);
        return created;
    }

    public async Task<DomainDefinition> UpdateAsync(int id, DomainDefinition input, int actorUserId, string? operatedBy, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"域定义不存在：{id}");

        if (!string.Equals(existing.DomainKey, input.DomainKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"DomainKey 一经创建不可变更：{existing.DomainKey} → {input.DomainKey}");
        }

        await ValidateCoreAsync(input, ct);
        await EnsureDomainAllowsAsync(actorUserId, existing.DomainKey, ct);
        await EnsureKeyUniqueAsync(input.DomainKey, excludeId: id, ct);

        var entity = new DomainDefinition
        {
            Id = id,
            DomainKey = existing.DomainKey,
            DomainName = input.DomainName.Trim(),
            ScopeType = input.ScopeType.Trim().ToUpperInvariant(),
            ProductFamilyId = input.ProductFamilyId,
            FactoryId = input.FactoryId,
            IsActive = existing.IsActive,
            SortOrder = input.SortOrder > 0 ? input.SortOrder : DefaultSortOrder,
            CreatedBy = existing.CreatedBy,
            CreatedAt = existing.CreatedAt,
            UpdatedBy = operatedBy,
            UpdatedAt = DateTime.Now
        };

        await _auditLogRepository.EnsureWritableAsync(ct);

        await _repository.UpdateAsync(entity, ct);
        await AuditAsync("Update", entity, beforeStatus: StatusOf(existing.IsActive), afterStatus: StatusOf(entity.IsActive), actorUserId, operatedBy, ct);
        return entity;
    }

    public async Task<DomainDefinition> SetActiveAsync(int id, bool isActive, int actorUserId, string? operatedBy, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"域定义不存在：{id}");

        if (existing.IsActive == isActive)
        {
            return existing; // 幂等：状态未变不重复审计
        }

        await EnsureDomainAllowsAsync(actorUserId, existing.DomainKey, ct);

        await _auditLogRepository.EnsureWritableAsync(ct);

        await _repository.SetActiveAsync(id, isActive, operatedBy, DateTime.Now, ct);

        var updated = new DomainDefinition
        {
            Id = existing.Id,
            DomainKey = existing.DomainKey,
            DomainName = existing.DomainName,
            ScopeType = existing.ScopeType,
            ProductFamilyId = existing.ProductFamilyId,
            FactoryId = existing.FactoryId,
            IsActive = isActive,
            SortOrder = existing.SortOrder,
            CreatedBy = existing.CreatedBy,
            CreatedAt = existing.CreatedAt,
            UpdatedBy = operatedBy,
            UpdatedAt = DateTime.Now
        };

        await AuditAsync(isActive ? "Enable" : "Disable", updated, beforeStatus: StatusOf(existing.IsActive), afterStatus: StatusOf(isActive), actorUserId, operatedBy, ct);
        return updated;
    }

    private async Task ValidateCoreAsync(DomainDefinition input, CancellationToken ct)
    {
        // F1：sortOrder 边界 0-9999（越界显式 400，不回落到默认值）
        if (input.SortOrder < MinSortOrder || input.SortOrder > MaxSortOrder)
        {
            throw new InvalidOperationException($"SortOrder 须在 {MinSortOrder}-{MaxSortOrder} 之间");
        }

        if (string.IsNullOrWhiteSpace(input.DomainKey))
        {
            throw new InvalidOperationException("DomainKey 不能为空");
        }
        // F2：DomainKey 字符集 + 长度（首字符大写字母，4-50 字符，仅 [A-Z0-9_-]；作为 DomainKeyPath 一部分，禁止中文/空格/SQL 注入字符）
        if (!DomainKeyPattern.IsMatch(input.DomainKey.Trim()))
        {
            throw new InvalidOperationException("DomainKey 须以大写字母开头，4-50 字符，仅含 [A-Z0-9_-]");
        }

        if (string.IsNullOrWhiteSpace(input.DomainName))
        {
            throw new InvalidOperationException("DomainName 不能为空");
        }
        // F4：DomainName 长度 2-64
        if (input.DomainName.Trim().Length < MinDomainNameLength)
        {
            throw new InvalidOperationException($"DomainName 至少 {MinDomainNameLength} 个字符");
        }
        if (input.DomainName.Trim().Length > MaxDomainNameLength)
        {
            throw new InvalidOperationException($"DomainName 长度不能超过 {MaxDomainNameLength} 字符");
        }

        // F3：ScopeType 严格大小写（仅 FAMILY / FACTORY_FAMILY，移除 ToUpper 宽容）
        var scopeType = (input.ScopeType ?? string.Empty).Trim();
        if (scopeType != ScopeTypeFamily && scopeType != ScopeTypeFactoryFamily)
        {
            throw new InvalidOperationException($"ScopeType 仅支持 {ScopeTypeFamily} / {ScopeTypeFactoryFamily}（严格大小写），当前：{input.ScopeType}");
        }

        if (input.ProductFamilyId <= 0)
        {
            throw new InvalidOperationException("ProductFamilyId 必须指定");
        }
        if (!await _repository.ProductFamilyExistsAsync(input.ProductFamilyId, ct))
        {
            throw new InvalidOperationException($"产品族不存在：{input.ProductFamilyId}");
        }

        if (scopeType == ScopeTypeFactoryFamily)
        {
            if (input.FactoryId is null)
            {
                throw new InvalidOperationException($"{ScopeTypeFactoryFamily} 必须指定 FactoryId");
            }
            if (!await _repository.FactoryExistsAsync(input.FactoryId.Value, ct))
            {
                throw new InvalidOperationException($"工厂不存在：{input.FactoryId}");
            }
        }
        else
        {
            if (input.FactoryId is not null)
            {
                throw new InvalidOperationException($"{ScopeTypeFamily} 不得指定 FactoryId");
            }
        }
    }

    private async Task EnsureKeyUniqueAsync(string domainKey, int? excludeId, CancellationToken ct)
    {
        if (await _repository.ExistsByKeyAsync(domainKey, excludeId, ct))
        {
            throw new InvalidOperationException($"DomainKey 已存在：{domainKey}");
        }
    }

    /// <summary>
    /// 业务范围校验（F-G4）：仅 Domain 维度可门禁域治理，fail-closed。
    /// DomainKey 的唯一权威映射源是 DomainDefinition；Factory / ProductFamily 维度不得作为 Domain 别名放行。
    /// 未授权该 DomainKey（或仅有 Factory/ProductFamily 等非 Domain 授权）时抛异常拒绝。
    /// </summary>
    private async Task EnsureDomainAllowsAsync(int actorUserId, string domainKey, CancellationToken ct)
    {
        var scope = await _dataScopeService.ResolveScopeAsync(actorUserId, ct);
        if (!scope.Allows(DataScopeTypes.Domain, domainKey))
        {
            throw new InvalidOperationException($"当前用户（UserId={actorUserId}）无权操作 Domain={domainKey}（业务范围未授权）");
        }
    }

    private static string StatusOf(bool isActive) => isActive ? "Active" : "Inactive";

    private async Task AuditAsync(
        string operationType,
        DomainDefinition entity,
        string? beforeStatus,
        string? afterStatus,
        int actorUserId,
        string? operatedBy,
        CancellationToken ct)
    {
        try
        {
            await _auditLogRepository.AddAsync(new AuditLog
            {
                ActionCode = operationType,
                EntityType = EntityTypeDomainDefinition,
                EntityId = entity.Id.ToString(),
                VersionCode = null,
                OldValue = beforeStatus,
                NewValue = afterStatus,
                UserId = actorUserId,
                UserCode = operatedBy,
                OccurredAt = DateTime.Now,
                Remark = $"域定义 {entity.DomainKey}（{entity.ScopeType}）"
            }, ct);
        }
        catch (Exception ex)
        {
            // P1-05：审计事实与状态变更同属关键治理写，审计失败即抛（fail-closed），绝不静默吞掉审计（可追溯性不降级）。
            _logger.LogError(ex,
                "域定义审计写入失败（状态已落库、审计缺、需对账）：OperationType={OperationType} EntityType={EntityType} EntityId={EntityId} Before={BeforeStatus} After={AfterStatus} OperatedBy={OperatedBy} DomainKey={DomainKey}",
                operationType, EntityTypeDomainDefinition, entity.Id, beforeStatus, afterStatus, operatedBy, entity.DomainKey);
            throw;
        }
    }
}
