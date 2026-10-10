using System.ComponentModel.DataAnnotations.Schema;

namespace LPS.APS.Core.Entities.APS;

/// <summary>
/// 执行批拆分规则（Batch Policy / 批量策略）主题表
/// 对应 APS_Production.TaskSplitRuleConfig（冻结 DDL v5.1.8.3 §2.14；0号位 2026-10-07 裁决：本轮落码）。
///
/// 两级承接（0号位 2026-09-28 裁决 + 2026-10-07 翻案「不落码→落码」）：
/// 1. 本表（主题表）              —— 3号位 治理 CRUD + 生效区间，直接生效（非版本化 JSON）。
/// 2. FrozenStrategySnapshot.BatchPolicy —— 发布时把 active + 生效区间内规则投影进版本 ContentSnapshotJson.BatchPolicy 子块，
///    供 1号位 Solver 经快照消费（缺策略 fail-closed：BATCH_POLICY_MISSING）。
///
/// 正式匹配粒度（V1）：Material + ProductionDepartment（MaterialId NOT NULL；ProductionDepartmentId 正式业务键须明确，NULL 仅历史兼容，
/// v5.1.10 收口④：NULL 部门历史记录不默认为所有部门的生效规则，治理写路径拒绝新增/更新为 NULL 部门规则）。
/// 兼容字段（ResourceGroupId / MinimumOrderQuantity / EconomicOrderQuantity）仅历史兼容，不参与 V1 正式匹配、不投快照。
/// v5.1.10 收口①②（2026-10-09 生效）：MaxOptimizationSplitCount / MaxBatchCandidates（1号位 Solver 技术预算）与
/// BottleneckSplitStrategy / NonBottleneckStrategy（历史兼容列，V1 主链不得消费拆/合批倾向）不再作为治理业务配置，物理列保留历史。
/// </summary>
[Table("TaskSplitRuleConfig")]
public class TaskSplitRuleConfig
{
    public int Id { get; set; }

    /// <summary>物料（正式业务键，NOT NULL）</summary>
    public int MaterialId { get; set; }

    /// <summary>生产部门（正式业务键须明确；可空仅历史兼容，不默认为所有部门生效）</summary>
    public int? ProductionDepartmentId { get; set; }

    /// <summary>硬最小执行批量</summary>
    public decimal? MinExecutionBatchQty { get; set; }

    /// <summary>硬最大执行批量（NULL = 无硬上限）</summary>
    public decimal? MaxExecutionBatchQty { get; set; }

    /// <summary>软偏好切点批量</summary>
    public decimal? PreferredBatchQty { get; set; }

    /// <summary>是否允许拆分</summary>
    public bool AllowSplit { get; set; } = true;

    /// <summary>是否允许合并</summary>
    public bool AllowMerge { get; set; }

    /// <summary>历史列（v5.1.10 收口①：1号位 Solver 技术预算，版本化安全默认，治理不再维护/投影）</summary>
    public int? MaxOptimizationSplitCount { get; set; }

    /// <summary>历史列（v5.1.10 收口①：1号位 Solver 技术预算，版本化安全默认，治理不再维护/投影）</summary>
    public int? MaxBatchCandidates { get; set; }

    /// <summary>兼容字段（v5.0 已废弃 ResourceGroup，不为 V1 正式粒度）</summary>
    public int? ResourceGroupId { get; set; }

    /// <summary>兼容字段（旧 MOQ）</summary>
    public decimal? MinimumOrderQuantity { get; set; }

    /// <summary>兼容字段（旧 EOQ）</summary>
    public decimal? EconomicOrderQuantity { get; set; }

    /// <summary>历史兼容列（v5.1.10 收口②：V1 主链无论 NULL/非 NULL 均不得消费拆/合批倾向，仅追溯）</summary>
    public string? BottleneckSplitStrategy { get; set; }

    /// <summary>历史兼容列（v5.1.10 收口②：V1 主链无论 NULL/非 NULL 均不得消费拆/合批倾向，仅追溯）</summary>
    public string? NonBottleneckStrategy { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}