using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;

namespace LPS.APS.Core.Interfaces;

/// <summary>
/// Pegging 业务规则服务接口（5号位）
///
/// 职责边界：
///   - 只做规则插件判断和裁决建议，返回 PeggingRuleVoucher
///   - 不执行库存扣减，不生成 Task，不写任何数据库
///   - 不是 Pegging 主引擎入口（主流程由 2号位 PeggingOrchestrator 驱动）
///
/// 5号位被 2号位在主流程各关键节点调用：
///   1. BOM 遍历到跨厂边时 → EvaluateCrossFactoryModeAsync
///   2. 枚举供给候选后 → SelectSupplyCandidatesByRuleAsync 排序
///   3. 遇到 ZP/BP Received 时 → ValidateZpBpDocumentMatchAsync
///   4. 遇到人工冻结需求时 → EvaluateManualFreezeRuleAsync
///   5. 最终分配方案确定后 → ValidateBusinessRuleResultAsync 红线校验
/// </summary>
public interface IPeggingRuleService
{
    /// <summary>
    /// 判断跨厂边模式（STAGE_HANDOFF 还是 INTER_FACTORY_ORDER）
    /// 5号位读取规则配置后裁决；2号位根据结果决定供给来源查找路径
    /// </summary>
    Task<CrossFactoryModeDecision> EvaluateCrossFactoryModeAsync(
        string sourceFactoryCode,
        string targetFactoryCode,
        int materialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据规则包对供给候选列表进行排序/筛选，返回建议优先级
    ///
    /// INTER_FACTORY_ORDER 场景下，5号位必须保证返回顺序为：
    ///   PIPELINE（在途）→ PRODUCTION_INSTRUCTION（ZP/BP Received，须匹配出荷指示号）→ NEW_REQUIREMENT
    ///
    /// 注：PRODUCTION_INSTRUCTION 候选须事先通过 ValidateZpBpDocumentMatchAsync 校验
    /// </summary>
    Task<List<SupplyCandidate>> SelectSupplyCandidatesByRuleAsync(
        List<SupplyCandidate> rawCandidates,
        long orderId,
        int materialId,
        decimal requiredQuantity,
        string factoryCode,
        CrossFactoryMode? crossFactoryMode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验 ZP/BP Received 是否可用于当前出荷指示号
    ///
    /// 通过条件（三者必须同时满足）：
    ///   1. DocumentNo = 当前出荷指示号
    ///   2. DocumentType = SHIPPING_INSTRUCTION
    ///   3. 出荷指示号未完成（未全量 Received）
    ///
    /// ZP/BP 不可作为通用库存，不满足以上条件时 IsMatched = false
    /// </summary>
    Task<ZpBpValidationResult> ValidateZpBpDocumentMatchAsync(
        string shippingInstructionNo,
        string documentNo,
        string documentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 人工冻结规则裁决（PMC 界面干预 / 特殊业务规则）
    ///
    /// 滑动窗口冻结（MES_DISPATCHED）由 2号位系统自动判断，不走此方法。
    /// 此方法只处理：MANUAL_LOCK / CUSTOMER_COMMITMENT / CONSTRAINT_FIXED 等人工/业务规则冻结。
    /// </summary>
    Task<FreezeDecision> EvaluateManualFreezeRuleAsync(
        long taskId,
        int planVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 构建规则裁决凭证（汇总本轮 Pegging 的所有规则决策）
    /// 在 2号位完成所有供给分配计算后调用，5号位做最终裁决并签发 Voucher
    /// </summary>
    Task<PeggingRuleVoucher> BuildPeggingVoucherAsync(
        int planVersionId,
        long orderId,
        List<SupplyCandidate> finalAllocations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 业务规则红线校验（2号位提交状态变更前最后一道门）
    ///
    /// 红线清单：
    ///   - ZP/BP 不可通用（PRODUCTION_INSTRUCTION 必须匹配出荷指示号）
    ///   - 在途优先于 Received（PIPELINE 未耗尽时不得跳过）
    ///   - 非 Task 供给不得进入物理 Pegging 表
    ///   - INTER_FACTORY_ORDER 供给链必须连续，不得断层
    /// </summary>
    Task<(bool IsValid, List<string> Errors)> ValidateBusinessRuleResultAsync(
        PeggingRuleVoucher voucher,
        CancellationToken cancellationToken = default);
}
