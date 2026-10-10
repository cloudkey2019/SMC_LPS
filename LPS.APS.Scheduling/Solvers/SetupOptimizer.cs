namespace LPS.APS.Scheduling.Solvers;

using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;

/// <summary>
/// Setup 换型规则解析骨架（v1.2 收口版规则模型）。
///
/// 【口径来源】《APS V1 Setup换型规则与有限产能优化_冻结文档修改指导_v1.2_20260916_最终收口版》：
/// - §1.1/§20.2 废止属性式换型（Mold/Tool/Material/Color SetupAttribute），旧 SelectBestCandidate/CalculateSetupTime 已删除；
/// - §2/§3 换型键 =「当前 Task 自身 OperationCode + 当前 Resource + 前产品 FromMaterial + 后产品 ToMaterial」，方向性，
///   不读上一 Task 工序（废止 FromOperation/ToOperation 模型）；
/// - §5 命中三层：EXACT 产品对 → DEFAULT 工序+设备 → 无规则 0 分钟 + 记「规则缺失」解释；
/// - §6 同产品 A→A 默认 0 分钟，允许显式规则覆盖；
/// - §1.2/§20.3 禁止 RoutingOperation.SetupTime / MES SetupTime fallback；
/// - §14.3/0号位 20260917 裁决：「生产日」= Resource Calendar 连续可用生产窗口（不按自然日切，跨零点不断开）；
///   无上一产品 → 初始设备状态 Setup=0（INITIAL_SETUP_STATE 追踪说明，非 ReasonCode），与「规则缺失降级」严格分开；
/// - §8.2 裁决项1 已由 0号位 终裁（20260918）：0号位 只冻结红线——**必须有界搜索、不得因 Setup 搜索破坏
///   夜间约 15 分钟总性能目标**；具体预算/邻域尝试次数由 1号位 按性能标定提「默认值+允许范围」
///   （标定基准：10 万 Task / 90 天 / 夜间约 15 分钟），3号位 纳入 ParameterSetVersion 版本化治理；
///   500（有界搜索预算）/50（最大邻域尝试）仅作 1号位 测试初值，不得写成 0号位 冻结值；
///   「单生产日保护阈值」删除（语义未定义；未来确需时由 1号位 提五件套：中文技术含义/触发条件/
///   为什么现有总预算不足/建议默认值/性能测试依据，再由 3号位 纳入治理，不提前造字段）；
///   （旧 SetupLookAheadSize=5 / DefaultSetupMinutes=30 是已废止旧锚，不得沿用——3号位 20260917 §三）。
///
/// 【数据通道】r13376（3号位 2026-09-18）已落地 `SetupTransitionRuleSnapshot`（FrozenStrategySnapshot 第⑦块，
/// 随 RuleSetVersionId 锚冻结）；2号位 按 Domain 裁剪投影进 DomainSolveRequest 后，Phase1 经
/// <see cref="BuildRuleLookups"/> 适配为字典传入 <see cref="ResolveSetup"/>；本骨架不自建规则读取、不读 3号位 数据库。
///
/// 【当前状态】骨架 + 纯函数 + 快照适配，五阶段流程尚未接线（接线依赖 2号位 把第⑦块透传进 DomainSolveRequest）。
/// 夜间 FULL 接线时按 0号位 红线设计：有界搜索（500/50 测试初值起步）+ 总耗时不破 15 分钟夜间目标，
/// 标定后把「默认值+允许范围」回传 3号位 入 ParameterSetVersion。
/// </summary>
public class SetupOptimizer
{
    /// <summary>
    /// EXACT 规则键：当前工序 + 当前设备 + 前产品 + 后产品（方向性，(A,B) ≠ (B,A)）。
    /// ProductionDepartment/Stage 作为规则裁剪与唯一性上下文由 2号位/3号位 治理，不进运行时查找键（v1.2 §19.3）。
    /// </summary>
    public readonly record struct SetupExactKey(string OperationCode, int ResourceId, int FromMaterialId, int ToMaterialId);

    /// <summary>DEFAULT 规则键：当前工序 + 当前设备（v1.2 §四）。</summary>
    public readonly record struct SetupDefaultKey(string OperationCode, int ResourceId);

