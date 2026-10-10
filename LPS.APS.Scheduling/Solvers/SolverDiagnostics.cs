using System.Threading;

namespace LPS.APS.Scheduling.Solvers;

/// <summary>
/// **求解器性能计数器（诊断用，非契约）**。
///
/// 【为什么在这里而不在 Core】
///   0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§九 要求性能压测输出
///   「BatchPlanCandidate 试跑次数 / Routing 试跑次数 / CloneOccupancy 次数 / CloneTaskList 次数 /
///     SlotSearch 次数 / Phase4 传播 Task 数」。
///   但把 Phase 级字段加进 `DomainSolveResult` / `SolveSummary` 属于 **Core 契约变更**（Core 归 2号位）
///   ⇒ 本类放在 1号位 自己的 `LPS.APS.Scheduling` 内，**不触碰任何契约 DTO**，
///     由压测工装（`LPS.APS.Tests/Benchmarks/`）直接读取。
///
/// 【并发语义（为什么用 AsyncLocal 而不是全局 static long）】
///   `Counters` 对象存在 <see cref="AsyncLocal{T}"/> 里，由 <see cref="BeginScope"/> 在**调用方**
///   建立 ⇒ `await SolveAsync(...)` 时 ExecutionContext 把该引用流进求解器内部，
///   内部只**改对象字段**（引用不变）⇒ 调用方读同一个对象即得本次数。
///   · 并发求解（Web 端多 Domain 并行）各持**各自**的 Counters ⇒ 互不污染；
///   · **未开 scope 时**（正常生产路径）所有 `Count*` 调用只做一次 null 判断 ⇒ 近乎零开销、零行为影响。
///   ⇒ 计数器**只增不减**、**不参与任何业务判定**，纯观测。
///
/// 【明确不做的】
///   · 不统计耗时（耗时由压测工装用 `Stopwatch` 在 Phase 边界测，避免热点内取时钟）；
///   · 不写 `SolveTraceNotes`（trace 体积约束 + 与 2号位 的 ReasonCode 命名待定项）。
/// </summary>
internal static class SolverDiagnostics
{
    /// <summary>本次求解的计数器（字段为 `long`，仅供单线程求解内自增；读取前请确保求解已结束）。</summary>
    internal sealed class Counters
    {
        /// <summary>`SelectBestBatchPlan` 里每个**批方案候选**触发一次完整 `RunBatchPlan` 试跑。</summary>
        public long BatchPlanCandidateTrials;

        /// <summary>`RunBatchPlan` 里每个（执行批 × Routing 候选）触发一次候选试排。</summary>
        public long RoutingTrials;

        /// <summary>`CloneOccupancy` 调用次数（每次复制全资源占用表）。</summary>
        public long CloneOccupancy;

        /// <summary>`CloneTaskList` 调用次数（每次复制全量已排任务表，O(N)）。</summary>
        public long CloneTaskList;

        /// <summary>`CloneShares` 调用次数（每次重建全量份额字典，O(N)）。</summary>
        public long CloneShares;

        /// <summary>时间槽搜索次数（正排 `FindFirstAvailableSlot` + 倒排 `FindBackwardSlot`）。</summary>
        public long SlotSearches;

        /// <summary>Phase4 传播过程中实际访问（重排/改期）的 Task 次数。</summary>
        public long Phase4PropagatedTasks;

        /// <summary>Phase5 `CompactGaps`（空隙压实）**门控放行并进入优化体**的次数（0 = 未进入）。</summary>
        public long Phase5CompactionRuns;

        /// <summary>Phase5 `OptimizeSetupSequences`（Setup 序列优化）**门控放行并进入优化体**的次数。</summary>
        public long Phase5SetupOptimizationRuns;

