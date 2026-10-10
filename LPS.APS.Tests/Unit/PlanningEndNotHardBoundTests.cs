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
/// OWN-P0-01 专项反证（0号位 2026-10-10《APS_V1_1_20261010.md》§三）：
/// **滚动 90 天是「需求进入本轮求解的范围」，不是「资源时间终点」** ——
/// 生产路径不得把 <c>PlanningEnd</c> 当成产能截止；延期是正式排程结果。
///
/// 复审判词（本号位核对：成立）：
///   「`PlanningEnd` 在合批路径被当成硬可行性上界……不是优化偏好，而是错误的可行性判断。」
///
/// 本文件四组反证（全部走**完整** <see cref="FiniteCapacitySolver.SolveAsync"/>，纯内存，不触库）：
///   ① <see cref="OWN_P0_01_交期第20天_资源最早第91天空档_须成功排至第91天并显延期"/>
///   ② <see cref="OWN_P0_01_第180天有资源空档_须成功排至第180天并显延期"/>
///   ③ <see cref="OWN_P0_01_两条可合并C需求_第120天空档_须纳入合批候选而非因PlanningEnd失败"/>  ← 本次修复的**鉴别性**反证
///   ④ <see cref="OWN_P0_01_未来有真实日历但真实约束冲突_须输出真实原因不得硬判90天末期"/>
///
/// 口径常量：<c>PlanningEnd = PlanningStart + 90 天</c>（滚动范围），而资源日历窗一律落在 90 天**之后**。
/// </summary>
public class PlanningEndNotHardBoundTests
{
    /// <summary>计划期起点 = 2026-09-01 00:00。</summary>
    private static readonly DateTime Day = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningStart = Day;

    /// <summary>滚动 90 天 = **需求进入本轮求解的范围**（不是资源时间终点）。</summary>
    private static readonly DateTime PlanningEnd = Day.AddDays(90);

    private const int MaterialId = 1;
    private const int DeptId = 100;

    private readonly FiniteCapacitySolver _solver = new();

    // ════════════════════════════════════════════════════════════════════
    // ① 交期第 20 天、资源最早第 91 天有空档 ⇒ 必须成功排到第 91 天并显延期
    //    （单工序正排本就不看 PlanningEnd；本用例是**全文扫描**的正面证据 ——
    //      证明「交期已过 + 资源在 90 天之后」这一主路径必经分支不被 90 天截断。）
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task OWN_P0_01_交期第20天_资源最早第91天空档_须成功排至第91天并显延期()
    {
        var request = Build(
            demands: new[] { new DemandSpec("D1", 1, 1m, Day.AddDays(20)) },
            ops: new[] { new OpSpec("STAGE1", "OP10", 1, 60m) },
            deps: Array.Empty<(string, string)>(),
            calendar: new[] { (1, Day.AddDays(91), Day.AddDays(92)) });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // 需求必须被**排下**（不是 Unscheduled、不是技术失败）
        Assert.DoesNotContain(result.UnscheduledTasks, u => u.DraftId == "D1");

        var task = Assert.Single(result.FinalTasks.Where(t => t.SourceDraftId == "D1"));
        // 落在 90 天**之后**的真实日历窗内（第 91 天）
        Assert.True(task.PlannedStartTime >= Day.AddDays(91),
            $"Task 应被排到第 91 天之后的真实日历窗，实际 {task.PlannedStartTime:O}");
        Assert.True(task.PlannedEndTime <= Day.AddDays(92),
            $"Task 应完整落在真实日历窗 [Day+91, Day+92] 内，实际 {task.PlannedEndTime:O}");

        // 延期必须作为**正式排程结果**显式输出（ORDER 级 ExplanationFact，ImpactHours > 0）
        var delayFact = result.ExplanationFacts
            .FirstOrDefault(f => f.ObjectType == "ORDER" && f.ImpactHours > 0m);
        Assert.NotNull(delayFact);
        Assert.False(string.IsNullOrEmpty(delayFact!.ReasonCode));
    }