    /// <summary>Setup 解析结果类型（0号位 20260917 Q4 裁决：初始状态与规则缺失严格分开，不得合并分支）。</summary>
    public enum SetupOutcome
    {
        /// <summary>命中 EXACT 产品转换规则（§5 第一优先）。</summary>
        ExactHit,
        /// <summary>命中当前工序+设备默认规则（§5 第二优先）。</summary>
        DefaultHit,
        /// <summary>同产品连续 A→A 无显式规则，默认 0 分钟（§6）。</summary>
        SameProductZero,
        /// <summary>无上一产品 → 初始设备状态，Setup=0，记 INITIAL_SETUP_STATE 追踪说明（§14.3，非 ReasonCode）。</summary>
        InitialState,
        /// <summary>有前后产品但 EXACT/DEFAULT 均缺失 → 规则缺失降级 Setup=0，必须记「换型规则缺失」解释（§5 第三优先）。</summary>
        RuleMissing
    }

    /// <summary>
    /// SetupOutcome → 落库值（大写 4 态字符串）。契约源：0号位 20260922 裁决
    /// （Task.SetupSource 保留，枚举统一 4 态，不采用 5 值；SAME_PRODUCT/NONE 不作正式值）。
    /// 映射：ExactHit→EXACT、DefaultHit→DEFAULT、SameProductZero→DEFAULT
    /// （同产品连续无显式规则 = 业务默认不换型，非独立来源）、
    /// RuleMissing→SETUP_RULE_MISSING_ZERO_FALLBACK、InitialState→INITIAL_SETUP_STATE。
    /// 2号位 只承载原样落库，枚举→4 态转换在 1号位（本方法）。
    /// </summary>
    public static string SetupOutcomeToSource(SetupOutcome outcome) => outcome switch
    {
        SetupOutcome.ExactHit => "EXACT",
        SetupOutcome.DefaultHit => "DEFAULT",
        SetupOutcome.SameProductZero => "DEFAULT",
        SetupOutcome.RuleMissing => RuleMissingZeroFallbackType,
        SetupOutcome.InitialState => InitialSetupStateType,
        _ => RuleMissingZeroFallbackType
    };

    /// <summary>Setup 追踪统一 TraceType（0号位 20260917 正式回复 §5.1：TIME_CALCULATION）。</summary>
    public const string TraceType = "TIME_CALCULATION";

    /// <summary>初始设备状态解释类型符号锚（ExplainTrace 内部类型，非 ScheduleExplanationFact.ReasonCode——0号位 裁决项4 §5.1，INFO 级）。</summary>
    public const string InitialSetupStateType = "INITIAL_SETUP_STATE";

    /// <summary>DEFAULT 回退解释类型符号锚（0号位 正式回复 §5.2，INFO 级；定名 DEFAULT_SETUP_FALLBACK——2号位 20260920 回执建议，1号位 采纳，待三方词表终对齐）。</summary>
    public const string DefaultSetupFallbackType = "DEFAULT_SETUP_FALLBACK";

    /// <summary>换型规则缺失 0 分钟兜底解释类型符号锚（0号位 裁决项4 §5.3，WARNING 级；同时进 4号位「换型规则缺失/0分钟兜底」数据质量查询）。
    /// ContextData 需含 productionDepartmentId/stageCode/operationCode/resourceId/fromMaterialId/toMaterialId/setupMinutes——
    /// Dept/Stage 不在运行时查找键内（2号位 已按 Domain 裁剪），由调用方（Phase 接线时）从 Task 上下文补齐。</summary>
    public const string RuleMissingZeroFallbackType = "SETUP_RULE_MISSING_ZERO_FALLBACK";

    /// <summary>Setup 解析结果：分钟数 + 命中类型 + 追踪三元组（TraceLevel/ExplanationType/TraceMessage，按 0号位 §五口径；ExplainTrace 载体待 2号位 DTO 落地后写入）。</summary>
    public readonly record struct SetupResolution(
        decimal SetupMinutes,
        SetupOutcome Outcome,
        string? TraceMessage,
        string? TraceLevel = null,
        string? ExplanationType = null);

