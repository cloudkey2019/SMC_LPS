namespace LPS.APS.Core.Authorization;

/// <summary>
/// V1 功能权限点标准码（职责裁决 v1.0 §7.1，3号位 治理）
/// 命名规则：模块.动作（小写驼峰）。需与 4号位 前端按钮/菜单对齐后冻结。
/// </summary>
public static class PermissionCodes
{
    /// <summary>JWT 中权限码声明的类型名（登录签发时注入，供 me 端点与前端读取）</summary>
    public const string PermissionClaimType = "permissionCode";

    /// <summary>查看计划</summary>
    public const string PlanView = "plan.view";

    /// <summary>CTP</summary>
    public const string PlanCtp = "plan.ctp";

    /// <summary>插单影响分析</summary>
    public const string OrderInsertImpact = "order.insertImpact";

    /// <summary>局部重排</summary>
    public const string ReschedulePartial = "reschedule.partial";

    /// <summary>人工重排</summary>
    public const string RescheduleManual = "reschedule.manual";

    /// <summary>Candidate 确认</summary>
    public const string CandidateConfirm = "candidate.confirm";

    /// <summary>规则/参数维护</summary>
    public const string RuleMaintain = "rule.maintain";

    /// <summary>规则发布</summary>
    public const string RulePublish = "rule.publish";

    /// <summary>Manual ETA 维护</summary>
    public const string ManualEtaMaintain = "manualEta.maintain";

    /// <summary>Demand Protection 释放</summary>
    public const string DemandProtectionRelease = "demandProtection.release";

    /// <summary>MES 受控操作</summary>
    public const string MesControlledOperation = "mes.controlledOperation";

    /// <summary>用户/角色/权限管理</summary>
    public const string AuthManage = "auth.manage";

    /// <summary>审计查看</summary>
    public const string AuditView = "audit.view";

    /// <summary>全部 V1 功能权限点（供策略注册遍历）</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        PlanView, PlanCtp, OrderInsertImpact, ReschedulePartial, RescheduleManual,
        CandidateConfirm, RuleMaintain, RulePublish, ManualEtaMaintain,
        DemandProtectionRelease, MesControlledOperation, AuthManage, AuditView
    };
}
