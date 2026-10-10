using System.Collections;
using System.Text.Json;
using LPS.APS.Core.Dto;

namespace LPS.APS.Application.Services;

/// <summary>
/// 冻结快照 DemandPriority 子块 → 执行器配置 的投影（2号位 投影层，唯一实现处）。
///
/// 为什么需要一次投影：契约上 3号位 冻结的是 <see cref="DemandPriorityBlock"/>（强类型枚举、**无计算层维度**），
/// 而 <see cref="IDemandPriorityExecutor"/> 消费的是 <see cref="DemandPriorityConfig"/>
/// （字符串词表、每段带 <c>CalculationLayer</c>）。两者不是同一类型，必须有一个显式投影点；
/// 本类是唯一投影实现，禁止在 Provider / 执行器里各写一份。
///
/// 投影规则：
/// 1. **Field / Operator / Direction**：快照用枚举、执行器用字符串词表 → 逐值**显式**映射
///    （刻意不写 <c>enum.ToString()</c>：枚举一旦改名会静默变成未知字段，而显式 switch 会在编译期/运行期立刻暴露）。
///    执行器白名单外的枚举值一律**显式抛错**，不猜配——与执行器 P1-02「未知字段显式报错」同一口径。
/// 2. **Value**：快照为 <c>object?</c>（JSON 反序列化后实为 <see cref="JsonElement"/>；<c>In</c> 时为数组）
///    → 统一转字符串；<c>In</c> 用逗号连接（执行器 IN 分支按 ',' 拆分）。
/// 3. **CalculationLayer**：**快照子块没有这个字段** ⇒ 统一投影为 <see cref="TopLevelCalculationLayer"/>=1。
///    依据：V1 唯一调用点 <c>PeggingOrchestrator.BuildDemandSequenceMapAsync</c> 硬取第 1 层
///    （<c>const int currentCalculationLayer = 1</c>，「顶层独立需求=订单」，PM 0923 方案 A：按层在 Demand 集合形成点调用）。
///    若投影成 0，执行器侧 <c>Where(CalculationLayer == 1)</c> 会过滤成**空集**，
///    全体 Demand 静默退化为 DemandKey ASC 兜底排序 ⇒ 冻结策略形同未生效。故此处必须给 1，且不得给 0。
///    ⚠️ 待 3号位/0号位 确认：冻结快照 DemandPriority 子块是否需要携带 CalculationLayer（多计算层排序用）。
///    快照块一旦扩该字段，本投影改为**透传**，并移除本条常量。
/// 4. **StableTieBreakFields**：契约上是 `List&lt;string&gt;` **自由文本**（无枚举约束）⇒ 编译器挡不住名字漂移，
///    必须在此按词表换算（见 <c>MapTieBreakField</c>）。实测 2026-09-29（RuleSet 478 / 策略包 811）三个 Segment
///    的 Tie-break 全是 `"OrderId"`，而执行器白名单里叫 `DEMANDKEY` —— 原样透传会让**整条 Pegging 失败**
///    （执行器 `ValidateConfigFields` 逐条 `EnsureKnownField`）。两者同一取值，换算成立。
///    ⚠️ 待 3号位/0号位 确认：Tie-break 是否应改为强类型枚举（与 Field/Operator 同等待遇），
///    或 3号位 直接按执行器白名单词表产出 —— 否则每加一个 Tie-break 名都要 2号位 手工换算，迟早再撞。
/// </summary>
internal static class DemandPriorityProjector
{
    /// <summary>
    /// V1 唯一计算层 = 顶层独立需求（订单）。
    /// 与 <c>PeggingOrchestrator.BuildDemandSequenceMapAsync</c> 的 <c>currentCalculationLayer</c> 必须一致。
    /// </summary>
    internal const int TopLevelCalculationLayer = 1;

    /// <summary>把冻结快照的 DemandPriority 子块投影成执行器配置。子块缺失即报错，不静默给空配置。</summary>
    internal static DemandPriorityConfig Project(DemandPriorityBlock? block)
    {
        if (block is null)
        {
            throw new InvalidOperationException(
                "冻结策略快照的 DemandPriority 子块为空，无法投影为执行器配置（禁止静默当空策略执行）");
        }

        return new DemandPriorityConfig
        {
            Segments = block.Segments.Select(ProjectSegment).ToList()
        };
    }

    private static PrioritySegmentConfig ProjectSegment(PrioritySegment segment) => new()
    {
        // 快照无计算层维度 ⇒ 统一第 1 层（见类注释第 3 条）。不可改成 0。
        CalculationLayer = TopLevelCalculationLayer,
        SegmentOrder = segment.SegmentOrder,
        IsEnabled = segment.IsEnabled,
        MatchConditions = segment.MatchConditions.Select(ProjectCondition).ToList(),
        SortFields = segment.SortFields.Select(ProjectSortField).ToList(),
        // Tie-break 同样必须换算：执行器 ValidateConfigFields 会逐条 EnsureKnownField(tieBreakField)，
        // 原样透传 ⇒ 快照的 "OrderId" 直接撞白名单，整条 Pegging 失败（2026-09-29 真跑实测踩到）。
        StableTieBreakFields = segment.StableTieBreakFields.Select(MapTieBreakField).ToList(),
    };