    /// <summary>
    /// 解析当前 Task 的换型时间（v1.2 §5 三层确定性命中，无额外 fallback）。
    /// </summary>
    /// <param name="operationCode">当前要排的 Task 自身小工序（不是上一 Task 的工序）。</param>
    /// <param name="resourceId">当前候选/实际设备。</param>
    /// <param name="fromMaterialId">该设备上一相邻 Task 的产品；null = 无可追溯上一产品（初始设备状态）。</param>
    /// <param name="toMaterialId">当前 Task 产品。</param>
    /// <param name="exactRules">EXACT 产品转换规则（2号位 装载，Phase1 适配传入）。</param>
    /// <param name="defaultRules">DEFAULT 工序+设备规则（同上）。</param>
    public SetupResolution ResolveSetup(
        string operationCode,
        int resourceId,
        int? fromMaterialId,
        int toMaterialId,
        IReadOnlyDictionary<SetupExactKey, decimal> exactRules,
        IReadOnlyDictionary<SetupDefaultKey, decimal> defaultRules)
        => ResolveSetupCore(operationCode, resourceId, fromMaterialId, toMaterialId, exactRules, defaultRules);

    /// <summary>纯函数解析核心（item1 接线阶段二：供 Phase2/Phase4 静态复用，实例方法 ResolveSetup 委托于此，行为一致）。</summary>
    internal static SetupResolution ResolveSetupCore(
        string operationCode,
        int resourceId,
        int? fromMaterialId,
        int toMaterialId,
        IReadOnlyDictionary<SetupExactKey, decimal> exactRules,
        IReadOnlyDictionary<SetupDefaultKey, decimal> defaultRules)
    {
        // 初始设备状态：无上一产品 → 不存在转换关系，Setup=0（不人为构造虚拟前产品，§0.1-4/§14.3）。
        // 追踪规格：0号位 正式回复 §5.1（TraceType=TIME_CALCULATION / TraceLevel=INFO / INITIAL_SETUP_STATE）。
        if (fromMaterialId is null)
        {
            return new SetupResolution(0m, SetupOutcome.InitialState,
                "当前资源无可追溯上一产品，按初始设备状态处理，Setup=0。",
                "INFO", InitialSetupStateType);
        }

        var from = fromMaterialId.Value;

        // 第一优先：EXACT 产品对（同产品 A→A 显式规则可覆盖默认 0，§6）。
        if (exactRules.TryGetValue(new SetupExactKey(operationCode, resourceId, from, toMaterialId), out var exactMinutes))
        {
            return new SetupResolution(exactMinutes, SetupOutcome.ExactHit, null);
        }

        // 同产品连续且无显式规则 → 0 分钟（§6；只比较前后产品，与上一工序无关）。
        if (from == toMaterialId)
        {
            return new SetupResolution(0m, SetupOutcome.SameProductZero, null);
        }

        // 第二优先：当前工序+当前设备默认规则（§四）。
        // 追踪规格：0号位 正式回复 §5.2（正常 fallback，INFO 轻量追踪；符号采 2号位 20260920 建议 DEFAULT_SETUP_FALLBACK）。
        if (defaultRules.TryGetValue(new SetupDefaultKey(operationCode, resourceId), out var defaultMinutes))
        {
            return new SetupResolution(defaultMinutes, SetupOutcome.DefaultHit,
                "未命中明确产品转换规则，使用当前工序/设备默认换型时间。",
                "INFO", DefaultSetupFallbackType);
        }

        // 第三优先：无规则 → 0 分钟 + 必须记录缺失解释（§5；禁止任何额外 fallback）。
        // 追踪规格：0号位 正式回复 §5.3（WARNING + SETUP_RULE_MISSING_ZERO_FALLBACK，进 4号位 数据质量查询）。
        return new SetupResolution(0m, SetupOutcome.RuleMissing,
            $"当前部门/Stage/工序[{operationCode}]/设备[{resourceId}]/前后产品[{from}→{toMaterialId}]未维护Setup规则，本次按0分钟计算。",
            "WARNING", RuleMissingZeroFallbackType);
    }

