using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Auth;

/// <summary>
/// 权限码种子服务实现（F-G5 收尾，3号位）
/// 启动时确保 Permission 表包含代码侧 13 个 V1 功能权限码（与 <see cref="LPS.APS.Core.Authorization.PermissionCodes.All"/> 一一对应）。
/// 注意：以代码侧权限码为准，非 DDL v1.1 的 aps.* 细粒度码。
/// </summary>
public class PermissionSeedService : IPermissionSeedService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<PermissionSeedService> _logger;

    /// <summary>V1 功能权限码种子（Code / Name / Module / Category / Description）。</summary>
    private static readonly (string Code, string Name, string Module, string Category, string Description)[] Seeds =
    {
        ("plan.view", "查看计划", "Plan", "View", "查看授权业务范围内计划"),
        ("plan.ctp", "CTP", "CTP", "Execute", "发起承诺交期评估"),
        ("order.insertImpact", "插单影响分析", "Order", "Execute", "发起插单影响分析"),
        ("reschedule.partial", "局部重排", "Reschedule", "Execute", "发起局部重排"),
        ("reschedule.manual", "人工重排", "Reschedule", "Execute", "发起人工重排"),
        ("candidate.confirm", "Candidate 确认", "Candidate", "Execute", "确认候选计划"),
        ("rule.maintain", "规则/参数维护", "Rule", "Edit", "编辑规则/参数草稿"),
        ("rule.publish", "规则发布", "Rule", "Execute", "发布规则版本"),
        ("manualEta.maintain", "Manual ETA 维护", "ManualEta", "Edit", "维护人工到货时间"),
        ("demandProtection.release", "Demand Protection 释放", "DemandProtection", "Execute", "受控释放需求保护"),
        ("mes.controlledOperation", "MES 受控操作", "MES", "Execute", "发起 MES 受控操作"),
        ("auth.manage", "用户/角色/权限管理", "Auth", "Edit", "用户、角色、权限管理"),
        ("audit.view", "审计查看", "Audit", "View", "查看权限及关键业务审计"),
    };

    public PermissionSeedService(DatabaseConnectionManager connectionManager, ILogger<PermissionSeedService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (code, name, module, category, description) in Seeds)
        {
            var exists = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
                "SELECT COUNT(*) FROM [Permission] WHERE PermissionCode = @Code",
                new { Code = code },
                db: DatabaseId.Auth) ?? 0;

            if (exists > 0)
                continue;

            await _connectionManager.ExecuteAsync(
                @"INSERT INTO [Permission]
                    (PermissionCode, PermissionName, Description, Module, Category, IsActive, CreatedAt, UpdatedAt)
                  VALUES (@Code, @Name, @Description, @Module, @Category, 1, GETDATE(), GETDATE())",
                new { Code = code, Name = name, Description = description, Module = module, Category = category },
                db: DatabaseId.Auth);

            _logger.LogInformation("播种权限码成功: {Code}", code);
        }
    }
}
