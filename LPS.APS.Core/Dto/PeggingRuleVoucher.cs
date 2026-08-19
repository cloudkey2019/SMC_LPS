using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Dto;

/// <summary>
/// 5号位规则裁决凭证（PeggingRuleVoucher）
///
/// 5号位只做规则插件判断，不执行扣减、不生成Task、不写库。
/// 所有裁决结果打包进此 Voucher 返回给 2号位，由 2号位决定如何执行。
/// </summary>
public class PeggingRuleVoucher
{
    public string VoucherId { get; set; } = Guid.NewGuid().ToString();
    public int PlanVersionId { get; set; }
    public long OrderId { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DateTime EvaluatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 跨厂模式裁决结果（STAGE_HANDOFF 或 INTER_FACTORY_ORDER）
    /// </summary>
    public CrossFactoryModeDecision? CrossFactoryDecision { get; set; }

    /// <summary>
    /// 规则排序后的供给候选列表（只是建议顺序，2号位决定最终扣减量）
    /// </summary>
    public List<SupplyCandidate> RankedSupplyCandidates { get; set; } = new();

    /// <summary>
    /// ZP/BP 出荷指示号匹配结果
    /// </summary>
    public ZpBpValidationResult? ZpBpValidation { get; set; }

    /// <summary>
    /// 冻结裁决结果
    /// </summary>
    public FreezeDecision? FreezeDecision { get; set; }

    /// <summary>
    /// 业务规则红线校验结果
    /// </summary>
    public List<string> BusinessRuleErrors { get; set; } = new();
    public bool PassedBusinessRules => BusinessRuleErrors.Count == 0;
}

/// <summary>
/// 跨厂模式裁决结果
/// </summary>
public class CrossFactoryModeDecision
{
    /// <summary>
    /// 来源工厂代码
    /// </summary>
    public string SourceFactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 目标工厂代码
    /// </summary>
    public string TargetFactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 裁决的跨厂模式
    /// </summary>
    public CrossFactoryMode Mode { get; set; }

    /// <summary>
    /// 裁决依据（规则名称或说明）
    /// </summary>
    public string RuleBasis { get; set; } = string.Empty;
}

/// <summary>
/// 供给候选项（5号位规则排序后的建议，不含实际扣减量）
/// </summary>
public class SupplyCandidate
{
    /// <summary>
    /// 候选序号（越小优先级越高）
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// 供应来源类型
    /// </summary>
    public SupplySourceType SourceType { get; set; }

    /// <summary>
    /// 供应来源ID（仓库批次号 / WIP单号 / 在途单号等）
    /// </summary>
    public string SourceReference { get; set; } = string.Empty;

    /// <summary>
    /// 工厂代码
    /// </summary>
    public string FactoryCode { get; set; } = string.Empty;

    /// <summary>
    /// 可用量（2号位参考，实际扣减以扣减执行时为准）
    /// </summary>
    public decimal AvailableQuantity { get; set; }

    /// <summary>
    /// 供应可用时间
    /// </summary>
    public DateTime AvailableAt { get; set; }

    /// <summary>
    /// 适用的出荷指示号（PRODUCTION_INSTRUCTION 类型专用）
    /// </summary>
    public string? ShippingInstructionNo { get; set; }
}

/// <summary>
/// ZP/BP 出荷指示号匹配校验结果
/// </summary>
public class ZpBpValidationResult
{
    /// <summary>
    /// 出荷指示号
    /// </summary>
    public string ShippingInstructionNo { get; set; } = string.Empty;

    /// <summary>
    /// 文件类型（SHIPPING_INSTRUCTION）
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// 是否匹配（满足：DocumentNo = 当前出荷指示号 且 未完成）
    /// </summary>
    public bool IsMatched { get; set; }

    /// <summary>
    /// 可用的 Received 数量（仅 IsMatched = true 时有效）
    /// </summary>
    public decimal MatchedReceivedQty { get; set; }

    /// <summary>
    /// 不匹配原因
    /// </summary>
    public string? MismatchReason { get; set; }
}

/// <summary>
/// 冻结裁决结果
/// </summary>
public class FreezeDecision
{
    /// <summary>
    /// 是否冻结
    /// </summary>
    public bool IsFrozen { get; set; }

    /// <summary>
    /// 冻结原因
    /// </summary>
    public FrozenReasonType? Reason { get; set; }

    /// <summary>
    /// 冻结说明
    /// </summary>
    public string? Description { get; set; }
}
