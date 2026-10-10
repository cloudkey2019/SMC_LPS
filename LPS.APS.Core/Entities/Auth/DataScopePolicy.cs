namespace LPS.APS.Core.Entities.Auth;

/// <summary>
/// 数据范围策略表
/// 对应 APS_Auth.DataScopePolicy（DDL v1.3：Id / ScopeType / ScopeValue / Description / IsEnabled / CreatedAt / UpdatedAt）
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

    /// <summary>启用标志（D-2 裁决：停用即停止参与分配与授权）</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>更新时间</summary>
    public DateTime UpdatedAt { get; set; }
}
