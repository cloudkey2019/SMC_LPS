namespace LPS.APS.Application.Services;

/// <summary>
/// 人工能力槽 → SolverResource 的运行时投影规则（PM 0923 资源模型裁决）。
/// ManualCapacitySlot 不进入正式 Resource 主数据；装载时投影为合成 Solver Resource，
/// 合成 ResourceId 与设备 Resource.Id 分属不同数值空间、避免冲突：
///   合成 ResourceId = ManualSlotResourceOffset + ManualSlotId。
/// 该键仅存在于运行时对象（DomainSolveRequest.Resources / CalendarSlots / OperationResourceEligibility），
/// 不落库、不暴露负号规则；结果落库时拆回：Task.ResourceId = NULL + Task.ManualSlotId。
/// </summary>
internal static class SolverResourceProjection
{
    /// <summary>人工槽合成 ResourceId 偏移（实测设备 Resource.Id MAX=14508，10 亿偏移绝不冲突）。</summary>
    public const int ManualSlotResourceOffset = 1_000_000_000;

    /// <summary>人工槽合成 ResourceCode 前缀（落库用：MAN: + OperationName + SlotCode，与设备业务码通过前缀区分，不冲突）。</summary>
    public const string ManualSlotResourceCodePrefix = "MAN:";

    /// <summary>判断一个运行时 ResourceId 是否为人工槽合成键。</summary>
    public static bool IsManualSlotId(int resourceId) => resourceId >= ManualSlotResourceOffset;

    /// <summary>从合成键拆回 ManualSlotId。</summary>
    public static int ToManualSlotId(int resourceId) => resourceId - ManualSlotResourceOffset;
}