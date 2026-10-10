namespace LPS.APS.Core.Authorization;

/// <summary>
/// V1 功能权限点标准码（职责裁决 v1.0 §7.1；0号位裁决：V1 权限码采用 Auth DDL v1.3 细粒度 34 码，3号位 治理）
/// 命名规则：aps.模块.动作（统一 aps. 前缀，与 Auth DDL v1.3 冻结权限码种子一一对应）。
/// </summary>
public static class PermissionCodes
{
    /// <summary>JWT 中权限码声明的类型名（登录签发时注入，供 me 端点与前端读取）</summary>
    public const string PermissionClaimType = "permissionCode";

    // ---------- Plan ----------
    /// <summary>查看计划</summary>
    public const string PlanView = "aps.plan.view";
    /// <summary>发起排程/运行（白天候选运行创建、FAILED 恢复）</summary>
    public const string PlanRun = "aps.plan.run";
    /// <summary>计划对比</summary>
    public const string PlanCompare = "aps.plan.compare";
    /// <summary>导出计划</summary>
    public const string PlanExport = "aps.plan.export";

    // ---------- CTP / Insert / Reschedule ----------
    /// <summary>查看承诺交期评估</summary>
    public const string CtpView = "aps.ctp.view";
    /// <summary>CTP 试算（发起承诺交期评估）</summary>
    public const string PlanCtp = "aps.ctp.evaluate";
    /// <summary>插单影响分析</summary>
    public const string OrderInsertImpact = "aps.insert.impact.evaluate";
    /// <summary>局部重排</summary>
    public const string ReschedulePartial = "aps.reschedule.local";
    /// <summary>人工重排</summary>
    public const string RescheduleManual = "aps.reschedule.manual";

    // ---------- Candidate ----------
    /// <summary>查看候选计划</summary>
    public const string CandidateView = "aps.candidate.view";
    /// <summary>Candidate 确认</summary>
    public const string CandidateConfirm = "aps.candidate.confirm";
    /// <summary>Candidate 激活</summary>
    public const string CandidateActivate = "aps.candidate.activate";

    // ---------- Rule / Parameter / Strategy ----------
    /// <summary>查看规则</summary>
    public const string RuleView = "aps.rule.view";
    /// <summary>编辑规则</summary>
    public const string RuleMaintain = "aps.rule.edit";
    /// <summary>发布规则</summary>
    public const string RulePublish = "aps.rule.publish";
    /// <summary>查看参数</summary>
    public const string ParameterView = "aps.parameter.view";
    /// <summary>编辑参数</summary>
    public const string ParameterEdit = "aps.parameter.edit";
    /// <summary>查看策略</summary>
    public const string StrategyView = "aps.strategy.view";
    /// <summary>编辑策略</summary>
    public const string StrategyEdit = "aps.strategy.edit";
    /// <summary>发布策略</summary>
    public const string StrategyPublish = "aps.strategy.publish";

    // ---------- Manual ETA ----------
    /// <summary>查看人工到货时间</summary>
    public const string ManualEtaView = "aps.manual_eta.view";
    /// <summary>维护人工到货时间</summary>
    public const string ManualEtaMaintain = "aps.manual_eta.edit";
    /// <summary>取消人工到货时间</summary>
    public const string ManualEtaCancel = "aps.manual_eta.cancel";

    // ---------- Demand Protection ----------
    /// <summary>查看需求保护</summary>
    public const string DemandProtectionView = "aps.demand_protection.view";
    /// <summary>Demand Protection 释放</summary>
    public const string DemandProtectionRelease = "aps.demand_protection.release";

    // ---------- Setup 换型 ----------
    /// <summary>查看 Setup 换型规则</summary>
    public const string SetupView = "aps.setup.view";
    /// <summary>维护 Setup 换型规则</summary>
    public const string SetupEdit = "aps.setup.edit";
    /// <summary>发布 Setup 换型规则版本</summary>
    public const string SetupPublish = "aps.setup.publish";

    // ---------- ResourceCalendar 设备资源日历 ----------
    /// <summary>查看设备资源日历</summary>
    public const string ResourceCalendarView = "aps.resource_calendar.view";
    /// <summary>维护设备资源日历</summary>
    public const string ResourceCalendarEdit = "aps.resource_calendar.edit";
    /// <summary>删除设备资源日历</summary>
    public const string ResourceCalendarDelete = "aps.resource_calendar.delete";

    // ---------- ManualCapacity 人工能力槽 ----------
    /// <summary>查看人工能力槽</summary>
    public const string ManualCapacityView = "aps.manual_capacity.view";
    /// <summary>维护人工能力槽</summary>
    public const string ManualCapacityEdit = "aps.manual_capacity.edit";
    /// <summary>删除人工能力槽</summary>
    public const string ManualCapacityDelete = "aps.manual_capacity.delete";

    // ---------- MES ----------
    /// <summary>查看 MES 状态</summary>
    public const string MesView = "aps.mes.view";
    /// <summary>MES 受控操作</summary>
    public const string MesControlledOperation = "aps.mes.dispatch";

    // ---------- Auth / Scope ----------
    /// <summary>查看用户</summary>
    public const string AuthUserView = "aps.auth.user.view";
    /// <summary>维护用户（V1 聚合入口码，RBAC 端点当前统一挂此码；端点细分授权归 P0-02 批次）</summary>
    public const string AuthManage = "aps.auth.user.edit";
    /// <summary>查看角色</summary>
    public const string AuthRoleView = "aps.auth.role.view";
    /// <summary>维护角色</summary>
    public const string AuthRoleEdit = "aps.auth.role.edit";
    /// <summary>分配功能权限</summary>
    public const string AuthPermissionAssign = "aps.auth.permission.assign";
    /// <summary>分配业务范围</summary>
    public const string AuthScopeAssign = "aps.auth.scope.assign";

    // ---------- Audit ----------
    /// <summary>查看审计日志</summary>
    public const string AuditView = "aps.audit.view";

    /// <summary>全部 V1 功能权限点（43 码，供策略注册遍历；34 码基线 + Setup 换型 3 码 + ResourceCalendar 3 码 + ManualCapacity 3 码，后两者为 5号位 权限码申请 2026-09-24 新增）</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        PlanView, PlanRun, PlanCompare, PlanExport,
        CtpView, PlanCtp, OrderInsertImpact, ReschedulePartial, RescheduleManual,
        CandidateView, CandidateConfirm, CandidateActivate,
        RuleView, RuleMaintain, RulePublish,
        ParameterView, ParameterEdit,
        StrategyView, StrategyEdit, StrategyPublish,
        ManualEtaView, ManualEtaMaintain, ManualEtaCancel,
        DemandProtectionView, DemandProtectionRelease,
        SetupView, SetupEdit, SetupPublish,
        ResourceCalendarView, ResourceCalendarEdit, ResourceCalendarDelete,
        ManualCapacityView, ManualCapacityEdit, ManualCapacityDelete,
        MesView, MesControlledOperation,
        AuthUserView, AuthManage, AuthRoleView, AuthRoleEdit, AuthPermissionAssign, AuthScopeAssign,
        AuditView
    };
}