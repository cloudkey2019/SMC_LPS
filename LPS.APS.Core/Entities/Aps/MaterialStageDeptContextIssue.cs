namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 物料阶段部门上下文问题表（v5.0.16新增）
/// 对应 APS_Production.MaterialStageDeptContext_Issues
///
/// 业务定位：sp_RebuildMaterialStageDeptContext 重建时遇到无法自动拍板的情况，登记到此表
/// 降级哲学：旧值不动（IsCurrent=1 上一版本继续供 1号位使用），新问题登记到本表
///          人工修正 Override 后触发局部重建 → 新版本上线
///
/// 典型 IssueType：
/// - MULTI_DEPT_CONFLICT_FOR_STAGE  : MSC 同物料同阶段对应多部门，无法自动拍板
/// - MISSING_DEPT_IN_MSC            : MSC 该物料该仓库无 DefaultProductionDept
/// - DEPT_NOT_IN_DICT               : MSC 部门码在 ProductionDepartment 字典中找不到
/// - STAGE_NOT_IN_DICT              : 推导出的 StageCode 在 StageDict 中找不到
/// - MTS_INCONSISTENT               : MTS 中部门与 MSC/Override 结果不一致（一致性校验降级）
/// - OVERRIDE_MODEL_AMBIGUOUS       : Override 维护时 Model 1:N 多个 MaterialCode（导入拒收）
///
/// Severity：INFO/WARN/ERROR（与 BOM_Workset_Issues 风格一致）
/// </summary>
public class MaterialStageDeptContextIssue
{
    public long Id { get; set; }

    /// <summary>
    /// 触发本次重建的批次
    /// </summary>
    public string BatchNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联物料（部分场景如 OVERRIDE_MODEL_AMBIGUOUS 仅有 Model）
    /// </summary>
    public string? MaterialCode { get; set; }

    public string? Model { get; set; }
    public string? StageCode { get; set; }

    /// <summary>
    /// 问题类型（见类注释）
    /// </summary>
    public string IssueType { get; set; } = string.Empty;

    /// <summary>
    /// 严重程度：INFO / WARN / ERROR
    /// </summary>
    public string Severity { get; set; } = "WARN";

    /// <summary>
    /// 明细描述（含冲突部门列表/Model 多解列表等）
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// 降级动作（如"沿用旧值" / "跳过此条" / "拒收 Override"）
    /// </summary>
    public string? DegradeAction { get; set; }

    /// <summary>
    /// 审核状态：PENDING / CONFIRMED / IGNORED / FIXED
    /// </summary>
    public string ReviewStatus { get; set; } = "PENDING";

    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
