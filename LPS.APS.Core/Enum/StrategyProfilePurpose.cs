namespace LPS.APS.Core.Enum;

/// <summary>
/// 策略包用途（B-1：白天候选运行冻结契约）
/// 值即业务字面量；实施包 §十九 RunType×Purpose 冻结合法组合：
///   INSERT_ORDER_WHATIF → CTP / INSERT_IMPACT_ANALYSIS（二者永远不得激活）
///   LOCAL_RESCHEDULE    → INSERT_RESCHEDULE / MANUAL_ADJUSTMENT
///   MANUAL_RESCHEDULE   → MANUAL_ADJUSTMENT
/// 边界：DDL v5.1.4 ScheduleRun 无独立 Purpose 列（契约点 P1），本轮 Purpose 仅校验 + 审计，不落库。
/// </summary>
/// <remarks>开发者：3号位</remarks>
public static class StrategyProfilePurpose
{
    /// <summary>CTP（可承诺量分析；仅组合 INSERT_ORDER_WHATIF，不得激活）</summary>
    public const string Ctp = "CTP";

    /// <summary>插单影响分析（仅组合 INSERT_ORDER_WHATIF，不得激活）</summary>
    public const string InsertImpactAnalysis = "INSERT_IMPACT_ANALYSIS";

    /// <summary>局部插单重排（仅组合 LOCAL_RESCHEDULE）</summary>
    public const string InsertReschedule = "INSERT_RESCHEDULE";

    /// <summary>手工调整（组合 LOCAL_RESCHEDULE / MANUAL_RESCHEDULE）</summary>
    public const string ManualAdjustment = "MANUAL_ADJUSTMENT";

    /// <summary>全部用途（校验/枚举用）</summary>
    public static readonly string[] All = [Ctp, InsertImpactAnalysis, InsertReschedule, ManualAdjustment];
}
