namespace LPS.APS.Core.Enum;

/// <summary>
/// 策略包运行类型（冻结 DDL v5.1.2 §3.10.5 StrategyProfile.RunType CK 校验字面量）
/// 值即 DDL CHECK 字面量，实体 RunType 字段直接存储该字符串。
/// 默认版本选择语义（C2-3）：未显式指定 StrategyProfileVersionId 时按 RunType 取唯一无歧义默认 PUBLISHED 版本。
/// </summary>
public static class StrategyProfileRunType
{
    public const string FullSchedule = "FULL_SCHEDULE";
    public const string ManualReschedule = "MANUAL_RESCHEDULE";
    public const string LocalReschedule = "LOCAL_RESCHEDULE";
    public const string Simulation = "SIMULATION";
    public const string InsertOrderWhatIf = "INSERT_ORDER_WHATIF";

    /// <summary>全部运行类型（校验/枚举用）</summary>
    public static readonly string[] All = [FullSchedule, ManualReschedule, LocalReschedule, Simulation, InsertOrderWhatIf];
}
