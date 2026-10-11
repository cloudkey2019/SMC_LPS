using System.Text.Json;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;
using LPS.APS.Core.Exceptions;

namespace LPS.APS.Application.Models;

/// <summary>
/// Batch Policy（批量策略）投影/编解码纯函数（0号位 2026-10-07 裁决：本轮落码）。无状态、无 IO，供单测钉死：
///   * Project：TaskSplitRuleConfig 实体 → BatchPolicyRuleSnapshot（筛 IsActive + 生效区间，去审计/兼容字段）；
///   * SetRules / ExtractRules：ContentSnapshotJson.BatchPolicy 子块编解码（与 SetupTransitionRules 同轨，零 DDL）。
/// 发布编排在 GovernanceVersionService.PublishParameterSetVersionAsync（读表 active 规则 → Project → 注入第⑧块源）；
/// 装载在 FrozenStrategySnapshotProvider.DeserializeBatchPolicy（ContentSnapshotJson.BatchPolicy 子块 → 第⑧块）。
/// 装载 B-010 三态（3-NEW-P0-04，0号位 再审核 2026-10-10）：缺子块/null/显式空配置 → 空列表（合法未配置）；
/// JSON 损坏 → 显式失败（抛 SetupRuleDataRedLineException，⑧ 为非必填块但损坏≠缺配）；
/// 匹配终端（某 Material+Dept 经 ①②③ 仍无命中）按冻结 §十五 ④ fail-closed，由 1号位 消费侧在 Strategy Snapshot 校验阶段执行，装载端不承载信号位。
/// 开发者：3号位
/// </summary>
public static class TaskSplitRuleConfigProjector
{
    /// <summary>ContentSnapshotJson 子块键名（契约登记 §6.10.5 扩展）。</summary>
    public const string BatchPolicyBlockName = "BatchPolicy";

    /// <summary>
    /// 把 active + 生效区间内规则投影为快照（去审计字段 Id/Created/Updated/IsActive + 兼容字段，仅业务键 + 参数）。
    /// 生效区间以 asOf 判定（发布时传入，默认当前本地时间，R03 与实现 DateTime.Now 对齐），采用**半开区间 [EffectiveFrom, EffectiveTo)**——
    /// P0-04（0号位 审核 2026-10-09）：与治理写路径有效期排重（严格 &lt; 判定相切不重叠）保持一致——
    /// 相切窗口 [10:00,11:00) + [11:00,12:00) 同一瞬间至多一条有效，杜绝投影重复键；
    /// IsActive=false、超出生效窗或 ProductionDepartmentId 为 NULL 的行排除
    /// （v5.1.10 收口④：NULL 部门历史记录不默认为所有部门的生效规则，仅显式部门规则入快照）。
    /// v5.1.10 收口①（2026-10-09 生效）：不再投 MaxOptimizationSplitCount/MaxBatchCandidates（1号位 Solver 技术预算）
    /// 与 BottleneckSplitStrategy/NonBottleneckStrategy（历史兼容列，V1 主链不得消费拆/合批倾向）——
    /// 三个字段恒 null，仅保留物理列历史；BatchPolicyRuleSnapshot 类上仍保留字段以兼容旧快照反序列化与 1号位 消费侧类型。
    /// </summary>
    public static List<BatchPolicyRuleSnapshot> Project(IEnumerable<TaskSplitRuleConfig> rules, DateTime? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var now = asOf ?? DateTime.Now;

        return rules
            .Where(r => r.IsActive
                && r.ProductionDepartmentId.HasValue
                && (r.EffectiveFrom is null || r.EffectiveFrom <= now)
                && (r.EffectiveTo is null || r.EffectiveTo > now))
            .Select(r => new BatchPolicyRuleSnapshot
            {
                MaterialId = r.MaterialId,
                ProductionDepartmentId = r.ProductionDepartmentId,
                MinExecutionBatchQty = r.MinExecutionBatchQty,
                MaxExecutionBatchQty = r.MaxExecutionBatchQty,
                PreferredBatchQty = r.PreferredBatchQty,
                AllowSplit = r.AllowSplit,
                AllowMerge = r.AllowMerge,
                // v5.1.10 收口①：MaxOptimizationSplitCount / MaxBatchCandidates / BottleneckSplitStrategy / NonBottleneckStrategy 恒 null，不投快照。
            })
            .ToList();
    }

    /// <summary>装载端便捷入口：ContentSnapshotJson.BatchPolicy 子块 → 第⑧块快照（fail-open：缺失/为空/损坏 → 空列表，⑧ 为非必填块）。</summary>
    public static List<BatchPolicyRuleSnapshot> ProjectFromSnapshot(string? contentSnapshotJson)
        => ExtractRules(contentSnapshotJson);

