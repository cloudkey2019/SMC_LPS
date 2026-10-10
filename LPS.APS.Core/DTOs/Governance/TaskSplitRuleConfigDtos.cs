namespace LPS.APS.Core.DTOs.Governance;

/// <summary>
/// 执行批拆分规则（Batch Policy / 批量策略）治理投影——供 4号位 配置页面列表 / 维护展示。
/// 0号位 2026-10-07 裁决「本轮落码」：3号位 治理 CRUD，1号位 经 FrozenStrategySnapshot.BatchPolicy 消费。
/// 正式匹配粒度 = Material + ProductionDepartment（v5.1.10 收口：生效规则须明确部门，NULL 部门历史记录不默认为所有部门生效）。
/// 兼容字段（ResourceGroupId / MinimumOrderQuantity / EconomicOrderQuantity）仅历史兼容，不参与 V1 正式匹配、不投快照。
/// v5.1.10 收口（2026-10-09 生效）：治理投影不含 MaxOptimizationSplitCount / MaxBatchCandidates（1号位 Solver 技术预算）
/// 与 BottleneckSplitStrategy / NonBottleneckStrategy（历史兼容列，V1 主链不得消费拆/合批倾向）——物理列保留历史，治理 API 不再暴露。
/// </summary>
public sealed class TaskSplitRuleConfigDto
{
    public int Id { get; init; }
    public int MaterialId { get; init; }
    public int? ProductionDepartmentId { get; init; }
    public decimal? MinExecutionBatchQty { get; init; }
    public decimal? MaxExecutionBatchQty { get; init; }
    public decimal? PreferredBatchQty { get; init; }
    public bool AllowSplit { get; init; }
    public bool AllowMerge { get; init; }
    public bool IsActive { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 新增/编辑执行批拆分规则请求（治理侧直维护；PUT 整对象替换，不改 IsActive）。
/// 粒度主键 = MaterialId + ProductionDepartmentId。
/// v5.1.10 收口（2026-10-09 生效）：ProductionDepartmentId 为正式业务键，NULL 部门仅历史兼容（生效校验拒绝新增 NULL 部门规则）；
/// PreferredBatchQty 业务生效必填且 &gt;0、处于硬 Min/Max 合法范围内（后端发布校验，物理列仍 Nullable）。
/// 不再接收 MaxOptimizationSplitCount / MaxBatchCandidates / BottleneckSplitStrategy / NonBottleneckStrategy。
/// </summary>
public sealed class SaveTaskSplitRuleConfigRequest
{
    public int MaterialId { get; init; }
    public int? ProductionDepartmentId { get; init; }
    public decimal? MinExecutionBatchQty { get; init; }
    public decimal? MaxExecutionBatchQty { get; init; }
    public decimal? PreferredBatchQty { get; init; }
    public bool? AllowSplit { get; init; }
    public bool? AllowMerge { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    public DateTime? EffectiveTo { get; init; }
}