using LPS.APS.BusinessRules.Models;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Rules;

namespace LPS.APS.Application.Services;

/// <summary>
/// AvailableTime 纯函数计算（2号位职责，阶段 3）。
/// 消费 5号位已交付的原始事实（<see cref="RawProcurementFact"/>）+ Manual ETA 覆盖，
/// 按 3号位 <see cref="EtaInvariant"/> 固化优先级链（Manual &gt; ERP &gt; ReleaseDate+DefaultLT）解析 Effective ETA，
/// 再加 Arrival-to-Usable Offset 得 AvailableTime。
/// 纯静态、无 I/O、确定性——便于单测。
/// </summary>
internal static class AvailableTimeCalculator
{
    /// <summary>
    /// 构建 Manual ETA 覆盖查找表（键：PONo+LineNo+MaterialId+ReceivingWarehouse）。
    /// 仅保留 IsActive 且 ManualEta 非 default 的记录；default(DateTime) 按 EtaInvariant 约定规范化为无值。
    /// </summary>
    public static Dictionary<(string, int, int, string), DateTime> BuildManualEtaMap(
        IReadOnlyList<ProcurementManualEtaOverride> overrides)
    {
        var map = new Dictionary<(string, int, int, string), DateTime>();
        foreach (var o in overrides)
        {
            if (!o.IsActive || o.ManualEta == default)
                continue;
            map[(o.PONo, o.LineNo, o.MaterialId, o.ReceivingWarehouse)] = o.ManualEta;
        }
        return map;
    }

    /// <summary>
    /// 计算 AvailableTime = EtaInvariant.Resolve(Manual, ERP, ReleaseDate+DefaultLT) + ArrivalToUsableOffset。
    /// 三级来源均无有效 ETA 时返回 null（无可用时间）。
    /// 注意：5号位 ODS 未透出 ReleaseDate（恒 NULL）时 DefaultLT 兜底链走不通，但 Manual/ERP 两级不受影响。
    /// </summary>
    public static DateTime? Compute(
        RawProcurementFact fact,
        IReadOnlyDictionary<(string, int, int, string), DateTime> manualEtaMap,
        FrozenStrategySnapshot snapshot)
    {
        // 1) Manual ETA（最高优先，来源 ProcurementManualEtaOverride 表）
        DateTime? manualEta = null;
        if (int.TryParse(fact.SourceDocumentLineNo, out var lineNo)
            && manualEtaMap.TryGetValue((fact.SourceDocumentNo, lineNo, fact.MaterialId, fact.StorageCode), out var manual))
        {
            manualEta = manual;
        }

        // 2) ERP ETA
        var erpEta = fact.Eta;

        // 3) DefaultLT 推算：ReleaseDate + DefaultPurchaseLt（按 Warehouse + Material 维度）
        DateTime? defaultLtEta = null;
        var ltRule = ResolvePurchaseLtRule(snapshot.Procurement.DefaultPurchaseLt, fact.StorageCode, fact.MaterialId);
        if (ltRule != null && fact.ReleaseDate.HasValue)
            defaultLtEta = fact.ReleaseDate.Value.AddDays(ltRule.DefaultLtDays);

        var resolution = EtaInvariant.Resolve(manualEta, erpEta, defaultLtEta);
        if (!resolution.HasEta)
            return null;

        // 4) Arrival-to-Usable Offset（按 Receiving Warehouse）
        var offsetHours = ResolveArrivalOffset(snapshot.Procurement.ArrivalToUsableOffsets, fact.StorageCode);
        return resolution.EffectiveEta!.Value.AddHours(offsetHours);
    }

    private static PurchaseLtRule? ResolvePurchaseLtRule(
        IReadOnlyList<PurchaseLtRule> rules, string warehouseCode, int materialId)
    {
        if (rules is null || rules.Count == 0) return null;
        var matId = materialId.ToString();

        // 优先 Material 级精确匹配
        var materialRule = rules.FirstOrDefault(r =>
            string.Equals(r.WarehouseCode, warehouseCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.MaterialId, matId, StringComparison.OrdinalIgnoreCase));
        if (materialRule != null) return materialRule;

        // 回退 Warehouse 级默认（MaterialId 为空）
        return rules.FirstOrDefault(r =>
            string.Equals(r.WarehouseCode, warehouseCode, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(r.MaterialId));
    }

    private static double ResolveArrivalOffset(IReadOnlyList<WarehouseOffsetRule> rules, string warehouseCode)
    {
        if (rules is null) return 0;
        var rule = rules.FirstOrDefault(r =>
            string.Equals(r.WarehouseCode, warehouseCode, StringComparison.OrdinalIgnoreCase));
        return rule?.OffsetHours ?? 0;
    }
}
