namespace LPS.APS.Core.Dto;

/// <summary>
/// 物料×阶段→默认生产部门 上下文（2号位从 MaterialStageDeptContext 裁剪当前 Domain 后传给 1号位）。
///
/// PM 裁定（ProductionDepartment回复.md）：最小 B ——
/// MaterialStageDeptContext 作为显式 DTO 传入 1号位，1号位按 (MaterialId, StageCode) → ProductionDepartmentId
/// 锁定 Routing 三件套（RoutingOperation / RoutingDependency / OperationResourceEligibility），
/// 不得重新推导部门、不得跨部门优化选择。
///
/// 仅传本次 Domain 涉及的 (MaterialId, StageCode) 条目；不带 SourceType / SourceDetail / ValidFrom 等
/// 2号位数据治理/追溯字段（1号位不需要）。
/// </summary>
public sealed class MaterialStageDepartmentContextDto
{
    public int MaterialId { get; init; }

    /// <summary>必须存在于 StageDict 的合法大工艺阶段码</summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>该物料在该阶段下的默认生产部门 Id</summary>
    public int ProductionDepartmentId { get; init; }
}
