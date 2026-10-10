using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LPS.APS.Core.Enum;

/// <summary>
/// 优先级模式（ScopeJsonV2 独立轴；冻结 §十九 / Delta §4.3~4.4）
/// 二值轴 NORMAL / EXPEDITE；DOMAIN_MANUAL_RESCHEDULE 走既有正式优先规则，不使用本轴（对应字段具现为 null）。
/// 序列化为契约字面量（EnumMemberJsonConverter 读 EnumMember；STJ 内置 JsonStringEnumConverter 不读 EnumMember），非数字。
/// </summary>
/// <remarks>开发者：3号位</remarks>
[JsonConverter(typeof(EnumMemberJsonConverter<PriorityMode>))]
public enum PriorityMode
{
    /// <summary>常规优先级（默认；GANTT/EQUIPMENT/RESOURCE_CALENDAR/NEW_ORDER_CTP 固定此值）</summary>
    [EnumMember(Value = "NORMAL")] Normal = 1,

    /// <summary>加急优先级（NEW_ORDER_IMPACT 固定此值；NEW_ORDER_INSERT / EXISTING_ORDER_ADVANCE 可选）</summary>
    [EnumMember(Value = "EXPEDITE")] Expedite = 2,
}