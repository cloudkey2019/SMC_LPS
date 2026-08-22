using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// Candidate Guardrail 发布前校验服务（阶段 E-4）
/// 纯内容校验（不依赖物理落点），供 P0-02 裁决后接入参数集发布路径；
/// 校验对象为 FrozenStrategySnapshot.CandidateGuardrailBlock（契约 v0.2 §二-⑤）。
/// 红线校验（DTO 注释 / 冻结约束）：
/// 1. Normal / Soft / LocalHard Ms 必须为正（"Guardrail 为正"）；
/// 2. 时序关系 NormalMs &lt;= SoftMs &lt;= LocalHardMs（默认 60/90/180；软超时不得早于正常、硬超时不得早于软超时）；
/// 3. ImpactedTaskWarningPercent 0~100；
/// 4. Repair/Propagation/ResourceTopN/SplitAlternatives 非负。
/// 与 <see cref="DemandPriorityValidator"/> 同款：无状态纯校验，Validate 返回 ValidationResult。
/// 开发者：3号位
/// </summary>
public sealed class CandidateGuardrailValidator
{
    /// <summary>验证 CandidateGuardrailBlock 配置的合法性</summary>
    public ValidationResult Validate(CandidateGuardrailBlock block)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateThresholdMs(block, errors);
        ValidateWarningPercent(block, errors);
        ValidateCounts(block, errors);

        return new ValidationResult(errors.Count == 0, errors, warnings);
    }

    /// <summary>超时阈值：各 Ms 必须为正，且 Normal &lt;= Soft &lt;= LocalHard（60/90/180 时序）</summary>
    private static void ValidateThresholdMs(CandidateGuardrailBlock block, List<string> errors)
    {
        if (block.NormalMs <= 0)
        {
            errors.Add($"CandidateGuardrail.NormalMs 必须为正（当前：{block.NormalMs}）");
        }

        if (block.SoftMs <= 0)
        {
            errors.Add($"CandidateGuardrail.SoftMs 必须为正（当前：{block.SoftMs}）");
        }

        if (block.LocalHardMs <= 0)
        {
            errors.Add($"CandidateGuardrail.LocalHardMs 必须为正（当前：{block.LocalHardMs}）");
        }

        if (block.NormalMs > block.SoftMs)
        {
            errors.Add($"CandidateGuardrail.NormalMs 必须小于等于 SoftMs（当前 NormalMs={block.NormalMs} &gt; SoftMs={block.SoftMs}）");
        }

        if (block.SoftMs > block.LocalHardMs)
        {
            errors.Add($"CandidateGuardrail.SoftMs 必须小于等于 LocalHardMs（当前 SoftMs={block.SoftMs} &gt; LocalHardMs={block.LocalHardMs}）");
        }
    }

    /// <summary>受影响 Task 警戒百分比：0~100</summary>
    private static void ValidateWarningPercent(CandidateGuardrailBlock block, List<string> errors)
    {
        if (block.ImpactedTaskWarningPercent is < 0 or > 100)
        {
            errors.Add($"CandidateGuardrail.ImpactedTaskWarningPercent 必须在 0~100 之间（当前：{block.ImpactedTaskWarningPercent}）");
        }
    }

    /// <summary>Repair / Propagation / ResourceTopN / SplitAlternatives 非负</summary>
    private static void ValidateCounts(CandidateGuardrailBlock block, List<string> errors)
    {
        if (block.MaxRepairAttempts < 0)
        {
            errors.Add($"CandidateGuardrail.MaxRepairAttempts 不能为负（当前：{block.MaxRepairAttempts}）");
        }

        if (block.MaxPropagationRounds < 0)
        {
            errors.Add($"CandidateGuardrail.MaxPropagationRounds 不能为负（当前：{block.MaxPropagationRounds}）");
        }

        if (block.ResourceTopN < 0)
        {
            errors.Add($"CandidateGuardrail.ResourceTopN 不能为负（当前：{block.ResourceTopN}）");
        }

        if (block.SplitAlternatives < 0)
        {
            errors.Add($"CandidateGuardrail.SplitAlternatives 不能为负（当前：{block.SplitAlternatives}）");
        }
    }
}
