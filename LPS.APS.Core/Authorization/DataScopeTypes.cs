namespace LPS.APS.Core.Authorization;

/// <summary>
/// 业务范围维度标准值（F-G4）
/// 对齐 APS_Auth.DataScopePolicy 的 CK_DataScope_Type 检查约束（DDL v1.3 冻结对齐版）。
/// </summary>
public static class DataScopeTypes
{
    /// <summary>工厂</summary>
    public const string Factory = "Factory";

    /// <summary>产品族</summary>
    public const string ProductFamily = "ProductFamily";

    /// <summary>部门（生产部门 ProductionDepartment）</summary>
    public const string Department = "Department";

    /// <summary>域</summary>
    public const string Domain = "Domain";

    /// <summary>资源组织组（v1.0 遗留兼容维度，不替代 Department/Domain）</summary>
    public const string ResourceOrgGroup = "ResourceOrgGroup";

    /// <summary>全局（全放行）</summary>
    public const string Global = "Global";
}
