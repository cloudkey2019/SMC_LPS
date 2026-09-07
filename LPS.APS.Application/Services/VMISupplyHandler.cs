using LPS.APS.Core.Dto;
using LPS.APS.Core.Models;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// VMI供给识别器（2号位职责 — VMI vs Placeholder分离）
///
/// 职责边界：
/// - 5号位负责标准化Timed Supply事实（包括VMI_ONSITE的Commitment/Confidence）
/// - 2号位负责VMI识别和Placeholder触发逻辑
///
/// PM冻结口径：
/// - VMI是正式物理Supply，不要硬编码为CONFIRMED + COMMITTED
/// - 应尊重5号位标准化的Commitment/Confidence
/// - 只有检查完所有正式合格Supply（Inventory/Arrived/PO/VMI）仍存在缺口，才触发Planning-only Placeholder
/// </summary>
public sealed class VMISupplyHandler
{
    private readonly ILogger<VMISupplyHandler> _logger;

    public VMISupplyHandler(ILogger<VMISupplyHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 判断Supply是否为VMI供给
    /// </summary>
    public bool IsVMISupply(SupplyFact supply)
    {
        // INTEGRATION TODO: 实际判断逻辑待5号位Timed Supply标准化后确认
        // 当前按SupplyType字段判断
        return string.Equals(supply.SupplyType, "VMI_ONSITE", StringComparison.OrdinalIgnoreCase)
               || string.Equals(supply.SupplyType, "VMI", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查所有正式Supply后的剩余缺口
    ///
    /// 按PM冻结口径，正式Supply包括：
    /// - Inventory（库存）
    /// - Arrived-not-inbound（已到货未入库）
    /// - 正式PO/采购在途
    /// - VMI（寄售库存）
    /// - 其它冻结的正式供给
    /// </summary>
    public decimal CalculateGapAfterFormalSupply(
        decimal demandQty,
        IEnumerable<SupplyFact> formalSupplies)
    {
        var totalFormalSupply = formalSupplies
            .Where(s => IsFormalSupply(s))
            .Sum(s => s.AvailableQuantity);

        var gap = demandQty - totalFormalSupply;
        return gap > 0 ? gap : 0;
    }

    /// <summary>
    /// 判断是否为正式Supply（非Planning-only Placeholder）
    /// </summary>
    private bool IsFormalSupply(SupplyFact supply)
    {
        // Planning-only Placeholder特征：
        // - SupplyType包含"PLACEHOLDER"或"PLANNING_ONLY"
        // - 或者Commitment = PLANNING_ONLY
        if (supply.SupplyType?.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        if (supply.SupplyType?.Contains("PLANNING_ONLY", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        if (string.Equals(supply.Commitment, "PLANNING_ONLY", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 触发Planning-only Purchase Placeholder
    ///
    /// PM冻结口径：
    /// - 只有当检查完所有正式合格Supply仍存在缺口时才触发
    /// - Placeholder不得进入MES下发流程
    /// </summary>
    public SupplyFact CreatePlaceholder(
        int materialId,
        int factoryId,
        decimal gapQuantity,
        DateTime requiredTime)
    {
        _logger.LogInformation(
            "Creating Planning-only Placeholder: Material={MaterialId}, Factory={FactoryId}, Gap={Gap}",
            materialId, factoryId, gapQuantity);

        // INTEGRATION TODO: 实际创建逻辑待确认
        // 当前返回占位结构
        return new SupplyFact
        {
            SupplyType = "PLANNING_ONLY_PLACEHOLDER",
            MaterialId = materialId,
            FactoryId = factoryId,
            AvailableQuantity = gapQuantity,
            AvailableTime = requiredTime,
            Commitment = "PLANNING_ONLY",
            Confidence = "LOW",
            SourceKey = $"PLACEHOLDER_{Guid.NewGuid():N}"
        };
    }
}
