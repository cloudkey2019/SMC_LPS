using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LPS.APS.BusinessRules.Rules.Pegging;

/// <summary>
/// Pegging 业务规则服务（5号位）— 桩实现
///
/// 职责边界（已按 pegging3.md 修正）：
///   - 只做规则插件判断和裁决建议，不执行扣减，不写库，不生成Task
///   - 主 Pegging 流程（BOM遍历 / 供给扣减 / Task生成 / 落库）均由 2号位 PeggingOrchestrator 负责
///
/// 5号位待实现方法（优先级排序）：
///   1. SelectSupplyCandidatesByRuleAsync — 供给排序规则（PIPELINE→PRODUCTION_INSTRUCTION→NEW_REQUIREMENT）
///   2. ValidateZpBpDocumentMatchAsync    — ZP/BP 出荷指示号匹配校验（P0红线）
///   3. EvaluateCrossFactoryModeAsync     — 跨厂模式裁决（STAGE_HANDOFF vs INTER_FACTORY_ORDER）
///   4. ValidateBusinessRuleResultAsync   — 红线校验（在途优先 / 非Task供给不写Pegging表等）
///   5. EvaluateManualFreezeRuleAsync     — 人工冻结裁决（PMC干预 / 客户承诺）
///   6. BuildPeggingVoucherAsync          — 汇总裁决结果签发凭证
/// </summary>
public class PeggingRuleService : IPeggingRuleService
{
    private readonly ILogger<PeggingRuleService> _logger;

