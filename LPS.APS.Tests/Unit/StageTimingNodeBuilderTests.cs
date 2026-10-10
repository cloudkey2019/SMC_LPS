using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;
using RoutingOperation = LPS.APS.Core.Entities.APS.RoutingOperation;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 无 Routing Stage 纯内存时间节点（0号位 2026-09-29 裁决 §十四第 4/5/9 项）契约测试。
/// 纯内存，直接调 <see cref="StageTimingNodeBuilder.BuildCore"/> —— 不碰数据库、不起 Solver、不读 Context。
///
/// 覆盖：
/// ④ 节点标记恒 `STAGE_TIMING`（不得被当真实 Task / 不得生成 TaskNo）；
/// ⑤ `MatchLevel` 4 值契约：3 个合法值原样采纳；旧名 `MATERIAL`/`FAMILY` ⇒ 违例兜底；
///    命中值原样透传（**含 0 / 负，我方不做符号判断**）；无 fact ⇒ 兜底；`MISSING` 带值 ⇒ 用其值、不叠加；
/// ⑨ 只累加末端真实 Task **之后**的 Stage；整条无真实 Task 时自 `PlanningStart` 起累计；
/// 边界：无 Stage 链不猜顺序 / 求解范围外 Stage 不算 / 非 DEFAULT 路由不算有工序 / 同 `StageSeq` 定序可复现。
/// </summary>
public class StageTimingNodeBuilderTests
{
    private static readonly DateTime Start = new(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime TerminalEnd = new(2026, 9, 10, 8, 0, 0);

    private const string Timing = "STAGE_TIMING";
    private const string Missing = "STAGE_LEADTIME_MISSING";

    // ─────────────────────────── ④ 纯内存标记 ───────────────────────────

    [Fact]
    public void Segments_AreMarkedAsStageTiming_NotRealTasks()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT") },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);
        var seg = Assert.Single(fact.StageTimings);

        Assert.Equal(Timing, seg.NodeType);
        Assert.Equal("CN_FINAL", seg.StageCode);
        Assert.Equal(4m, seg.LeadTimeHours);
        Assert.Equal("DEPT_EXACT", seg.MatchLevel);
        Assert.Equal(TerminalEnd, seg.StartTime);
        Assert.Equal(TerminalEnd.AddHours(4), seg.EndTime);

        Assert.Equal(TerminalEnd.AddHours(4), fact.CompletionTime);
        Assert.Equal(TerminalEnd.AddHours(4), fact.AvailableTime);
        Assert.Equal("F2", fact.TerminalFinalDraftId);
        Assert.Equal(1001, fact.MaterialId);
        Assert.Equal(1L, fact.AllocationSequence);
    }

    // ─────────────────────────── ⑤ 4 值契约 ───────────────────────────

    [Theory]
    [InlineData("DEPT_EXACT", 120)]
    [InlineData("FACTORY_STAGE_DEFAULT", 96)]
    [InlineData("GLOBAL_STAGE_DEFAULT", 48)]
    public void LegalLevels_AreAdoptedVerbatim(string level, int hours)
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", hours, level) },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var seg = Assert.Single(Assert.Single(result).StageTimings);

