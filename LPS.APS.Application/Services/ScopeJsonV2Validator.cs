using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Enum;

namespace LPS.APS.Application.Services;

/// <summary>
/// ScopeJsonV2 范围载荷运行时校验（M1；冻结 §十九 / Delta §4.3~4.6）
/// 语义：fail-closed——任一违反抛 InvalidOperationException（与 RunLifecycleService 创建门禁一致），不静默降级。
/// 纯静态无状态：供 RunLifecycleService.CreateCandidateRunAsync 调用，亦可单测隔离。
/// 校验四点：
///   ① 触发类型 → RunType×Purpose 固定映射一致（冻结 §十九 八码表）；
///   ② PriorityMode 轴约束（GANTT/EQUIPMENT/RESOURCE_CALENDAR + EXPEDITE 拒绝；DOMAIN_MANUAL_RESCHEDULE 须 null）；
///   ③ OrderTargets 唯一（单一真相约束，OrderCanonicalId 重复即数据错误）。
///   ④ OrderTargets 存在性（M4 · 2号位）：每个 OrderCanonicalId 须落在本次 Run 装载订单集内，缺失则拒绝执行。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public static class ScopeJsonV2Validator
{
    /// <summary>校验范围载荷与 RunType×Purpose 的一致性；scope 为 null 直接放行（旧调用兼容，不额外校验）</summary>
    public static void Validate(ScopeJsonV2? scope, string runType, string purpose)
    {
        if (scope is null)
        {
            return;
        }

        ValidateTriggerMapping(scope.Trigger, runType, purpose);
        ValidatePriorityMode(scope.Trigger, scope.PriorityMode);
        ValidateOrderTargetsUnique(scope.OrderTargets);
    }

    /// <summary>① 触发类型 → RunType×Purpose 固定映射（冻结 §十九 八码表）</summary>
    private static void ValidateTriggerMapping(BusinessTriggerType trigger, string runType, string purpose)
    {
        var (expectedRunType, expectedPurpose) = trigger switch
        {
            BusinessTriggerType.NewOrderCtp => (StrategyProfileRunType.InsertOrderWhatIf, StrategyProfilePurpose.Ctp),
            BusinessTriggerType.NewOrderImpact => (StrategyProfileRunType.InsertOrderWhatIf, StrategyProfilePurpose.InsertImpactAnalysis),
            BusinessTriggerType.NewOrderInsert => (StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.InsertReschedule),
            BusinessTriggerType.ExistingOrderAdvance => (StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment),
            BusinessTriggerType.GanttAdjustment => (StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment),
            BusinessTriggerType.EquipmentFailure => (StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment),
            BusinessTriggerType.ResourceCalendarChange => (StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment),
            BusinessTriggerType.DomainManualReschedule => (StrategyProfileRunType.ManualReschedule, StrategyProfilePurpose.ManualAdjustment),
            _ => throw new InvalidOperationException($"BusinessTriggerType 非法值（{(int)trigger}），不在冻结八码内"),
        };

        if (!string.Equals(runType, expectedRunType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"触发类型 {trigger} 的 RunType 应为 {expectedRunType}，实为 {runType}（冻结 §十九 八码映射）");
        }

        if (!string.Equals(purpose, expectedPurpose, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"触发类型 {trigger} 的 Purpose 应为 {expectedPurpose}，实为 {purpose}（冻结 §十九 八码映射）");
        }
    }

    /// <summary>② PriorityMode 轴约束（冻结 §十九）</summary>
    private static void ValidatePriorityMode(BusinessTriggerType trigger, PriorityMode? priorityMode)
    {
        switch (trigger)
        {
            case BusinessTriggerType.GanttAdjustment:
            case BusinessTriggerType.EquipmentFailure:
            case BusinessTriggerType.ResourceCalendarChange:
                // 固定 NORMAL：GANTT/EQUIPMENT/RESOURCE_CALENDAR + EXPEDITE 必须拒绝
                if (priorityMode == PriorityMode.Expedite)
                {
                    throw new InvalidOperationException(
                        $"触发类型 {trigger} 固定 NORMAL，不得指定 EXPEDITE（冻结 §十九：GANTT/EQUIPMENT/RESOURCE_CALENDAR + EXPEDITE 拒绝）");
                }
                break;

            case BusinessTriggerType.NewOrderCtp when priorityMode == PriorityMode.Expedite:
                throw new InvalidOperationException("NEW_ORDER_CTP 固定 NORMAL，不得指定 EXPEDITE（冻结 §十九）");

            case BusinessTriggerType.NewOrderImpact when priorityMode == PriorityMode.Normal:
                throw new InvalidOperationException("NEW_ORDER_IMPACT 固定 EXPEDITE，不得指定 NORMAL（冻结 §十九）");

            case BusinessTriggerType.DomainManualReschedule when priorityMode is PriorityMode.Normal or PriorityMode.Expedite:
                throw new InvalidOperationException(
                    "DOMAIN_MANUAL_RESCHEDULE 走既有正式优先规则，PriorityMode 须为 null（非 NORMAL/EXPEDITE，冻结 §十九）");
        }
    }

    /// <summary>③ OrderTargets 唯一（单一真相：OrderCanonicalId 去重，重复即数据错误，冻结 §4.6）</summary>
    private static void ValidateOrderTargetsUnique(IReadOnlyList<OrderTarget> orderTargets)
    {
        if (orderTargets.Count == 0)
        {
            return;
        }

        if (orderTargets.Select(t => t.OrderCanonicalId).Distinct().Count() != orderTargets.Count)
        {
            throw new InvalidOperationException("OrderTargets 含重复 OrderCanonicalId（单一真相约束：目标订单须去重唯一，冻结 §4.6）");
        }
    }

    /// <summary>
    /// ④ OrderTargets 存在性（M4 · 2号位）：ScopeJsonV2.OrderTargets 引用的 OrderCanonicalId 必须全部落在本次 Run
    /// 实际装载的订单集内；任一缺失即「候选范围与装载订单集不一致」—— fail-closed 拒绝执行。
    /// 与 M1 的唯一性校验互补；由 SchedulingOrchestrator.ExecuteDomainAsync 装载 [Order] 后按 demand 侧
    /// OrderCanonicalId 集合调用。scope 为 null / OrderTargets 为空时放行（FULL 语义或本 Run 未承载订单目标）。
    /// </summary>
    public static void ValidateOrderTargetsConsistency(
        IReadOnlyList<OrderTarget> orderTargets, IReadOnlyCollection<long> loadedOrderCanonicalIds)
    {
        if (orderTargets.Count == 0)
        {
            return;
        }

        var missing = orderTargets
            .Select(t => t.OrderCanonicalId)
            .Where(id => !loadedOrderCanonicalIds.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"ScopeJsonV2.OrderTargets 引用了不在本次 Run 装载订单集内的 OrderCanonicalId（{string.Join(",", missing)}）——候选范围与装载订单集不一致，拒绝执行（M4）");
        }
    }
}