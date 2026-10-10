namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 角色业务范围关联表
/// 对应 APS_Auth.RoleDataScope（复合主键 RoleId + ScopePolicyId，DDL v1.3）
/// </summary>
public class RoleDataScope
{
    /// <summary>角色 Id（关联 Role.Id）</summary>
    public int RoleId { get; set; }

    /// <summary>范围策略 Id（关联 DataScopePolicy.Id）</summary>
    public int ScopePolicyId { get; set; }

    /// <summary>分配时间</summary>
    public DateTime AssignedAt { get; set; }
}
