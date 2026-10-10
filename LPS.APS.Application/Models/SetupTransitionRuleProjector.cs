using System.Text.Json;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Application.Models;

/// <summary>
/// Setup 换型规则投影/裁剪纯函数（v1.2 §19.3 ①/②）。无状态、无 IO，供单测钉死：
///   * Project：SetupTransitionRule 实体 → SetupTransitionRuleSnapshot（筛 IsActive，去审计字段）；
///   * CropToDomain：按 Domain（ProductionDepartmentId + StageCode）裁剪，只保留本 Domain 涉及规则。
/// 装载编排在 PeggingOrchestrator.LoadSetupTransitionRulesAsync（取表 → Project → 填第⑦块）；
/// 透传前用 CropToDomain 裁剪。不参与 EXACT/DEFAULT 命中（属 1号位 SetupOptimizer.ResolveSetup）。
/// </summary>
public static class SetupTransitionRuleProjector
{
    /// <summary>筛 IsActive=true 并投影为快照（仅业务键 + 分钟 + 类型，不含审计字段）。</summary>
    public static List<SetupTransitionRuleSnapshot> Project(IEnumerable<SetupTransitionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules
            .Where(r => r.IsActive)
            .Select(r => new SetupTransitionRuleSnapshot
            {
                ProductionDepartmentId = r.ProductionDepartmentId,
                StageCode              = r.StageCode,
                OperationCode          = r.OperationCode,
                ResourceId             = r.ResourceId,
                FromMaterialId         = r.FromMaterialId,
                ToMaterialId           = r.ToMaterialId,
                RuleType               = r.RuleType,
                SetupMinutes           = r.SetupMinutes
            })
            .ToList();
    }

    /// <summary>
    /// 消费端便捷入口（S-8：Project 输入源 = 快照子块，并入反序列化一行到位）：
    /// ContentSnapshotJson 的 SetupTransitionRules 子块 → 消费快照。语义同 ExtractRules（fail-open）：
    /// 子块缺失/为空/JSON 损坏 → 空列表（无规则 DEFAULT 兜底），随后 Project 筛 IsActive。
    /// </summary>
    public static List<SetupTransitionRuleSnapshot> ProjectFromSnapshot(string? contentSnapshotJson)
        => Project(ExtractRules(contentSnapshotJson));

    /// <summary>按 Domain 裁剪：保留 (ProductionDepartmentId, StageCode) 落在本 Domain 物料阶段上下文集合内的规则。</summary>
    public static IReadOnlyList<SetupTransitionRuleSnapshot> CropToDomain(
        IReadOnlyList<SetupTransitionRuleSnapshot> rules,
        IReadOnlyList<MaterialStageDepartmentContextDto> domainContexts)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(domainContexts);

        if (rules.Count == 0 || domainContexts.Count == 0)
            return Array.Empty<SetupTransitionRuleSnapshot>();

        var domainKeys = domainContexts
            .Select(c => (c.ProductionDepartmentId, c.StageCode))
            .ToHashSet();

        return rules
            .Where(r => domainKeys.Contains((r.ProductionDepartmentId, r.StageCode)))
            .ToList();
    }

    // ==================== 治理端子块编解码（重构方案 S-1：Setup 规则承载 → RuleSetVersion.ContentSnapshotJson.SetupTransitionRules） ====================
    // 承载语义：治理端规则（含 Id/IsActive/审计字段）以 JSON 数组存于 RuleSetVersion.ContentSnapshotJson 的
    // "SetupTransitionRules" 子块（与 DemandPriority 子块同轨，零 DDL）。消费端仍由 SetupOptimizer 走
    // SetupTransitionRuleSnapshot 快照（本项目不涉及）。仅增删改查编解码，无状态、无 IO，供单测钉死。

    /// <summary>ContentSnapshotJson 子块键名（契约登记 §6.10.5 扩展，S-7 登记件）</summary>
    public const string SetupTransitionRulesBlockName = "SetupTransitionRules";

    /// <summary>
    /// 从 RuleSetVersion.ContentSnapshotJson 提取 Setup 规则列表（反序列化 "SetupTransitionRules" 子块）。
    /// 子块缺失/为 null/JSON 损坏 → 返回空列表（治理读路径 fail-open，写路径 DRAFT 态重建）。
    /// </summary>
    public static List<SetupTransitionRule> ExtractRules(string? contentSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(contentSnapshotJson))
        {
            return new List<SetupTransitionRule>();
        }

        try
        {
            using var doc = JsonDocument.Parse(contentSnapshotJson);
            if (!doc.RootElement.TryGetProperty(SetupTransitionRulesBlockName, out var block))
            {
                return new List<SetupTransitionRule>();
            }

            var rules = block.Deserialize<List<SetupTransitionRule>>(JsonOptions);
            return rules ?? new List<SetupTransitionRule>();
        }
        catch (JsonException)
        {
            return new List<SetupTransitionRule>();
        }
    }

    /// <summary>
    /// 将 Setup 规则列表写回 RuleSetVersion.ContentSnapshotJson（保留其它子块，如 DemandPriority）。
    /// 返回新 ContentSnapshotJson 字符串；原子替换 "SetupTransitionRules" 子块，不触碰其它键。
    /// </summary>
    public static string SetRules(string? contentSnapshotJson, IReadOnlyList<SetupTransitionRule> rules)
    {
        var blocks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(contentSnapshotJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(contentSnapshotJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, SetupTransitionRulesBlockName, StringComparison.Ordinal))
                    {
                        blocks[prop.Name] = prop.Value.Clone();
                    }
                }
            }
            catch (JsonException)
            {
                // 原快照损坏 → 忽略原内容，仅以 Setup 子块重建（写路径 DRAFT 态可重建）。
            }
        }

        blocks[SetupTransitionRulesBlockName] = JsonSerializer.SerializeToElement(rules, JsonOptions);

        return JsonSerializer.Serialize(blocks, JsonOptions);
    }

    /// <summary>治理 Id 合成：ruleSetVersionId 高位 + 版本内序号低位（long，契约 Id 类型不变，全局唯一）。</summary>
    public static long BuildRuleId(long ruleSetVersionId, int seq) => ruleSetVersionId * 10_000_000L + seq;

    /// <summary>治理 Id 解析：拆回 (ruleSetVersionId, 版本内序号)。</summary>
    public static (long RuleSetVersionId, int Seq) ParseRuleId(long id) => (id / 10_000_000L, (int)(id % 10_000_000L));

    /// <summary>子块序列化选项（属性名原样，不启用缩进——ContentSnapshotJson 单列存储）。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
}
