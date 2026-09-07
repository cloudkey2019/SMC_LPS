using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using GovernanceAuditLog = LPS.APS.Core.Entities.Auth.GovernanceAuditLog;
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
/// 每次 Create / Update / Enable / Disable 落一条 GovernanceAuditLog（APS_Auth）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class DomainDefinitionGovernanceService : IDomainDefinitionGovernanceService
{
    private const string ScopeTypeFamily = "FAMILY";
    private const string ScopeTypeFactoryFamily = "FACTORY_FAMILY";
    private const string EntityTypeDomainDefinition = "DomainDefinition";
    private const int DefaultSortOrder = 100;
    private const int MaxDomainKeyLength = 50;
    private const int MaxDomainNameLength = 200;

    private readonly IDomainDefinitionRepository _repository;
    private readonly IGovernanceAuditLogRepository _auditLogRepository;
    private readonly ILogger<DomainDefinitionGovernanceService> _logger;

    public DomainDefinitionGovernanceService(
        IDomainDefinitionRepository repository,
        IGovernanceAuditLogRepository auditLogRepository,
        ILogger<DomainDefinitionGovernanceService> logger)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _logger = logger;
    }

    public Task<DomainDefinition?> GetByIdAsync(int id, CancellationToken ct = default)
        => _repository.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<DomainDefinition>> GetAllAsync(CancellationToken ct = default)
        => _repository.GetAllAsync(null, ct);

    public Task<IReadOnlyList<DomainDefinition>> GetActiveAsync(CancellationToken ct = default)
        => _repository.GetActiveAsync(ct);

    public async Task<DomainDefinition> CreateAsync(DomainDefinition input, string? operatedBy, CancellationToken ct = default)
    {
        await ValidateCoreAsync(input, ct);
        await EnsureKeyUniqueAsync(input.DomainKey, excludeId: null, ct);

        var now = DateTime.UtcNow;
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

        var created = await _repository.CreateAsync(entity, ct);
        await AuditAsync("Create", created, beforeStatus: null, afterStatus: StatusOf(created.IsActive), operatedBy, ct);
        return created;
    }

    public async Task<DomainDefinition> UpdateAsync(int id, DomainDefinition input, string? operatedBy, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"域定义不存在：{id}");

        if (!string.Equals(existing.DomainKey, input.DomainKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"DomainKey 一经创建不可变更：{existing.DomainKey} → {input.DomainKey}");
        }

        await ValidateCoreAsync(input, ct);
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
            UpdatedAt = DateTime.UtcNow
        };

        await _repository.UpdateAsync(entity, ct);
        await AuditAsync("Update", entity, beforeStatus: StatusOf(existing.IsActive), afterStatus: StatusOf(entity.IsActive), operatedBy, ct);
        return entity;
    }

    public async Task<DomainDefinition> SetActiveAsync(int id, bool isActive, string? operatedBy, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException($"域定义不存在：{id}");

        if (existing.IsActive == isActive)
        {
            return existing; // 幂等：状态未变不重复审计
        }

        await _repository.SetActiveAsync(id, isActive, operatedBy, DateTime.UtcNow, ct);

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
            UpdatedAt = DateTime.UtcNow
        };

        await AuditAsync(isActive ? "Enable" : "Disable", updated, beforeStatus: StatusOf(existing.IsActive), afterStatus: StatusOf(isActive), operatedBy, ct);
        return updated;
    }

    private async Task ValidateCoreAsync(DomainDefinition input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.DomainKey))
        {
            throw new InvalidOperationException("DomainKey 不能为空");
        }
        if (input.DomainKey.Trim().Length > MaxDomainKeyLength)
        {
            throw new InvalidOperationException($"DomainKey 长度不能超过 {MaxDomainKeyLength} 字符");
        }
        if (string.IsNullOrWhiteSpace(input.DomainName))
        {
            throw new InvalidOperationException("DomainName 不能为空");
        }
        if (input.DomainName.Trim().Length > MaxDomainNameLength)
        {
            throw new InvalidOperationException($"DomainName 长度不能超过 {MaxDomainNameLength} 字符");
        }

        var scopeType = (input.ScopeType ?? string.Empty).Trim().ToUpperInvariant();
        if (scopeType != ScopeTypeFamily && scopeType != ScopeTypeFactoryFamily)
        {
            throw new InvalidOperationException($"ScopeType 仅支持 {ScopeTypeFamily} / {ScopeTypeFactoryFamily}，当前：{input.ScopeType}");
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

    private static string StatusOf(bool isActive) => isActive ? "Active" : "Inactive";

    private async Task AuditAsync(
        string operationType,
        DomainDefinition entity,
        string? beforeStatus,
        string? afterStatus,
        string? operatedBy,
        CancellationToken ct)
    {
        try
        {
            await _auditLogRepository.AddAsync(new GovernanceAuditLog
            {
                OperationType = operationType,
                EntityType = EntityTypeDomainDefinition,
                EntityId = entity.Id,
                VersionCode = null,
                BeforeStatus = beforeStatus,
                AfterStatus = afterStatus,
                OperatedBy = operatedBy,
                OperatedAt = DateTime.UtcNow,
                Remarks = $"域定义 {entity.DomainKey}（{entity.ScopeType}）"
            }, ct);
        }
        catch (Exception ex)
        {
            // 审计失败不阻断主流程，但必须记录（治理可追溯性降级）
            _logger.LogError(ex, "域定义审计写入失败：{OperationType} {DomainKey}", operationType, entity.DomainKey);
        }
    }
}