    /// <summary>
    /// 把 2号位/3号位 冻结的 <see cref="SetupTransitionRuleSnapshot"/> 列表（FrozenStrategySnapshot 第⑦块，
    /// r13376 落地）适配为运行时 EXACT/DEFAULT 查找字典。
    /// - 只取 IsActive 语义已由装载端保证（快照只含有效规则），此处按 RuleType 分流；
    /// - EXACT 要求 From/To 均有值，DEFAULT 忽略产品字段；
    /// - 同键重复时取第一条（唯一性由 3号位 发布前冲突校验保证，Solver 不随机选规则——v1.2 §十）。
    /// </summary>
    public static (Dictionary<SetupExactKey, decimal> Exact, Dictionary<SetupDefaultKey, decimal> Default) BuildRuleLookups(
        IEnumerable<SetupTransitionRuleSnapshot>? rules)
    {
        var exact = new Dictionary<SetupExactKey, decimal>();
        var def = new Dictionary<SetupDefaultKey, decimal>();

        if (rules is null) return (exact, def);

        foreach (var rule in rules)
        {
            if (string.Equals(rule.RuleType, "EXACT", StringComparison.Ordinal))
            {
                if (rule.FromMaterialId is null || rule.ToMaterialId is null)
                    continue;   // EXACT 缺产品 → 无效行，防御性跳过

                exact.TryAdd(new SetupExactKey(rule.OperationCode, rule.ResourceId, rule.FromMaterialId.Value, rule.ToMaterialId.Value),
                    rule.SetupMinutes);
            }
            else if (string.Equals(rule.RuleType, "DEFAULT", StringComparison.Ordinal))
            {
                def.TryAdd(new SetupDefaultKey(rule.OperationCode, rule.ResourceId), rule.SetupMinutes);
            }
            // 其它 RuleType 值不识别，防御性忽略。
        }

        return (exact, def);
    }

    /// <summary>
    /// 生产日窗口切分（0号位 20260917 Q1 裁决）：同一 Resource 的日历 Slot 按 Start 排序，
    /// 取 IsAvailable=true 的连续无间断区间合并为一个「连续可用生产窗口」；跨自然日零点不断开；
    /// 遇停机/非可用/班次断点结束当前窗口。生产日只是搜索边界，不重置设备上一产品状态（v1.2 §14.3）。
    /// </summary>
    /// <param name="slots">单一 Resource 的日历 Slot（Start, End, IsAvailable）。</param>
    /// <returns>合并后的连续生产窗口列表（按时间升序，空输入返回空列表）。</returns>
    public static List<(DateTime Start, DateTime End)> BuildProductionWindows(
        IEnumerable<(DateTime Start, DateTime End, bool IsAvailable)> slots)
    {
        var windows = new List<(DateTime Start, DateTime End)>();

        foreach (var slot in slots.Where(s => s.IsAvailable).OrderBy(s => s.Start))
        {
            if (windows.Count > 0 && windows[^1].End == slot.Start)
            {
                // 相邻且时间无间断 → 并入当前窗口（含跨零点场景）。
                windows[^1] = (windows[^1].Start, slot.End);
            }
            else if (windows.Count > 0 && slot.Start < windows[^1].End)
            {
                // 重叠可用段 → 扩展窗口末端（容错日历数据重叠）。
                if (slot.End > windows[^1].End)
                    windows[^1] = (windows[^1].Start, slot.End);
            }
            else
            {
                // 间断（停机/班次断点）→ 开新窗口。
                windows.Add((slot.Start, slot.End));
            }
        }

        return windows;
    }

