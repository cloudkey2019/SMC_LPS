using LPS.APS.Application.Services;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Enum;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// ScopeJsonV2Validator 单元测试（M1；冻结 §十九 八码映射 + PriorityMode 轴 + 单一真相）
/// </summary>
/// <remarks>开发者：3号位</remarks>
public class ScopeJsonV2ValidatorTests
{
    [Fact]
    public void Validate_NullScope_DoesNotThrow()
    {
        ScopeJsonV2Validator.Validate(
            null, StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment);
    }

    [Fact]
    public void Validate_GanttAdjustmentWithNormal_DoesNotThrow()
    {
        var scope = new ScopeJsonV2 { Trigger = BusinessTriggerType.GanttAdjustment, PriorityMode = PriorityMode.Normal };

        ScopeJsonV2Validator.Validate(
            scope, StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment);
    }

    [Fact]
    public void Validate_NewOrderInsertWithExpedite_DoesNotThrow()
    {
        var scope = new ScopeJsonV2 { Trigger = BusinessTriggerType.NewOrderInsert, PriorityMode = PriorityMode.Expedite };

        ScopeJsonV2Validator.Validate(
            scope, StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.InsertReschedule);
    }

    [Fact]
    public void Validate_DomainManualRescheduleWithNullPriority_DoesNotThrow()
    {
        var scope = new ScopeJsonV2 { Trigger = BusinessTriggerType.DomainManualReschedule, PriorityMode = null };

        ScopeJsonV2Validator.Validate(
            scope, StrategyProfileRunType.ManualReschedule, StrategyProfilePurpose.ManualAdjustment);
    }

    [Fact]
    public void Validate_TriggerRunTypeMismatch_Throws()
    {
        var scope = new ScopeJsonV2 { Trigger = BusinessTriggerType.GanttAdjustment };

        Assert.Throws<InvalidOperationException>(() =>
            ScopeJsonV2Validator.Validate(
                scope, StrategyProfileRunType.ManualReschedule, StrategyProfilePurpose.ManualAdjustment));
    }

    [Fact]
    public void Validate_TriggerPurposeMismatch_Throws()
    {
        var scope = new ScopeJsonV2 { Trigger = BusinessTriggerType.NewOrderInsert };

        Assert.Throws<InvalidOperationException>(() =>
            ScopeJsonV2Validator.Validate(
                scope, StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment));
    }

    [Fact]
    public void Validate_GanttAdjustmentWithExpedite_Throws()
    {
        var scope = new ScopeJsonV2 { Trigger = BusinessTriggerType.GanttAdjustment, PriorityMode = PriorityMode.Expedite };

        Assert.Throws<InvalidOperationException>(() =>
            ScopeJsonV2Validator.Validate(
                scope, StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment));
    }

    [Fact]
    public void Validate_DomainManualRescheduleWithNormal_Throws()
    {
        var scope = new ScopeJsonV2 { Trigger = BusinessTriggerType.DomainManualReschedule, PriorityMode = PriorityMode.Normal };

        Assert.Throws<InvalidOperationException>(() =>
            ScopeJsonV2Validator.Validate(
                scope, StrategyProfileRunType.ManualReschedule, StrategyProfilePurpose.ManualAdjustment));
    }

    [Fact]
    public void Validate_DuplicateOrderCanonicalId_Throws()
    {
        var scope = new ScopeJsonV2
        {
            Trigger = BusinessTriggerType.NewOrderInsert,
            PriorityMode = PriorityMode.Normal,
            OrderTargets =
            [
                new OrderTarget { OrderCanonicalId = 1 },
                new OrderTarget { OrderCanonicalId = 1 },
            ],
        };

        Assert.Throws<InvalidOperationException>(() =>
            ScopeJsonV2Validator.Validate(
                scope, StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.InsertReschedule));
    }

    [Fact]
    public void Validate_ChangedResourceIdsIntArray_DoesNotThrow()
    {
        var scope = new ScopeJsonV2
        {
            Trigger = BusinessTriggerType.EquipmentFailure,
            PriorityMode = PriorityMode.Normal,
            ChangedResourceIds = [12, 18, 33],
        };

        ScopeJsonV2Validator.Validate(
            scope, StrategyProfileRunType.LocalReschedule, StrategyProfilePurpose.ManualAdjustment);
    }

    // ── M4 · 2号位：OrderTargets 存在性（OrderCanonicalId ↔ 装载订单集一致性）──────

    [Fact]
    public void ValidateOrderTargetsConsistency_AllPresent_DoesNotThrow()
    {
        var targets = new[]
        {
            new OrderTarget { OrderCanonicalId = 1 },
            new OrderTarget { OrderCanonicalId = 2 },
        };

        ScopeJsonV2Validator.ValidateOrderTargetsConsistency(targets, new HashSet<long> { 1, 2, 3 });
    }

    [Fact]
    public void ValidateOrderTargetsConsistency_Missing_Throws()
    {
        var targets = new[]
        {
            new OrderTarget { OrderCanonicalId = 1 },
            new OrderTarget { OrderCanonicalId = 999 },
        };

        Assert.Throws<InvalidOperationException>(() =>
            ScopeJsonV2Validator.ValidateOrderTargetsConsistency(targets, new HashSet<long> { 1, 2, 3 }));
    }

    [Fact]
    public void ValidateOrderTargetsConsistency_Empty_DoesNotThrow()
    {
        ScopeJsonV2Validator.ValidateOrderTargetsConsistency(Array.Empty<OrderTarget>(), new HashSet<long>());
    }
}