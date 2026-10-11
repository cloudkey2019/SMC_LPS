using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;
using RoutingOperation = LPS.APS.Core.Entities.APS.RoutingOperation;
using RoutingDependency = LPS.APS.Core.Entities.APS.RoutingDependency;
using OperationResourceEligibility = LPS.APS.Core.Entities.APS.OperationResourceEligibility;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// OWN-P0-02 专项反证（0号位 2026-10-10《APS_V1_1_20261010.md》§三）：
/// **多 Operation Stage 执行批的真实合批能力**。
///
/// 复审判词（本号位核对：**成立**）：
///   · `FindMergeableTasks` 首行 `if (operations.Count != 1) return candidates;` ⇒ 正常 Stage 多 Operation
///     工艺**无法参与完整合批**；
///   · `#L3308-L3314` 先形成带 `ExecutionBatchDraftKey` 的 Task，再由 `requireIdentityPreserving` 拒绝已归批
///     Task ⇒ 当前常规多批 C 需求**缺真实合批路径**；
///   · `#L1115-L1130` 源码自认多批合并「当前实际不发生」。
///
/// 冻结模型（同文 §二.4）：C 桶可在合法条件下合批；「同一 Stage 执行批承载 N 个 Operation Task，
///   多个需求份额各自可追溯」；「若 30 件+20 件合批方案胜出，形成**一个 50 件 Stage 执行批**及该 Path 上的
///   N 条 Operation 级 FinalTask…需求 A 的 30 件与 B 的 20 件由**份额账本**追溯」；「**不能把 50 件经过
///   3 道工序错误加成 150 件**」。
///
/// 本文件六组反证（全部走**完整** <see cref="FiniteCapacitySolver.SolveAsync"/>，纯内存，不触库）：
///   · <see cref="M_01_合批应胜_三工序两需求_须形成一个50件Stage执行批与完整Operation链"/>
///   · <see cref="M_02_不合批应胜_合批更晚且不多省Setup_须取独立排程两批各合法Path"/>  ← **反向用例**
///   · <see cref="M_03_多Routing_合批候选须在完整Path维度真实选路_不得因身份冲突放弃合批"/>
///   · <see cref="M_04_AB边界_连续份额侧不得被普通C合批改写批身份"/>
///   · <see cref="M_05_量Stage路径_三工序共享同一Stage批身份_份额闭合且不重复生成工单"/>
///   · <see cref="M_06_Calendar跨90天_合法完成日第120天不得由PlanningEnd否决"/>
///
/// ⚠ 夹具口径常量：`PlanningEnd = PlanningStart + 90 天`（滚动**需求范围**，不是资源时间终点）。
/// ⚠ 本文件**不替代**第 91/180 天时间反证 —— 那三组在 <see cref="PlanningEndNotHardBoundTests"/>。
/// </summary>
public class StageBatchMergeTests
{
    private static readonly DateTime Day = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningStart = Day;

    /// <summary>滚动 90 天 = 需求进入本轮求解的范围（**不是**资源时间终点）。</summary>
    private static readonly DateTime PlanningEnd = Day.AddDays(90);

    private const int MaterialId = 1;
    private const int DeptId = 100;

    private readonly FiniteCapacitySolver _solver = new();

