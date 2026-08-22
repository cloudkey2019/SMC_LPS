using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// Solver Strategy 发布前校验服务（阶段 E-4）
/// 纯内容校验（不依赖物理落点），供 P0-02 裁决后接入参数集发布路径；
/// 校验对象为 FrozenStrategySnapshot.SolverStrategyBlock（契约 v0.2 §二-⑤）。
/// 红线校验：
/// 1. On-time Target 必须在 0~100（DTO 注释"0~100（发布前校验）"）；
/// 2. Split / Setup / StageOverlap 数值域合法（不允许负拆分、非正 Setup 时长的"隐形无效配置"）；
/// 3. SolverStrategyMode 枚举合法（防御数字越界反序列化场景）。
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
        ValidateSetup(block, errors);
        ValidateStageOverlap(block, errors);

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    /// <summary>SolverStrategyMode 枚举合法性（Forward/Backward/Mixed；防御数字越界反序列化）</summary>
    private static void ValidateMode(SolverStrategyBlock block, List<string> errors)
    {
        if (!Enum.IsDefined(block.Mode))
        {
            errors.Add($"SolverStrategyMode 必须为 Forward/Backward/Mixed 之一（当前值：{(int)block.Mode}）");
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

        if (block.Split.MinBatchQty < 0)
        {
            errors.Add($"Split.MinBatchQty 不能为负（当前：{block.Split.MinBatchQty}）");
        }
    }

    /// <summary>Setup：换型时长必须为正、LookAhead 非负（默认 30 / 5，DTO 注释）</summary>
    private static void ValidateSetup(SolverStrategyBlock block, List<string> errors)
    {
        if (block.Setup.DefaultSetupMinutes <= 0)
        {
            errors.Add($"Setup.DefaultSetupMinutes 必须为正（当前：{block.Setup.DefaultSetupMinutes}）");
        }

        if (block.Setup.SetupLookAheadSize < 0)
        {
            errors.Add($"Setup.SetupLookAheadSize 不能为负（当前：{block.Setup.SetupLookAheadSize}）");
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
}
