using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Auth;

/// <summary>
/// 权限码种子服务实现（F-G5 收尾，3号位）
/// 启动时确保 Permission 表包含代码侧 V1 功能权限码（与 <see cref="LPS.APS.Core.Authorization.PermissionCodes.All"/> 一一对应）。
/// 注意：P0-01 裁决后统一 `aps.` 前缀；0号位裁决 V1 权限码采用 DDL v1.3 细粒度 34 码 + Setup 换型 3 码（4号位 Setup 契约 §11.1）+ ResourceCalendar/ManualCapacity 各 3 码（5号位 权限码申请 2026-09-24）。
/// </summary>
public class PermissionSeedService : IPermissionSeedService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<PermissionSeedService> _logger;

    /// <summary>V1 功能权限码种子（Code / Name / Module / ActionType / Description），对齐 DDL v1.3 的 34 码 + Setup 换型 3 码 + ResourceCalendar/ManualCapacity 各 3 码（43 码）。</summary>
    private static readonly (string Code, string Name, string Module, string ActionType, string Description)[] Seeds =
    {
        // Plan
        ("aps.plan.view", "查看计划", "Plan", "View", "查看授权业务范围内计划"),
        ("aps.plan.run", "发起排程", "Plan", "Execute", "按授权范围发起排程/运行"),
        ("aps.plan.compare", "计划对比", "Plan", "View", "查看版本/Candidate对比"),
        ("aps.plan.export", "导出计划", "Plan", "Execute", "导出授权范围内计划"),

        // CTP / Insert / Reschedule
        ("aps.ctp.view", "查看承诺交期评估", "CTP", "View", "查看CTP结果"),
        ("aps.ctp.evaluate", "发起承诺交期评估", "CTP", "Execute", "发起CTP试算"),
        ("aps.insert.impact.evaluate", "发起插单影响分析", "Insert", "Execute", "发起INSERT_IMPACT_ANALYSIS"),
        ("aps.reschedule.local", "发起局部重排", "Reschedule", "Execute", "发起LOCAL_RESCHEDULE"),
        ("aps.reschedule.manual", "发起人工重排", "Reschedule", "Execute", "发起MANUAL_RESCHEDULE"),

        // Candidate
        ("aps.candidate.view", "查看候选计划", "Candidate", "View", "查看Candidate"),
        ("aps.candidate.confirm", "确认候选计划", "Candidate", "Execute", "人工确认Candidate"),
        ("aps.candidate.activate", "激活候选计划", "Candidate", "Execute", "在业务允许时激活Candidate"),

        // Rule / Parameter / Strategy
        ("aps.rule.view", "查看规则", "Rule", "View", "查看规则集/版本"),
        ("aps.rule.edit", "编辑规则", "Rule", "Edit", "编辑规则草稿"),
        ("aps.rule.publish", "发布规则", "Rule", "Execute", "发布规则版本"),
        ("aps.parameter.view", "查看参数", "Parameter", "View", "查看参数集/版本"),
        ("aps.parameter.edit", "编辑参数", "Parameter", "Edit", "编辑参数草稿"),
        ("aps.strategy.view", "查看策略", "Strategy", "View", "查看策略包/版本"),
        ("aps.strategy.edit", "编辑策略", "Strategy", "Edit", "编辑策略草稿"),
        ("aps.strategy.publish", "发布策略", "Strategy", "Execute", "发布策略版本"),

        // Setup 换型（4号位 Setup 契约 §11.1 新增 3 码）
        ("aps.setup.view", "查看Setup换型规则", "Setup", "View", "查看Setup换型规则/版本"),
        ("aps.setup.edit", "维护Setup换型规则", "Setup", "Edit", "维护Setup换型规则草稿"),
        ("aps.setup.publish", "发布Setup换型规则", "Setup", "Execute", "发布Setup换型规则版本"),

        // ResourceCalendar 设备资源日历（5号位 权限码申请 2026-09-24 新增 3 码）
        ("aps.resource_calendar.view", "查看设备资源日历", "ResourceCalendar", "View", "查询设备日历窗口"),
        ("aps.resource_calendar.edit", "维护设备资源日历", "ResourceCalendar", "Edit", "批量铺窗/修改设备日历"),
        ("aps.resource_calendar.delete", "删除设备资源日历", "ResourceCalendar", "Execute", "物理删除设备日历窗口"),

        // ManualCapacity 人工能力槽（5号位 权限码申请 2026-09-24 新增 3 码）
        ("aps.manual_capacity.view", "查看人工能力槽", "ManualCapacity", "View", "查询人工槽主档/日历"),
        ("aps.manual_capacity.edit", "维护人工能力槽", "ManualCapacity", "Edit", "新增主档/批量铺窗"),
        ("aps.manual_capacity.delete", "删除人工能力槽", "ManualCapacity", "Execute", "软删主档/物理删窗口"),

        // Manual ETA
        ("aps.manual_eta.view", "查看人工到货时间", "ManualEta", "View", "查看Manual ETA"),
        ("aps.manual_eta.edit", "维护人工到货时间", "ManualEta", "Edit", "新增/修改Manual ETA"),
        ("aps.manual_eta.cancel", "取消人工到货时间", "ManualEta", "Execute", "取消Manual ETA"),

        // Demand Protection
        ("aps.demand_protection.view", "查看需求保护", "DemandProtection", "View", "查看Demand Protection状态"),
        ("aps.demand_protection.release", "释放需求保护", "DemandProtection", "Execute", "受控释放Demand Protection"),

        // MES
        ("aps.mes.view", "查看MES状态", "MES", "View", "查看MES相关状态/资格"),
        ("aps.mes.dispatch", "发起MES受控操作", "MES", "Execute", "按冻结资格执行MES受控操作"),

        // Auth / Scope
        ("aps.auth.user.view", "查看用户", "Auth", "View", "查看用户"),
        ("aps.auth.user.edit", "维护用户", "Auth", "Edit", "创建/修改/禁用用户"),
        ("aps.auth.role.view", "查看角色", "Auth", "View", "查看角色"),
        ("aps.auth.role.edit", "维护角色", "Auth", "Edit", "创建/修改角色"),
        ("aps.auth.permission.assign", "分配功能权限", "Auth", "Edit", "角色权限分配"),
        ("aps.auth.scope.assign", "分配业务范围", "Auth", "Edit", "用户/角色业务Scope分配"),

        // Audit
        ("aps.audit.view", "查看审计日志", "Audit", "View", "查看权限及关键业务审计"),
    };

    public PermissionSeedService(DatabaseConnectionManager connectionManager, ILogger<PermissionSeedService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        foreach (var (code, name, module, actionType, description) in Seeds)
        {
            var exists = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
                "SELECT COUNT(*) FROM [Permission] WHERE PermissionCode = @Code",
                new { Code = code },
                db: DatabaseId.Auth) ?? 0;

            if (exists > 0)
                continue;

            await _connectionManager.ExecuteAsync(
                @"INSERT INTO [Permission]
                    (PermissionCode, PermissionName, Description, Module, ActionType, IsActive, CreatedAt, UpdatedAt)
                  VALUES (@Code, @Name, @Description, @Module, @ActionType, 1, GETDATE(), GETDATE())",
                new { Code = code, Name = name, Description = description, Module = module, ActionType = actionType },
                db: DatabaseId.Auth);

            _logger.LogInformation("播种权限码成功: {Code}", code);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> FindMissingPermissionCodesAsync(CancellationToken cancellationToken = default)
    {
        // P1-06：以代码侧权威清单 PermissionCodes.All（43 码）为基线校验落库情况，
        // 未落库即视为「权限基线未对齐」。
        var missing = new List<string>();
        foreach (var code in PermissionCodes.All)
        {
            var exists = await _connectionManager.QueryFirstOrDefaultAsync<int?>(
                "SELECT COUNT(*) FROM [Permission] WHERE PermissionCode = @Code",
                new { Code = code },
                db: DatabaseId.Auth) ?? 0;

            if (exists == 0)
                missing.Add(code);
        }

        return missing;
    }
}