    /// <summary>
    /// P1-02 item1 接线（阶段二）：动态 Setup 感知的槽查找（v1.2 §12：Setup 从初始 Resource/时间槽候选评价阶段就参与）。
    /// 鸡生蛋问题——Setup 取决于槽起点的前产品、前产品取决于槽位置、槽位置取决于 Setup 总时长——用有界迭代解：
    /// 以 floor 处前产品估算 Setup → 搜槽 → 用槽起点真实前产品重解析 → 更短则原窗必容得下（收敛返回）；
    /// 更长则以新值重搜。maxIterations 内未收敛则按最后解析值终搜一次（残余偏差由 Phase4 邻接重算/夜间 FULL 修正）。
    /// </summary>
    /// <param name="floor">最早占用开始时间（物料/依赖等下界，占用口径与 Phase2/Phase4 FindForwardSlot 用法一致）。</param>
    /// <param name="processDuration">加工时长（不含 Setup）。</param>
    /// <param name="findSlot">各 Phase 自己的日历感知槽查找：(最早开始, 含 Setup 总时长) → 占用窗；null = 无可行槽。</param>
    /// <returns>(占用窗[含Setup], Setup分钟, 解析结果)；null = 无可行槽。追踪三元组的写出待 ExplainTrace 载体（2号位 DTO）落地。</returns>
    internal static (TimeWindow Slot, decimal SetupMinutes, SetupResolution Resolution)? FindSlotWithDynamicSetup(
        DateTime floor,
        TimeSpan processDuration,
        int resourceId,
        string operationCode,
        int toMaterialId,
        ResourceProductTimeline timeline,
        IReadOnlyDictionary<SetupExactKey, decimal> exactRules,
        IReadOnlyDictionary<SetupDefaultKey, decimal> defaultRules,
        Func<DateTime, TimeSpan, TimeWindow?> findSlot,
        int maxIterations = 3)
    {
        var resolution = ResolveSetupCore(operationCode, resourceId,
            timeline.GetPrevMaterial(resourceId, floor), toMaterialId, exactRules, defaultRules);

        for (int i = 0; i < maxIterations; i++)
        {
            var slot = findSlot(floor, processDuration + TimeSpan.FromMinutes((double)resolution.SetupMinutes));
            if (slot == null) return null;

            var atSlot = ResolveSetupCore(operationCode, resourceId,
                timeline.GetPrevMaterial(resourceId, slot.Value.Start), toMaterialId, exactRules, defaultRules);

            if (atSlot.SetupMinutes <= resolution.SetupMinutes)
            {
                // 解析值 ≤ 搜索值：占用窗按真实 Setup 重算，必落在已验证的日历窗/空闲段内 → 收敛
                var occ = new TimeWindow(slot.Value.Start,
                    slot.Value.Start + TimeSpan.FromMinutes((double)atSlot.SetupMinutes) + processDuration);
                return (occ, atSlot.SetupMinutes, atSlot);
            }

            resolution = atSlot;   // 变大：以新 Setup 重搜
        }

        // 有界未收敛：按最后解析值终搜一次
        var finalSlot = findSlot(floor, processDuration + TimeSpan.FromMinutes((double)resolution.SetupMinutes));
        if (finalSlot == null) return null;
        var finalOcc = new TimeWindow(finalSlot.Value.Start,
            finalSlot.Value.Start + TimeSpan.FromMinutes((double)resolution.SetupMinutes) + processDuration);
        return (finalOcc, resolution.SetupMinutes, resolution);
    }
}

/// <summary>
/// P1-02 item1 接线（阶段二）：资源级「已排产品时间线」。
/// 维护 ResourceId → 按占用结束时间升序的 (End, MaterialId) 列表，支撑 v1.2 语义
/// 「FromMaterial = 当前设备上一相邻 Task 的产品」——按**时间邻接**查询（最晚 End ≤ 我的占用起点），
/// 而非按放置顺序（任务可能被排进中间空档，放置序 ≠ 时间序）。
///
/// 口径：
/// - 锁定继承 Task（Firm/Frozen/Execution）计入——它们是「可追溯上一产品」（v1.2 §14.3）；
/// - ExternalDomain ResourceBlocks 不计入——阻挡块不是 Task、无产品语义（查不到更早 Task 时按初始设备状态）；
/// - 放置/移动/合并时由调用方同步 Place/Remove（与 resourceOccupancy 成对维护）。
/// 性能：二分查询 O(log n) + 有序插入；10万 Task 规模标定如现瓶颈再换结构（性能标定阶段处理）。
/// </summary>
internal sealed class ResourceProductTimeline
{
    private Dictionary<int, List<(DateTime End, int MaterialId)>> _byResource = new();

    /// <summary>
    /// 克隆快照（**仅克隆实例非 null**）：记录「本实例某资源的 list 是否仍与快照共享」
    /// ⇒ 共享则写入前需深拷该 list（copy-on-write）。
    /// </summary>
    private Dictionary<int, List<(DateTime End, int MaterialId)>>? _pristine;

