using System.Collections.Generic;

namespace LPS.APS.Core.DTOs.Scope;

/// <summary>
/// 白天 Candidate 操作允许影响的业务对象范围（0号位《Candidate规范统一裁决》§五/§七）。
/// 作用「谁可发起什么变化」；随 ScopeJsonV2 并入 <see cref="Governance.ScopeJsonV2.BusinessScope"/> 持久化到 ScheduleRun.ScopeJson（零 DDL）。
/// 维度取值对齐 <see cref="Authorization.DataScopeTypes"/> 常量：订单类=ProductFamily、Task 局部=ResourceOrgGroup/Department、系统级=Global。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class BusinessScopeDto
{
    /// <summary>全局放行（DataScopeTypes.Global）；为 true 时忽略 <see cref="ScopeType"/> 与 <see cref="ScopeValues"/>。</summary>
    public bool IsGlobal { get; set; }

    /// <summary>业务范围维度（DataScopeTypes 常量值：ProductFamily / ResourceOrgGroup / Department 等）；Global 放行时可空。</summary>
    public string ScopeType { get; set; } = string.Empty;

    /// <summary>允许影响的业务对象键集合（业务键字符串，与 IDataScopeService.EnsureInScopeAsync 的 value 同口径）。</summary>
    public IReadOnlyList<string> ScopeValues { get; set; } = [];
}