        /// <summary>
        /// Phase5 `OptimizeSegment`（**资源 × 连续生产窗口 × 固定锚点间可移动段**）**真正进入有界局部优化体**
        ///   的段数 —— 即「段内 ≥2 个 Task 且全部可移动」的段。
        ///   与 <see cref="Phase5SetupOptimizationRuns"/> 的区别：后者只说明「整 Run 未被否决」，
        ///   本计数说明「**确实有一个可重排的段被送进优化体**」（P1-02 验收：混合方向下 FORWARD 段必须可达）。
        /// </summary>
        public long Phase5SetupSegmentsOptimized;

        /// <summary>Phase 1（硬约束构建）耗时 ms。</summary>
        public long Phase1Ms;

        /// <summary>Phase 2（初始有限产能排程）耗时 ms。</summary>
        public long Phase2Ms;

        /// <summary>Phase 3（可行性与延期诊断）耗时 ms。</summary>
        public long Phase3Ms;

        /// <summary>Phase 4（有界局部修复）耗时 ms。</summary>
        public long Phase4Ms;

        /// <summary>Phase 5（压缩空隙与最终评价）耗时 ms。</summary>
        public long Phase5Ms;

        // ══════════════════════════════════════════════════════════════════
        // **热点累计耗时**（ticks；含嵌套，故各项之和会大于 Phase2Ms —— 用途是「谁最慢」，
        //   不是「把 Phase2 拆完」）。用 `Stopwatch.GetTimestamp()` 零分配采集：
        //   热点方法每次调用都可能执行 9.5 万次以上，`new Stopwatch()` 会自己成为热点。
        // ══════════════════════════════════════════════════════════════════
        private readonly long[] _hotspotTicks = new long[HotspotCount];

        /// <summary>热点数量（= <c>SolverDiagnostics.Hotspot</c> 枚举项数）。</summary>
        public const int HotspotCount = 8;

        internal void AddHotspotTicks(int hotspot, long ticks)
        {
            if ((uint)hotspot < HotspotCount) _hotspotTicks[hotspot] += ticks;
        }

        /// <summary>取热点累计耗时（ms）。</summary>
        public double HotspotMs(int hotspot)
            => _hotspotTicks[hotspot] * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>热点标识（索引须与 <see cref="Counters.HotspotCount"/> 对齐）。</summary>
    internal static class Hotspot
    {
        /// <summary>候选试排里的 `ProductTimeline.Clone()`。</summary>
        public const int TimelineClone = 0;
        /// <summary>`CloneOccupancy`（复制全资源占用表）。</summary>
        public const int CloneOccupancy = 1;
        /// <summary>`new List&lt;FinalTaskDraft&gt;(scheduledTasks)`（复制全量任务表，O(N)）。</summary>
        public const int CloneTaskList = 2;
        /// <summary>`CloneShares`（重建全量份额字典，O(N)）。</summary>
        public const int CloneShares = 3;
        /// <summary>时间槽搜索（正排 `FindFirstAvailableSlot` + 倒排 `FindBackwardSlot`）。</summary>
        public const int SlotSearch = 4;
        /// <summary>`RunBatchPlan` 整体（批方案候选试跑，含其内部候选路径试排）。</summary>
        public const int BatchPlanTrial = 5;
        /// <summary>`RunDemandSchedule`（单条候选路径的完整试排）。</summary>
        public const int DemandSchedule = 6;
        /// <summary>Phase4 传播循环整体。</summary>
        public const int Phase4Propagation = 7;
    }

    private static readonly AsyncLocal<Counters?> Current = new();

    /// <summary>
    /// 建立计数作用域。**必须在调用 `SolveAsync` 之前**建立（ExecutionContext 在 async 方法入口捕获）。
    /// </summary>
    internal static Scope BeginScope() => new();

    /// <summary>计数作用域：`Counters` 在 <see cref="Dispose"/> 后仍可安全读取（只是不再累加）。</summary>
    internal sealed class Scope : System.IDisposable
    {
        private readonly Counters? _previous;

        internal Scope()
        {
            Counters = new Counters();
            _previous = Current.Value;
            Current.Value = Counters;
        }

        /// <summary>本次作用域的计数器。</summary>
        public Counters Counters { get; }