    /// <summary>
    /// 本实例**已被 Clone 过**：下游快照可能正引用本实例的 list ⇒ 本实例写入前必须先把
    /// 全部 list 换成自有副本，以**冻结**那些快照（否则下游 COW 会拷到已被我改过的状态）。
    /// </summary>
    private bool _sharedAsPristine;

    /// <summary>从既有 Task 集合构建（Phase2 Schedule 入口重置 / Phase4 兜底重跑时用）。</summary>
    public static ResourceProductTimeline FromTasks(IEnumerable<FinalTaskDraft> tasks)
    {
        var timeline = new ResourceProductTimeline();
        foreach (var t in tasks)
        {
            // 非资源 Task（ResourceId=NULL）不占资源，不进产品时间线
            if (t.ResourceId is not int rid) continue;
            timeline.Place(rid, t.PlannedEndTime, t.MaterialId);
        }
        return timeline;
    }

    /// <summary>
    /// 拷贝（C桶候选内择优用）：每个候选 Path 必须在**同一初始上下文**上试排，互不污染；
    /// 试排结果择优后再在真实上下文上重跑选中候选落定（0号位 2026-10-07 裁决 Q-2：
    /// 「候选评价必须看到当前 Domain 真实的资源占用、Calendar、已排 Task…」，故不能只跑孤立试算）。
    ///
    /// ── 2026-10-08 性能（0号位《未命名的Markdown文件 (2)(1).md》§7.1/§7.2 + §十 允许的「增量 Delta /
    ///    Copy-on-write」）──
    /// 旧实现**每次**深拷 `_byResource` 的**每一个** list。实测本方法在 42,261 Task 场景被调用
    /// **94,940 次**（= 批方案候选试跑 14,046 + 候选路径试排 80,894），累计 17,778 ms = **ΣPhase 的 13.1%**，
    /// 且因「每次 O(全部资源 × 全部占用窗)」而随 N 呈 **O(N²)**（任务 ×1.97 ⇒ 本项耗时 ×3.30，实测）。
    ///
    /// 新实现改为**惰性深拷**：`Clone()` 只做 **O(资源数)** 的浅拷（list 仍共享）+ 一份**冻结快照**；
    /// 谁的 list 真被写、谁才付费深拷该 list。单条需求试排最多命中其工序合格资源（本例 ≤3 个），
    /// 而资源数为 200 ⇒ **单次克隆的 list 复制量降到约 1/66**。
    /// **语义零变化**：`GetPrevMaterial` 读到的内容、以及各候选之间的隔离性与旧实现逐字相同
    /// （脱钩规则见 <see cref="Mutable"/>，对「源被克隆后再写」「克隆再被克隆」两种情形都作了冻结处理）。
    /// </summary>
    public ResourceProductTimeline Clone()
    {
        var clone = new ResourceProductTimeline
        {
            // 浅拷：只复制「资源 Id → list 引用」这一层（O(资源数)），list 本体暂共享
            _byResource = new Dictionary<int, List<(DateTime End, int MaterialId)>>(_byResource),
            // 冻结快照：与浅拷分开一份，作为「哪些 list 仍共享」的判据，且本身**永不被改写**
            _pristine = new Dictionary<int, List<(DateTime End, int MaterialId)>>(_byResource),
        };
        // 通知本实例：你的 list 已被快照引用 ⇒ 你下次写入前要先自脱钩
        _sharedAsPristine = true;
        return clone;
    }

