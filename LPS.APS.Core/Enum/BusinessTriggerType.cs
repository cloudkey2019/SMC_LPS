using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace LPS.APS.Core.Enum;

/// <summary>
/// 白天候选业务触发类型（ScopeJsonV2 触发器轴；冻结 §十九 / Delta §4.3~4.4）
/// 「十码」实为「八码」：§41.2 的 10 个场景行因 NEW_ORDER_INSERT / EXISTING_ORDER_ADVANCE
/// 各拆 NORMAL / EXPEDITE 两行，触发器轴坍缩为 8 码，PriorityMode 独立成轴。
/// RunType × Purpose 固定映射见 ScopeJsonV2Validator（Application 层）。
/// 序列化为契约字面量（EnumMemberJsonConverter 读 EnumMember；STJ 内置 JsonStringEnumConverter 不读 EnumMember），非数字。
/// </summary>
/// <remarks>开发者：3号位</remarks>
[JsonConverter(typeof(EnumMemberJsonConverter<BusinessTriggerType>))]
public enum BusinessTriggerType
{
    /// <summary>新订单 CTP（仅组合 INSERT_ORDER_WHATIF + CTP，固定 NORMAL，永远不得激活）</summary>
    [EnumMember(Value = "NEW_ORDER_CTP")] NewOrderCtp = 1,

    /// <summary>新订单插单影响分析（仅组合 INSERT_ORDER_WHATIF + INSERT_IMPACT_ANALYSIS，固定 EXPEDITE，永远不得激活）</summary>
    [EnumMember(Value = "NEW_ORDER_IMPACT")] NewOrderImpact = 2,

    /// <summary>新订单插单重排（组合 LOCAL_RESCHEDULE + INSERT_RESCHEDULE，NORMAL 或 EXPEDITE）</summary>
    [EnumMember(Value = "NEW_ORDER_INSERT")] NewOrderInsert = 3,

    /// <summary>既有订单提前（组合 LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT，NORMAL 或 EXPEDITE）</summary>
    [EnumMember(Value = "EXISTING_ORDER_ADVANCE")] ExistingOrderAdvance = 4,

    /// <summary>甘特调整（组合 LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT，固定 NORMAL）</summary>
    [EnumMember(Value = "GANTT_ADJUSTMENT")] GanttAdjustment = 5,

    /// <summary>设备故障（组合 LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT，固定 NORMAL）</summary>
    [EnumMember(Value = "EQUIPMENT_FAILURE")] EquipmentFailure = 6,

    /// <summary>资源日历变更（组合 LOCAL_RESCHEDULE + MANUAL_ADJUSTMENT，固定 NORMAL）</summary>
    [EnumMember(Value = "RESOURCE_CALENDAR_CHANGE")] ResourceCalendarChange = 7,

    /// <summary>域内手工整体重排（组合 MANUAL_RESCHEDULE + MANUAL_ADJUSTMENT，走既有正式优先规则，非 NORMAL/EXPEDITE）</summary>
    [EnumMember(Value = "DOMAIN_MANUAL_RESCHEDULE")] DomainManualReschedule = 8,
}