        Assert.Equal(level, seg.MatchLevel);
        Assert.Equal(hours, seg.LeadTimeHours);
        Assert.Equal(TerminalEnd.AddHours(hours), seg.EndTime);
    }

    /// <summary>
    /// 旧名 `MATERIAL` / `FAMILY` = 契约违例（0号位 §4.3 已移除；1号位 回执 §四 定 4 个之外一律兜底）。
    /// 关键：**违例名不得向外透传** —— 否则下游会把它当合法层级审计。
    /// </summary>
    [Theory]
    [InlineData("MATERIAL")]
    [InlineData("FAMILY")]
    public void LegacyLevels_AreContractViolations_FallBackTo72(string legacyLevel)
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", 120m, legacyLevel) },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var seg = Assert.Single(Assert.Single(result).StageTimings);

        Assert.Equal(72m, seg.LeadTimeHours);
        Assert.Equal(Missing, seg.MatchLevel);
        Assert.NotEqual(legacyLevel, seg.MatchLevel);
    }

    /// <summary>
    /// 实测 4 行 `*_FINAL` 的 `LeadTimeDays = 0.00`。
    ///
    /// ⚠️ **2026-09-29 口径更正（2号位 r13519 载入 Core 契约）**：早先那条
    /// 「命中到参数行但折算值 ≤ 0 ⇒ 视同未命中 ⇒ 72h 兜底」**已作废**。
    /// 现行口径：**命中就是命中** —— 值 0（或负）**按命中产出**，`MatchLevel` 保持实际命中级。
    /// 理由：0 是参数行写下的值，装载层与消费侧都**不得替参数侧改判**。
    ///
    /// ⇒ 本用例锁的是「我方**不做符号判断**」这条决定：值原样透传，层级**不得**被改写成 `MISSING`。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ExplicitValue_IsPassedThroughVerbatim_NoSignJudgement(int hours)
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", hours, "FACTORY_STAGE_DEFAULT") },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);
        var seg = Assert.Single(fact.StageTimings);

        Assert.Equal((decimal)hours, seg.LeadTimeHours);
        Assert.Equal("FACTORY_STAGE_DEFAULT", seg.MatchLevel);   // 不得被改写成 MISSING
        Assert.Equal(TerminalEnd.AddHours(hours), fact.CompletionTime);
    }

    /// <summary>
    /// 2号位 装载层对「三级全不中」与「命中但值 ≤ 0」统一产 `LeadTimeHours = 72` + `MISSING`
    /// ⇒ 1号位 **用其值**，不得再加一次 72（名字里的 MISSING 指「参数未命中」，不是「本条无提前期」）。
    /// </summary>
    [Fact]
    public void MissingLevelWithValue_UsesItsValue_NotDoubled()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", 72m, Missing) },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);
        var seg = Assert.Single(fact.StageTimings);

        Assert.Equal(72m, seg.LeadTimeHours);
        Assert.Equal(Missing, seg.MatchLevel);
        Assert.Equal(TerminalEnd.AddHours(72), fact.CompletionTime);
    }

    /// <summary>防御分支：该 (物料, 阶段) 一条 fact 都没有 ⇒ 仍不得静默 0 小时（0号位 §4.4 明禁）。</summary>
    [Fact]
    public void NoFact_FallsBackTo72()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var seg = Assert.Single(Assert.Single(result).StageTimings);

        Assert.Equal(72m, seg.LeadTimeHours);
        Assert.Equal(Missing, seg.MatchLevel);
    }

    // ─────────────────────────── ⑨ 时间出口 ───────────────────────────

    /// <summary>
    /// 只取末端真实 Task **之后**的 Stage：`CN_PRE`(Seq=1) 在末端之前 ⇒ 不得计入，
    /// 否则会把本次不求解、已完成的上游阶段提前期凭空加进来。
    /// </summary>
    [Fact]
    public void OnlyStagesAfterTerminal_AreAccumulated()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[]
                {
                    Fact(1001, "CN_PRE", 10m, "DEPT_EXACT"),
                    Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT")
                },
                chains: new[] { Chain(1001, ("CN_PRE", 1), ("CN_MACH", 2), ("CN_FINAL", 3)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_PRE", "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);
        var seg = Assert.Single(fact.StageTimings);

        Assert.Equal("CN_FINAL", seg.StageCode);
        Assert.Equal(TerminalEnd, seg.StartTime);
        Assert.Equal(TerminalEnd.AddHours(4), fact.CompletionTime);
        Assert.DoesNotContain(fact.StageTimings, s => s.StageCode == "CN_PRE");
    }

    /// <summary>整条需求无真实 Operation Task ⇒ 自 `PlanningStart` 起按 Stage 链顺序纯累计（0号位 §10.3）。</summary>
    [Fact]
    public void NoRealTask_AccumulatesFromPlanningStart()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[]
                {
                    Fact(1001, "CN_PRE", 10m, "DEPT_EXACT"),
                    Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT")
                },
                chains: new[] { Chain(1001, ("CN_PRE", 1), ("CN_MACH", 2), ("CN_FINAL", 3)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_PRE", "CN_MACH", "CN_FINAL"),
            Terminals());

        var fact = Assert.Single(result);

        Assert.Equal(2, fact.StageTimings.Count);
        Assert.Equal("CN_PRE", fact.StageTimings[0].StageCode);
        Assert.Equal(Start, fact.StageTimings[0].StartTime);
        Assert.Equal("CN_FINAL", fact.StageTimings[1].StageCode);
        Assert.Equal(Start.AddHours(10), fact.StageTimings[1].StartTime);
        Assert.Equal(Start.AddHours(14), fact.CompletionTime);
        Assert.Null(fact.TerminalFinalDraftId);
    }

    // ─────────────────────────── 边界 ───────────────────────────

    /// <summary>无 Stage 链 ⇒ **不猜顺序**，该物料的无 Routing Stage 一律不并入（宁可少算，不可凭空多算）。</summary>
    [Fact]
    public void NoStageChain_DoesNotGuessOrder()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT") },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);

        Assert.Empty(fact.StageTimings);
        Assert.Equal(TerminalEnd, fact.CompletionTime);
    }

    /// <summary>求解范围（`EffectiveStages`）之外的 Stage 不算 —— 否则会把本次不求解的 Stage 也算进来。</summary>
    [Fact]
    public void StageOutsideEffectiveScope_IsIgnored()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT") },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);

        Assert.Empty(fact.StageTimings);
        Assert.Equal(TerminalEnd, fact.CompletionTime);
    }

    /// <summary>在求解范围内、但不在 Stage 链里 ⇒ 无顺序依据，不得并入。</summary>
    [Fact]
    public void StageInScopeButAbsentFromChain_IsIgnored()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "TJ_OUTS", 120m, "GLOBAL_STAGE_DEFAULT") },
                chains: new[] { Chain(1001, ("CN_MACH", 1)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "TJ_OUTS"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);

        Assert.Empty(fact.StageTimings);
        Assert.Equal(TerminalEnd, fact.CompletionTime);
    }

    /// <summary>V1 只认 `RouteCode = "DEFAULT"`；非 DEFAULT 路径的工序不构成「该 Stage 有工序」。</summary>
    [Fact]
    public void NonDefaultRouteOperation_DoesNotCountAsRouting()
    {
        var demand = new[] { Demand("D1", 1001) };
        var chain = new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) };
        var facts = new[] { Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT") };
        var terminals = Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd));

        var withAltRoute = Run(
            Req(demand, facts, chain, new[] { Op(1001, "M01", "CN_MACH"), Op(1001, "X01", "CN_FINAL", "ALT") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            terminals);

        var withDefaultRoute = Run(
            Req(demand, facts, chain, new[] { Op(1001, "M01", "CN_MACH"), Op(1001, "X01", "CN_FINAL", "DEFAULT") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            terminals);

        Assert.Single(Assert.Single(withAltRoute).StageTimings);      // 非 DEFAULT ⇒ 仍算无工序
        Assert.Empty(Assert.Single(withDefaultRoute).StageTimings);   // DEFAULT ⇒ 有工序，不建时间节点
    }

    /// <summary>同 `StageSeq` 时按 `StageCode` 定序 ⇒ 结果可复现（不得依赖数据库返回顺序）。</summary>
    [Fact]
    public void SameStageSeq_IsOrderedByStageCode_Deterministically()
    {
        var request = Req(new[] { Demand("D1", 1001) },
            facts: new[]
            {
                Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT"),
                Fact(1001, "BJ_FINAL", 6m, "DEPT_EXACT")
            },
            chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 3), ("BJ_FINAL", 3)) },
            ops: new[] { Op(1001, "M01", "CN_MACH") });
        var stages = Stages(1001, "CN_MACH", "CN_FINAL", "BJ_FINAL");
        var terminals = Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd));

        var first = Run(request, stages, terminals);
        var second = Run(request, stages, terminals);

        var segs = Assert.Single(first).StageTimings;
        Assert.Equal(new[] { "BJ_FINAL", "CN_FINAL" }, segs.Select(s => s.StageCode).ToArray());
        Assert.Equal(
            segs.Select(s => (s.StageCode, s.StartTime, s.EndTime)).ToArray(),
            Assert.Single(second).StageTimings.Select(s => (s.StageCode, s.StartTime, s.EndTime)).ToArray());
    }

    // ─────────────────────────── RequiredStageCompletionTime ───────────────────────────

    /// <summary>供给阈值 Stage 落在末端**之后**的无 Routing 段 ⇒ 取其段末。</summary>
    [Fact]
    public void RequiredStageAfterTerminal_ReportsThatSegmentEnd()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001, requiredStage: "CN_FINAL") },
                facts: new[] { Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT") },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);

        Assert.Equal("CN_FINAL", fact.RequiredStageCode);
        Assert.Equal(TerminalEnd.AddHours(4), fact.RequiredStageCompletionTime);
        Assert.Equal(TerminalEnd.AddHours(4), fact.CompletionTime);
    }

    /// <summary>供给阈值 Stage 在末端真实 Task 处或之前 ⇒ 完成时间即末端 Task.End（不必等无 Routing 段）。</summary>
    [Fact]
    public void RequiredStageAtOrBeforeTerminal_ReportsTerminalEnd()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001, requiredStage: "CN_MACH") },
                facts: new[] { Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT") },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);

        Assert.Equal(TerminalEnd, fact.RequiredStageCompletionTime);
        Assert.Equal(TerminalEnd.AddHours(4), fact.CompletionTime); // 整条仍算到 CN_FINAL
    }

    /// <summary>无供给阈值语义（`RequiredStageCode` 为 null）⇒ 该字段为 null，不臆造。</summary>
    [Fact]
    public void NoRequiredStage_LeavesRequiredStageCompletionTimeNull()
    {
        var result = Run(
            Req(new[] { Demand("D1", 1001) },
                facts: new[] { Fact(1001, "CN_FINAL", 4m, "DEPT_EXACT") },
                chains: new[] { Chain(1001, ("CN_MACH", 1), ("CN_FINAL", 2)) },
                ops: new[] { Op(1001, "M01", "CN_MACH") }),
            Stages(1001, "CN_MACH", "CN_FINAL"),
            Terminals(Task("F2", "D1", 1001, "CN_MACH", "M01", TerminalEnd)));

        var fact = Assert.Single(result);

        Assert.Null(fact.RequiredStageCode);
        Assert.Null(fact.RequiredStageCompletionTime);
    }

    // ─────────────────────────── 夹具 ───────────────────────────

    private static List<DemandCompletionFact> Run(
        DomainSolveRequest request,
        IReadOnlyDictionary<int, HashSet<string>> stages,
        IReadOnlyDictionary<string, FinalTaskDraft> terminals)
        => StageTimingNodeBuilder.BuildCore(request, stages, terminals);

    private static DomainSolveRequest Req(
        IReadOnlyList<LogicalProductionDemand> demands,
        IReadOnlyList<StageLeadTimeFact>? facts = null,
        IReadOnlyList<StageSequenceChain>? chains = null,
        IReadOnlyList<RoutingOperation>? ops = null)
        => new()
        {
            PlanningStart = Start,
            PlanningEnd = Start.AddYears(100),
            LogicalProductionDemands = demands,
            StageLeadTimes = facts ?? Array.Empty<StageLeadTimeFact>(),
            StageSequenceChains = chains ?? Array.Empty<StageSequenceChain>(),
            RoutingOperations = ops ?? Array.Empty<RoutingOperation>()
        };

    private static LogicalProductionDemand Demand(string key, int materialId, string? requiredStage = null)
        => new()
        {
            LogicalDemandKey = key,
            MaterialId = materialId,
            AllocationSequence = 1,
            RequiredStageCode = requiredStage
        };

    private static StageSequenceChain Chain(int materialId, params (string Stage, int Seq)[] steps)
        => new()
        {
            MaterialId = materialId,
            Stages = steps.Select(s => new StageSequenceStep { StageCode = s.Stage, StageSeq = s.Seq }).ToList()
        };

    private static StageLeadTimeFact Fact(int materialId, string stageCode, decimal hours, string level)
        => new()
        {
            MaterialId = materialId,
            StageCode = stageCode,
            LeadTimeHours = hours,
            MatchLevel = level
        };

    private static RoutingOperation Op(int materialId, string operationCode, string stageCode, string routeCode = "DEFAULT")
        => new()
        {
            MaterialId = materialId,
            OperationCode = operationCode,
            StageCode = stageCode,
            RouteCode = routeCode
        };

    private static FinalTaskDraft Task(
        string draftId, string demandKey, int materialId, string stageCode, string operationCode, DateTime end)
        => new()
        {
            FinalDraftId = draftId,
            SourceDraftId = demandKey,
            MaterialId = materialId,
            StageCode = stageCode,
            OperationCode = operationCode,
            PlannedStartTime = end.AddHours(-8),
            PlannedEndTime = end
        };

    private static IReadOnlyDictionary<int, HashSet<string>> Stages(int materialId, params string[] stageCodes)
        => new Dictionary<int, HashSet<string>>
        {
            [materialId] = new HashSet<string>(stageCodes, StringComparer.Ordinal)
        };

    private static IReadOnlyDictionary<string, FinalTaskDraft> Terminals(params FinalTaskDraft[] tasks)
        => tasks.ToDictionary(t => t.SourceDraftId, StringComparer.Ordinal);
}
