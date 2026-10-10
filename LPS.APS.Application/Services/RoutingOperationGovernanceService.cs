using System.Data;
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
/// 工序工艺属性治理服务实现（T1 · S2/S3 OPM 治理 API）。
/// OperationPlanningMode（OPM）属 APS 工艺规划属性（0号位 2026-09-23 Q1 裁决 = 治理侧直维护唯一主路径）。
/// 直接持有 DatabaseConnectionManager 写 APS_Production.RoutingOperation（DML，无 DDL，红线 #6 不违）；
/// 审计写统一 AuditLog 表（IAuditLogRepository）。
/// </summary>
public sealed class RoutingOperationGovernanceService : IRoutingOperationGovernanceService
{
    /// <summary>统一审计实体类型常量（AuditLog.EntityType）</summary>
    public const string EntityTypeRoutingOperation = "RoutingOperation";

    private const string ActionUpdate = "Update";

    private readonly DatabaseConnectionManager _connectionManager;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<RoutingOperationGovernanceService> _logger;

    public RoutingOperationGovernanceService(
        DatabaseConnectionManager connectionManager,
        IAuditLogRepository auditLogRepository,
        IDataScopeService dataScopeService,
        ILogger<RoutingOperationGovernanceService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// P1-04 Department Scope：目标工序部门码（ProductionDepartment.DeptCode）调 EnsureInScopeAsync(Department)。
    /// 部门不存在 → 404（ResourceNotFoundException）；越界 → ScopeViolationException（Controller 映射 403）。
    /// 依据：RoutingOperation.ProductionDepartmentId 即业务范围维度（工序节点属生产部门），冻结 Auth §9.4「目标对象须在业务 Scope 内」。
    /// </summary>
    private async Task EnsureDepartmentScopeAsync(int departmentId, int actorUserId, CancellationToken ct)
    {
        var deptCode = await _connectionManager.QueryFirstOrDefaultAsync<string?>(
            "SELECT DeptCode FROM ProductionDepartment WHERE Id = @Id",
            new { Id = departmentId },
            db: DatabaseId.APS);

        if (string.IsNullOrWhiteSpace(deptCode))
        {
            throw new ResourceNotFoundException($"生产部门（ProductionDepartment.Id={departmentId}）不存在。");
        }

        await _dataScopeService.EnsureInScopeAsync(actorUserId, DataScopeTypes.Department, deptCode, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoutingOperationDto>> ListOperationsAsync(int materialId, CancellationToken ct = default)
    {
        if (materialId <= 0)
        {
            throw new ArgumentException("materialId 须大于 0。", nameof(materialId));
        }

        var rows = await _connectionManager.QueryAsync<RoutingOperationDto>(
            @"SELECT Id, MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode,
                     OperationName, ProcessType, StageCode, OperationPlanningMode
              FROM RoutingOperation
              WHERE IsActive = 1 AND MaterialId = @MaterialId
              ORDER BY RouteCode, PathId, OperationCode",
            new { MaterialId = materialId },
            db: DatabaseId.APS);

        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<RoutingOperationDto> UpdateOperationPlanningModeAsync(
        long operationId,
        string mode,
        int actorUserId,
        string actorUserCode,
        CancellationToken ct = default)
    {
        if (operationId <= 0)
        {
            throw new ArgumentException("operationId 须大于 0。", nameof(operationId));
        }

        if (!OperationPlanningModeValues.IsValid(mode))
        {
            throw new SetupRuleDataRedLineException(
                $"OperationPlanningMode 非法：'{mode}'。合法三态：FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY。");
        }

        var existing = await _connectionManager.QueryFirstOrDefaultAsync<RoutingOperationDto>(
            @"SELECT Id, MaterialId, ProductionDepartmentId, RouteCode, PathId, OperationCode,
                     OperationName, ProcessType, StageCode, OperationPlanningMode
              FROM RoutingOperation
              WHERE Id = @Id",
            new { Id = operationId },
            db: DatabaseId.APS);

        if (existing is null)
        {
            throw new ResourceNotFoundException($"工序（RoutingOperation.Id={operationId}）不存在。");
        }

        // P1-04：写路径须目标工序部门在业务 Scope 内（越界 403）。
        await EnsureDepartmentScopeAsync(existing.ProductionDepartmentId, actorUserId, ct);

        // P1-03：审计前置预检（跨库无 2PC）——审计库不可写则整操作失败、业务零变更。
        await _auditLogRepository.EnsureWritableAsync(ct);

        var updatedAt = DateTime.Now;
        await _connectionManager.ExecuteAsync(
            @"UPDATE RoutingOperation
              SET OperationPlanningMode = @Mode, UpdatedAt = @UpdatedAt
              WHERE Id = @Id",
            new { Mode = mode, UpdatedAt = updatedAt, Id = operationId },
            db: DatabaseId.APS);

        await _auditLogRepository.AddAsync(new AuditLog
        {
            ActionCode = ActionUpdate,
            EntityType = EntityTypeRoutingOperation,
            EntityId = operationId.ToString(),
            OldValue = existing.OperationPlanningMode,
            NewValue = mode,
            UserId = actorUserId,
            UserCode = actorUserCode,
            OccurredAt = updatedAt,
            Remark = $"工序[{existing.OperationCode}] OPM {existing.OperationPlanningMode} → {mode}",
        }, ct);

        return new RoutingOperationDto
        {
            Id = existing.Id,
            MaterialId = existing.MaterialId,
            ProductionDepartmentId = existing.ProductionDepartmentId,
            RouteCode = existing.RouteCode,
            PathId = existing.PathId,
            OperationCode = existing.OperationCode,
            OperationName = existing.OperationName,
            ProcessType = existing.ProcessType,
            StageCode = existing.StageCode,
            OperationPlanningMode = mode,
        };
    }
}