    // ════════════════════════════════════════════════════════════════════
    // ② 第 180 天才有资源空档 ⇒ 同理必须成功排到第 180 天并显延期
    //    （① 证明「刚过 90 天」，② 证明「远超 90 天」—— 不存在任何隐式上界。）
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task OWN_P0_01_第180天有资源空档_须成功排至第180天并显延期()
    {
        var request = Build(
            demands: new[] { new DemandSpec("D1", 1, 1m, Day.AddDays(20)) },
            ops: new[] { new OpSpec("STAGE1", "OP10", 1, 60m) },
            deps: Array.Empty<(string, string)>(),
            calendar: new[] { (1, Day.AddDays(180), Day.AddDays(181)) });

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.DoesNotContain(result.UnscheduledTasks, u => u.DraftId == "D1");

        var task = Assert.Single(result.FinalTasks.Where(t => t.SourceDraftId == "D1"));
        Assert.True(task.PlannedStartTime >= Day.AddDays(180),
            $"Task 应被排到第 180 天的真实日历窗，实际 {task.PlannedStartTime:O}");

        var delayFact = result.ExplanationFacts
            .FirstOrDefault(f => f.ObjectType == "ORDER" && f.ImpactHours > 0m);
        Assert.NotNull(delayFact);
    }

    // ════════════════════════════════════════════════════════════════════
    // ③【本次修复的鉴别性反证】两条可合并的 C 桶需求，唯一资源空档在第 120 天
    //    ⇒ 必须**进入合批候选并合并**，不得因 PlanningEnd（第 90 天）拒绝合批。
    //
    //    整改前：`TryMergeDemandIntoTask` 的 `if (newEndTime > planningEnd) return null;`
    //      ⇒ 合并被拒 ⇒ 回落 `ScheduleDemandOperations` ⇒ 产出 **2** 个 Task。
    //    整改后：改用**真实资源日历窗**判定承载 ⇒ 合并成立 ⇒ 产出 **1** 个 Task（数量 50）。
    //    ⚠ 判别器 = **Task 数（1 vs 2）**，不是「是否报错」。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task OWN_P0_01_两条可合并C需求_第120天空档_须纳入合批候选而非因PlanningEnd失败()
    {
        // 两条需求同物料、同单工序（Merge 仅对单工序开放）、同资源；唯一日历窗在第 120 天。
        // 交期设到第 200 天 ⇒ 合批的「交期不恶化」检查（P0-04）不拦截，本用例**只**检验 PlanningEnd 口径。
        var request = Build(
            demands: new[]
            {
                new DemandSpec("D1", 1, 30m, Day.AddDays(200)),
                new DemandSpec("D2", 2, 20m, Day.AddDays(200))
            },
            ops: new[] { new OpSpec("STAGE1", "OP10", 1, 1m) },   // 1 min/件 ⇒ 30min / 20min，合并 50min
            deps: Array.Empty<(string, string)>(),
            calendar: new[] { (1, Day.AddDays(120), Day.AddDays(121)) },
            allowMerge: true);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);

        // 合并成立 ⇒ 恰 1 个 Task（整改前为 2 个：合批被 PlanningEnd 拒绝后回落独立排程）
        var merged = Assert.Single(result.FinalTasks);
        Assert.Equal(50m, merged.Quantity);
        Assert.Equal(30m + 20m, merged.PlannedProcessQty);
        // 合并后的 Task 落在第 120 天的**真实日历窗**内（远超 PlanningEnd 第 90 天）
        Assert.True(merged.PlannedStartTime >= Day.AddDays(120),
            $"合并 Task 应落在第 120 天真实日历窗，实际 {merged.PlannedStartTime:O}");
        Assert.True(merged.PlannedEndTime <= Day.AddDays(121));

