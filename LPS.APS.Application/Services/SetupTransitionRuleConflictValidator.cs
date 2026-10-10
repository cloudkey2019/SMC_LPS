using LPS.APS.Core.Dto;
using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Application.Services;

/// <summary>
/// 产品转换换型规则（SetupTransitionRule）发布前唯一键冲突校验（阶段 E-4 扩展）
/// 依据：《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》§十：
///   - 明确规则（EXACT）唯一键 = 生产部门 + 大工艺 + 当前工序 + 设备 + 前产品 + 后产品；
///   - 默认规则（DEFAULT）唯一键 = 生产部门 + 大工艺 + 当前工序 + 设备。
/// 仅校验 IsActive=true 的规则；空规则集/无冲突合法（无规则 => 运行时 Setup 兜底 0 分钟，§十八）。
/// 无状态纯校验，返回 ValidationResult；与 <see cref="SolverStrategyValidator"/> 同款。
/// 开发者：3号位
/// </summary>
public sealed class SetupTransitionRuleConflictValidator
{
    /// <summary>校验规则集合内 EXACT/DEFAULT 唯一键无冲突（§十）</summary>
    public ValidationResult Validate(IReadOnlyCollection<SetupTransitionRule> rules)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var active = rules.Where(r => r.IsActive).ToList();

        ValidateExactConflicts(active, errors);
        ValidateDefaultConflicts(active, errors);

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    /// <summary>EXACT 唯一键冲突：同（生产部门+大工艺+工序+设备+前产品+后产品）仅一条有效</summary>
    private static void ValidateExactConflicts(List<SetupTransitionRule> rules, List<string> errors)
    {
        var duplicates = rules
            .Where(r => r.RuleType == SetupTransitionRuleType.Exact)
            .GroupBy(r => (r.ProductionDepartmentId, r.StageCode, r.OperationCode, r.ResourceId, r.FromMaterialId, r.ToMaterialId))
            .Where(g => g.Count() > 1);

        foreach (var g in duplicates)
        {
            errors.Add($"明确换型规则（EXACT）唯一键冲突（§十）：工序[{g.Key.OperationCode}] 设备[{g.Key.ResourceId}] 前产品[{g.Key.FromMaterialId}] 后产品[{g.Key.ToMaterialId}] 存在 {g.Count()} 条有效规则，仅允许一条。");
        }
    }

    /// <summary>DEFAULT 唯一键冲突：同（生产部门+大工艺+工序+设备）仅一条有效</summary>
    private static void ValidateDefaultConflicts(List<SetupTransitionRule> rules, List<string> errors)
    {
        var duplicates = rules
            .Where(r => r.RuleType == SetupTransitionRuleType.Default)
            .GroupBy(r => (r.ProductionDepartmentId, r.StageCode, r.OperationCode, r.ResourceId))
            .Where(g => g.Count() > 1);

        foreach (var g in duplicates)
        {
            errors.Add($"默认换型规则（DEFAULT）唯一键冲突（§十）：工序[{g.Key.OperationCode}] 设备[{g.Key.ResourceId}] 存在 {g.Count()} 条有效规则，仅允许一条。");
        }
    }
}