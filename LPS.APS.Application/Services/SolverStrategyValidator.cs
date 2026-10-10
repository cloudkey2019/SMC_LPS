using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// Solver Strategy 发布前校验服务（阶段 E-4）
/// 纯内容校验（不依赖物理落点），供 P0-02 裁决后接入参数集发布路径；
/// 校验对象为 FrozenStrategySnapshot.SolverStrategyBlock（契约 v0.2 §二-⑤）。
/// 红线校验：
/// 1. On-time Target 必须在 0~100（DTO 注释"0~100（发布前校验）"）；
/// 2. Split / StageOverlap 数值域合法（不允许负拆分等"隐形无效配置"）；
/// 3. SolverStrategyMode 枚举合法（防御数字越界反序列化场景）。
/// 注：换型（Setup）校验已迁至 <see cref="SetupTransitionRuleConflictValidator"/>（§十 产品转换规则唯一键冲突）；
///     SolverStrategyBlock 不再承载换型规则校验。
/// 与 <see cref="DemandPriorityValidator"/> 同款：无状态纯校验，Validate 返回 ValidationResult。
/// 开发者：3号位
/// </summary>
public sealed class SolverStrategyValidator
{
    /// <summary>验证 SolverStrategyBlock 配置的合法性</summary>
    public ValidationResult Validate(SolverStrategyBlock block)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateMode(block, errors);
        ValidateOnTimeTarget(block, errors);
        ValidateSplit(block, errors);
        ValidateStageOverlap(block, errors);
        ValidateBottleneckUtilizationThresholds(block, errors);
        ValidateSetupBudget(block, errors);

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    /// <summary>SolverStrategyMode 枚举合法性（Auto/Forward/Backward/Mixed；防御数字越界反序列化）</summary>
    private static void ValidateMode(SolverStrategyBlock block, List<string> errors)
    {
        if (!Enum.IsDefined(block.Mode))
        {
            errors.Add($"SolverStrategyMode 必须为 Auto/Forward/Backward/Mixed 之一（当前值：{(int)block.Mode}）");
        }
    }

    /// <summary>On-time Target：0~100（DTO 注释"0~100（发布前校验）"）</summary>
    private static void ValidateOnTimeTarget(SolverStrategyBlock block, List<string> errors)
    {
        if (block.OnTimeTarget.TargetPercent is < 0 or > 100)
        {
            errors.Add($"OnTimeTarget.TargetPercent 必须在 0~100 之间（当前：{block.OnTimeTarget.TargetPercent}）");
        }
    }

    /// <summary>Split：拆分次数 / 最小批量非负（不允许负拆分，清单 31）</summary>
    private static void ValidateSplit(SolverStrategyBlock block, List<string> errors)
    {
        if (block.Split.MaxOptimizationSplitCount < 0)
        {
            errors.Add($"Split.MaxOptimizationSplitCount 不能为负（当前：{block.Split.MaxOptimizationSplitCount}）");
        }

        if (block.Split.MinBatchQty <= 0)
        {
            errors.Add($"Split.MinBatchQty 必须为正（当前：{block.Split.MinBatchQty}）");
        }
    }

    /// <summary>StageOverlap：Transfer/Threshold 数量非负、ThresholdPercent 0~100</summary>
    private static void ValidateStageOverlap(SolverStrategyBlock block, List<string> errors)
    {
        if (block.StageOverlap.TransferBatchQty < 0)
        {
            errors.Add($"StageOverlap.TransferBatchQty 不能为负（当前：{block.StageOverlap.TransferBatchQty}）");
        }

        if (block.StageOverlap.ThresholdQty < 0)
        {
            errors.Add($"StageOverlap.ThresholdQty 不能为负（当前：{block.StageOverlap.ThresholdQty}）");
        }

        if (block.StageOverlap.ThresholdPercent is < 0 or > 100)
        {
            errors.Add($"StageOverlap.ThresholdPercent 必须在 0~100 之间（当前：{block.StageOverlap.ThresholdPercent}）");
        }
    }

    /// <summary>瓶颈利用率/产能短缺阈值：必须是 (0,1) 开区间比例（默认为 0.85 / 0.90，非 0~100 百分比）</summary>
    private static void ValidateBottleneckUtilizationThresholds(SolverStrategyBlock block, List<string> errors)
    {
        if (block.BottleneckUtilizationThreshold is <= 0 or > 1)
        {
            errors.Add($"BottleneckUtilizationThreshold 必须是 (0,1] 比例（当前：{block.BottleneckUtilizationThreshold}）");
        }

        if (block.CapacityShortageUtilizationThreshold is <= 0 or > 1)
        {
            errors.Add($"CapacityShortageUtilizationThreshold 必须是 (0,1] 比例（当前：{block.CapacityShortageUtilizationThreshold}）");
        }
    }

    /// <summary>三预算参数（1号位 2026-09-20 提值）：有界搜索预算 [100,5000]、最大邻域尝试次数 [10,500]，均为正整数（次数量纲）</summary>
    private static void ValidateSetupBudget(SolverStrategyBlock block, List<string> errors)
    {
        if (block.Setup.SetupSearchBudget is < 100 or > 5000)
        {
            errors.Add($"Setup.SetupSearchBudget 必须在 [100, 5000] 之间（当前：{block.Setup.SetupSearchBudget}）");
        }

        if (block.Setup.SetupMaxNeighborhoodTries is < 10 or > 500)
        {
            errors.Add($"Setup.SetupMaxNeighborhoodTries 必须在 [10, 500] 之间（当前：{block.Setup.SetupMaxNeighborhoodTries}）");
        }
    }
}
