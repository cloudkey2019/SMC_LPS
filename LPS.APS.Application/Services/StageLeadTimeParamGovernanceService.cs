using System.Data;
using System.Text;
using Dapper;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Exceptions;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// 阶段提前期参数治理服务实现（StageLeadTimeParam，3号位 规则参数体系）。
/// 直接持有 DatabaseConnectionManager 写 APS_Production.StageLeadTimeParam（DML，无 DDL，红线 #6 不违）；
/// 审计写统一 AuditLog 表（IAuditLogRepository）。载体「直接生效（时间窗）」：不进策略包/快照，CRUD 即最终态。
/// </summary>
public sealed class StageLeadTimeParamGovernanceService : IStageLeadTimeParamGovernanceService
{
    /// <summary>统一审计实体类型常量（AuditLog.EntityType）</summary>
    public const string EntityTypeStageLeadTimeParam = "StageLeadTimeParam";

    private const string ActionCreate = "Create";
    private const string ActionUpdate = "Update";
    private const string ActionDeactivate = "Deactivate";

    private const string SelectColumns =
        "Id, FactoryCode, StageCode, ProductionDeptCode, MaterialCode, ProductFamilyCode, " +
        "LeadTimeDays, LeadTimeHours, Priority, EffectiveFrom, EffectiveTo, IsDefault, IsActive, CreatedAt, UpdatedAt";

    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<StageLeadTimeParamGovernanceService> _logger;

    public StageLeadTimeParamGovernanceService(
        DatabaseConnectionManager connectionManager,
        IAuditLogRepository auditLogRepository,
        IDataScopeService dataScopeService,
        ILogger<StageLeadTimeParamGovernanceService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StageLeadTimeParamDto>> ListAsync(
        string? factoryCode, string? stageCode, string? productionDeptCode, bool? isActive,
        int actorUserId, CancellationToken ct = default)
    {
        var sql = new StringBuilder($"SELECT {SelectColumns} FROM StageLeadTimeParam WHERE 1=1");
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(factoryCode)) { sql.Append(" AND FactoryCode = @FactoryCode"); p.Add("FactoryCode", factoryCode); }
        if (!string.IsNullOrWhiteSpace(stageCode)) { sql.Append(" AND StageCode = @StageCode"); p.Add("StageCode", stageCode); }
        if (!string.IsNullOrWhiteSpace(productionDeptCode)) { sql.Append(" AND ProductionDeptCode = @ProductionDeptCode"); p.Add("ProductionDeptCode", productionDeptCode); }
        if (isActive.HasValue) { sql.Append(" AND IsActive = @IsActive"); p.Add("IsActive", isActive.Value); }

        // P0-03（0号位 审核 2026-10-09）：以受信主体 Business Scope 约束列表（Auth §9.3 禁止先全量再前端隐藏）。
        // 非 Global：Factory 授权集合（若授权）IN 约束；Department 授权集合（若授权）IN 约束（NULL 部门行放行——无部门归属不越权）；
        // 维度未授权（GetValues=null）不施加约束；无任何授权（Empty）→ fail-closed 空列表。
        var scope = await _dataScopeService.ResolveScopeAsync(actorUserId, ct);
        if (scope.IsEmpty)
        {
            return new List<StageLeadTimeParamDto>();
        }

        if (!scope.IsGlobal)
        {
            var factories = scope.GetValues(DataScopeTypes.Factory);
            if (factories is { Count: > 0 })
            {
                sql.Append(" AND FactoryCode IN @Factories");
                p.Add("Factories", factories);
            }
            else
            {
                // 非 Global 且无 Factory 授权：每行必有 FactoryCode，AND 交集语义下无任何可见行 → fail-closed 空列表。
                return new List<StageLeadTimeParamDto>();
            }

            var depts = scope.GetValues(DataScopeTypes.Department);
            if (depts is { Count: > 0 })
            {
                sql.Append(" AND (ProductionDeptCode IS NULL OR ProductionDeptCode IN @Depts)");
                p.Add("Depts", depts);
            }
        }

        sql.Append(" ORDER BY FactoryCode, StageCode, Priority, Id");

        var rows = await _connectionManager.QueryAsync<StageLeadTimeParamDto>(sql.ToString(), p, db: DatabaseId.APS);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<StageLeadTimeParamDto> CreateAsync(
        SaveStageLeadTimeParamRequest input, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        Validate(input);
        await EnsureTargetInScopeAsync(input.FactoryCode, input.ProductionDeptCode, actorUserId, ct);
        await EnsureNotDuplicateAsync(input.FactoryCode, input.StageCode, input.ProductionDeptCode, input.MaterialCode, input.ProductFamilyCode, excludeId: 0);

        var now = DateTime.Now;
        const string insertSql = @"
INSERT INTO StageLeadTimeParam
(FactoryCode, StageCode, ProductionDeptCode, MaterialCode, ProductFamilyCode,
 LeadTimeDays, LeadTimeHours, Priority, EffectiveFrom, EffectiveTo, IsDefault, IsActive, CreatedAt, UpdatedAt)
OUTPUT INSERTED.Id
VALUES (@FactoryCode, @StageCode, @ProductionDeptCode, @MaterialCode, @ProductFamilyCode,
 @LeadTimeDays, @LeadTimeHours, @Priority, @EffectiveFrom, @EffectiveTo, @IsDefault, 1, @CreatedAt, @UpdatedAt)";

        var p = new DynamicParameters();
        p.Add("FactoryCode", input.FactoryCode);
        p.Add("StageCode", input.StageCode);
        p.Add("ProductionDeptCode", input.ProductionDeptCode, DbType.String);
        p.Add("MaterialCode", input.MaterialCode, DbType.String);
        p.Add("ProductFamilyCode", input.ProductFamilyCode, DbType.String);
        p.Add("LeadTimeDays", input.LeadTimeDays);
        p.Add("LeadTimeHours", input.LeadTimeHours);
        p.Add("Priority", input.Priority);
        p.Add("EffectiveFrom", input.EffectiveFrom);
        p.Add("EffectiveTo", input.EffectiveTo, DbType.DateTime);
        p.Add("IsDefault", input.IsDefault);
        p.Add("CreatedAt", now);
        p.Add("UpdatedAt", now);

        // P1-03（0号位 审核 §五 P1-03）：审计前置预检先于 DML（治理 DML 在 APS / 审计在 Auth，跨库无 2PC）——
        // 审计库不可写则整操作失败、业务零变更（杜绝「已生效但无追溯审计」的参数）。
        await _auditLogRepository.EnsureWritableAsync(ct);

        var id = await _connectionManager.QueryFirstOrDefaultAsync<int>(insertSql, p, db: DatabaseId.APS);

        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = ActionCreate,
            EntityType = EntityTypeStageLeadTimeParam,
            EntityId = id.ToString(),
            OldValue = null,
            NewValue = input.StageCode,
            UserId = actorUserId,
            UserCode = actorUserCode,
            OccurredAt = now,
            Remark = $"新增阶段提前期参数 [{input.FactoryCode}/{input.StageCode}] LeadTimeDays={input.LeadTimeDays}",
        }, ct);

