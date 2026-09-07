using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Calculators;

/// <summary>
/// Timed Supply事实计算器
///
/// 【职责边界说明 - 2026-08-25新基线】
/// - 本类包含ETA优先级公式和AvailableTime计算逻辑
/// - 根据新冻结基线，Effective ETA和AvailableTime的最终计算属于2号位职责
/// - 本类保留作为工具类供2号位调用，不作为5号位正式主链
/// - 5号位正式职责：提供原始事实（ETA/ReleaseDate/Warehouse等），不计算最终时间
///
/// 参考：文档/20260818/更新文档20260825/APS_V1_5号位新基线增量整改开发包_v1.0_20260825.md
/// 核心逻辑：F13-F18、F20
/// </summary>
public sealed class TimedSupplyFactCalculator
{
    /// <summary>
    /// 计算生效的Timed Supply事实
    /// </summary>
    /// <param name="raw">原始采购事实（包含ManualEta/ErpEta/ReleaseDate等内部字段）</param>
    /// <param name="parameters">冻结参数快照</param>
    /// <param name="referenceTime">本次运行的统一参考时间（用于F16逾期判断，不允许逐条DateTime.Now）</param>
    /// <returns>标准化的TimedSupplyFact</returns>
    public TimedSupplyFact CalculateEffectiveSupply(
        RawProcurementFact raw,
        FrozenFactParameters parameters,
        DateTime referenceTime)
    {
        // F13/F14/F15: 确定生效ETA（三级优先级）
        var effectiveEta = DetermineEffectiveEta(raw, parameters);

        // F16: 逾期容差（仅对默认ETA生效的情况）
        if (effectiveEta.HasValue &&
            !raw.ManualEta.HasValue &&
            !raw.Eta.HasValue &&
            effectiveEta.Value < referenceTime)
        {
            effectiveEta = ApplyOverdueMargin(effectiveEta.Value, parameters.OverdueMargin, referenceTime);
        }

        // F17: 到货可用偏移
        var availableTime = ApplyArrivalOffset(effectiveEta, raw.StorageCode, parameters.ArrivalToUsableOffsets);

        return new TimedSupplyFact
        {
            SupplyType = raw.SupplyType,
            PhysicalSourceKey = raw.PhysicalSourceKey,
            MaterialId = raw.MaterialId,
            MaterialCode = raw.MaterialCode,
            FactoryId = raw.FactoryId,
            FactoryCode = raw.FactoryCode,
            WarehouseCode = raw.StorageCode,
            RemainingQty = raw.RemainingQty,
            Eta = effectiveEta,
            AvailableTime = availableTime,
            CommitmentStatus = raw.CommitmentStatus,
            SourceDocumentNo = raw.SourceDocumentNo,
            SourceDocumentLineNo = raw.SourceDocumentLineNo,
            SourceUpdatedAt = raw.SourceUpdatedAt
        };
    }

    /// <summary>
    /// F13/F14/F15: 确定生效ETA（三级优先级）
    /// 优先级：Manual ETA > ERP ETA > PO Release Date + DefaultLT
    /// </summary>
    private DateTime? DetermineEffectiveEta(RawProcurementFact raw, FrozenFactParameters parameters)
    {
        // F13: 人工ETA优先（最高优先级）
        if (raw.ManualEta.HasValue)
        {
            return raw.ManualEta.Value;
        }

        // F14: 人工ETA删除/取消后回退ERP ETA（V1简化：ManualEta=null即为取消）
        // 次优先级：ERP ETA
        if (raw.Eta.HasValue)
        {
            return raw.Eta.Value;
        }

        // F15: DefaultLT兜底
        // 基准必须是PO Release/Issue Date，不是DataCutoffTime
        if (raw.ReleaseDate.HasValue)
        {
            return raw.ReleaseDate.Value.AddDays(parameters.DefaultPurchaseLt);
        }

        // 如果连ReleaseDate都没有，返回null（真实空）
        return null;
    }

    /// <summary>
    /// F16: 应用逾期容差
    /// 仅当默认ETA（ReleaseDate + DefaultLT）落在运行参考时间之前时应用
    /// </summary>
    private DateTime ApplyOverdueMargin(DateTime overdueEta, int marginDays, DateTime referenceTime)
    {
        // 逾期ETA + Margin容差
        var adjustedEta = overdueEta.AddDays(marginDays);

        // 如果加了Margin还是过期，至少返回当前参考时间
        return adjustedEta < referenceTime ? referenceTime : adjustedEta;
    }

    /// <summary>
    /// F17: 应用到货可用偏移
    /// Arrived-not-inbound的AvailableTime = ETA + Warehouse/Inspection/Inbound Offset
    /// </summary>
    private DateTime? ApplyArrivalOffset(
        DateTime? eta,
        string warehouseCode,
        Dictionary<string, int> offsets)
    {
        if (!eta.HasValue)
        {
            return null;
        }

        // 查找仓库偏移配置（小时）
        if (offsets.TryGetValue(warehouseCode, out var offsetHours))
        {
            return eta.Value.AddHours(offsetHours);
        }

        // 没有配置偏移，直接返回ETA
        return eta.Value;
    }
}