        // M:N 血缘不丢：两条需求的份额都在
        Assert.Equal(30m, result.AllocationShares.Where(s => s.AllocationSequence == 1).Sum(s => s.ComponentQty));
        Assert.Equal(20m, result.AllocationShares.Where(s => s.AllocationSequence == 2).Sum(s => s.ComponentQty));
    }

    // ════════════════════════════════════════════════════════════════════
    // ④ 未来**有真实日历**、但真实约束冲突 ⇒ 必须输出**真实约束原因**，
    //    不得硬判「90 天末期」。两组对照（同测试内两次 SolveAsync）：
    //      A. 工序在唯一资格资源上的真实日历窗**装不下**工序时长
    //         ⇒ 原因 = `CALENDAR_COVERAGE_INSUFFICIENT`（基于**真实日历末端**第 200 天，非 PlanningEnd 第 90 天）；
    //      B. 需求带**锁定硬约束**（锁窗在第 200 天，远超 90 天）
    //         ⇒ 锁定窗被**原样尊重**（Task 落在第 200 天），且延期原因 = `FROZEN_ZONE_LOCK`（真实约束），
    //            不是 `CALENDAR_COVERAGE_INSUFFICIENT`、更不是「90 天末期」。
    //    两者原因**互不相同** ⇒ 证明归因是**逐需求真实判据**，不存在「一刀切 90 天末期」。
    // ════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task OWN_P0_01_未来有真实日历但真实约束冲突_须输出真实原因不得硬判90天末期()
    {
        // ── A：真实日历覆盖不足（日历窗仅 30min，工序需 60min）──
        var requestA = Build(
            demands: new[] { new DemandSpec("D1", 1, 1m, Day.AddDays(200)) },
            ops: new[] { new OpSpec("STAGE1", "OP10", 1, 60m) },
            deps: Array.Empty<(string, string)>(),
            calendar: new[] { (1, Day.AddDays(200), Day.AddDays(200).AddMinutes(30)) });

        var resultA = await _solver.SolveAsync(requestA);

        // 业务结果（不是技术失败）
        Assert.True(resultA.Success, resultA.ErrorMessage);
        Assert.Empty(resultA.FinalTasks);
        var unschedA = Assert.Single(resultA.UnscheduledTasks);
        Assert.Equal("D1", unschedA.DraftId);
        // 真实原因 = 真实日历覆盖不足（判据落在**真实日历**第 200 天，与 PlanningEnd 第 90 天无关）
        Assert.Equal("CALENDAR_COVERAGE_INSUFFICIENT", unschedA.Reason);
        Assert.DoesNotContain("90", unschedA.Reason);
        Assert.DoesNotContain("PlanningEnd", unschedA.Reason);
        Assert.DoesNotContain("计划窗口", unschedA.Reason);

        // ── B：锁定硬约束（锁窗在第 200 天，远超 PlanningEnd 第 90 天）──
        //     锁定 Task 是**原地继承的硬锚点** ⇒ 其锁定窗不得被 90 天口径裁剪；
        //     延期原因必须是**锁**（FROZEN_ZONE_LOCK），而不是日历/90 天。
        var requestB = Build(
            demands: new[] { new DemandSpec("D1", 1, 1m, Day.AddDays(20)) },
            ops: new[] { new OpSpec("STAGE1", "OP10", 1, 60m) },
            deps: Array.Empty<(string, string)>(),
            calendar: new[] { (1, Day.AddDays(200), Day.AddDays(201)) },
            executionConstraints: new[]
            {
                new ExecutionConstraint
                {
                    DraftId = "D1",
                    ResourceId = 1,
                    LockedStart = Day.AddDays(200),
                    LockedEnd = Day.AddDays(200).AddMinutes(60),
                    ConstraintType = "MANUAL",
                    StageCode = "STAGE1",
                    OperationCode = "OP10",
                    LockedQuantity = 1m
                }
            });

        var resultB = await _solver.SolveAsync(requestB);

        Assert.True(resultB.Success, resultB.ErrorMessage);
        Assert.DoesNotContain(resultB.UnscheduledTasks, u => u.DraftId == "D1");

        // 锁定窗被原样尊重（落在第 200 天，远超 90 天）
        var lockedTask = Assert.Single(resultB.FinalTasks.Where(t => t.SourceDraftId == "D1"));
        Assert.Equal(Day.AddDays(200), lockedTask.PlannedStartTime);
        Assert.Equal(Day.AddDays(200).AddMinutes(60), lockedTask.PlannedEndTime);

        // 延期原因 = 真实约束（锁），不是日历覆盖不足、更不是「90 天末期」
        var delayFactB = resultB.ExplanationFacts
            .FirstOrDefault(f => f.ObjectType == "ORDER" && f.ImpactHours > 0m);
        Assert.NotNull(delayFactB);
        Assert.Equal("FROZEN_ZONE_LOCK", delayFactB!.ReasonCode);
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private readonly record struct DemandSpec(string Key, long Seq, decimal Qty, DateTime? Due);

    private readonly record struct OpSpec(string StageCode, string OpCode, int ResourceId, decimal Minutes);

    private static DomainSolveRequest Build(
        IReadOnlyList<DemandSpec> demands,
        IReadOnlyList<OpSpec> ops,
        IReadOnlyList<(string From, string To)> deps,
        IReadOnlyList<(int ResourceId, DateTime Start, DateTime End)> calendar,
        bool allowMerge = false,
        bool allowSplit = false,
        IReadOnlyList<ExecutionConstraint>? executionConstraints = null)
    {
        var routingOps = new List<RoutingOperation>();
        var elig = new List<OperationResourceEligibility>();
        foreach (var op in ops)
        {
            routingOps.Add(new RoutingOperation
            {
                MaterialId = MaterialId,
                ProductionDepartmentId = DeptId,
                RouteCode = "RT",
                PathId = 1,
                OperationCode = op.OpCode,
                StageCode = op.StageCode,
                StandardDuration = op.Minutes,
                OperationPlanningMode = "FINITE_RESOURCE"
            });
            elig.Add(new OperationResourceEligibility
            {
                MaterialId = MaterialId,
                ProductionDepartmentId = DeptId,
                RouteCode = "RT",
                PathId = 1,
                OperationCode = op.OpCode,
                ResourceId = op.ResourceId,
                Priority = 1,
                CapacityFactor = 1m
            });
        }

        var resources = calendar
            .Select(c => c.ResourceId)
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

        var stageDepts = ops
            .Select(o => o.StageCode)
            .Distinct()
            .Select(sc => new MaterialStageDepartmentContextDto
            {
                MaterialId = MaterialId, StageCode = sc, ProductionDepartmentId = DeptId
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
                LogicalDemandKey = d.Key,
                PlanVersionId = 1L,
                DomainKey = "DOMAIN",
                AllocationSequence = d.Seq,
                DemandKey = d.Key,
                MaterialId = MaterialId,
                FactoryId = 1,
                NetOutputQty = d.Qty,
                PlannedProcessQty = d.Qty,
                RequiredAvailableTime = d.Due ?? PlanningStart.AddDays(20),
                DemandSequence = (int)d.Seq,
                RouteCode = "RT",
                PathId = 1
            }).ToList(),
            RoutingOperations = routingOps,
            RoutingDependencies = deps.Select(dp => new RoutingDependency
            {
                MaterialId = MaterialId,
                ProductionDepartmentId = DeptId,
                RouteCode = "RT",
                PathId = 1,
                FromOperationCode = dp.From,
                ToOperationCode = dp.To,
                DependencyType = "ES",
                LagTime = 0m,
                IsActive = true
            }).ToList(),
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = stageDepts,
            ExecutionConstraints = executionConstraints ?? Array.Empty<ExecutionConstraint>(),
            Resources = resources,
            CalendarSlots = slots,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = allowMerge,
                    AllowSplit = allowSplit
                },
                BatchPolicies = TestBatchPolicy.Permissive(new[] { MaterialId }, allowMerge, allowSplit)
            }
        };
    }
}