        return new StageLeadTimeParamDto
        {
            Id = id,
            FactoryCode = input.FactoryCode,
            StageCode = input.StageCode,
            ProductionDeptCode = input.ProductionDeptCode,
            MaterialCode = input.MaterialCode,
            ProductFamilyCode = input.ProductFamilyCode,
            LeadTimeDays = input.LeadTimeDays,
            LeadTimeHours = input.LeadTimeHours,
            Priority = input.Priority,
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            IsDefault = input.IsDefault,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <inheritdoc />
    public async Task<StageLeadTimeParamDto> UpdateAsync(
        int id, SaveStageLeadTimeParamRequest input, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ArgumentException("id 须大于 0。", nameof(id));
        }

        Validate(input);

        var existing = await GetByIdAsync(id);
        if (existing is null)
        {
            throw new ResourceNotFoundException($"阶段提前期参数（StageLeadTimeParam.Id={id}）不存在。");
        }

        // P0-03：老归属 + 新归属分别校验，防 IDOR 式跨工厂/跨部门记录迁移。
        await EnsureTargetInScopeAsync(existing.FactoryCode, existing.ProductionDeptCode, actorUserId, ct);
        await EnsureTargetInScopeAsync(input.FactoryCode, input.ProductionDeptCode, actorUserId, ct);

        await EnsureNotDuplicateAsync(input.FactoryCode, input.StageCode, input.ProductionDeptCode, input.MaterialCode, input.ProductFamilyCode, excludeId: id);

        var now = DateTime.Now;
        const string updateSql = @"
UPDATE StageLeadTimeParam SET
  FactoryCode = @FactoryCode, StageCode = @StageCode, ProductionDeptCode = @ProductionDeptCode,
  MaterialCode = @MaterialCode, ProductFamilyCode = @ProductFamilyCode,
  LeadTimeDays = @LeadTimeDays, LeadTimeHours = @LeadTimeHours, Priority = @Priority,
  EffectiveFrom = @EffectiveFrom, EffectiveTo = @EffectiveTo, IsDefault = @IsDefault,
  UpdatedAt = @UpdatedAt
WHERE Id = @Id";

        var p = new DynamicParameters();
        p.Add("Id", id);
        p.Add("FactoryCode", input.FactoryCode);
        p.Add("StageCode", input.StageCode);
        p.Add("ProductionDeptCode", input.ProductionDeptCode, DbType.String);
        p.Add("MaterialCode", input.MaterialCode, DbType.String);
        p.Add("ProductFamilyCode", input.ProductFamilyCode, DbType.String);
        p.Add("LeadTimeDays", input.LeadTimeDays);
        p.Add("LeadTimeHours", input.LeadTimeHours);
        p.Add("Priority", input.Priority);
        p.Add("EffectiveFrom", input.EffectiveFrom);
        p.Add("EffectiveTo", input.EffectiveTo, DbType.DateTime);
        p.Add("IsDefault", input.IsDefault);
        p.Add("UpdatedAt", now);

        // P1-03：审计前置预检（同 Create，先于 DML）。
        await _auditLogRepository.EnsureWritableAsync(ct);

        await _connectionManager.ExecuteAsync(updateSql, p, db: DatabaseId.APS);

        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = ActionUpdate,
            EntityType = EntityTypeStageLeadTimeParam,
            EntityId = id.ToString(),
            OldValue = existing.StageCode,
            NewValue = input.StageCode,
            UserId = actorUserId,
            UserCode = actorUserCode,
            OccurredAt = now,
            Remark = $"更新阶段提前期参数 [{input.FactoryCode}/{input.StageCode}]",
        }, ct);

        return new StageLeadTimeParamDto
        {
            Id = id,
            FactoryCode = input.FactoryCode,
            StageCode = input.StageCode,
            ProductionDeptCode = input.ProductionDeptCode,
            MaterialCode = input.MaterialCode,
            ProductFamilyCode = input.ProductFamilyCode,
            LeadTimeDays = input.LeadTimeDays,
            LeadTimeHours = input.LeadTimeHours,
            Priority = input.Priority,
            EffectiveFrom = input.EffectiveFrom,
            EffectiveTo = input.EffectiveTo,
            IsDefault = input.IsDefault,
            IsActive = existing.IsActive,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now,
        };
    }

    /// <inheritdoc />
    public async Task<StageLeadTimeParamDto> DeactivateAsync(
        int id, int actorUserId, string actorUserCode, CancellationToken ct = default)
    {
        if (id <= 0)
        {
            throw new ArgumentException("id 须大于 0。", nameof(id));
        }

        var existing = await GetByIdAsync(id);
        if (existing is null)
        {
            throw new ResourceNotFoundException($"阶段提前期参数（StageLeadTimeParam.Id={id}）不存在。");
        }

        await EnsureTargetInScopeAsync(existing.FactoryCode, existing.ProductionDeptCode, actorUserId, ct);

        // P1-03：审计前置预检（同 Create/Update，先于 DML）。
        await _auditLogRepository.EnsureWritableAsync(ct);

        var now = DateTime.Now;
        await _connectionManager.ExecuteAsync(
            "UPDATE StageLeadTimeParam SET IsActive = 0, UpdatedAt = @UpdatedAt WHERE Id = @Id",
            new { UpdatedAt = now, Id = id },
            db: DatabaseId.APS);

        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = ActionDeactivate,
            EntityType = EntityTypeStageLeadTimeParam,
            EntityId = id.ToString(),
            OldValue = existing.IsActive ? "Active" : "Inactive",
            NewValue = "Inactive",
            UserId = actorUserId,
            UserCode = actorUserCode,
            OccurredAt = now,
            Remark = $"停用阶段提前期参数 [{existing.FactoryCode}/{existing.StageCode}]",
        }, ct);

        return new StageLeadTimeParamDto
        {
            Id = existing.Id,
            FactoryCode = existing.FactoryCode,
            StageCode = existing.StageCode,
            ProductionDeptCode = existing.ProductionDeptCode,
            MaterialCode = existing.MaterialCode,
            ProductFamilyCode = existing.ProductFamilyCode,
            LeadTimeDays = existing.LeadTimeDays,
            LeadTimeHours = existing.LeadTimeHours,
            Priority = existing.Priority,
            EffectiveFrom = existing.EffectiveFrom,
            EffectiveTo = existing.EffectiveTo,
            IsDefault = existing.IsDefault,
            IsActive = false,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now,
        };
    }

    private async Task<StageLeadTimeParamDto?> GetByIdAsync(int id)
        => await _connectionManager.QueryFirstOrDefaultAsync<StageLeadTimeParamDto>(
            $"SELECT {SelectColumns} FROM StageLeadTimeParam WHERE Id = @Id",
            new { Id = id },
            db: DatabaseId.APS);

    private static void Validate(SaveStageLeadTimeParamRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.FactoryCode))
        {
            throw new ArgumentException("FactoryCode 不能为空。", nameof(input.FactoryCode));
        }

        if (string.IsNullOrWhiteSpace(input.StageCode))
        {
            throw new ArgumentException("StageCode 不能为空。", nameof(input.StageCode));
        }

        if (input.LeadTimeDays < 0 || input.LeadTimeHours < 0)
        {
            throw new SetupRuleDataRedLineException("提前期不允许为负（LeadTimeDays / LeadTimeHours ≥ 0）。");
        }

        if (input.LeadTimeDays == 0 && input.LeadTimeHours == 0)
        {
            throw new SetupRuleDataRedLineException("阶段提前期须大于 0（LeadTimeDays 与 LeadTimeHours 不能同时为 0）。");
        }

        if (input.Priority < 0)
        {
            throw new SetupRuleDataRedLineException("Priority 不允许为负。");
        }

        if (input.EffectiveTo.HasValue && input.EffectiveTo.Value < input.EffectiveFrom)
        {
            throw new SetupRuleDataRedLineException("EffectiveTo 不能早于 EffectiveFrom。");
        }
    }

    /// <summary>
    /// P0-03（0号位 审核）：写路径（Create/Update/Deactivate）按 Factory + Department 维度校验目标归属（Auth §9.3/9.4，fail-closed）。
    /// 非 Global 用户：FactoryCode 必在 Factory 授权内；ProductionDeptCode 非空时必在 Department 授权内（不同维度 AND）。
    /// 越界 → ScopeViolationException（Controller 映射 403）。
    /// </summary>
    private async Task EnsureTargetInScopeAsync(string factoryCode, string? productionDeptCode, int actorUserId, CancellationToken ct)
    {
        var scope = await _dataScopeService.ResolveScopeAsync(actorUserId, ct);
        if (scope.IsGlobal)
        {
            return;
        }

        if (!scope.Allows(DataScopeTypes.Factory, factoryCode))
        {
            throw new ScopeViolationException(DataScopeTypes.Factory, factoryCode);
        }

        if (!string.IsNullOrWhiteSpace(productionDeptCode)
            && !scope.Allows(DataScopeTypes.Department, productionDeptCode))
        {
            throw new ScopeViolationException(DataScopeTypes.Department, productionDeptCode);
        }
    }

    private async Task EnsureNotDuplicateAsync(
        string factoryCode, string stageCode, string? productionDeptCode, string? materialCode, string? productFamilyCode, int excludeId)
    {
        const string sql = @"
SELECT COUNT(1) FROM StageLeadTimeParam
WHERE FactoryCode = @FactoryCode AND StageCode = @StageCode
  AND ((@Dept IS NULL AND ProductionDeptCode IS NULL) OR ProductionDeptCode = @Dept)
  AND ((@Material IS NULL AND MaterialCode IS NULL) OR MaterialCode = @Material)
  AND ((@Family IS NULL AND ProductFamilyCode IS NULL) OR ProductFamilyCode = @Family)
  AND Id <> @ExcludeId";

        var p = new DynamicParameters();
        p.Add("FactoryCode", factoryCode);
        p.Add("StageCode", stageCode);
        p.Add("Dept", productionDeptCode, DbType.String);
        p.Add("Material", materialCode, DbType.String);
        p.Add("Family", productFamilyCode, DbType.String);
        p.Add("ExcludeId", excludeId);

        var count = await _connectionManager.QueryFirstOrDefaultAsync<int>(sql, p, db: DatabaseId.APS);
        if (count > 0)
        {
            throw new SetupRuleDataRedLineException("已存在相同（工厂+阶段+生产部门+物料+产品族）组合的阶段提前期参数，不允许重复。");
        }
    }
}