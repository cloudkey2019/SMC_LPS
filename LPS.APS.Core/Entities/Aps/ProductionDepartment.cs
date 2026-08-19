namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 生产部门表（v5.0.16新增）
/// 对应 APS_Production.ProductionDepartment
///
/// 业务定位：APS 排程责任部门字典
/// 核心约束：一个 ProductionDepartment 只归属一个 StageCode（部门 vs 阶段 1:1）
///          一个 StageCode 可对应多个 ProductionDepartment（阶段 vs 部门 1:N）
///
/// 消费方：
/// - Resource.ProductionDepartmentId（资源归属）
/// - RoutingOperation/RoutingDependency/OperationResourceEligibility.ProductionDepartmentId（部门版本路由）
/// - MaterialSupplyContext.DefaultProductionDepartmentId（仓库级默认）
/// - MaterialStageDeptContext.DefaultProductionDepartmentId（1号位排程主链入口）
/// </summary>
public class ProductionDepartment
{
    public int Id { get; set; }

    /// <summary>
    /// APS 业务键（如 'CN_MACH_DEPT_01'）
    /// </summary>
    public string DeptCode { get; set; } = string.Empty;

    /// <summary>
    /// 部门中文名（如"加工一部"）
    /// </summary>
    public string DeptName { get; set; } = string.Empty;

    /// <summary>
    /// 工厂归属（可空：兼容早期未明确归厂的部门）
    /// </summary>
    public int? FactoryId { get; set; }

    /// <summary>
    /// 单值归属阶段（业务约束 1:1）
    /// 软引用 StageDict.StageCode
    /// </summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>
    /// 可选业务标签（MACHINING/ASSEMBLY/SURFACE/OUTSOURCE/SPECIAL/OTHER）
    /// </summary>
    public string? DeptType { get; set; }

    /// <summary>
    /// 来源标记（ERP/MES/APS 自建）
    /// </summary>
    public string? SourceSystem { get; set; }

    /// <summary>
    /// 源系统部门码（审计用）
    /// </summary>
    public string? SourceDeptCode { get; set; }

    /// <summary>
    /// 是否参与 APS 排程（0=仅作汇总维度，不承担 Routing 路由职责）
    /// </summary>
    public bool IsSchedulingDept { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