        public void Dispose() => Current.Value = _previous;
    }

    /// <summary>
    /// 开始一个 Phase 计时。**未开 scope 时返回 `null`** ⇒ 调用方零开销、零行为影响
    /// （Phase 边界由 <see cref="FiniteCapacitySolver.SolveAsync"/> 埋点）。
    /// </summary>
    internal static System.Diagnostics.Stopwatch? StartPhase()
        => Current.Value is null ? null : System.Diagnostics.Stopwatch.StartNew();

    /// <summary>结束 Phase 计时并登记到对应 Phase 槽（<paramref name="phase"/> ∈ 1..5）。</summary>
    internal static void EndPhase(System.Diagnostics.Stopwatch? sw, int phase)
    {
        if (sw is null) return;
        sw.Stop();
        if (Current.Value is not { } c) return;

        switch (phase)
        {
            case 1: c.Phase1Ms = sw.ElapsedMilliseconds; break;
            case 2: c.Phase2Ms = sw.ElapsedMilliseconds; break;
            case 3: c.Phase3Ms = sw.ElapsedMilliseconds; break;
            case 4: c.Phase4Ms = sw.ElapsedMilliseconds; break;
            case 5: c.Phase5Ms = sw.ElapsedMilliseconds; break;
        }
    }

    internal static void CountBatchPlanCandidateTrial()
    {
        if (Current.Value is { } c) c.BatchPlanCandidateTrials++;
    }

    internal static void CountRoutingTrial()
    {
        if (Current.Value is { } c) c.RoutingTrials++;
    }

    internal static void CountCloneOccupancy()
    {
        if (Current.Value is { } c) c.CloneOccupancy++;
    }

    internal static void CountCloneTaskList()
    {
        if (Current.Value is { } c) c.CloneTaskList++;
    }

    internal static void CountCloneShares()
    {
        if (Current.Value is { } c) c.CloneShares++;
    }

    internal static void CountSlotSearch()
    {
        if (Current.Value is { } c) c.SlotSearches++;
    }

    internal static void CountPhase4PropagatedTask()
    {
        if (Current.Value is { } c) c.Phase4PropagatedTasks++;
    }

    /// <summary>Phase5 `CompactGaps` 门控放行（进入压实优化体）时 +1。纯观测，不参与判定。</summary>
    internal static void CountPhase5CompactionRun()
    {
        if (Current.Value is { } c) c.Phase5CompactionRuns++;
    }

    /// <summary>Phase5 `OptimizeSetupSequences` 门控放行（进入序列优化体）时 +1。纯观测，不参与判定。</summary>
    internal static void CountPhase5SetupOptimizationRun()
    {
        if (Current.Value is { } c) c.Phase5SetupOptimizationRuns++;
    }

    /// <summary>Phase5 `OptimizeSegment` 真正进入有界局部优化体（可重排段）时 +1。纯观测，不参与判定。</summary>
    internal static void CountPhase5SetupSegmentOptimized()
    {
        if (Current.Value is { } c) c.Phase5SetupSegmentsOptimized++;
    }

    // ══════════════════════════════════════════════════════════════════
    // 热点计时（**零分配**）：未开 scope 时 `HotspotStart()` 返回 0 ⇒ `HotspotEnd` 立刻返回
    //   ⇒ 生产路径只多两次比较，无行为影响、无分配。
    // ══════════════════════════════════════════════════════════════════

    /// <summary>开始一次热点计时；未开 scope 返回 0（调用方原样传回 <see cref="HotspotEnd"/>）。</summary>
    internal static long HotspotStart()
        => Current.Value is null ? 0L : System.Diagnostics.Stopwatch.GetTimestamp();

    /// <summary>结束热点计时并累计到 <paramref name="hotspot"/> 槽。</summary>
    internal static void HotspotEnd(long startTimestamp, int hotspot)
    {
        if (startTimestamp == 0L) return;
        if (Current.Value is not { } c) return;
        c.AddHotspotTicks(hotspot, System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp);
    }
}
