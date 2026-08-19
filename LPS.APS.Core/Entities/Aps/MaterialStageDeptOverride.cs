namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 物料阶段部门覆盖表（v5.0.16新增）
/// 对应 APS_Production.MaterialStageDeptOverride
///
/// 业务定位：弥补 MSC 数据缺失/冲突的人工维护入口
///
/// 适用场景：
/// 1. MSC 中没有生产部门
/// 2. MSC 自动归一化后出现歧义/冲突，无法自动拍板
/// 3. ERP/MES 信息不全，需业务显式指定
///
/// 维护粒度：必须维护到 (Model 或 MaterialCode) × StageCode → ProductionDeptCode
/// ⚠️ 不能只维护 Model → Department（部门是物料×阶段联合属性）
///
/// 输入键策略：
/// - 业务人员可用 Model 录入（更熟悉）；2号位导入时做 Model→MaterialCode 1:1 检查
/// - Model 1:N 多个 MaterialCode 时拒收，返回明细，要求业务确认到 MaterialCode
///
/// 优先级：人工维护 > 自动草稿（详见 sp_RebuildMaterialStageDeptContext）
/// </summary>
public class MaterialStageDeptOverride
{
    public long Id { get; set; }

    /// <summary>
    /// 业务录入键（与 MaterialCode 至少填一项）
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 物料编码（业务能直接给出时优先填这里）
    /// </summary>
    public string? MaterialCode { get; set; }

    /// <summary>
    /// 大工艺阶段码（必须取自 StageDict）
    /// </summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>
    /// 指定的生产部门码（必须存在于 ProductionDepartment.DeptCode）
    /// </summary>
    public string ProductionDeptCode { get; set; } = string.Empty;

    /// <summary>
    /// 维护原因说明
    /// </summary>
    public string? Reason { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    /// <summary>
    /// 是否当前有效版本
    /// </summary>
    public bool IsCurrent { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
}
