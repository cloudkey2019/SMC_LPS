namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 用户直接业务范围关联表
/// 对应 APS_Auth.UserDataScope（复合主键 UserId + ScopePolicyId，DDL v1.3）
/// </summary>
public class UserDataScope
{
    /// <summary>用户 Id（关联 User.Id）</summary>
    public int UserId { get; set; }

    /// <summary>范围策略 Id（关联 DataScopePolicy.Id）</summary>
    public int ScopePolicyId { get; set; }

    /// <summary>分配时间</summary>
    public DateTime AssignedAt { get; set; }

    /// <summary>分配人 Id（可空）</summary>
    public int? AssignedBy { get; set; }
}
