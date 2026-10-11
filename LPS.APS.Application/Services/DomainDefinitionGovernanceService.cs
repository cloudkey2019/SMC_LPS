using DomainDefinition = LPS.APS.Core.Entities.APS.DomainDefinition;
using ProductFamily = LPS.APS.Core.Entities.APS.ProductFamily;
using Factory = LPS.APS.Core.Entities.APS.Factory;
using AuditLog = LPS.APS.Core.Entities.Auth.AuditLog;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;

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

    public DomainDefinitionGovernanceService(
        IDomainDefinitionRepository repository,
        IAuditLogRepository auditLogRepository,
        IDataScopeService dataScopeService)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _dataScopeService = dataScopeService;
    }

    public async Task<DomainDefinition?> GetByIdAsync(int id, int actorUserId, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
        {
            return null;
        }

        // N-03（0号位 复审 v1.1）：非 Global 用户目标域不在授权 Domain 范围 → 403（防通过 Id 窥探他域定义）。
        var allowedDomainKeys = await ResolveAllowedDomainKeysAsync(actorUserId, ct);
        if (allowedDomainKeys is not null
            && (allowedDomainKeys.Count == 0 || !allowedDomainKeys.Contains(entity.DomainKey)))
        {
            throw new ScopeViolationException(DataScopeTypes.Domain, entity.DomainKey);
        }

        return entity;
    }

    public async Task<IReadOnlyList<DomainDefinition>> GetAllAsync(int actorUserId, CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(null, ct);

        // N-03（0号位 复审 v1.1）：列表按操作人 Domain Scope 过滤——Global 不过滤；
        // 非 Global 仅返回授权域；无任何 Domain 授权 → fail-closed 空列表（禁止先全量返回再前端隐藏）。
        var allowedDomainKeys = await ResolveAllowedDomainKeysAsync(actorUserId, ct);
        if (allowedDomainKeys is null)
        {
            return all;
        }

        if (allowedDomainKeys.Count == 0)
        {
            return new List<DomainDefinition>();
        }

        return all.Where(d => allowedDomainKeys.Contains(d.DomainKey)).ToList();
    }

    public Task<IReadOnlyList<DomainDefinition>> GetActiveAsync(CancellationToken ct = default)
        => _repository.GetActiveAsync(ct); // N-03：信任调用（2号位 归域执行唯一事实源，G-D09/G-D10），不过滤

    public Task<IReadOnlyList<ProductFamily>> GetProductFamiliesAsync(CancellationToken ct = default)
        => _repository.GetProductFamiliesAsync(ct);

    public Task<IReadOnlyList<Factory>> GetFactoriesAsync(CancellationToken ct = default)
        => _repository.GetFactoriesAsync(ct);

    public async Task<DomainDefinition> CreateAsync(DomainDefinition input, int actorUserId, string? operatedBy, CancellationToken ct = default)
    {
        await ValidateCoreAsync(input, ct);
        await EnsureDomainAllowsAsync(actorUserId, input.DomainKey.Trim(), ct);
        await EnsureKeyUniqueAsync(input.DomainKey, excludeId: null, ct);
        await EnsureScopeComboUniqueAsync(input, excludeId: null, ct);

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
        await EnsureScopeComboUniqueAsync(input, excludeId: id, ct);

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

        // 越权校验先于幂等早返回（SCOPE-04）：防对授权范围外已启用/已停用域做幂等 enable/disable 探到 200+完整对象（Id 窥探）
        await EnsureDomainAllowsAsync(actorUserId, existing.DomainKey, ct);

        if (existing.IsActive == isActive)
        {
            return existing; // 幂等：状态未变不重复审计
        }

        // G2：启用时同样校验 scope 组合唯一性（排除自身），防止重复启用域
        if (isActive)
        {
            await EnsureScopeComboUniqueAsync(existing, excludeId: existing.Id, ct);
        }

        await _auditLogRepository.EnsureWritableAsync(ct);

        await _repository.SetActiveAsync(
            id, isActive, operatedBy, DateTime.Now,
            existing.ScopeType, existing.ProductFamilyId, existing.FactoryId, ct);

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
    /// scope 组合唯一性校验（G2，3号位 合法性校验职责，Domain裁决 §4.3）：
    /// FAMILY 同一 ProductFamilyId 仅允许一个启用域；FACTORY_FAMILY 同一 FactoryId+ProductFamilyId 仅允许一个启用域。
    /// 停用域不占位（仅 IsActive=1 参与判重）；excludeId 用于更新/启用时排除自身。
    /// 注：非冻结 DDL 红线（DDL 仅 DomainKey UNIQUE），属 3号位 治理完备性校验。
    /// </summary>
    private async Task EnsureScopeComboUniqueAsync(DomainDefinition target, int? excludeId, CancellationToken ct)
    {
        var scopeType = target.ScopeType.Trim().ToUpperInvariant();
        var exists = await _repository.ExistsActiveScopeComboAsync(
            scopeType, target.ProductFamilyId, target.FactoryId, excludeId, ct);
        if (exists)
        {
            throw new InvalidOperationException(
                scopeType == ScopeTypeFactoryFamily
                    ? $"已存在启用中的 {ScopeTypeFactoryFamily} 域（FactoryId={target.FactoryId}, ProductFamilyId={target.ProductFamilyId}），同一工厂+产品族组合仅允许一个启用域"
                    : $"已存在启用中的 {ScopeTypeFamily} 域（ProductFamilyId={target.ProductFamilyId}），同一产品族仅允许一个启用域");
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
            // N-04（0号位 复审 v1.1）：业务范围越权统一抛 ScopeViolationException（Web 层映射 403），
            // 区别于参数/状态非法（InvalidOperationException → 400）。
            throw new ScopeViolationException(DataScopeTypes.Domain, domainKey);
        }
    }

    /// <summary>
    /// 解析当前用户在 Domain 维度可查询的域键集合（N-03，查询 Scope）。
    /// Global → null（不过滤，全放行）；非 Global → 授权 DomainKey 集合（无任何 Domain 授权 → 空集合，fail-closed 空列表）。
    /// 与写路径 <see cref="EnsureDomainAllowsAsync"/>（按目标对象单点校验）互补：查询侧按集合过滤。
    /// </summary>
    private async Task<IReadOnlySet<string>?> ResolveAllowedDomainKeysAsync(int actorUserId, CancellationToken ct)
    {
        var ctx = await _dataScopeService.ResolveScopeAsync(actorUserId, ct);
        if (ctx.IsGlobal)
        {
            return null;
        }

        var domainKeys = ctx.GetValues(DataScopeTypes.Domain);
        if (domainKeys is null || domainKeys.Count == 0)
        {
            return new HashSet<string>();
        }

        return domainKeys.ToHashSet();
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
        // P1-05 + R-01（0号位 复审 v1.1，方案B）：审计写入失败由 AuditLogRepository.AddAsync 统一落对账日志
        // （AuditReconcile：完整审计负载 JSON 供运维人工补录）并原样重抛——fail-closed 保持、可追溯性不降级。
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
}
