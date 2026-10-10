using System.Text.Json;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Core.Enum;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// ScheduleRun.ScopeJson 序列化口径测试（A 口径：契约字符串码，2号位 2026-09-21 回执定案）。
/// 锁定 ScheduleRunRepository 同款默认序列化：BusinessTriggerType / PriorityMode 经类型级
/// JsonStringEnumConverter + EnumMember 输出契约字面量；null 哨兵、结构化目标、int 数组符合冻结 §十九 / Delta §4.5。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public sealed class ScheduleRunScopeJsonSerializationTests
{
    [Fact]
    public void Serialize_DefaultShape_EmitsContractStringCodes()
    {
        // Arrange —— DOMAIN_MANUAL_RESCHEDULE 走既有正式优先规则，PriorityMode 具现为 null 哨兵
        var scope = new ScopeJsonV2
        {
            Trigger = BusinessTriggerType.DomainManualReschedule,
            PriorityMode = null,
            OrderTargets =
            [
                new OrderTarget { OrderCanonicalId = 1001, ManualTargetDueDate = new DateTime(2026, 10, 1) },
            ],
            TaskTargets =
            [
                new TaskTarget { TaskId = 7, TargetTime = new DateTime(2026, 9, 22, 9, 0, 0) },
            ],
            ChangedResourceIds = [12, 18, 33],
        };

        // Act —— 与 ScheduleRunRepository 同款默认序列化（无额外选项，枚举走类型级 JsonStringEnumConverter）
        var json = JsonSerializer.Serialize(scope);

        // Assert —— 字符串码 + 冻结字段形状 + SchemaVersion 演进标识
        Assert.Contains("\"SchemaVersion\":2", json);
        Assert.Contains("\"Trigger\":\"DOMAIN_MANUAL_RESCHEDULE\"", json);
        Assert.Contains("\"PriorityMode\":null", json);
        Assert.Contains("\"OrderCanonicalId\":1001", json);
        Assert.Contains("\"TaskId\":7", json);
        Assert.Contains("\"ChangedResourceIds\":[12,18,33]", json);
    }

    [Fact]
    public void Serialize_NonNullPriorityMode_EmitsContractLiteral()
    {
        // Arrange
        var scope = new ScopeJsonV2
        {
            Trigger = BusinessTriggerType.ExistingOrderAdvance,
            PriorityMode = PriorityMode.Expedite,
        };

        // Act
        var json = JsonSerializer.Serialize(scope);

        // Assert —— 二值轴字面量（非数字）
        Assert.Contains("\"Trigger\":\"EXISTING_ORDER_ADVANCE\"", json);
        Assert.Contains("\"PriorityMode\":\"EXPEDITE\"", json);
        Assert.DoesNotContain("\"PriorityMode\":2", json);

        // 反序列化回读（2号位 投影解析路径同款）：契约字面量可回读为等价枚举
        var roundTrip = JsonSerializer.Deserialize<ScopeJsonV2>(json);
        Assert.NotNull(roundTrip);
        Assert.Equal(BusinessTriggerType.ExistingOrderAdvance, roundTrip.Trigger);
        Assert.Equal(PriorityMode.Expedite, roundTrip.PriorityMode);
    }
}