    /// <summary>
    /// R02（0号位 再审核反证 2026-10-10）：找出 (MaterialId, ProductionDepartmentId) 分组内生效窗口重叠的规则 Id
    ///（组内两两判定，O(n²)，组内行数少）。与治理写路径唯一有效期（EnsureUniqueEffectiveWindowAsync）同口径：
    /// 半开区间 [EffectiveFrom, EffectiveTo)、NULL 无界、严格 &lt; 相切不重叠。纯函数、零 DB——发布前对存量行全量判定。
    /// </summary>
    public static IReadOnlyList<int> FindOverlappingWindowIds(IEnumerable<TaskSplitRuleConfig> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var rows = rules.ToList();
        var overlapIds = new HashSet<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            for (var j = i + 1; j < rows.Count; j++)
            {
                var a = rows[i];
                var b = rows[j];
                if (a.MaterialId != b.MaterialId || a.ProductionDepartmentId != b.ProductionDepartmentId)
                {
                    continue;
                }

                if (!Overlaps(a, b))
                {
                    continue;
                }

                overlapIds.Add(a.Id);
                overlapIds.Add(b.Id);
            }
        }

        return overlapIds.ToList();
    }

    /// <summary>半开区间重叠判定（[EffectiveFrom, EffectiveTo)，NULL 无界；严格 &lt; 相切不重叠——与 EnsureUniqueEffectiveWindowAsync SQL 口径一致）。</summary>
    private static bool Overlaps(TaskSplitRuleConfig a, TaskSplitRuleConfig b)
        => (a.EffectiveFrom is null || b.EffectiveTo is null || a.EffectiveFrom < b.EffectiveTo)
           && (b.EffectiveFrom is null || a.EffectiveTo is null || b.EffectiveFrom < a.EffectiveTo);

    /// <summary>
    /// 提取 BatchPolicy 子块（反序列化 BatchPolicyRuleSnapshot 列表）。
    /// B-010 三态（3-NEW-P0-04，0号位 再审核 2026-10-10）：缺子块/null/显式空配置 → 空列表（合法未配置）；
    /// JSON 结构损坏 → 抛 <see cref="Exceptions.SetupRuleDataRedLineException"/>（显式失败，不再静默空列表）。
    /// 冻结 §十五 fail-closed 属匹配终端 ④（某 Material+Dept 经 ①②③ 仍无命中 → Strategy Snapshot 校验失败），
    /// 由 1号位 消费侧执行，本装载端不承载信号位。
    /// </summary>
    public static List<BatchPolicyRuleSnapshot> ExtractRules(string? contentSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(contentSnapshotJson))
        {
            // B-010 三态①：子块缺失 / null / 空串 → 该版本未配置批量策略（合法，非装载失败）。
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(contentSnapshotJson);
            if (!doc.RootElement.TryGetProperty(BatchPolicyBlockName, out var block)
                || block.ValueKind == JsonValueKind.Null)
            {
                // B-010 三态①：子块缺失或为 null → 该版本未配置批量策略（合法，非装载失败）。
                return [];
            }

            // B-010 三态②：显式空配置 "BatchPolicy":[] → 空列表（明确空配置，合法）。
            return block.Deserialize<List<BatchPolicyRuleSnapshot>>(JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            // B-010 三态③（3-NEW-P0-04）：JSON 结构损坏 → 显式失败。「完全无匹配」≠「已有配置但损坏」；
            // 损坏子块不再被误当「缺配兜底」消费，版本追溯不失真（调用方 422 数据红线）。
            throw new SetupRuleDataRedLineException(
                $"ContentSnapshotJson.BatchPolicy 子块 JSON 损坏，无法装载批量策略快照：{ex.Message}", ex);
        }
    }

    /// <summary>将 BatchPolicy 规则列表写回 ContentSnapshotJson（保留其它子块，原子替换 BatchPolicy 键）。</summary>
    public static string SetRules(string? contentSnapshotJson, IReadOnlyList<BatchPolicyRuleSnapshot> rules)
    {
        var blocks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(contentSnapshotJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(contentSnapshotJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, BatchPolicyBlockName, StringComparison.Ordinal))
                    {
                        blocks[prop.Name] = prop.Value.Clone();
                    }
                }
            }
            catch (JsonException)
            {
                // 原快照损坏 → 忽略原内容，仅以 BatchPolicy 子块重建（与 Setup 规则写路径同语义）。
            }
        }

        blocks[BatchPolicyBlockName] = JsonSerializer.SerializeToElement(rules, JsonOptions);

        return JsonSerializer.Serialize(blocks, JsonOptions);
    }

    /// <summary>子块序列化选项（属性名原样，不启用缩进——ContentSnapshotJson 单列存储）。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
}