    // ════════════════════════════════════════════════════════════════════
    // M-01 合批应胜
    //   复审要求：同 Stage 同产品、需求 A30 / B20、≥2 个 Operation、存在公共合法 Route/Path、
    //   完整硬约束允许合批且冻结的目标评价**合批更优** ⇒ 最终**一个** Stage 执行批、完整 Operation 链、
    //   A/B 份额 30/20、净产出 50（按 Stage 正确计一次）。
    //
    //   本夹具让合批**真实更优**的业务机制 = **省换型**：三道工序的资源上各有一条
    //   `EXACT(M→M) = 60min` 规则（同产品连续、有显式规则时可覆盖默认 0 —— SetupOptimizer §6）。
    //     · 独立排程：B 的三道工序各自命中 M→M 换型 ⇒ 3×60 = 180min Setup，完成时间被推后；
    //     · 合批：目标批被移出后重排，三道工序在其资源上均无前产品（InitialState ⇒ Setup=0），
    //       且 50 件一次成型 ⇒ 完成更早、Setup 总量 0。
    //   ⇒ 冻结四层目标 ③（更早完成）与 ④（Setup 更小）**两层同时**指向合批。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_01_合批应胜_三工序两需求_须形成一个50件Stage执行批与完整Operation链()
    {
        var request = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 一个 Stage 执行批：恰 3 条 Operation 级 FinalTask，全部同一批键
        Assert.Equal(3, result.FinalTasks.Count);
        var batchKeys = result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct().ToList();
        var batchKey = Assert.Single(batchKeys);
        Assert.False(string.IsNullOrEmpty(batchKey));

        // ── AUD-1-002 整改（0号位 2026-10-10《APS_V1_2_20261010.md》§3，P0/CONFIRMED）──
        //   **执行批身份（跨 Stage 工序链）** 与 **Stage 执行批身份（Stage 内 MES 批）** 必须**分开**：
        //     · `ExecutionBatchDraftKey` = 该执行批**跨整个 Routing Path** 的身份 ⇒ 本夹具 1 个；
        //     · `StageExecutionBatchDraftKey` = **Stage 内** MES 批身份（T-002「TaskNo 是 Stage 内 MES 执行批身份」、
        //       T-003「MES 工单不跨 Stage」）⇒ 三道工序分属 STAGE1/2/3 ⇒ **3 个互不相同**。
        //   ⇒ 本条即「**跨 Stage 不得共用一个 MES 工单**」在 1号位 出口的反证：
        //     若三个不同 Stage 的 Task 共用一个 `StageExecutionBatchDraftKey`（旧实现），本条**红**。
        //     （最终 `TaskNo` / MES 工单归组由 2号位 在 FinalTask 之后完成，须与其联验。）
        var stageKeys = result.FinalTasks.Select(t => t.StageExecutionBatchDraftKey).ToList();
        Assert.All(stageKeys, k => Assert.False(string.IsNullOrEmpty(k)));
        Assert.Equal(3, stageKeys.Distinct(StringComparer.Ordinal).Count());

        // 完整 Operation 链：三道工序齐备，且**净产出 50 按 Stage 正确计一次**（不得 150）
        Assert.Equal(
            new[] { "OP10", "OP20", "OP30" },
            result.FinalTasks.Select(t => t.OperationCode).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.All(result.FinalTasks, t => Assert.Equal(50m, t.Quantity));
        Assert.All(result.FinalTasks, t => Assert.Equal(50m, t.PlannedProcessQty));

        // 一条完整真实 Path（不得混 Path）
        Assert.All(result.FinalTasks, t => Assert.Equal("RT", t.RouteCode));
        Assert.All(result.FinalTasks, t => Assert.Equal(1, t.PathId));

        // 份额账本：A 30 / B 20，闭合到声明量
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
        Assert.Equal(50m, result.AllocationShares.Sum(s => s.ComponentQty));

        // 跨 90 天承载：合并批落在第 120 天的真实日历窗内
        Assert.All(result.FinalTasks, t => Assert.True(t.PlannedStartTime >= Day.AddDays(120)));

        // ── 复审 §六.5「**硬约束及目标优于分开方案**」：同夹具对照组（`AllowMerge=false` = 候选 A「单独排」）──
        //   不能只断言「合批胜出」这一**结果**，必须在**冻结目标层**上断言它**严格优于**分开方案。
        //   `allowMerge=false` ⇒ 求解器不尝试合批 ⇒ 直接给出「单独排」候选（**候选 A 的确定取法**；
        //   M-02 则是**开着 Merge** 由 `PreferStageMerge` 判独立排胜出 —— 两者取法不同，勿混）。
        var separateRequest = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3),
            allowMerge: false);

        var separate = await _solver.SolveAsync(separateRequest);

        Assert.True(separate.Success, separate.ErrorMessage);
        // 分开方案：两条需求各自成批 ⇒ 6 条 Task / 2 个批键（即候选 A）
        Assert.Equal(6, separate.FinalTasks.Count);
        Assert.Equal(2, separate.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct().Count());

        var mergedEnd = result.FinalTasks.Max(t => t.PlannedEndTime);
        var separateEnd = separate.FinalTasks.Max(t => t.PlannedEndTime);
        var mergedSetup = result.FinalTasks.Sum(t => t.SetupTime);
        var separateSetup = separate.FinalTasks.Sum(t => t.SetupTime);