    /// <summary>
    /// 取「可写」的资源 list（必要时先 copy-on-write —— **逐资源**脱钩，不整表拷贝）。
    ///
    /// 不变式：**任何实例都不得原地修改一个可能被别人（父实例 或 任何快照）引用的 list**。
    /// 需要脱钩的两种情形（其余情况可原地写，因为该 list 已为本实例独占）：
    ///   ① 本实例**已被 Clone 过**（`_sharedAsPristine`）⇒ 下游快照可能正引用该 list；
    ///   ② 本实例是**克隆**，且该资源 list 仍与自己的冻结快照共享 ⇒ 父侧仍引用该 list。
    /// 脱钩方式统一为「把**自己这一条**换成新 list（拷贝），再在副本上写」——
    /// 快照与被共享的原 list **始终不被改写**，故下游 COW 拿到的永远是克隆时刻的状态。
    ///
    /// ⚠ 为什么不整表脱钩：winner 落定是**逐批**写真实上下文（`clone ×k → 写真实 → clone ×k …` 交替），
    ///   整表脱钩会**每批**触发一次 O(全部资源 × 全部占用窗) 的拷贝 ⇒ 实测收益归零。
    ///   逐资源脱钩下，单批只为其工序命中的 ≤3 个资源付费。
    /// </summary>
    private List<(DateTime End, int MaterialId)> Mutable(int resourceId)
    {
        if (!_byResource.TryGetValue(resourceId, out var list))
        {
            list = new List<(DateTime End, int MaterialId)>();
            _byResource[resourceId] = list;
            return list;
        }

        var sharedWithMySnapshot = _pristine is not null
                                   && _pristine.TryGetValue(resourceId, out var snapshot)
                                   && ReferenceEquals(list, snapshot);

        if (_sharedAsPristine || sharedWithMySnapshot)
        {
            list = new List<(DateTime End, int MaterialId)>(list);
            _byResource[resourceId] = list;
        }

        return list;
    }

    /// <summary>上一相邻 Task 产品：最晚占用结束时间 ≤ occStart 者；null = 无可追溯上一产品（初始设备状态，Setup=0）。</summary>
    public int? GetPrevMaterial(int resourceId, DateTime occStart)
    {
        if (!_byResource.TryGetValue(resourceId, out var list) || list.Count == 0)
            return null;

        // 二分：最右一个 End <= occStart
        int lo = 0, hi = list.Count - 1, ans = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (list[mid].End <= occStart) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans >= 0 ? list[ans].MaterialId : null;
    }

    /// <summary>登记一个已放置 Task 的产品与占用结束时间（有序插入）。</summary>
    public void Place(int resourceId, DateTime occEnd, int materialId)
    {
        // COW：源被克隆过后，源自己写入也须先脱钩（否则会改到下游快照可见的 list）
        var list = Mutable(resourceId);

        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (list[mid].End < occEnd) lo = mid + 1;
            else hi = mid;
        }
        list.Insert(lo, (occEnd, materialId));
    }

    /// <summary>移除一个 Task 的登记（移动/重建前调用；按 End+MaterialId 匹配第一条）。</summary>
    public void Remove(int resourceId, DateTime occEnd, int materialId)
    {
        if (!_byResource.TryGetValue(resourceId, out _))
            return;

        // COW：同上 —— 任何写入前先确保该 list 为本实例独有
        var list = Mutable(resourceId);

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].End == occEnd && list[i].MaterialId == materialId)
            {
                list.RemoveAt(i);
                return;
            }
        }
    }
}

/// <summary>
/// P1-02 item1（夜间 FULL）：确定性伪随机源（LCG，Knuth MMIX 常数）。
/// 不用 BCL Random——其算法跨 .NET 版本可变，会破坏「同 Run 重放同结果」（Run 重放/审计/回归依赖确定性）。
/// 种子由 ScheduleRunId/PlanVersionId 派生：同一 Run 重放 → 同一搜索轨迹 → 同一结果。
/// public：性能标定工装（LPS.APS.Tests/Benchmarks/SolverBenchmark）复用同一 LCG，保证基准与生产轨迹一致
/// （Scheduling 无 InternalsVisibleTo(Tests)，故放宽可见性；无签名变化）。
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(long seed)
    {
        _state = unchecked((ulong)seed * 6364136223846793005UL + 1442695040888963407UL);
    }

    public uint NextUInt()
    {
        _state = unchecked(_state * 6364136223846793005UL + 1442695040888963407UL);
        return (uint)(_state >> 33);   // 高位质量更好
    }

    /// <summary>[0, maxExclusive) 均匀整数；maxExclusive ≤ 0 返回 0。</summary>
    public int Next(int maxExclusive)
        => maxExclusive <= 0 ? 0 : (int)(NextUInt() % (uint)maxExclusive);
}
