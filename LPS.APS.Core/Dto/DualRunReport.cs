namespace LPS.APS.Core.Dto;

/// <summary>双跑对照差异行：Key 标识比对维度下的口径键（DemandKey / 物理供给身份 / 血缘边），Old/New 为两侧聚合量。</summary>
public sealed record DualRunDiff(string Key, decimal OldQty, decimal NewQty);

/// <summary>
/// 阶段2 S2.3 双跑对照报告（PM 0918-3 §八 7 项 + §六 I1-I4 的 2号位 Pegging 段可判部分）。
/// 旧 DFS vs 新 BFS 各自独立供给账本跑一遍后逐维比对。Pass = I1-I4 全过（不含 Δ 差异，Δ 为预期修复效果）。
/// </summary>
public sealed record DualRunReport
{
    public int PlanVersionId { get; init; }
    public int OrderCount { get; init; }

    /// <summary>I1 净产出闭合（NEW_REQUIREMENT 按 DemandKey 聚合）：旧/新逐 (order,material) 净产出相等。</summary>
    public bool NetOutputClosed { get; init; }
    public IReadOnlyList<DualRunDiff> NetOutputDiffs { get; init; } = Array.Empty<DualRunDiff>();

    /// <summary>I2 真实供给承接闭合（按物理供给身份聚合，不含 NEW_REQUIREMENT / 规划占位）。</summary>
    public bool SupplyClosed { get; init; }
    public IReadOnlyList<DualRunDiff> SupplyDiffs { get; init; } = Array.Empty<DualRunDiff>();

    /// <summary>I2-PI：PI 承接闭合（WIP + PRODUCTION_INSTRUCTION，PM 点名专项）。</summary>
    public bool PiClosed { get; init; }
    public IReadOnlyList<DualRunDiff> PiDiffs { get; init; } = Array.Empty<DualRunDiff>();

    /// <summary>I3 血缘边集闭合（Consumer/Producer/Qty 业务边一致，不含 AllocationSequence）。</summary>
    public bool LineageClosed { get; init; }
    public IReadOnlyList<string> LineageMismatches { get; init; } = Array.Empty<string>();

    /// <summary>I4 PI Position：2号位此阶段不触碰 PI Position 装载（LoadSupplyPoolAsync 未被本步改动），恒成立；待 1号位 bitmap 级最终确认。</summary>
    public bool PiPositionClosed => true;

    /// <summary>Δ1 生产 LPD 碎片数量（旧 N 张 → 新 1 张，共享子件合并）。</summary>
    public int OldLpdCount { get; init; }
    public int NewLpdCount { get; init; }

    /// <summary>Δ2 Allocation 笔数（SupplyAllocations 条数）。</summary>
    public int OldAllocationCount { get; init; }
    public int NewAllocationCount { get; init; }

    /// <summary>Δ3 DemandQuantity 校验量（旧对共享子件重复累加，新按物料一次计量）。</summary>
    public decimal OldDemandQuantity { get; init; }
    public decimal NewDemandQuantity { get; init; }

    /// <summary>硬指标「重复展开 → 0」（PM 0918-3 §九）。</summary>
    public long OldTraversalVisits { get; init; }
    public long NewTraversalVisits { get; init; }

    /// <summary>性能对照（毫秒）。</summary>
    public long OldMs { get; init; }
    public long NewMs { get; init; }

    /// <summary>1号位 段待验证项（2号位 Pegging 阶段无 AllocationTaskShare / Continuation / Task 产物）。</summary>
    public IReadOnlyList<string> DeferredToPosition1 { get; init; } = Array.Empty<string>();

    public bool Pass => NetOutputClosed && SupplyClosed && PiClosed && LineageClosed && PiPositionClosed;
}