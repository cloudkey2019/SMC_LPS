namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 阶段提前期参数（StageLeadTimeParam）治理投影——供 4号位 配置页面列表 / 维护展示。
/// 载体「直接生效（时间窗）」：PM 2026-09-28 终裁归 3号位 规则参数体系维护，不进 FrozenStrategySnapshot。
/// 只读投影；维护走 <see cref="SaveStageLeadTimeParamRequest"/>（新增/编辑共用，PUT 整对象替换）。
/// 治理侧一律写非空 LeadTime / EffectiveFrom（DDL 可空为宽松非强制，见契约件 §漂移说明）。
/// </summary>
public sealed class StageLeadTimeParamDto
{
    public int Id { get; init; }
    public string FactoryCode { get; init; } = string.Empty;
    public string StageCode { get; init; } = string.Empty;
    public string? ProductionDeptCode { get; init; }
    public string? MaterialCode { get; init; }
    public string? ProductFamilyCode { get; init; }
    public decimal LeadTimeDays { get; init; }
    public decimal LeadTimeHours { get; init; }
    public int Priority { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 新增/编辑阶段提前期参数请求（治理侧直维护；PUT 整对象替换，不改 IsActive）。
/// 粒度主键 = 工厂 + StageCode + 生产部门；物料/产品族为旧层，仅历史兼容（不参与 V1 正常匹配）。
/// </summary>
public sealed class SaveStageLeadTimeParamRequest
{
    public string FactoryCode { get; init; } = string.Empty;
    public string StageCode { get; init; } = string.Empty;
    public string? ProductionDeptCode { get; init; }
    public string? MaterialCode { get; init; }
    public string? ProductFamilyCode { get; init; }
    public decimal LeadTimeDays { get; init; }
    public decimal LeadTimeHours { get; init; }
    public int Priority { get; init; }
    public DateTime EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public bool IsDefault { get; init; }
}