    public PeggingRuleService(ILogger<PeggingRuleService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<CrossFactoryModeDecision> EvaluateCrossFactoryModeAsync(
        string sourceFactoryCode,
        string targetFactoryCode,
        int materialId,
        CancellationToken cancellationToken = default)
    {
        // TODO: 5号位实现
        // 从规则配置表读取：哪些工厂对走 STAGE_HANDOFF，哪些走 INTER_FACTORY_ORDER
        // 规则来源：跨厂规则配置表（待3号位确认表名）
        _logger.LogDebug(
            "[PeggingRuleService] EvaluateCrossFactoryMode（桩）: {Src}→{Tgt}, Material={Mid}",
            sourceFactoryCode, targetFactoryCode, materialId);

        return Task.FromResult(new CrossFactoryModeDecision
        {
            SourceFactoryCode = sourceFactoryCode,
            TargetFactoryCode = targetFactoryCode,
            Mode              = CrossFactoryMode.STAGE_HANDOFF,
            RuleBasis         = "桩实现：默认 STAGE_HANDOFF"
        });
    }

    /// <inheritdoc />
    public Task<List<SupplyCandidate>> SelectSupplyCandidatesByRuleAsync(
        List<SupplyCandidate> rawCandidates,
        long orderId,
        int materialId,
        decimal requiredQuantity,
        string factoryCode,
        CrossFactoryMode? crossFactoryMode,
        CancellationToken cancellationToken = default)
    {
        // TODO: 5号位实现（P0，最高优先级）
        //
        // INTER_FACTORY_ORDER 场景，必须严格按以下顺序排序：
        //   1. PIPELINE（在途）— 已发出未到货，优先耗尽
        //   2. PRODUCTION_INSTRUCTION（ZP/BP Received）— 须先经过 ValidateZpBpDocumentMatchAsync 校验
        //   3. NEW_REQUIREMENT — 仍不足则触发新排产
        //
        // STAGE_HANDOFF 场景：
        //   1. INVENTORY（接收工厂 X库/M库）
        //   2. WIP（接收工厂在制）
        //   3. PIPELINE（上游M库在途）
        //   4. NEW_REQUIREMENT
        //
        // 严禁：跳过 PIPELINE 直接消费 PRODUCTION_INSTRUCTION；ZP/BP 通用消费
        _logger.LogDebug(
            "[PeggingRuleService] SelectSupplyCandidatesByRule（桩）: 候选数={Count}, Mode={Mode}",
            rawCandidates.Count, crossFactoryMode);

        // 桩实现：原样返回，不做排序
        return Task.FromResult(rawCandidates);
    }

    /// <inheritdoc />
    public Task<ZpBpValidationResult> ValidateZpBpDocumentMatchAsync(
        string shippingInstructionNo,
        string documentNo,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        // TODO: 5号位实现（P0红线）
        //
        // 校验逻辑：
        //   1. documentType 必须 = "SHIPPING_INSTRUCTION"
        //   2. documentNo 必须 = shippingInstructionNo（严格匹配，不允许通配）
        //   3. 出荷指示号状态必须未完成（从 ERP_Received_ByDocument_View 查询）
        //
        // 不满足以上任一条件 → IsMatched = false，此候选不得进入供给分配
        _logger.LogDebug(
            "[PeggingRuleService] ValidateZpBpDocumentMatch（桩）: ShippingNo={Sno}, DocNo={Dno}",
            shippingInstructionNo, documentNo);

        var isMatched = documentType == "SHIPPING_INSTRUCTION"
                     && documentNo == shippingInstructionNo;

        return Task.FromResult(new ZpBpValidationResult
        {
            ShippingInstructionNo = shippingInstructionNo,
            DocumentType          = documentType,
            IsMatched             = isMatched,
            MatchedReceivedQty    = 0,
            MismatchReason        = isMatched ? null : "桩实现：DocumentNo或DocumentType不匹配"
        });
    }

    /// <inheritdoc />
    public Task<FreezeDecision> EvaluateManualFreezeRuleAsync(
        long taskId,
        int planVersionId,
        CancellationToken cancellationToken = default)
    {
        // TODO: 5号位实现
        //
        // 注意：此方法只处理人工/业务规则冻结：
        //   - MANUAL_LOCK：PMC 界面手动锁定
        //   - CUSTOMER_COMMITMENT：客户承诺交期锁定
        //   - CONSTRAINT_FIXED：资源约束固定
        //
        // MES_DISPATCHED（系统滑动窗口冻结）由 2号位 UpdateFrozenZoneSnapshotAsync 自动处理，
        // 不走此方法。
        _logger.LogDebug(
            "[PeggingRuleService] EvaluateManualFreezeRule（桩）: TaskId={TaskId}", taskId);

        return Task.FromResult(new FreezeDecision
        {
            IsFrozen    = false,
            Reason      = null,
            Description = "桩实现：默认不冻结"
        });
    }

    /// <inheritdoc />
    public Task<PeggingRuleVoucher> BuildPeggingVoucherAsync(
        int planVersionId,
        long orderId,
        List<SupplyCandidate> finalAllocations,
        CancellationToken cancellationToken = default)
    {
        // TODO: 5号位实现
        // 在 2号位完成所有供给分配计算后调用，5号位做最终裁决并签发 Voucher
        // Voucher 包含：所有规则决策的汇总 + 红线校验通过标记
        _logger.LogDebug(
            "[PeggingRuleService] BuildPeggingVoucher（桩）: PV={Pv}, Order={Oid}, 分配数={Count}",
            planVersionId, orderId, finalAllocations.Count);

        return Task.FromResult(new PeggingRuleVoucher
        {
            PlanVersionId        = planVersionId,
            OrderId              = orderId,
            IsSuccess            = true,
            RankedSupplyCandidates = finalAllocations,
            EvaluatedAt          = DateTime.Now
        });
    }

    /// <inheritdoc />
    public Task<(bool IsValid, List<string> Errors)> ValidateBusinessRuleResultAsync(
        PeggingRuleVoucher voucher,
        CancellationToken cancellationToken = default)
    {
        // TODO: 5号位实现
        //
        // 红线清单（全部不可绕过）：
        //   1. PRODUCTION_INSTRUCTION 候选必须有对应的 ZpBpValidationResult.IsMatched = true
        //   2. 如果有 PIPELINE 候选且未耗尽，不得存在 PRODUCTION_INSTRUCTION 候选排在其前面
        //   3. NEW_REQUIREMENT 必须排在所有现有供给之后
        //   4. INTER_FACTORY_ORDER 供给链不得断层（上游工厂必须可溯源）
        var errors = new List<string>();

        if (!voucher.IsSuccess)
            errors.Add($"Voucher 标记为失败: {voucher.ErrorMessage}");

        return Task.FromResult((errors.Count == 0, errors));
    }
}
