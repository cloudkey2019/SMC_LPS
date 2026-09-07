namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 数据范围策略表
/// 对应 APS_Auth.DataScopePolicy（DDL v1.1 冻结对齐：Id / ScopeType / ScopeValue / Description / CreatedAt）
/// ScopeType 值域见 <see cref="LPS.APS.Core.Authorization.DataScopeTypes"/>：
///   Factory / ProductFamily / Department / Domain / ResourceOrgGroup(兼容) / Global
/// </summary>
public class DataScopePolicy
{
    /// <summary>主键</summary>
    public int Id { get; set; }

    /// <summary>范围维度（Factory / ProductFamily / Department / Domain / ResourceOrgGroup / Global）</summary>
    public string ScopeType { get; set; } = string.Empty;

    /// <summary>范围值（工厂编码、产品族编码、部门编码、域 Key，或 Global 的 '*'）</summary>
    public string ScopeValue { get; set; } = string.Empty;

    /// <summary>说明</summary>
    public string? Description { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }
}