        // 冻结目标 ③（FORWARD：更早可行完成优先）—— 合批严格更早
        Assert.True(mergedEnd < separateEnd,
            $"合批完成（{mergedEnd:O}）须早于分开方案（{separateEnd:O}）");
        // 冻结目标 ④（Setup 总量小者优先）—— 合批严格更省
        Assert.True(mergedSetup < separateSetup,
            $"合批 Setup（{mergedSetup}）须小于分开方案（{separateSetup}）");
    }

    // ════════════════════════════════════════════════════════════════════
    // M-02 不合批应胜（**反向用例** —— 复审 §六.5「还需一例合法不合批更优的反向用例」）
    //   同产品、同工序集、无换型规则（合批省不下 Setup），交期宽松 ⇒ 合批**合法**但**更晚完成**：
    //     · 独立排程：B 紧接 A 之后逐工序落位 ⇒ 完成更早；
    //     · 合批：三工序各 50 件，占用被拉长 ⇒ 完成更晚。
    //   ⇒ 冻结四层目标 ③（FORWARD：更早可行完成优先）指向独立排程。
    //   复审要求：「不可强制为了减少 Setup 而延误高优先需求」；本用例进一步证明
    //   **合法即采用**的贪心合批同样不被允许 —— 必须有可度量的改善。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_02_不合批应胜_合批更晚且不多省Setup_须取独立排程两批各合法Path()
    {
        var request = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            });
        // 无 SetupTransitionRules ⇒ 合批省不下换型 ⇒ 合批只剩「更晚完成」这一个差别。

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 未合批：两条需求各自成批 ⇒ 6 条 Task、两个批键
        Assert.Equal(6, result.FinalTasks.Count);
        Assert.Equal(2, result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct().Count());

        var aTasks = result.FinalTasks.Where(t => t.SourceDraftId == "A").ToList();
        var bTasks = result.FinalTasks.Where(t => t.SourceDraftId == "B").ToList();
        Assert.Equal(3, aTasks.Count);
        Assert.Equal(3, bTasks.Count);
        Assert.All(aTasks, t => Assert.Equal(30m, t.Quantity));
        Assert.All(bTasks, t => Assert.Equal(20m, t.Quantity));

        // 两批各自合法 Path
        Assert.All(result.FinalTasks, t => Assert.Equal("RT", t.RouteCode));
        Assert.All(result.FinalTasks, t => Assert.Equal(1, t.PathId));

        // B 的完成时间 = 独立排程的真实完成（紧接 A 之后），**早于**合批会给出的完成
        var mergedAlternativeEnd = Day.AddDays(120).AddMinutes(150);   // 50 件 × 3 工序
        var bEnd = bTasks.Max(t => t.PlannedEndTime);
        Assert.True(bEnd < mergedAlternativeEnd,
            $"独立排程的 B 完成（{bEnd:O}）应早于合批方案（{mergedAlternativeEnd:O}）");
    }

    // ════════════════════════════════════════════════════════════════════
    // M-03 多Routing
    //   复审要求：合法公共 Path1/Path2 存在；合批候选必须在**完整 Path 维度**真实选路，
    //   不能「先 A 去 Path1、B 去 Path2，然后因身份冲突放弃合批」。
    //
    //   夹具：Path1 = 快路径（1 min/件 + 三条 M→M 换型规则 60min）；Path2 = 慢路径（10 min/件、无规则）。
    //     · A 先排：Path1 完成更早 ⇒ A 落 Path1（并成批，带批键）；
    //     · B 排：Path1 候选 ⇒ **命中 A 的 Stage 执行批** ⇒ 合批（同批键、完整 Path1）；
    //              Path2 候选 ⇒ 独立排程但工序长 10 倍 ⇒ 完成远晚。
    //   ⇒ 择优必须真的在 Path 维度比较，且合批**不得**因「目标已带批键」被拒。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_03_多Routing_合批候选须在完整Path维度真实选路_不得因身份冲突放弃合批()
    {
        var request = Build(
            paths: new[]
            {
                ThreeOpPath("RT", 1, 1, 2, 3, 1m),
                ThreeOpPath("RT", 2, 11, 12, 13, 10m)
            },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(200)),
                (2, Day.AddDays(120), Day.AddDays(200)),
                (3, Day.AddDays(120), Day.AddDays(200)),
                (11, Day.AddDays(120), Day.AddDays(200)),
                (12, Day.AddDays(120), Day.AddDays(200)),
                (13, Day.AddDays(120), Day.AddDays(200))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 合批成立 ⇒ 一个 Stage 执行批、完整 Path1（**没有** A 在 Path1、B 在 Path2 的分裂结果）
        Assert.Equal(3, result.FinalTasks.Count);
        var batchKey = Assert.Single(result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct());
        Assert.False(string.IsNullOrEmpty(batchKey));
        Assert.All(result.FinalTasks, t => Assert.Equal(1, t.PathId));
        Assert.All(result.FinalTasks, t => Assert.Equal(50m, t.Quantity));

        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════════════
    // M-04 A/B 边界
    //   复审要求：相同产品但其中一方已下发/锁定/**连续份额**，普通 C 合批不得改写历史 MES 工单/批身份。
    //   夹具：A 是 **A/B 连续份额**（`IsContinuation = true`，契约同源 `NoSplitMerge = true`）。
    //     ⇒ `FindMergeableStageBatches` 的 ⑤ 条必须把「连续份额侧的批」整批排除 ⇒ B 独立排程。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_04_AB边界_连续份额侧不得被普通C合批改写批身份()
    {
        var request = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200), IsContinuation: true),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // A 的连续份额批**原样保留**（30 件、自己一条批键），B 另起一批（20 件）⇒ 未被合批改写
        Assert.Equal(6, result.FinalTasks.Count);
        Assert.Equal(2, result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct().Count());

        var aTasks = result.FinalTasks.Where(t => t.SourceDraftId == "A").ToList();
        Assert.Equal(3, aTasks.Count);
        Assert.All(aTasks, t => Assert.Equal(30m, t.Quantity));

        var bTasks = result.FinalTasks.Where(t => t.SourceDraftId == "B").ToList();
        Assert.Equal(3, bTasks.Count);
        Assert.All(bTasks, t => Assert.Equal(20m, t.Quantity));

        // 份额账本不得出现跨需求合并记录：A/B 各自闭合
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════════════
    // M-05 量 / Stage / 路径
    //   复审要求：三 Operation 共享**同一 Stage 批身份**、两份 Demand 份额闭合，
    //   不能重复生成工单、混 Path、提前删除工序依赖。
    //   断言侧重 M-01 未覆盖的两项：**工序依赖边未丢**、**无重复 Task**。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_05_量Stage路径_三工序共享同一Stage批身份_份额闭合且不重复生成工单()
    {
        var request = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // 无重复 Task：逐 (StageCode, OperationCode) 唯一
        Assert.Equal(
            result.FinalTasks.Count,
            result.FinalTasks.Select(t => (t.StageCode, t.OperationCode)).Distinct().Count());

        // 工序依赖边**未被提前删除**：OP10→OP20、OP20→OP30 两条边都在
        //   （`PhysicalPeggingDrafts` 按 FinalDraftId 连边 ⇒ 先映回工序码再断言）
        var opByDraft = result.FinalTasks.ToDictionary(t => t.FinalDraftId, t => t.OperationCode);
        var edges = result.PhysicalPeggingDrafts
            .Where(p => opByDraft.ContainsKey(p.UpstreamFinalDraftId)
                        && opByDraft.ContainsKey(p.DownstreamFinalDraftId))
            .Select(p => (From: opByDraft[p.UpstreamFinalDraftId], To: opByDraft[p.DownstreamFinalDraftId]))
            .ToList();
        Assert.Contains(("OP10", "OP20"), edges);
        Assert.Contains(("OP20", "OP30"), edges);

        // 同一 Stage 批身份 + 份额闭合
        Assert.Single(result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct());
        Assert.Equal(50m, result.AllocationShares.Sum(s => s.ComponentQty));
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));

        // 不得把 50 件经 3 道工序错误加成 150 件：逐工序净产出均为 50
        Assert.All(result.FinalTasks, t => Assert.Equal(50m, t.Quantity));
    }

    // ════════════════════════════════════════════════════════════════════
    // M-06 Calendar 跨 90 天
    //   复审要求：合并方案**合法完成日第 120 天**，不能由 `PlanningEnd` 直接否决；
    //   不得替代第 91/180 天时间反证（那三组在 PlanningEndNotHardBoundTests）。
    //
    //   夹具刻意让日历窗**横跨第 90 天**：res1 从第 89 天起（90 天以内），res2/res3 从第 120 天起
    //   （远超 90 天）⇒ 合并方案的承载必须同时用到**两侧**日历，完成日落在第 120 天之后。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_06_Calendar跨90天_合法完成日第120天不得由PlanningEnd否决()
    {
        var request = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(89), Day.AddDays(200)),
                (2, Day.AddDays(120), Day.AddDays(121)),
                (3, Day.AddDays(120), Day.AddDays(121))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 合批成立（一个 Stage 执行批、50 件）
        Assert.Equal(3, result.FinalTasks.Count);
        Assert.Single(result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct());
        Assert.All(result.FinalTasks, t => Assert.Equal(50m, t.Quantity));

        // 承载跨过第 90 天：末道工序的完成**晚于** `PlanningEnd`，即合并方案没有被 90 天口径否决
        var endTask = result.FinalTasks.Single(t => t.OperationCode == "OP30");
        Assert.True(endTask.PlannedEndTime > PlanningEnd,
            $"末道工序完成（{endTask.PlannedEndTime:O}）须晚于 PlanningEnd（{PlanningEnd:O}）");
        Assert.True(endTask.PlannedStartTime >= Day.AddDays(120),
            $"末道工序须落在第 120 天的真实日历窗内，实际 {endTask.PlannedStartTime:O}");

        // 逐工序净产出 50（按 Stage 正确计一次）
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════════════
    // OWN-P1-03 专项：Unscheduled 归因不得是「90 天末期」
    //   复审要求：「在**可用合法日历确实覆盖未来**的场景，不得仅因超出 90 天、客户交期或既有排程拥挤
    //   而产出『部分批未排 / Domain 失败』」；「缺 Eligibility、资源 Calendar 未维护…仍是需要明确输出
    //   原因的**真实异常**」。
    //   本用例同一几何两组对照：
    //     A. 日历覆盖未来（第 200 天）⇒ **必须排下**，不得 Unscheduled；
    //     B. 该需求**根本没有可用的资源日历**（资源未维护）⇒ Unscheduled，但原因必须指向
    //        **真实缺失**（日历/Eligibility 类），**不得**出现任何「90 天/PlanningEnd/计划窗口」字样。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task OWN_P1_03_未来有合法日历须排下_无日历侧须报真实缺失原因而非90天末期()
    {
        // ── A：日历覆盖第 200 天（远超 90 天）⇒ 必须排下 ──
        var requestA = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[] { new DemandSpec("D1", 1, 30m, Day.AddDays(20)) },
            calendar: new[]
            {
                (1, Day.AddDays(200), Day.AddDays(201)),
                (2, Day.AddDays(200), Day.AddDays(201)),
                (3, Day.AddDays(200), Day.AddDays(201))
            },
            allowMerge: false);

        var resultA = await _solver.SolveAsync(requestA);

        Assert.True(resultA.Success, resultA.ErrorMessage);
        Assert.Empty(resultA.UnscheduledTasks);
        Assert.Equal(3, resultA.FinalTasks.Count);
        Assert.All(resultA.FinalTasks, t => Assert.True(t.PlannedStartTime >= Day.AddDays(200)));

        // ── B：同一几何但**资源未维护日历** ⇒ 真实缺失，原因不得是 90 天口径 ──
        var requestB = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[] { new DemandSpec("D1", 1, 30m, Day.AddDays(200)) },
            calendar: Array.Empty<(int, DateTime, DateTime)>(),   // 无任何日历事实
            allowMerge: false);

        var resultB = await _solver.SolveAsync(requestB);

        Assert.True(resultB.Success, resultB.ErrorMessage);
        Assert.Empty(resultB.FinalTasks);
        var unsched = Assert.Single(resultB.UnscheduledTasks);
        Assert.Equal("D1", unsched.DraftId);
        Assert.False(string.IsNullOrWhiteSpace(unsched.Reason));
        Assert.DoesNotContain("90", unsched.Reason);
        Assert.DoesNotContain("PlanningEnd", unsched.Reason);
        Assert.DoesNotContain("计划窗口", unsched.Reason);
    }

    // ════════════════════════════════════════════════════════════════════
    // M-07（AUD-1-R01）多个合法锚点：必须**逐锚点**择优，不得只试 `stageBatches[0]`
    //   复审判词（本号位核对：**成立**）：旧实现只试第一个合法合批目标（`scheduledTasks` 中出现最早者）
    //     ⇒ 存在多个合法锚点时可能漏掉业务更优方案（复审明确**不要求**穷举组合，逐锚点即可）。
    //
    //   夹具（三条需求 A30 / B20 / C10，同一 Path、同三道工序，各资源 M→M 显式换型 60min，单一宽窗）：
    //     · **A 交期收紧到 D+120 01:40（+100min）** —— A 独立排程 90min 完成（[0,30]/[30,60]/[60,90]，各资源首件 ⇒ Setup 0）
    //       恰在交期内；**任何把 A 批延长的合批**都会被 P0-04 交期保护拒掉。
    //     · A 交期紧 ⇒ **B 无法并入 A**（合批 A+B 完成 150min > A 交期 100min ⇒ 拒）⇒ B 独立成批（完成 270min）。
    //       于是 C 到来时**存在两个合法锚点**：`stageBatches[0] = A 批`、`stageBatches[1] = B 批`。
    //     · C 侧（交期宽松）：
    //         - 并入 **A 批**（A+C=40）：重排后完成 370min > A 交期 100min ⇒ **交期保护拒绝**；
    //         - 并入 **B 批**（B+C=30）：完成 180min，**远早于独立排程的 340min** ⇒ 冻结目标 ③ 胜出。
    //   ⇒ **旧实现只试 `stageBatches[0]`（A 批）**：合并被拒 ⇒ 回落独立排程 ⇒ 9 条 Task / 3 个批键（本条**红**）；
    //     整改后逐锚点试排 ⇒ 选中 B 批 ⇒ 6 条 Task / 2 个批键（A 批 + B∪C 批）。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_07_多锚点_须逐锚点择优_不得只试第一个合法合批目标()
    {
        var request = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                // A 交期紧（+100min）：独立排 90min 可满足；被合批延长即违约 ⇒ 使 B 无法并入 A。
                new DemandSpec("A", 1, 30m, Day.AddDays(120).AddMinutes(100)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200)),
                new DemandSpec("C", 3, 10m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 合批胜出且**并入第二个锚点（B 批）**：6 条 Task = A 批 3 条 + 合成批 3 条；2 个执行批键。
        //   旧实现只试 A 批 ⇒ 并入 A 批被交期保护拒绝且不再回落试 B 批 ⇒ 退化成 9 条 / 3 个批键 ⇒ 下列断言**红**。
        Assert.Equal(6, result.FinalTasks.Count);
        Assert.Equal(2, result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct().Count());

        var aTasks = result.FinalTasks.Where(t => t.SourceDraftId == "A").ToList();
        Assert.Equal(3, aTasks.Count);
        Assert.All(aTasks, t => Assert.Equal(30m, t.Quantity));

        var merged = result.FinalTasks.Where(t => t.SourceDraftId == "B").ToList();
        Assert.Equal(3, merged.Count);
        Assert.All(merged, t => Assert.Equal(30m, t.Quantity));   // B20 + C10

        // 份额闭合：A30 / B20 / C10 各**恰好一次**
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
        Assert.Equal(10m, result.AllocationShares.Where(s => s.AllocationSequence == 3).Sum(s => s.ComponentQty));
        Assert.Equal(60m, result.AllocationShares.Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════════════
    // M-08（AUD-1-001）连续两次 Merge：份额**不得按 Operation 重复累加**
    //   复审 §7 第 1 项：A30+B20+C10 **连续两次 Merge**、三 Operation、净产出 60、
    //     A/B/C 分别 30/20/10，Phase5 完整闭合。
    //   缺陷形态（旧实现）：A+B 先合成 50 件批（3 条 Operation Task 各持同一份 [A30,B20]）；
    //     再合 C10 时把被移出批的 N 条副本**逐条累加** ⇒ B20×3 + C10 = 70，而新批 Task 数量只有 60
    //     ⇒ Phase5 逐 (需求 × 工序) 闭合失真（B 膨胀、A 锚点份额被淹没）。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_08_连续两次合批_三需求三工序_份额按需求各计一次_Phase5完整闭合()
    {
        var request = Build(
            paths: new[] { ThreeOpPath("RT", 1, 1, 2, 3, 1m) },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200)),
                new DemandSpec("C", 3, 10m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        // Phase5 硬校验 `ValidateHardResult` 逐 (需求 × 工序) 闭合 ⇒ 份额失真会直接使 Success=false
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 一个 Stage 执行批、完整三工序链、每条 Task 净产出 = 60（**不是** 180）
        Assert.Equal(3, result.FinalTasks.Count);
        Assert.Single(result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct());
        Assert.All(result.FinalTasks, t => Assert.Equal(60m, t.Quantity));
        Assert.All(result.FinalTasks, t => Assert.Equal(60m, t.PlannedProcessQty));

        // 份额：A30 / B20 / C10，各**恰好一次**，合计 60（判别器）
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
        Assert.Equal(10m, result.AllocationShares.Where(s => s.AllocationSequence == 3).Sum(s => s.ComponentQty));
        Assert.Equal(60m, result.AllocationShares.Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════════════
    // M-09（AUD-1-003）失败试排必须**全状态回滚**（单工序成功、下游工序失败）
    //   复审 §7 第 2 项要求：断言占用、时间线、Shares、Tasks 深度一致；测试单工序成功但下游 Operation 失败。
    //   夹具：两道工序 —— OP10@STAGE1 上 R1（10h 大窗）、OP20@STAGE2 上 R2（两段窄窗 30min / 40min）。
    //     A30 先排：OP10 00:00-00:30；OP20 落 R2 第一段 02:00-02:30。
    //     B20 到来：合批试排 = 合成 50 件 ⇒ OP10 可排（R1 宽），**OP20 需 50min、两段窄窗都装不下** ⇒ 失败。
    //       探针实测（2026-10-10）：该失败分支**确实被走到**（在 `combinedTasks.Count == 0` 处打 throw 探针，
    //       M-09 即因该异常失败 ⇒ 分支可达，非空跑）。
    //   判别器：合批**被尝试且失败**的结果，必须与**完全不尝试合批**（`allowMerge=false`）逐字段一致；
    //     若失败试排遗留前序工序（OP10）在 R1 上的**幽灵占用** ⇒ B 的 OP10 被推后 ⇒ 本条红。
    //
    //   ⚠ **本号位如实登记的实测结论（重要，不粉饰）**：
    //     把 AUD-1-003 的两处水位回滚**全部临时移除**后，本条**仍为绿**。原因：OWN-P0-02 的合批试排
    //     （`EnumerateLegalBatchPlanCandidates` 内）跑在 `CloneOccupancy(resourceOccupancy, …)` 的
    //     **COW 克隆**上（`EnsureOwned` 写前分叉，r13599 引入），失败写入落在**被丢弃的克隆**里，
    //     真实表从不被试排污染；落定调用仅在**试排成功**（`bestAnchorIndex >= 0`）时发生，
    //     且与试排同初始上下文（确定性）⇒ 真实表永不出现该幽灵。
    //   ⇒ 本号位**不宣称** M-09 是 AUD-1-003 的「整改前红」判别性反证（违反「写『整改前红』必先实测」）。
    //     本条的价值 = **零回归守卫**：钉死「失败试排不得污染后续计算」这一冻结不变式
    //     （RC-001～RC-004 / B-003），防止日后有路径把试排改回真实表时静默回归。
    //     水位回滚本身按 A 类自决保留为**纵深防御**（覆盖「落定调用返回 null」与「`mergeStructurallyPossible`
    //     隔离判据回归」两类风险），**不降目标**：冻结要求「失败试排不可污染」已由 COW 隔离 + 水位回滚
    //     双重达成。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_09_合批失败_单工序成功下游失败_须全状态回滚不留幽灵占用()
    {
        var twoOp = new PathSpec("RT", 1,
            new[] { new OpSpec("STAGE1", "OP10", 1, 1m), new OpSpec("STAGE2", "OP20", 2, 1m) },
            new[] { ("OP10", "OP20") });

        DomainSolveRequest BuildTwo(bool allowMerge) => Build(
            paths: new[] { twoOp },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(120).AddHours(10)),
                (2, Day.AddDays(120).AddHours(2), Day.AddDays(120).AddHours(2).AddMinutes(30)),
                (2, Day.AddDays(120).AddHours(4), Day.AddDays(120).AddHours(4).AddMinutes(40))
            },
            allowMerge: allowMerge);

        var withMergeAttempt = await _solver.SolveAsync(BuildTwo(allowMerge: true));
        var pureSeparate = await _solver.SolveAsync(BuildTwo(allowMerge: false));

        Assert.True(withMergeAttempt.Success, withMergeAttempt.ErrorMessage);
        Assert.True(pureSeparate.Success, pureSeparate.ErrorMessage);
        Assert.Empty(withMergeAttempt.UnscheduledTasks);
        Assert.Empty(pureSeparate.UnscheduledTasks);

        // **深度一致**：Task 数、逐 Task 的 (需求, Stage, 工序, 起, 止, 数量) 全部相同
        static List<(string, string, string, DateTime, DateTime, decimal)> Fingerprint(DomainSolveResult r)
            => r.FinalTasks
                .OrderBy(t => t.SourceDraftId, StringComparer.Ordinal)
                .ThenBy(t => t.OperationCode, StringComparer.Ordinal)
                .Select(t => (t.SourceDraftId, t.StageCode, t.OperationCode,
                              t.PlannedStartTime, t.PlannedEndTime, t.Quantity))
                .ToList();

        Assert.Equal(Fingerprint(pureSeparate), Fingerprint(withMergeAttempt));

        // 份额账本亦不得被污染
        static List<(long, decimal)> Shares(DomainSolveResult r)
            => r.AllocationShares
                .OrderBy(s => s.AllocationSequence)
                .Select(s => (s.AllocationSequence, s.ComponentQty))
                .ToList();

        Assert.Equal(Shares(pureSeparate), Shares(withMergeAttempt));

        // 幽灵占用的**直接**判别器：B 的 OP10 必须**紧接** A 的 OP10（中间不得出现空洞）
        var aOp10 = withMergeAttempt.FinalTasks.Single(t => t.SourceDraftId == "A" && t.OperationCode == "OP10");
        var bOp10 = withMergeAttempt.FinalTasks.Single(t => t.SourceDraftId == "B" && t.OperationCode == "OP10");
        Assert.Equal(aOp10.PlannedEndTime, bOp10.PlannedStartTime);

        // 未丢需求、未错批：两条需求各自一批
        Assert.Equal(2, withMergeAttempt.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct().Count());
    }

    // ════════════════════════════════════════════════════════════════════
    // M-10（AUD-1-002 正例）**真正同一 StageCode** 的 N Operation：合批后共用一个 Stage 执行批身份
    //   复审 §7 第 3 项：新增真正同一 `StageCode` 的 N Operation 场景（与 M-01 跨 Stage 反例成对）。
    //   夹具：三道工序**全部落 STAGE1**（OP10/OP20/OP30 @ R1/R2/R3），A30 + B20 合批。
    //   ⇒ `ExecutionBatchDraftKey` 1 个（跨 Stage 工序链身份）；
    //     `StageExecutionBatchDraftKey` **1 个**（同 Stage 的 N 条 Operation Task 共享同一 MES 执行批身份，
    //     T-002/T-005）—— 与 M-01 的「三个不同 Stage ⇒ 3 个 Stage 键」互为正反证。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task M_10_同一Stage内N工序_合批须共用一个Stage执行批身份()
    {
        var sameStage = new PathSpec("RT", 1,
            new[]
            {
                new OpSpec("STAGE1", "OP10", 1, 1m),
                new OpSpec("STAGE1", "OP20", 2, 1m),
                new OpSpec("STAGE1", "OP30", 3, 1m)
            },
            new[] { ("OP10", "OP20"), ("OP20", "OP30") });

        var request = Build(
            paths: new[] { sameStage },
            demands: new[]
            {
                new DemandSpec("A", 1, 30m, Day.AddDays(200)),
                new DemandSpec("B", 2, 20m, Day.AddDays(200))
            },
            calendar: new[]
            {
                (1, Day.AddDays(120), Day.AddDays(125)),
                (2, Day.AddDays(120), Day.AddDays(125)),
                (3, Day.AddDays(120), Day.AddDays(125))
            },
            setupRules: SameProductSetup(60m, 1, 2, 3));

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        Assert.Equal(3, result.FinalTasks.Count);
        Assert.All(result.FinalTasks, t => Assert.Equal("STAGE1", t.StageCode));

        // 跨 Stage 工序链的执行批身份：1 个
        Assert.Single(result.FinalTasks.Select(t => t.ExecutionBatchDraftKey).Distinct());

        // **Stage 内** MES 执行批身份：同一 StageCode ⇒ **1 个**（同批 N 条 Operation Task 共享一个 TaskNo）
        var stageKey = Assert.Single(result.FinalTasks.Select(t => t.StageExecutionBatchDraftKey).Distinct());
        Assert.False(string.IsNullOrEmpty(stageKey));

        // 净产出按 Stage 计一次（不得 50 × 3）
        Assert.All(result.FinalTasks, t => Assert.Equal(50m, t.Quantity));
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private readonly record struct OpSpec(string StageCode, string OpCode, int ResourceId, decimal Minutes);

    private readonly record struct PathSpec(
        string RouteCode, int PathId, IReadOnlyList<OpSpec> Ops, IReadOnlyList<(string From, string To)> Deps);

    private readonly record struct DemandSpec(
        string Key, long Seq, decimal Qty, DateTime? Due,
        string? RouteCode = null, int? PathId = null, bool IsContinuation = false);

    /// <summary>三工序串行路径（OP10@STAGE1 → OP20@STAGE2 → OP30@STAGE3），资源与工时按调用方给定。</summary>
    private static PathSpec ThreeOpPath(
        string routeCode, int pathId, int r1, int r2, int r3, decimal minutesPerUnit)
        => new(
            routeCode, pathId,
            new[]
            {
                new OpSpec("STAGE1", "OP10", r1, minutesPerUnit),
                new OpSpec("STAGE2", "OP20", r2, minutesPerUnit),
                new OpSpec("STAGE3", "OP30", r3, minutesPerUnit)
            },
            new[] { ("OP10", "OP20"), ("OP20", "OP30") });

    /// <summary>
    /// 同产品连续（M→M）显式换型规则（`SetupOptimizer` §6：同产品 A→A **有显式规则时可覆盖默认 0**）。
    /// 用途：让「独立排程」真实付出换型成本，而「合批」因为目标批被移出、资源上无前产品（InitialState）而不付
    /// ⇒ 合批在冻结目标第 ③（完成更早）与第 ④（Setup 更小）两层同时更优。
    /// </summary>
    private static IReadOnlyList<SetupTransitionRuleSnapshot> SameProductSetup(
        decimal minutes, params int[] resourceIds)
        => resourceIds
            .Select(rid => new SetupTransitionRuleSnapshot
            {
                ProductionDepartmentId = DeptId,
                StageCode = "",
                OperationCode = $"OP{rid}0",     // 与 ThreeOpPath 的工序码一一对应（资源 1/2/3 → OP10/OP20/OP30）
                ResourceId = rid,
                FromMaterialId = MaterialId,
                ToMaterialId = MaterialId,
                RuleType = "EXACT",
                SetupMinutes = minutes
            })
            .ToArray();

    private static DomainSolveRequest Build(
        IReadOnlyList<PathSpec> paths,
        IReadOnlyList<DemandSpec> demands,
        IReadOnlyList<(int ResourceId, DateTime Start, DateTime End)> calendar,
        bool allowMerge = true,
        bool allowSplit = false,
        string direction = "FORWARD",
        IReadOnlyList<SetupTransitionRuleSnapshot>? setupRules = null,
        IReadOnlyList<ExecutionConstraint>? locked = null)
    {
        var routingOps = new List<RoutingOperation>();
        var elig = new List<OperationResourceEligibility>();
        var deps = new List<RoutingDependency>();
        var stageDepts = new List<MaterialStageDepartmentContextDto>();

        foreach (var p in paths)
        {
            foreach (var op in p.Ops)
            {
                routingOps.Add(new RoutingOperation
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = p.RouteCode, PathId = p.PathId,
                    OperationCode = op.OpCode, StageCode = op.StageCode,
                    StandardDuration = op.Minutes, OperationPlanningMode = "FINITE_RESOURCE"
                });
                elig.Add(new OperationResourceEligibility
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = p.RouteCode, PathId = p.PathId,
                    OperationCode = op.OpCode, ResourceId = op.ResourceId,
                    Priority = 1, CapacityFactor = 1m
                });
                if (stageDepts.All(s => !string.Equals(s.StageCode, op.StageCode, StringComparison.Ordinal)))
                {
                    stageDepts.Add(new MaterialStageDepartmentContextDto
                    {
                        MaterialId = MaterialId, StageCode = op.StageCode, ProductionDepartmentId = DeptId
                    });
                }
            }

            foreach (var (from, to) in p.Deps)
            {
                deps.Add(new RoutingDependency
                {
                    MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                    RouteCode = p.RouteCode, PathId = p.PathId,
                    FromOperationCode = from, ToOperationCode = to,
                    DependencyType = "ES", LagTime = 0m, IsActive = true
                });
            }
        }

        var resources = paths
            .SelectMany(p => p.Ops.Select(o => o.ResourceId))
            .Distinct()
            .Select(rid => new ResourceDefinition
            {
                ResourceId = rid, ResourceCode = $"R{rid}", FactoryCode = "F1", Capacity = 1m
            })
            .ToList();

        var slots = calendar
            .Select(c => new ResourceCalendarSlot
            {
                ResourceId = c.ResourceId, Start = c.Start, End = c.End, IsAvailable = true
            })
            .ToList();

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = demands.Select(d => new LogicalProductionDemand
            {
                LogicalDemandKey = d.Key, PlanVersionId = 1L, DomainKey = "DOMAIN",
                AllocationSequence = d.Seq, DemandKey = d.Key, MaterialId = MaterialId, FactoryId = 1,
                StartStageCode = "STAGE1",
                NetOutputQty = d.Qty, PlannedProcessQty = d.Qty,
                RequiredAvailableTime = d.Due ?? PlanningStart.AddDays(20),
                DemandSequence = (int)d.Seq,
                RouteCode = d.RouteCode, PathId = d.PathId,
                IsContinuation = d.IsContinuation, NoSplitMerge = d.IsContinuation
            }).ToList(),
            RoutingOperations = routingOps,
            RoutingDependencies = deps,
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = stageDepts,
            ExecutionConstraints = locked ?? Array.Empty<ExecutionConstraint>(),
            Resources = resources,
            CalendarSlots = slots,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = direction,
                    AllowMerge = allowMerge,
                    AllowSplit = allowSplit
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                BatchPolicies = TestBatchPolicy.Permissive(MaterialId, DeptId, allowMerge, allowSplit),
                SetupTransitionRules = setupRules ?? Array.Empty<SetupTransitionRuleSnapshot>()
            }
        };
    }
}