    private static MatchCondition ProjectCondition(SegmentMatchCondition condition) => new()
    {
        FieldName = MapField(condition.Field),
        Operator = MapOperator(condition.Operator),
        Value = ToMatchValue(condition.Value),
    };

    private static string MapTieBreakField(string fieldName)
    {
        // 归一化后按**词表**换算，不按枚举 —— 契约上 Tie-break 是 List<string> 自由文本（无枚举约束），
        // 所以编译器不会替我们挡住名字漂移，只能在这里挡。
        var normalized = fieldName.Trim().ToUpperInvariant();

        return normalized switch
        {
            // 3号位 词表 "OrderId" → 2号位 执行器词表 "DEMANDKEY"。
            // 依据（符号锚）：BuildDemandSequenceMapAsync 构造 UpstreamDemand 时
            // `DemandKey = o.OrderId.ToString()` ⇒ 快照的 OrderId 与执行器的 DEMANDKEY **同一取值**（订单身份）。
            // 方向上：执行器 tie-break 走 OrderBy/ThenBy（升序），与执行器自带兜底「DemandKey ASC」同口径。
            "ORDERID" => "DEMANDKEY",

            // 非别名一律**原样交给执行器白名单校验**（EnsureKnownField 显式报错）。
            // 刻意**不**在这里复制一份白名单 —— 白名单唯一真源 = DemandPriorityExecutor.KnownFields，
            // 复制一份就是给自己造一个新的漂移面。
            _ => fieldName,
        };
    }

    private static SortField ProjectSortField(SegmentSortField sortField) => new()
    {
        FieldName = MapField(sortField.Field),
        Direction = sortField.Direction switch
        {
            SortDirection.Asc => "ASC",
            SortDirection.Desc => "DESC",
            _ => throw new NotSupportedException(
                $"DemandPriority 快照排序方向 {sortField.Direction} 不受支持（仅 Asc/Desc）")
        },
    };

    /// <summary>
    /// 快照字段枚举 → 执行器白名单字符串。
    /// 白名单以 <c>DemandPriorityExecutor.KnownFields</c> 为准：
    /// ORDERTYPE / DELAYSTATUS / CUSTOMERTIER / DUEDATE / ISSUEDATE / PROTECTIONSTATUS / DEMANDKEY。
    /// </summary>
    private static string MapField(DemandField field) => field switch
    {
        DemandField.DelayStatus => "DELAYSTATUS",
        DemandField.CustomerTier => "CUSTOMERTIER",
        DemandField.OrderType => "ORDERTYPE",
        DemandField.DueDate => "DUEDATE",
        DemandField.IssueDate => "ISSUEDATE",
        // RemainingTimeHours / IsPmcProtected / PriorityLevel：快照枚举里有，但执行器白名单无对应字段。
        // 不猜配（例如把 IsPmcProtected 猜成 PROTECTIONSTATUS）——静默错排比报错危险得多。
        _ => throw new NotSupportedException(
            $"DemandPriority 快照使用了字段 {field}，但 2号位 执行器白名单无对应字段"
            + "（白名单：ORDERTYPE/DELAYSTATUS/CUSTOMERTIER/DUEDATE/ISSUEDATE/PROTECTIONSTATUS/DEMANDKEY）。"
            + " 需 1↔3 契约对齐后再启用，不得猜配。")
    };

    /// <summary>快照操作符枚举 → 执行器词表。执行器支持 EQ/IN/LT/LTE/GT/GTE（无 NE）。</summary>
    private static string MapOperator(ConditionOperator op) => op switch
    {
        ConditionOperator.Equals => "EQ",
        ConditionOperator.LessThan => "LT",
        ConditionOperator.LessOrEqual => "LTE",
        ConditionOperator.GreaterThan => "GT",
        ConditionOperator.GreaterOrEqual => "GTE",
        ConditionOperator.In => "IN",
        _ => throw new NotSupportedException(
            $"DemandPriority 快照操作符 {op} 不受 2号位 执行器支持（支持 EQ/IN/LT/LTE/GT/GTE）")
    };

    /// <summary>
    /// 快照比较值 → 字符串。JSON 反序列化后 Value 实为 <see cref="JsonElement"/>
    /// （<c>In</c> 时为数组），必须显式处理，否则会得到 "System.Text.Json.JsonElement" 这种废串。
    /// </summary>
    private static string ToMatchValue(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;

            case JsonElement { ValueKind: JsonValueKind.Array } array:
                return string.Join(",", array.EnumerateArray().Select(e => e.ToString()));

            case JsonElement element:
                return element.ToString();

            case string s:
                return s;

            // In 的多值也可能是普通集合（快照对象由代码构造而非 JSON 时）
            case IEnumerable sequence:
                return string.Join(",", sequence.Cast<object?>().Select(v => v?.ToString() ?? string.Empty));

            default:
                return value.ToString() ?? string.Empty;
        }
    }
}
