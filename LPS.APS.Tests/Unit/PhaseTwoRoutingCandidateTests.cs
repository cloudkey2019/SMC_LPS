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
/// Phase2 多 Routing 候选（Path-aware）回归测试 —— 0号位 2026-10-07 裁决 Q-1/Q-2/Q-3 + §四/§五 落码验收。
///
/// 被测分支：<see cref="PhaseTwoInitialScheduler"/> 需求级「候选解析 → 试排 → 择优 → 落定」四步。
///
/// 判据来源（逐条对应，不引新口径）：
/// · **Q-1**：统一四层目标（① 硬约束 → ② 履约 → ③ 交期/Direction → ④ 次级），
///   **禁止新增 Routing 专属加权目标函数**；PreferredResource 只进次级、不得 Hard Lock。
/// · **Q-2**：「候选内有界联合择优就是 V1 范围」，但**不得脱离真实上下文孤立选路**；
///   冻结业务单位 = Execution Batch（每个 ExecutionBatchDraftKey 一条完整 Path）。
/// · **Q-3**：任务级按自身 RouteCode + PathId 解析；**Fail Closed 红线** ——
///   声明了固定路径而图中不存在 ⇒ 不排，**不得**回退 TryGetSingleRoutingGraph 猜 Path。
/// · **§四**：**禁止用「候选 Path 条数」反推 A/B/C 身份**（故本文件只断言行为，不造桶枚举）。
/// · **§五**：Scheduling 内部自组 RoutingCandidateView 属临时适配；正式 Contract 仍归 2号位。
///
/// 全部纯内存，不触库（用户红线：Integration 直连生产库，只跑 Unit）。
/// </summary>
public class PhaseTwoRoutingCandidateTests
{
    private static readonly DateTime PlanningStart = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningEnd = new DateTime(2026, 10, 31, 0, 0, 0);

    private const int MaterialId = 1;
    private const int DeptId = 100;

    private readonly FiniteCapacitySolver _solver = new();

    /// <summary>一条候选路径的描述：路由身份 + 承载资源 + 该资源日历窗（null = 无日历 ⇒ 不可行）
    /// + **本路径工序标准工时**（P1-01：双 Route **不同 Lead** ⇒ 同一需求的两条候选可自决出**不同** Direction）。</summary>
    private readonly record struct PathSpec(
        string RouteCode, int PathId, int ResourceId, DateTime? CalStart, DateTime? CalEnd,
        decimal LeadMinutes = 60m);

    /// <summary>
    /// 造一条两工序路径（OP10@STAGE1 → OP20@STAGE2，各 60 分钟），资源与日历按 <paramref name="paths"/> 铺。
    /// </summary>
    private static DomainSolveRequest Build(
        IReadOnlyList<PathSpec> paths,
        string? demandRouteCode = null,
        int? demandPathId = null,
        string direction = "FORWARD",
        ExecutionConstraint? locked = null,
        bool isContinuation = false,
        string? continuationKey = null,
        DateTime? due = null,
        string? preferredResourceCode = null)
    {
        var ops = new List<RoutingOperation>();
        var deps = new List<RoutingDependency>();
        var elig = new List<OperationResourceEligibility>();
        var resources = new List<ResourceDefinition>();
        var calendars = new List<ResourceCalendarSlot>();
        var stageDepts = new List<MaterialStageDepartmentContextDto>();

        foreach (var p in paths)
        {
            foreach (var (code, stage) in new[] { ("OP10", "STAGE1"), ("OP20", "STAGE2") })
            {
                ops.Add(new RoutingOperation
                {
                    MaterialId = MaterialId,
                    ProductionDepartmentId = DeptId,
                    RouteCode = p.RouteCode,
                    PathId = p.PathId,
                    OperationCode = code,
                    StageCode = stage,
                    StandardDuration = p.LeadMinutes,
                    OperationPlanningMode = "FINITE_RESOURCE"
                });

                elig.Add(new OperationResourceEligibility
                {
                    MaterialId = MaterialId,
                    ProductionDepartmentId = DeptId,
                    RouteCode = p.RouteCode,
                    PathId = p.PathId,
                    OperationCode = code,
                    ResourceId = p.ResourceId,
                    Priority = 1,
                    CapacityFactor = 1m
                });
            }

            // 同 Path 内 OP10 → OP20
            deps.Add(new RoutingDependency
            {
                MaterialId = MaterialId,
                ProductionDepartmentId = DeptId,
                RouteCode = p.RouteCode,
                PathId = p.PathId,
                FromOperationCode = "OP10",
                ToOperationCode = "OP20"
            });

            resources.Add(new ResourceDefinition
            {
                ResourceId = p.ResourceId, ResourceCode = $"R{p.ResourceId}",
                FactoryCode = "F1", Capacity = 1m
            });

            // 日历缺省 ⇒ 该路径不可行（不造虚拟产能，0号位 §11.2）
            if (p.CalStart is DateTime cs && p.CalEnd is DateTime ce)
            {
                calendars.Add(new ResourceCalendarSlot
                {
                    ResourceId = p.ResourceId, Start = cs, End = ce, IsAvailable = true
                });
            }
        }

        foreach (var stage in new[] { "STAGE1", "STAGE2" })
        {
            stageDepts.Add(new MaterialStageDepartmentContextDto
            {
                MaterialId = MaterialId, StageCode = stage, ProductionDepartmentId = DeptId
            });
        }

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = new List<LogicalProductionDemand>
            {
                new()
                {
                    LogicalDemandKey = "D1", PlanVersionId = 1L, DomainKey = "DOMAIN",
                    AllocationSequence = 1, DemandKey = "D1", MaterialId = MaterialId, FactoryId = 1,
                    StartStageCode = "STAGE1",
                    NetOutputQty = 1m, PlannedProcessQty = 1m,
                    // `due` 可覆写：B-005 Direction 自决的 E2E 用例靠「只改交期」驱动 Slack 变化。
                    RequiredAvailableTime = due ?? PlanningStart.AddDays(20), DemandSequence = 1,
                    RouteCode = demandRouteCode, PathId = demandPathId,
                    // B-005「Resource」上下文载体（P1-11 软偏好；仅进次级，不 Hard Lock）。
                    PreferredResourceCode = preferredResourceCode,
                    // 0号位 2026-10-07 (5).md P0-05：连续份额输入完整性 Fail Closed 的反证锁用。
                    IsContinuation = isContinuation,
                    ContinuationKey = continuationKey,
                    NoSplitMerge = isContinuation
                }
            },
            RoutingOperations = ops,
            RoutingDependencies = deps,
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = stageDepts,
            ExecutionConstraints = locked is null
                ? Array.Empty<ExecutionConstraint>()
                : new[] { locked },
            Resources = resources,
            CalendarSlots = calendars,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters { SchedulingDirection = direction },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是 Routing 候选择优，非批决策 ⇒ Material 级宽松策略（恒 1 批）。
                BatchPolicies = TestBatchPolicy.Permissive(MaterialId, DeptId)
            }
        };
    }

    /// <summary>
    /// ① 单路径零回归：既有单路径物料行为**完全不变**，且路径身份/归批键正确落到 Task。
    ///    （C桶单候选 = A/B 固定路径 = 既有行为，三者共用同一段代码 ⇒ 本用例是回归基线。）
    /// </summary>
    [Fact]
    public async Task 单路径_零回归_路径身份与归批键落到Task()
    {
        var result = await _solver.SolveAsync(Build(new[]
        {
            new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd)
        }));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        var tasks = result.FinalTasks.OrderBy(t => t.StageCode, StringComparer.Ordinal).ToList();
        Assert.Equal(2, tasks.Count);

        // v1.6 §1：真实 RouteCode/PathId（不得是 DEFAULT/1 归一化残值）
        Assert.All(tasks, t => Assert.Equal("RTA", t.RouteCode));
        Assert.All(tasks, t => Assert.Equal(1, t.PathId));

        // v1.6 `:30`：同一 Execution Batch 下多 Operation FinalTask 共 Key
        var batchKey = tasks[0].ExecutionBatchDraftKey;
        Assert.False(string.IsNullOrEmpty(batchKey));
        Assert.All(tasks, t => Assert.Equal(batchKey, t.ExecutionBatchDraftKey));

        // OP10 → OP20 串行（路径图依赖仍生效）
        var op10 = tasks.Single(t => t.OperationCode == "OP10");
        var op20 = tasks.Single(t => t.OperationCode == "OP20");
        Assert.True(op20.PlannedStartTime >= op10.PlannedEndTime);
    }

    /// <summary>
    /// ② 两条竞争路径、均可行 ⇒ 按 Q-1 统一四层目标（FORWARD ⇒ 更早可行完成优先）选出更早那条。
    ///    RTA 的资源日历从 D+10 才开 ⇒ RTB（D+0 开工）完成更早 ⇒ 应选 RTB。
    /// </summary>
    [Fact]
    public async Task 双路径_均可行_按Direction选更早完成者()
    {
        var result = await _solver.SolveAsync(Build(new[]
        {
            new PathSpec("RTA", 1, 1, PlanningStart.AddDays(10), PlanningEnd),
            new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
        }));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Empty(result.UnscheduledTasks);

        // 全批只允许一条 Path（Q-2：每个 Execution Batch 一条完整 Path）
        Assert.All(result.FinalTasks, t => Assert.Equal("RTB", t.RouteCode));
        Assert.All(result.FinalTasks, t => Assert.Equal(2, t.ResourceId));
    }

    /// <summary>
    /// ③ 两条路径**均不可行**（资源无日历）⇒ 需求 Unscheduled，不产 Task。
    ///    禁止「候选内挑不出就硬塞一条」。
    /// </summary>
    [Fact]
    public async Task 双路径_全不可行_需求Unscheduled()
    {
        var result = await _solver.SolveAsync(Build(new[]
        {
            new PathSpec("RTA", 1, 1, null, null),
            new PathSpec("RTB", 1, 2, null, null)
        }));

        Assert.Empty(result.FinalTasks);
        Assert.Contains(result.UnscheduledTasks, u => u.DraftId == "D1");
    }

    /// <summary>
    /// ④ Calendar 判定**只对选中 Path** 生效：RTA 无日历（不可行）但 RTB 可行
    ///    ⇒ 需求经 RTB 排下，**不得**因未选中的 RTA 覆盖不足而误判 `CALENDAR_COVERAGE_INSUFFICIENT`。
    ///    （0号位 §11.3：Calendar 覆盖判定须绑定实际选用的路径，不得对旁路候选误伤。）
    /// </summary>
    [Fact]
    public async Task 双路径_仅一条日历不覆盖_选可行者且不误报日历不足()
    {
        var result = await _solver.SolveAsync(Build(new[]
        {
            new PathSpec("RTA", 1, 1, null, null),
            new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
        }));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.All(result.FinalTasks, t => Assert.Equal("RTB", t.RouteCode));
        Assert.DoesNotContain(result.UnscheduledTasks,
            u => u.Reason == "CALENDAR_COVERAGE_INSUFFICIENT");
    }

    /// <summary>
    /// ⑤ 任务级不串 Path：两条路径**含同名工序码**（OP10/OP20 在 RTA、RTB 各一份）⇒
    ///    选中路径的每个 Task 的 (RouteCode, PathId) 必须自洽，且**不得**混入另一 Path 的节点/资源。
    ///    （Q-3 + 0号位 §5.3：节点身份与资格键都须带 Path 维度。）
    /// </summary>
    [Fact]
    public async Task 双路径_同名工序码_不串Path()
    {
        var result = await _solver.SolveAsync(Build(new[]
        {
            new PathSpec("RTA", 1, 1, PlanningStart.AddDays(10), PlanningEnd),
            new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
        }));

        Assert.True(result.Success, result.ErrorMessage);

        // 选中路径唯一
        var identities = result.FinalTasks
            .Select(t => (t.RouteCode, t.PathId))
            .Distinct()
            .ToList();
        Assert.Single(identities);

        // 选中 RTB ⇒ 资源只能是 RTB 的资源（RTA 的 R1 不得出现）
        Assert.All(result.FinalTasks, t => Assert.Equal(2, t.ResourceId));
        Assert.All(result.FinalTasks, t => Assert.Equal("RTB", t.RouteCode));
        Assert.All(result.FinalTasks, t => Assert.Equal(1, t.PathId));
    }

    /// <summary>
    /// ⑥ Q-3 Fail Closed 红线：需求**声明了固定路径**（A/B 桶语义）而图中不存在该路径
    ///    ⇒ **不排**（Unscheduled），**不得**回退 <c>TryGetSingleRoutingGraph</c> 猜唯一 Path 顶上。
    ///    本用例是「禁回退」的**反证锁**：图里确有 RTA/RTB 可用，若实现回退猜路径，本用例会排下 ⇒ 红。
    /// </summary>
    [Fact]
    public async Task 声明固定路径_图中不存在_FailClosed不排()
    {
        var result = await _solver.SolveAsync(Build(
            new[]
            {
                new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd),
                new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
            },
            demandRouteCode: "RTX",   // 图中不存在
            demandPathId: 1));

        Assert.Empty(result.FinalTasks);
        Assert.Contains(result.UnscheduledTasks, u => u.DraftId == "D1");
    }

    /// <summary>
    /// ⑦ Q-3 Fail Closed（**锁定任务**分支）：多路径物料 + 锁定任务**未带固定路径**
    ///    ⇒ 身份反查解不出真实 (RouteCode, PathId) ⇒ **不产伪身份 FinalTask**。
    ///    依据：0号位 2026-10-07 裁决 Q-3 `:186`「Route/Path 缺失时应 Fail Closed 或进入明确异常，
    ///    不得跨 Path 寻找替代节点」；同件职责表 `:357`「A/B 固定 Route/Path … **缺值 Fail Closed**」。
    ///    ⚠ 本用例是整改的**反证锁**：整改前该分支会带着 `RouteCode=null / PathId=null` 产出 FinalTask ⇒ 红。
    /// </summary>
    [Fact]
    public async Task 锁定任务_多路径且无固定路径_不产伪身份Task()
    {
        var locked = new ExecutionConstraint
        {
            DraftId = "D1",
            ResourceId = 1,
            LockedStart = PlanningStart,
            LockedEnd = PlanningStart.AddHours(1),
            ConstraintType = "MANUAL",
            StageCode = "STAGE1",
            OperationCode = "OP10",
            LockedQuantity = 0.5m
        };

        var result = await _solver.SolveAsync(Build(
            new[]
            {
                new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd),
                new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
            },
            locked: locked));

        // 核心断言：任何 FinalTask 都必须带**真实**路径身份（不得出现 null / 空串）
        Assert.DoesNotContain(result.FinalTasks,
            t => string.IsNullOrEmpty(t.RouteCode) || t.PathId is null);

        // 且身份必须落在**候选路径内**（不得跨 Path 编造替代节点）
        Assert.All(result.FinalTasks, t =>
        {
            Assert.True(t.RouteCode is "RTA" or "RTB", $"非候选路径身份：{t.RouteCode}");
            Assert.Equal(1, t.PathId);
        });

        // ── P0-01（0号位 2026-10-09 第三轮复审 §二）：「不得假成功」的**回归护栏** ──
        //   锁定任务身份不可恢复属**输入完整性问题**，绝不能被当成「普通业务排不下」（后者 Success 仍为 true）。
        //   ⚠ 本夹具（**部分**锁定 0.5）里 Phase5 硬校验会先一步拦下（「需求 D1 未排定却产出超额 FinalTask」）
        //     ⇒ 整改前后 Success 均为 false ⇒ 本断言**不是**该整改的鉴别器，只是护栏。
        //   鉴别器见 ⑦-a / ⑦-b：那两处 Phase5 校验**通过**，唯一使其 Success=false 的就是本整改。
        Assert.False(result.Success, "锁定Task身份不可恢复必须使 Success=false（不得假成功）");
    }

    /// <summary>
    /// ⑦-a P0-01 **反证**（复审 §二 点名：「应补 `LockedTask + 多Path + 路径缺失` 反证，并验证没有假成功」）：
    ///   多路径物料 + 锁定任务无固定路径 ⇒ 身份不可恢复；且锁定量 = 需求全量 ⇒ 需求**全量锁定**、
    ///   不再排剩余份额 ⇒ 最终集合里**没有任何 Task**，Phase5 的「锁定锚点时间」与「未排定却超额」两条
    ///   校验都**通过**。
    ///   ⇒ 此时**唯一**能让 `Success=false` 的就是本整改写入的 `TechnicalFailure`；
    ///     ⚠ 整改前该输入会得到「**无锁定 Task、却 Success=true**」的假成功 ⇒ 本用例**红**。
    /// </summary>
    [Fact]
    public async Task 锁定任务_多路径无固定路径_全量锁定_身份不可恢复_不假成功()
    {
        var locked = new ExecutionConstraint
        {
            DraftId = "D1",
            ResourceId = 1,
            LockedStart = PlanningStart,
            LockedEnd = PlanningStart.AddHours(1),
            ConstraintType = "MANUAL",
            StageCode = "STAGE1",
            OperationCode = "OP10",
            LockedQuantity = 1m        // = 需求 NetOutputQty ⇒ 全量锁定 ⇒ 不排剩余份额
        };

        var result = await _solver.SolveAsync(Build(
            new[]
            {
                new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd),
                new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
            },
            locked: locked));

        Assert.Empty(result.FinalTasks);
        Assert.False(result.Success, "有锁定事实却身份不可恢复，必须 Success=false（不得假成功）");
        Assert.Contains("锁定执行Task身份不可恢复", result.ErrorMessage ?? string.Empty);
    }

    /// <summary>
    /// ⑦-b P0-01 **反证**（**节点缺失**变体）：单路径物料（图可解）但锁定任务的
    ///   (StageCode, OperationCode) 在图中不存在 ⇒ 身份不可恢复；锁定量 = 需求全量 ⇒ 无 Task
    ///   ⇒ Phase5 校验通过 ⇒ 唯一使 Success=false 的是本整改。
    ///   本用例把「图缺失」与「节点缺失」两条路径分开锁死（复审 §二 点名三种情形）。
    /// </summary>
    [Fact]
    public async Task 锁定任务_节点在图中缺失_技术失败不假成功()
    {
        var locked = new ExecutionConstraint
        {
            DraftId = "D1",
            ResourceId = 1,
            LockedStart = PlanningStart,
            LockedEnd = PlanningStart.AddHours(1),
            ConstraintType = "MANUAL",
            StageCode = "STAGE9",        // 图中不存在
            OperationCode = "OP90",      // 图中不存在
            LockedQuantity = 1m          // 全量锁定 ⇒ 不排剩余份额
        };

        var result = await _solver.SolveAsync(Build(
            new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd) },
            locked: locked));

        Assert.False(result.Success, "锁定Task节点缺失必须使 Success=false（不得假成功）");
        Assert.Contains("锁定执行Task身份不可恢复", result.ErrorMessage ?? string.Empty);
        Assert.Contains("图中无该", result.ErrorMessage ?? string.Empty);
    }

    /// <summary>
    /// ⑦-c P0-01 反证（**对应需求缺失**变体）：锁定任务引用的 DraftId 在本次请求里
    ///   没有对应需求 ⇒ 身份不可恢复 ⇒ 同样必须 Success=false。
    ///   （其余需求仍正常排程 ⇒ 本用例同时锁死「技术失败 ≠ 整域无产出」的既有载体语义。）
    /// </summary>
    [Fact]
    public async Task 锁定任务_对应需求缺失_技术失败不假成功()
    {
        var locked = new ExecutionConstraint
        {
            DraftId = "D_MISSING",   // 请求中不存在该需求
            ResourceId = 1,
            LockedStart = PlanningStart,
            LockedEnd = PlanningStart.AddHours(1),
            ConstraintType = "MANUAL",
            StageCode = "STAGE1",
            OperationCode = "OP10",
            LockedQuantity = 0.5m
        };

        var result = await _solver.SolveAsync(Build(
            new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd) },
            locked: locked));

        Assert.False(result.Success, "锁定Task对应需求缺失必须使 Success=false（不得假成功）");
        Assert.Contains("锁定执行Task身份不可恢复", result.ErrorMessage ?? string.Empty);
        Assert.Contains("(对应需求缺失)", result.ErrorMessage ?? string.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ⑧ Direction `AUTO` **正式按上下文自决**（0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》
    //    §三 **P1-DIR-01** / §十四 第二优先级 / §十五 第 3 项）
    //
    // 整改要点：**撤销**「`AUTO` 与 `MIXED` 行为等价」（0号位 2026-10-07 Q-1 的旧口径已被明判
    //   「不能再作为最终 V1 实现」）。`AUTO` 现由 `SchedulingDirectionResolver` 按 Demand / Execution Batch
    //   **自身上下文**（RequiredAvailableTime/Slack、Material、Resource、Execution、Firm-Frozen-Lock；
    //   DemandGoal 载体缺失则登记缺口）正式裁决为 FORWARD / BACKWARD / MIXED 之一。
    //
    // 断言方式（行为层；**对 Phase5 压缩免疫**）：`AUTO` 的结果必须**逐字段等于**
    //   「把自决方向显式传入」的结果；且同一夹具**只改一处上下文**时结果必须随之变化
    //   ⇒ 证明 AUTO 不是「恒等某一固定方向」的空壳。
    //
    // 覆盖 §十五 第 3 项点名的四类场景：
    //   ① 明显倒排（交期紧）② 明显正排（交期松）③ 连续份额（A/B 既存执行批）④ Slack / 资源 致 Direction 变化。
    //   ⚠ Material（`dynamicMaterialFloor`）为 Scheduling 内部派生量，其 Direction 影响由
    //     `SchedulingDirectionResolverTests` 判据层覆盖；本文件只做行为层 E2E。
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>排程几何签名（确定性字符串）：用于「AUTO 结果 == 显式方向结果」与「上下文变 ⇒ 结果变」的比对。</summary>
    private static string Signature(DomainSolveResult r)
        => string.Join("\n", r.FinalTasks
            .OrderBy(t => t.StageCode, StringComparer.Ordinal)
            .ThenBy(t => t.OperationCode, StringComparer.Ordinal)
            .Select(t => $"{t.StageCode}|{t.OperationCode}|{t.ResourceId}|{t.RouteCode}|{t.PathId}"
                       + $"|{t.PlannedStartTime:O}|{t.PlannedEndTime:O}"));

    private static void AssertSameSchedule(DomainSolveResult expected, DomainSolveResult actual)
    {
        Assert.Equal(expected.FinalTasks.Count, actual.FinalTasks.Count);
        Assert.Equal(Signature(expected), Signature(actual));
    }

    /// <summary>
    /// ⑧-① **明显倒排场景**：两工序各 60min（lead = 120min），交期 = 计划起点 + 3h
    ///   ⇒ Slack = 180 − 120 = 60min，落在工艺周期内 ⇒ 无交期信号、无其它上下文
    ///   ⇒ 确定性默认 **BACKWARD**（`NO_CONTEXT_SIGNAL`，靠交期减少 WIP）。
    ///   行为断言：AUTO 结果 == 显式 BACKWARD 结果。
    /// </summary>
    [Fact]
    public async Task AUTO_明显倒排场景_自决为BACKWARD()
    {
        var paths = new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd) };
        var due = PlanningStart.AddHours(3);

        var auto = await _solver.SolveAsync(Build(paths, direction: "AUTO", due: due));
        var backward = await _solver.SolveAsync(Build(paths, direction: "BACKWARD", due: due));

        Assert.True(auto.Success, auto.ErrorMessage);
        AssertSameSchedule(backward, auto);
    }

    /// <summary>
    /// ⑧-② **明显正排场景**：同一夹具，只把交期推到 20 天后
    ///   ⇒ Slack = 20d − 120min ≫ lead ⇒ `DUE_LOOSE`（最早承诺）⇒ **FORWARD**。
    /// </summary>
    [Fact]
    public async Task AUTO_明显正排场景_自决为FORWARD()
    {
        var paths = new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd) };
        var due = PlanningStart.AddDays(20);

        var auto = await _solver.SolveAsync(Build(paths, direction: "AUTO", due: due));
        var forward = await _solver.SolveAsync(Build(paths, direction: "FORWARD", due: due));

        Assert.True(auto.Success, auto.ErrorMessage);
        AssertSameSchedule(forward, auto);
    }

    /// <summary>
    /// ⑧-③ **连续份额场景**：`IsContinuation = true`（A/B 既存执行批，已经开始）
    ///   ⇒ `CONTINUATION_SLICE` 正向信号（最早承诺）⇒ **FORWARD**（交期取中性值以免掩盖该信号）。
    ///   反向对照：同夹具去掉连续性 ⇒ 退回 ⑧-① 的 BACKWARD ⇒ 证明 Execution 上下文**真的**参与裁决。
    /// </summary>
    [Fact]
    public async Task AUTO_连续份额场景_自决为FORWARD()
    {
        var paths = new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd) };
        var due = PlanningStart.AddHours(3);

        var auto = await _solver.SolveAsync(Build(paths, direction: "AUTO",
            demandRouteCode: "RTA", demandPathId: 1, isContinuation: true, due: due));
        var forward = await _solver.SolveAsync(Build(paths, direction: "FORWARD",
            demandRouteCode: "RTA", demandPathId: 1, isContinuation: true, due: due));

        Assert.True(auto.Success, auto.ErrorMessage);
        AssertSameSchedule(forward, auto);

        // 反向对照：同一交期下，仅去掉连续性 ⇒ 方向退回 BACKWARD（= ⑧-①）
        var notContinuation = await _solver.SolveAsync(Build(paths, direction: "AUTO", due: due));
        AssertSameSchedule(
            await _solver.SolveAsync(Build(paths, direction: "BACKWARD", due: due)), notContinuation);
        Assert.NotEqual(Signature(auto), Signature(notContinuation));
    }

    /// <summary>
    /// ⑧-④ **Slack / Resource 导致 Direction 变化场景**：
    ///   · Slack：同一夹具只改交期（3h ↔ 20d）⇒ 方向 BACKWARD ↔ FORWARD，结果必须变；
    ///   · Resource：交期取中性（3h）时，仅加 `PreferredResourceCode` ⇒ `PREFERRED_RESOURCE` 正向信号
    ///     ⇒ 方向由 BACKWARD 翻为 FORWARD。
    /// </summary>
    [Fact]
    public async Task AUTO_仅改Slack或Resource_自决方向随之变化()
    {
        var paths = new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd) };
        var tightDue = PlanningStart.AddHours(3);

        // ── Slack 变化 ──
        var tight = await _solver.SolveAsync(Build(paths, direction: "AUTO", due: tightDue));
        var loose = await _solver.SolveAsync(Build(paths, direction: "AUTO", due: PlanningStart.AddDays(20)));

        Assert.True(tight.Success, tight.ErrorMessage);
        Assert.True(loose.Success, loose.ErrorMessage);
        AssertSameSchedule(
            await _solver.SolveAsync(Build(paths, direction: "BACKWARD", due: tightDue)), tight);
        AssertSameSchedule(
            await _solver.SolveAsync(Build(paths, direction: "FORWARD", due: PlanningStart.AddDays(20))), loose);
        Assert.NotEqual(Signature(tight), Signature(loose));

        // ── Resource 偏好变化（交期不变）──
        var withPref = await _solver.SolveAsync(Build(paths, direction: "AUTO",
            due: tightDue, preferredResourceCode: "R1"));

        Assert.True(withPref.Success, withPref.ErrorMessage);
        AssertSameSchedule(
            await _solver.SolveAsync(Build(paths, direction: "FORWARD",
                due: tightDue, preferredResourceCode: "R1")), withPref);
        Assert.NotEqual(Signature(tight), Signature(withPref));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ⑨ P1-01 反证（0号位 2026-10-09《APS_V1_2_20261009.md》§三 P1-01）：
    //    **每条候选的 Direction 必须与其自身工序一致；胜出候选按其自身方向落定**
    //
    // 复审判词（源码证实，成立）：旧实现取 `plannedCandidates.First(p => p.Ops.Count > 0).Ops`
    //   解析**一个** `resolvedDirection` 再套给所有 Batch / Route；而 Slack 判据（`DUE_TIGHT`/`DUE_LOOSE`）
    //   依赖该候选的**总标准工时** ⇒ 两条合法 Route 工时不同即可让一条判 FORWARD、另一条判 BACKWARD
    //   ⇒ **方向由「任意第一 Path」外推全路由**，候选比较 / 落定所用方向与该候选真实上下文脱节。
    //
    // ⚠ **关于「候选顺序互换」的如实说明**（不编造鉴别力）：
    //   `PhaseOneConstraintBuilder.TryGetRoutingGraphs`（`:1090-1093`）已按 `(RouteCode, PathId)` 排序
    //   ⇒ `plannedCandidates` 的**列表顺序与输入顺序无关** ⇒ 单纯交换 `RoutingOperations` 顺序
    //     **构造不出**「结果漂移」（旧实现同样不漂移）。故本反证**不用**「换序」，而直接断言
    //     「**非首条候选胜出时，它必须按自己的方向落定**」—— 这正是旧实现真正错的地方。
    //
    // 夹具几何（`EffectiveDue` = 交期；倒排下末工序锚在交期 ⇒ 可行候选完成时间恒 = 交期）：
    //   · `RTA`：lead 30min、**无日历 ⇒ 不可行**（但工序非空 ⇒ 旧实现仍取它做方向外推）。
    //       Slack = 400 − 30 = 370 > lead ⇒ `DUE_LOOSE` ⇒ **本候选自决 FORWARD**。
    //   · `RTB`：lead 300min、日历充足 ⇒ 可行。
    //       Slack = 400 − 300 = 100 ∈ [0, lead] ⇒ 无交期信号、无其它上下文
    //       ⇒ `NO_CONTEXT_SIGNAL` ⇒ **本候选自决 BACKWARD**。
    //   ⇒ 胜出者必为 RTB（RTA 不可行）。旧实现：方向取首条 RTA 的 FORWARD ⇒ RTB 按**正排**落定
    //     `[P, P+300]`；整改后：RTB 按**自己的 BACKWARD** 落定 `[P+100, P+400]`。
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⑨-① **双 Route 不同 Lead** ⇒ AUTO 下**每条候选按自身工序自决**，胜出候选按其**自身方向**落定。
    ///   夹具见上方几何说明：RTA（首条、不可行、自决 FORWARD）、RTB（可行、自决 BACKWARD）。
    ///   行为断言：AUTO 结果 == **显式 BACKWARD** 结果（都按 RTB 自己的 BACKWARD 落定），
    ///     且 != 显式 FORWARD 结果（证明不是「恒等 FORWARD」，也证明方向**不是**从首条 RTA 外推来的）。
    /// </summary>
    [Fact]
    public async Task AUTO_双Route不同Lead_胜出候选须按自身工序自决的方向落定()
    {
        var paths = new[]
        {
            // RTA：(RouteCode,PathId) 序最小 ⇒ 旧实现的「首条」；无日历 ⇒ 不可行（但仍参与方向外推）。
            new PathSpec("RTA", 1, 1, null, null, LeadMinutes: 30m),
            // RTB：可行且必为胜出者。
            new PathSpec("RTB", 1, 2, PlanningStart, PlanningStart.AddDays(30), LeadMinutes: 300m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 1m, null, null, Due: PlanningStart.AddMinutes(400)) };

        var auto = await _solver.SolveAsync(BuildSingleOp(paths, demands, direction: "AUTO"));
        var backward = await _solver.SolveAsync(BuildSingleOp(paths, demands, direction: "BACKWARD"));
        var forward = await _solver.SolveAsync(BuildSingleOp(paths, demands, direction: "FORWARD"));

        Assert.True(auto.Success, auto.ErrorMessage);
        Assert.True(backward.Success, backward.ErrorMessage);
        Assert.True(forward.Success, forward.ErrorMessage);

        // 夹具自证「有鉴别力」：两个显式方向对同一条可行路径必须给出**不同**落点
        //   （倒排 ⇒ 完成 = 交期 P+400；正排 ⇒ 完成 = P+300）。否则本用例无鉴别力。
        Assert.NotEqual(Signature(forward), Signature(backward));

        // 胜出路径必须是 RTB（RTA 无日历不可行）—— 即**非首条**候选胜出（旧实现的方向外推源就是首条 RTA）。
        var d1Auto = auto.FinalTasks.Single(t => t.SourceDraftId == "D1");
        Assert.Equal("RTB", d1Auto.RouteCode);

        // 反证核心：胜出候选必须按**它自己**（BACKWARD）的方向落定 ⇒ 与显式 BACKWARD 逐字段一致。
        //   旧实现用首条 RTA 的方向（FORWARD）外推 ⇒ RTB 落成 [P, P+300] ⇒ 本断言**红**。
        AssertSameSchedule(backward, auto);

        // 且**不得**退化成显式 FORWARD 的落点。
        Assert.NotEqual(Signature(forward), Signature(auto));
    }

    /// <summary>
    /// ⑨-③ **候选输入顺序互换 ⇒ 最终合法择优不得漂移**（0号位 2026-10-09《APS_V1_2_20261009.md》§五.4）。
    ///
    /// 几何：两条**均可行**的路径，lead 差异使**各自**自决出**不同** Direction ——
    ///   · `RTA`（lead 300）：Slack = 400 − 300 = 100 ∈ [0, 300] ⇒ `NO_CONTEXT_SIGNAL` ⇒ **BACKWARD**；
    ///   · `RTB`（lead 30） ：Slack = 400 − 30 = 370 &gt; 30 ⇒ `DUE_LOOSE` ⇒ **FORWARD**。
    ///   两条候选方向**不同** ⇒ 候选比较第 ③ 层（交期）**不可比**（各自目标函数相反：BACKWARD 要贴近 Due、
    ///   FORWARD 要更早）⇒ 判平后由第 ⑤ 层确定性 `(RouteCode, PathId)` 序裁决 ⇒ 胜出者恒为 `RTA`。
    ///
    /// 与 ⑨-① 的分工（**如实说明，不夸大鉴别力**）：
    ///   · ⑨-① 是**可判别的**反证（首条不可行 ⇒ 方向错用会真正改变胜出者的落点）；
    ///   · 本用例的鉴别对象是 §五.4 字面要求「候选顺序互换不漂移」。因
    ///     `PhaseOneConstraintBuilder.TryGetRoutingGraphs`（`:1090-1093`）已按 `(RouteCode, PathId)` 排序，
    ///     候选列表顺序**与输入声明顺序无关** ⇒ 本用例对旧实现同样通过 —— 它是**回归护栏**
    ///     （防未来把列表顺序重新引入决策），而非「整改前必红」的鉴别性反证。
    /// </summary>
    [Fact]
    public async Task AUTO_双Route_候选输入顺序互换_选路与方向不漂移()
    {
        var rta = new PathSpec("RTA", 1, 1, PlanningStart, PlanningStart.AddDays(30), LeadMinutes: 300m);
        var rtb = new PathSpec("RTB", 1, 2, PlanningStart, PlanningStart.AddDays(30), LeadMinutes: 30m);
        var demands = new[] { new DemandSpec2("D1", 1, 1m, null, null, Due: PlanningStart.AddMinutes(400)) };

        // 同一请求、只把两条候选的**声明顺序**互换（`BuildSingleOp` 按入参顺序逐条铺 `RoutingOperations`）。
        var ordered = await _solver.SolveAsync(BuildSingleOp(new[] { rta, rtb }, demands, direction: "AUTO"));
        var swapped = await _solver.SolveAsync(BuildSingleOp(new[] { rtb, rta }, demands, direction: "AUTO"));

        Assert.True(ordered.Success, ordered.ErrorMessage);
        Assert.True(swapped.Success, swapped.ErrorMessage);

        // ① §五.4 字面要求：候选输入顺序互换 ⇒ 最终合法择优**逐字段不漂移**。
        AssertSameSchedule(ordered, swapped);

        // ② 胜出者 = `(RouteCode, PathId)` 序最小者（确定性 tiebreak），**不是**「输入里的第一条」。
        var d1 = ordered.FinalTasks.Single(t => t.SourceDraftId == "D1");
        Assert.Equal("RTA", d1.RouteCode);

        // ③ 「候选比较所用方向」与「真实落定所用方向」同向：`RTA` 自决 BACKWARD ⇒ 落定与显式 BACKWARD 一致。
        var backward = await _solver.SolveAsync(BuildSingleOp(new[] { rta, rtb }, demands, direction: "BACKWARD"));
        Assert.True(backward.Success, backward.ErrorMessage);
        AssertSameSchedule(backward, ordered);

        // ④ 反向对照：方向确实参与结果 —— 显式 FORWARD 下 `RTB`（完成更早）胜出 ⇒ 签名必须不同
        //    （否则本夹具无法证明「方向真的影响了选路」，① 的不漂移也就没有内容）。
        var forward = await _solver.SolveAsync(BuildSingleOp(new[] { rta, rtb }, demands, direction: "FORWARD"));
        Assert.True(forward.Success, forward.ErrorMessage);
        Assert.NotEqual(Signature(forward), Signature(ordered));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ⑨-② P0-01 E2E 反证（0号位 2026-10-09《APS_V1_2_20261009.md》§三 P0-01）：
    //    **锁定 Task 可与非锁定同物料 / 同 Stage / 同 Operation 共存，但绝不能被吸收**
    //
    // 复审判词：`FindMergeableTasks` 只按 Material/Stage/Operation/Route/Path/身份筛选，
    //   **不检查目标 Task 是否锁定** ⇒ 既成事实锚点被当成合法 Merge 候选（候选评分 / 路径择优 /
    //   最终域失败都可能被污染）。整改：Merge 候选阶段**直接排除** `constraints.LockedTasks` 中的目标。
    //
    // 夹具：D0 = 锁定锚点（固定 RTA/1、锁定量 0.5 = 全量）；D1 = 自由候选（同物料同工序，量 1）。
    //   旧实现：D1 合批进 D0 的锚点 Task ⇒ D0 末端被后延、D1 无自有 Task。
    //   整改后：D1 **不得**并入 D0 ⇒ D1 必须有**自己的** Task，且 D0 锚点逐字段不动。
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⑨-② **锁定锚点不可被 Merge 吸收**：D1 与锁定 D0 同物料 / 同 Stage / 同 Operation / 同 Route
    ///   （= 旧实现会合批的全部条件），但 D0 是既成事实锚点 ⇒ D1 必须另立 Task，D0 锚点逐字段不变。
    /// </summary>
    [Fact]
    public async Task 锁定Task_同物料同工序_可共存但不得被Merge吸收()
    {
        var paths = new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningStart.AddDays(30)) };
        var demands = new[]
        {
            // D0：锁定锚点，固定 RTA/1（身份可解 ⇒ 不触发技术失败）。
            new DemandSpec2("D0", 1, 0.5m, "RTA", 1, Due: PlanningStart.AddDays(5)),
            // D1：自由候选，量 1；与 D0 同物料同工序同 Route ⇒ 满足旧实现 FindMergeableTasks 的**全部**筛选。
            new DemandSpec2("D1", 2, 1m, null, null, Due: PlanningStart.AddDays(5))
        };
        var locked = new ExecutionConstraint
        {
            DraftId = "D0",
            ResourceId = 1,
            LockedStart = PlanningStart,
            LockedEnd = PlanningStart.AddMinutes(30),
            ConstraintType = "MANUAL",
            StageCode = "STAGE1",
            OperationCode = "OP10",
            LockedQuantity = 0.5m          // = D0 需求全量 ⇒ 全量锁定 ⇒ 不排剩余份额
        };

        var result = await _solver.SolveAsync(BuildSingleOp(
            paths, demands, allowMerge: true, direction: "FORWARD", locked: new[] { locked }));

        Assert.True(result.Success, result.ErrorMessage);

        // ① 锁定锚点逐字段不变（未被后延 / 未被改写）。
        var d0 = result.FinalTasks.Single(t => t.SourceDraftId == "D0");
        Assert.Equal(PlanningStart, d0.PlannedStartTime);
        Assert.Equal(PlanningStart.AddMinutes(30), d0.PlannedEndTime);
        Assert.Equal(0.5m, d0.Quantity);

        // ② D1 **必须有自己的 Task**（旧实现会被并进 D0 ⇒ 这里 `Single` 抛异常 ⇒ 本用例红）。
        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        Assert.NotEqual(d0.FinalDraftId, d1.FinalDraftId);
        Assert.Equal(1m, d1.Quantity);

        // ③ 锁定 Task 与非锁定 Task **共存**（两条 Task 都在，锚点未被吸收、D1 也未被吞）。
        Assert.Equal(2, result.FinalTasks.Count);

        // ④ D1 的份额（AllocationSequence = 2）不得被并进 D0 的锁定 Task（合并血缘不得指向锁定锚点）。
        Assert.DoesNotContain(result.AllocationShares,
            s => s.AllocationSequence == 2 && s.FinalDraftId == d0.FinalDraftId);
    }

    /// <summary>
    /// ⑩ P1-02 反证（复审 §二）：**DemandGoal 缺失的缺口必须真正进入生产诊断出口**。
    ///   复审判词：「`DEMAND_GOAL_ABSENT` 只加到 `Decision.Signals`，未写入 `SolveTraceNotes`」
    ///   ⇒ 开发报告「生产路径恒记缺口」的说法只对内部对象成立，**不等于**用户/日志可见证据。
    ///   整改：AUTO 自决时若缺 DemandGoal 载体 ⇒ 经**既有合法追溯通道** `DomainSolveResult.SolveTraceNotes`
    ///   写出一条 `ReasonCode = DEMAND_GOAL_ABSENT` 的记录（**未自造** ReasonCode 枚举）。
    ///
    /// 反向对照：**显式**方向（BACKWARD）不经过 B-005 自决 ⇒ **不得**产出该 trace
    ///   （锁死「不是无条件乱写 trace」）。
    /// </summary>
    [Fact]
    public async Task AUTO解析_DemandGoal缺口经SolveTraceNotes出口()
    {
        var paths = new[] { new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd) };
        var due = PlanningStart.AddHours(3);

        var auto = await _solver.SolveAsync(Build(paths, direction: "AUTO", due: due));
        Assert.True(auto.Success, auto.ErrorMessage);

        // 出口断言：trace 载体（`SolveTraceNotes`）里必须能看到缺口
        Assert.Contains(auto.SolveTraceNotes, n => n.ReasonCode == "DEMAND_GOAL_ABSENT");
        // 追溯键归属该需求（可定位，不是匿名声）
        Assert.Contains(auto.SolveTraceNotes,
            n => n.ReasonCode == "DEMAND_GOAL_ABSENT" && n.Key == "D1");

        // 反向对照：显式方向不做 B-005 自决 ⇒ 无此 trace
        var explicitRun = await _solver.SolveAsync(Build(paths, direction: "BACKWARD", due: due));
        Assert.True(explicitRun.Success, explicitRun.ErrorMessage);
        Assert.DoesNotContain(explicitRun.SolveTraceNotes, n => n.ReasonCode == "DEMAND_GOAL_ABSENT");
    }

    // ─────────── 0号位 2026-10-07 (5).md 反证单测辅助 + ③④⑤ ───────────

    private readonly record struct DemandSpec2(
        string Key, int Seq, decimal Qty, string? RouteCode, int? PathId, bool IsContinuation = false,
        DateTime? Due = null);

    /// <summary>
    /// 单工序 Path 夹具（Merge 类反证专用）：每条 Path 只含一道 OP10@STAGE1，独立资源 + 日历。
    /// Merge 仅在 <c>operations.Count == 1</c> 时才会被尝试（<c>FindMergeableTasks</c> 守卫）
    /// ⇒ 反证「跨 Path 禁 Merge」「Merge 真实完成」必须用**单工序**路径。
    /// </summary>
    private static DomainSolveRequest BuildSingleOp(
        IReadOnlyList<PathSpec> paths,
        IReadOnlyList<DemandSpec2> demands,
        bool allowMerge = true,
        string direction = "FORWARD",
        IReadOnlyList<ExecutionConstraint>? locked = null)
    {
        var ops = new List<RoutingOperation>();
        var elig = new List<OperationResourceEligibility>();
        var resources = new List<ResourceDefinition>();
        var calendars = new List<ResourceCalendarSlot>();
        var stageDepts = new List<MaterialStageDepartmentContextDto>();

        foreach (var p in paths)
        {
            ops.Add(new RoutingOperation
            {
                MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                RouteCode = p.RouteCode, PathId = p.PathId,
                OperationCode = "OP10", StageCode = "STAGE1",
                StandardDuration = p.LeadMinutes, OperationPlanningMode = "FINITE_RESOURCE"
            });
            elig.Add(new OperationResourceEligibility
            {
                MaterialId = MaterialId, ProductionDepartmentId = DeptId,
                RouteCode = p.RouteCode, PathId = p.PathId,
                OperationCode = "OP10", ResourceId = p.ResourceId, Priority = 1, CapacityFactor = 1m
            });
            resources.Add(new ResourceDefinition
            {
                ResourceId = p.ResourceId, ResourceCode = $"R{p.ResourceId}", FactoryCode = "F1", Capacity = 1m
            });
            if (p.CalStart is DateTime cs && p.CalEnd is DateTime ce)
            {
                calendars.Add(new ResourceCalendarSlot
                {
                    ResourceId = p.ResourceId, Start = cs, End = ce, IsAvailable = true
                });
            }
        }

        stageDepts.Add(new MaterialStageDepartmentContextDto
        {
            MaterialId = MaterialId, StageCode = "STAGE1", ProductionDepartmentId = DeptId
        });

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
                RequiredAvailableTime = d.Due ?? PlanningStart.AddDays(20), DemandSequence = d.Seq,
                RouteCode = d.RouteCode, PathId = d.PathId,
                IsContinuation = d.IsContinuation, NoSplitMerge = d.IsContinuation
            }).ToList(),
            RoutingOperations = ops,
            RoutingDependencies = new List<RoutingDependency>(),
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = stageDepts,
            ExecutionConstraints = locked is null ? Array.Empty<ExecutionConstraint>() : locked,
            Resources = resources,
            CalendarSlots = calendars,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = direction,
                    AllowMerge = allowMerge
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   `AllowMerge` 镜像夹具参数：策略是 Merge 的正式控制源（§八 P1-01）。
                BatchPolicies = TestBatchPolicy.Permissive(MaterialId, DeptId, allowMerge)
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // P0-02 / P0-03 反证夹具（0号位 2026-10-09《APS_V1_2_20261009.md》§三）
    //   共同几何：**同一 Route/Path 内两个 Stage 各有一道同名 `OP10`**（`OperationNodeKey.Of(Stage,Op)`
    //   区分节点，但 `RoutingDependency` 只按 OperationCode 连边 ⇒ **不连边**，两个节点各自独立）。
    //   `LockedTasks` 的键域必须含 StageCode，否则两条锚点在字典建立阶段就撞键。
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>一条锚点规格：StageCode / OperationCode / 资源 / 锁定窗 / 锁定量。
    ///   P0-03（0号位 2026-10-09《APS_V1_3_20261009.md》§三）：可显式给出**净产出/加工量**两类覆盖量
    ///   （null ⇒ 只用 `LockedQuantity`，即既有夹具行为，零回归）。</summary>
    private readonly record struct AnchorSpec(
        string StageCode, string OperationCode, int ResourceId,
        DateTime LockedStart, DateTime LockedEnd, decimal LockedQty,
        decimal? LockedNetOutputQty = null, decimal? LockedPlannedProcessQty = null);

    /// <summary>一条工艺路径上的工序定义：Stage / 工序码 / 资源 / 部门 / 标准工时。</summary>
    private readonly record struct OpSpec(
        string StageCode, string OperationCode, int ResourceId, int DepartmentId, decimal LeadMinutes = 60m);

    /// <summary>
    /// 依赖边。⚠ 契约 `RoutingDependency` **只有 From/ToOperationCode + 单值 ProductionDepartmentId**
    ///   （无 StageCode）⇒ 同码跨 Stage 的边端点消解不唯一，**连不出边**。本夹具只用不同工序码连边。
    /// </summary>
    private readonly record struct DepSpec(string FromOperationCode, string ToOperationCode, int DepartmentId);

    /// <summary>
    /// 造「锁定锚点几何」：显式给出工序集 + 依赖边 + 锁定锚点 + 需求。
    ///
    /// ⚠ 本夹具必须遵守的两条**实测确认**的图裁剪规则（否则锚点会静默解不出身份）：
    ///   ① `PhaseOneConstraintBuilder.BuildReachableStages` 只保留**从根工序可达**的 Stage
    ///      （`CollectReachableStagesForPath` 走依赖邻接表）⇒ **无依赖边时只有根工序所在 Stage 进图**，
    ///      另一个 Stage 的工序被整条剔除 ⇒ 该 Stage 的锁定锚点在 Phase2 反查节点时落空。
    ///   ② 同工序码跨 Stage 且同部门时，`ResolveNodeKey` 消解不唯一 ⇒ 边被丢弃 ⇒ 等价于①。
    ///   ⇒ 要让两个 Stage 的**同名**工序同时进图，只能让它们**同为根工序**（不连边）。
    /// </summary>
    private static DomainSolveRequest BuildLockedGeometry(
        IReadOnlyList<OpSpec> ops,
        IReadOnlyList<DepSpec> deps,
        IReadOnlyList<AnchorSpec> anchors,
        IReadOnlyList<DemandSpec2> demands,
        string? startStageCode = null,
        string? startOperationCode = null,
        string direction = "FORWARD")
    {
        var routingOps = new List<RoutingOperation>();
        var elig = new List<OperationResourceEligibility>();
        var resources = new List<ResourceDefinition>();
        var calendars = new List<ResourceCalendarSlot>();
        var stageDepts = new List<MaterialStageDepartmentContextDto>();

        foreach (var op in ops)
        {
            routingOps.Add(new RoutingOperation
            {
                MaterialId = MaterialId, ProductionDepartmentId = op.DepartmentId,
                RouteCode = "RTA", PathId = 1,
                OperationCode = op.OperationCode, StageCode = op.StageCode,
                StandardDuration = op.LeadMinutes, OperationPlanningMode = "FINITE_RESOURCE"
            });
            elig.Add(new OperationResourceEligibility
            {
                MaterialId = MaterialId, ProductionDepartmentId = op.DepartmentId,
                RouteCode = "RTA", PathId = 1,
                OperationCode = op.OperationCode, ResourceId = op.ResourceId, Priority = 1, CapacityFactor = 1m
            });
            if (resources.All(r => r.ResourceId != op.ResourceId))
            {
                resources.Add(new ResourceDefinition
                {
                    ResourceId = op.ResourceId, ResourceCode = $"R{op.ResourceId}", FactoryCode = "F1", Capacity = 1m
                });
                calendars.Add(new ResourceCalendarSlot
                {
                    ResourceId = op.ResourceId, Start = PlanningStart, End = PlanningEnd, IsAvailable = true
                });
            }
            if (stageDepts.All(s => !string.Equals(s.StageCode, op.StageCode, StringComparison.Ordinal)))
            {
                stageDepts.Add(new MaterialStageDepartmentContextDto
                {
                    MaterialId = MaterialId, StageCode = op.StageCode, ProductionDepartmentId = op.DepartmentId
                });
            }
        }

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
                NetOutputQty = d.Qty, PlannedProcessQty = d.Qty,
                RequiredAvailableTime = d.Due ?? PlanningStart.AddDays(20), DemandSequence = d.Seq,
                RouteCode = d.RouteCode, PathId = d.PathId,
                StartStageCode = startStageCode, StartOperationCode = startOperationCode,
                IsContinuation = d.IsContinuation, NoSplitMerge = d.IsContinuation
            }).ToList(),
            RoutingOperations = routingOps,
            RoutingDependencies = deps.Select(dp => new RoutingDependency
            {
                MaterialId = MaterialId, ProductionDepartmentId = dp.DepartmentId,
                RouteCode = "RTA", PathId = 1,
                FromOperationCode = dp.FromOperationCode, ToOperationCode = dp.ToOperationCode,
                DependencyType = "ES", LagTime = 0m, IsActive = true
            }).ToList(),
            OperationResourceEligibility = elig,
            MaterialStageDepartmentContexts = stageDepts,
            ExecutionConstraints = anchors.Select(a => new ExecutionConstraint
            {
                DraftId = demands[0].Key,
                ResourceId = a.ResourceId,
                LockedStart = a.LockedStart,
                LockedEnd = a.LockedEnd,
                ConstraintType = "MANUAL",
                StageCode = a.StageCode,
                OperationCode = a.OperationCode,
                LockedQuantity = a.LockedQty,
                LockedNetOutputQty = a.LockedNetOutputQty,
                LockedPlannedProcessQty = a.LockedPlannedProcessQty
            }).ToList(),
            Resources = resources,
            CalendarSlots = calendars,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = direction,
                    AllowMerge = false
                },
                BatchPolicies = TestBatchPolicy.Permissive(MaterialId, DeptId, allowMerge: false)
            }
        };
    }

    /// <summary>
    /// P0-02 反证（复审 §五.2）：**同 DraftId、不同 Stage、同名 OperationCode** 的两条锁定约束
    ///   ⇒ 必须正确建索引并产出结果，**不得**在字典建立阶段抛重复 Key 异常。
    ///
    /// 整改前：`ToDictionary(ec => (ec.DraftId, ec.OperationCode))` ⇒ 两条 "OP10" 撞键 ⇒
    ///   `ArgumentException`（且 SolveAsync 未做受控 Fail Closed）⇒ 本用例以异常失败。
    /// 整改后：键域含 `StageCode` ⇒ 两条锚点各自建索引、各自原地继承为 Task。
    /// </summary>
    [Fact]
    public async Task 锁定键域_同DraftId不同Stage同名Operation_不撞键且两条锚点均保留()
    {
        // 几何：同一 Route/Path 内**两个 Stage 各一道同名 OP10**，二者**同为根工序**（不连边）。
        //   ① 同为根 ⇒ 两个 Stage 都在 `BuildReachableStages` 的可达集合内 ⇒ 两个节点都进图
        //      （若只有一个 Stage 进图，第二条锚点会因「图中无该 (StageCode, OperationCode) 节点」
        //        走技术失败，那是**另一种**结果，不能证明键域正确）。
        //   ② 同名不同 Stage ⇒ `OperationNodeKey.Of(Stage, Op)` 是两个不同节点，可各自反查。
        //   ③ 两条锁定约束 `(DraftId=D1, OP10)` 在**旧二键字典**下撞键 ⇒ `ArgumentException`。
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 1m),
            new AnchorSpec("STAGE2", "OP10", 2, PlanningStart.AddMinutes(30), PlanningStart.AddMinutes(60), 1m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 1m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[]
        {
            new OpSpec("STAGE1", "OP10", 1, DeptId, 30m),
            new OpSpec("STAGE2", "OP10", 2, DeptId, 30m)
        };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands));

        // ① 不抛异常、受控返回。
        Assert.True(result.Success, result.ErrorMessage);

        // ② 两条锚点**各自**原地保留为 Task（未被静默抹除、未被合并）。
        Assert.Equal(2, result.FinalTasks.Count);
        var s1 = result.FinalTasks.Single(t => t.StageCode == "STAGE1");
        var s2 = result.FinalTasks.Single(t => t.StageCode == "STAGE2");
        Assert.Equal(PlanningStart, s1.PlannedStartTime);
        Assert.Equal(PlanningStart.AddMinutes(30), s1.PlannedEndTime);
        Assert.Equal(PlanningStart.AddMinutes(30), s2.PlannedStartTime);
        Assert.Equal(PlanningStart.AddMinutes(60), s2.PlannedEndTime);

        // ③ 全量锁定（各锚点均覆盖全量 ⇒ 覆盖量 = max = 1 = 需求量）⇒ 无剩余份额需排。
        Assert.DoesNotContain("D1", result.UnscheduledTasks.Select(u => u.DraftId));
    }

    /// <summary>
    /// P0-03 反证（复审 §五.3 前半）：**同一执行批在多个 Operation 上重叠的锁定量不得重复扣除**。
    ///
    /// 几何（复审原文举例）：需求 2 件；两道工序（`STAGE1/OP10 → STAGE2/OP20`，不同工序码才连得出边）
    ///   各锁 1 件 —— 这是**同一执行批沿工序流动的同一份**数量。
    ///   · 整改前（`Sum`）：覆盖量 = 1 + 1 = 2 = 需求量 ⇒ 「剩余 0」⇒ **静默漏排**（只有 2 条锚点 Task）。
    ///   · 整改后（`Max`）：覆盖量 = max(1, 1) = 1 ⇒ 剩余 1 件**必须被排下**（共 4 条 D1 Task：
    ///     2 条锚点 + 剩余 1 件沿两道工序各 1 条）。
    /// </summary>
    [Fact]
    public async Task 锁定覆盖量_同批多工序重叠_不得重复扣()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 1m),
            new AnchorSpec("STAGE2", "OP20", 2, PlanningStart.AddMinutes(30), PlanningStart.AddMinutes(60), 1m)
        };
        // 需求 2 件（锚点各报 1 件 ⇒ 重叠的**同一份**数量）。
        var demands = new[] { new DemandSpec2("D1", 1, 2m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[]
        {
            new OpSpec("STAGE1", "OP10", 1, DeptId, 30m),
            new OpSpec("STAGE2", "OP20", 2, DeptId, 30m)
        };
        var deps = new[] { new DepSpec("OP10", "OP20", DeptId) };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, deps, anchors, demands, startStageCode: "STAGE1", startOperationCode: "OP10"));

        Assert.True(result.Success, result.ErrorMessage);

        // 反证核心：剩余 1 件必须被排下 ⇒ D1 共有 4 条 Task（2 条锚点 + 剩余 1 件沿 2 道工序）。
        //   整改前 `Sum` ⇒ 覆盖量 2 = 需求量 ⇒ 「剩余 0」⇒ 只有 2 条 ⇒ 本断言**红**。
        var d1Tasks = result.FinalTasks.Where(t => t.SourceDraftId == "D1").ToList();
        Assert.Equal(4, d1Tasks.Count);

        // 数量闭合（Phase5 已硬校验）：逐工序 Σ = 需求 2 件。
        Assert.Equal(2m, d1Tasks.Where(t => t.StageCode == "STAGE1").Sum(t => t.Quantity));
        Assert.Equal(2m, d1Tasks.Where(t => t.StageCode == "STAGE2").Sum(t => t.Quantity));

        // 新排的那条 STAGE1 工序承载剩余 1 件，且**不得早于**锁定锚点（锚点 [P, P+30] 原地不动）。
        var scheduled = d1Tasks.Where(t => t.StageCode == "STAGE1" && t.PlannedStartTime != PlanningStart).ToList();
        Assert.Single(scheduled);
        Assert.Equal(1m, scheduled[0].Quantity);
        Assert.True(scheduled[0].PlannedStartTime >= PlanningStart.AddMinutes(30),
            $"新排工序不得与锁定锚点重叠，实际起点 {scheduled[0].PlannedStartTime:HH:mm}");

        // 锚点原地不动。
        var anchor = d1Tasks.Single(t => t.StageCode == "STAGE1" && t.PlannedStartTime == PlanningStart);
        Assert.Equal(PlanningStart.AddMinutes(30), anchor.PlannedEndTime);
        Assert.Equal(1m, anchor.Quantity);
    }

    /// <summary>
    /// P0-03 反证（复审 §五.3 后半）：**独立 Slice 必须各自正确计量**。
    ///   两个需求（各自独立 `LogicalDemandKey`/`AllocationSequence`）各带一条全量锁定锚点
    ///   ⇒ 各自判为「全量锁定 ⇒ 无剩余」，互不串量、互不误扣。
    /// </summary>
    [Fact]
    public async Task 锁定覆盖量_独立Slice各自计量_互不串量()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 1m)
        };
        var demands = new[]
        {
            new DemandSpec2("D1", 1, 1m, "RTA", 1, Due: PlanningStart.AddDays(5)),
            new DemandSpec2("D2", 2, 1m, "RTA", 1, Due: PlanningStart.AddDays(5))
        };
        var ops = new[] { new OpSpec("STAGE1", "OP10", 1, DeptId, 30m) };

        // AUD-1-004：需求须带 StartStageCode（真实由 2号位 FillStartStageCodes 回填）
        //   否则部门解析不出 ⇒ C 桶 BATCH_POLICY_MISSING Fail Closed，与「Slice 各自计量」命题无关地变红。
        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands, startStageCode: "STAGE1"));

        Assert.True(result.Success, result.ErrorMessage);

        // D1 全量锁定 ⇒ 其锚点原地保留；D2 无锁定 ⇒ 其 1 件必须被排下。
        Assert.Single(result.FinalTasks.Where(t => t.SourceDraftId == "D1"));
        Assert.Single(result.FinalTasks.Where(t => t.SourceDraftId == "D2"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // P0-03 三组反证（0号位 2026-10-09《APS_V1_3_20261009.md》§三 / §六.1）
    //   复审判词（成立）：旧实现把负值/超界校验放在 `if (lockedNetOutputQty >= demand.NetOutputQty) continue;`
    //     **之后** ⇒ `lockedNetOutputQty > demand.NetOutputQty` 时必先 `continue`，
    //     专门的 Fail Closed 分支**不可达**（净产出刚好等于需求量而加工量超额时同样先被跳过）。
    //   要求（§六.1）：「超量锁定**先**受控 Fail Closed，**再**判断全量覆盖」。
    //   三组**必须走完整 `SolveAsync`**（复审明示），不得只测内部辅助方法。
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// P0-03 反证 ①（复审 §三 第 1 组）：`NetOutputQty=10 / PlannedProcessQty=10 /
    ///   锁定净产出=11 / 锁定加工量=11` ⇒ **必须** `Success=false` 且错误来自
    ///   `SolverInputContractException` 通道（**不得**先被「全量锁定」`continue` 吃掉、更不得排出一套计划）。
    ///
    /// 整改前本用例**红**：`lockedNetOutputQty (11) >= 10` ⇒ 先 `continue` ⇒ 校验不可达
    ///   ⇒ 既不报错也不排剩余 ⇒ 静默产出「0 新 Task 的成功结果」。
    /// </summary>
    [Fact]
    public async Task P0_03_锁定净产出超界_受控FailClosed_不得先跳过()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 11m,
                LockedNetOutputQty: 11m, LockedPlannedProcessQty: 11m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 10m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[] { new OpSpec("STAGE1", "OP10", 1, DeptId, 30m) };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands));

        Assert.False(result.Success, "锁定覆盖量超出需求总量必须受控 Fail Closed，不得假成功");
        Assert.Contains("Fail Closed", result.ErrorMessage ?? string.Empty);
        Assert.Contains("锁定数量闭合失败", result.ErrorMessage ?? string.Empty);
        Assert.Contains("锁定覆盖量超出需求总量", result.ErrorMessage ?? string.Empty);
        Assert.Empty(result.FinalTasks);
    }

    /// <summary>
    /// P0-03 反证 ②（复审 §三 第 2 组）：`NetOutputQty=10 / PlannedProcessQty=10 /
    ///   锁定净产出=10 / 锁定加工量=11` ⇒ 净产出**刚好等于**需求量，但**加工量超额**
    ///   ⇒ 同样必须受控 Fail Closed（口径不自洽），**不得**因「净产出已全量覆盖」而跳过。
    ///
    /// 整改前本用例**红**：`lockedNetOutputQty (10) >= 10` ⇒ 先 `continue` ⇒ 超额加工量被静默吞掉。
    /// </summary>
    [Fact]
    public async Task P0_03_仅加工量超界_同样受控FailClosed()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 11m,
                LockedNetOutputQty: 10m, LockedPlannedProcessQty: 11m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 10m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[] { new OpSpec("STAGE1", "OP10", 1, DeptId, 30m) };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands));

        Assert.False(result.Success, "加工量覆盖超出需求总量必须受控 Fail Closed");
        Assert.Contains("锁定数量闭合失败", result.ErrorMessage ?? string.Empty);
        Assert.Contains("锁定覆盖量超出需求总量", result.ErrorMessage ?? string.Empty);
        Assert.Empty(result.FinalTasks);
    }

    /// <summary>
    /// P0-03 反向护栏（复审 §三 第 3 组）：`10 / 10 / 锁定 10 / 锁定 10` —— 覆盖量**严格等于**
    ///   已声明需求且各项均合法 ⇒ **正常保留**（锚点原地继承）且**不新排** Task、需求**不**进 Unscheduled。
    ///
    /// 作用：证明整改「把校验提前」**没有**把合法的全量锁定也判成失败（过度收紧 = 另一种回归）。
    /// </summary>
    [Fact]
    public async Task P0_03_覆盖量严格等于需求_正常保留且不新排()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 10m,
                LockedNetOutputQty: 10m, LockedPlannedProcessQty: 10m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 10m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[] { new OpSpec("STAGE1", "OP10", 1, DeptId, 30m) };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands));

        Assert.True(result.Success, result.ErrorMessage);

        // ① 锚点原地继承、**不新排** ⇒ D1 恰有 1 条 Task。
        var d1 = result.FinalTasks.Where(t => t.SourceDraftId == "D1").ToList();
        Assert.Single(d1);
        Assert.Equal(PlanningStart, d1[0].PlannedStartTime);
        Assert.Equal(PlanningStart.AddMinutes(30), d1[0].PlannedEndTime);

        // ② 全量覆盖 ⇒ 需求**不得**进 Unscheduled（也不得因本整改被误判失败）。
        Assert.DoesNotContain("D1", result.UnscheduledTasks.Select(u => u.DraftId));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // NEW-P1-02 反证（0号位 2026-10-09《APS_V1_4_20261009.md》§三 NEW-P1-02）：
    //   「锁定数量输入有效性只校验上界，未履行既有『非负 / 口径一致性』检查」——
    //   判词点名的漏检：`需求净产出=10、锁定净产出=-1` 会被算成「剩余 11」而在输入闸门放行。
    //   整改：① 非负 ② 上界 ③ 双数量一致性（只判可排程性、**不推导良率**）④ 仅双量全量才跳过。
    //   三组反证均走完整 `SolveAsync`。整改前 ① ② ③ 全部以「静默排剩余 / 静默跳过」通过 ⇒ 本三项必红。
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>① 负量：`需求净产出=10 / 锁定净产出=-1` ⇒ 必须在**输入闸门**受控 Fail Closed
    ///   （整改前算出「剩余 11」并继续排程 ⇒ 本用例红）。</summary>
    [Fact]
    public async Task NEW_P1_02_锁定量为负_受控FailClosed()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), -1m,
                LockedNetOutputQty: -1m, LockedPlannedProcessQty: -1m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 10m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[] { new OpSpec("STAGE1", "OP10", 1, DeptId, 30m) };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands));

        Assert.False(result.Success);
        Assert.Contains("Fail Closed", result.ErrorMessage);
        Assert.Contains("锁定量为负", result.ErrorMessage);
        Assert.Empty(result.FinalTasks);
    }

    /// <summary>② 双数量一致性 —— 净产出**已全量**、加工量仍有剩余（只覆盖一侧）⇒ 语义未经 2号位 定义
    ///   ⇒ 受控 Fail Closed（整改前直接跳过 ⇒ 本用例红）。</summary>
    [Fact]
    public async Task NEW_P1_02_净产出全量_加工量部分_受控FailClosed()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 10m,
                LockedNetOutputQty: 10m, LockedPlannedProcessQty: 4m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 10m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[] { new OpSpec("STAGE1", "OP10", 1, DeptId, 30m) };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands));

        Assert.False(result.Success);
        Assert.Contains("Fail Closed", result.ErrorMessage);
        Assert.Contains("口径不一致", result.ErrorMessage);
        Assert.Contains("净产出已全量覆盖", result.ErrorMessage);
        Assert.Empty(result.FinalTasks);
    }

    /// <summary>③ 双数量一致性 —— 净产出**仍有剩余**、加工量已无剩余 ⇒ 剩余净产出无加工承载
    ///   （计划不可表达）⇒ 受控 Fail Closed（整改前会以 `PlannedProcessQty=0` 排零时长 Task ⇒ 本用例红）。</summary>
    [Fact]
    public async Task NEW_P1_02_加工量全量_净产出部分_受控FailClosed()
    {
        var anchors = new[]
        {
            new AnchorSpec("STAGE1", "OP10", 1, PlanningStart, PlanningStart.AddMinutes(30), 10m,
                LockedNetOutputQty: 4m, LockedPlannedProcessQty: 10m)
        };
        var demands = new[] { new DemandSpec2("D1", 1, 10m, "RTA", 1, Due: PlanningStart.AddDays(5)) };
        var ops = new[] { new OpSpec("STAGE1", "OP10", 1, DeptId, 30m) };

        var result = await _solver.SolveAsync(
            BuildLockedGeometry(ops, Array.Empty<DepSpec>(), anchors, demands));

        Assert.False(result.Success);
        Assert.Contains("Fail Closed", result.ErrorMessage);
        Assert.Contains("口径不一致", result.ErrorMessage);
        Assert.Contains("加工量已无剩余", result.ErrorMessage);
        Assert.Empty(result.FinalTasks);
    }


    /// <summary>
    /// ③ P0-05 反证锁：`IsContinuation=true` 但 **RouteCode / PathId / ContinuationKey / StartOperationCode
    ///    全缺** ⇒ 必须 Fail Closed（Unscheduled），**禁止**退化成「无固定路径 ⇒ 自由候选选路」。
    ///    图中**确有两条可行路径**：若实现按旧口径（`RouteCode 非空 || PathId 非空` 才固定路径）就会排下
    ///    ⇒ 本用例红。这正是 0号位 判的「把 A/B 当 C 桶选路」。
    /// </summary>
    [Fact]
    public async Task 连续份额_缺固定路径身份_FailClosed不排()
    {
        var result = await _solver.SolveAsync(Build(
            new[]
            {
                new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd),
                new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
            },
            isContinuation: true));

        Assert.Empty(result.FinalTasks);
        Assert.Contains(result.UnscheduledTasks, u => u.DraftId == "D1");
    }

    /// <summary>
    /// ④ P0-06 反证锁：两条 Path **含同名工序码**（OP10 在 RTA、RTB 各一份）时，
    ///    不得把 Path A 的既有 Task 当成 Path B 需求的合并目标（否则 target 的 RouteCode/PathId/
    ///    ExecutionBatchDraftKey 被保留 ⇒ 同一 Execution Batch 混入两条 Path 的节点）。
    ///
    /// ⚠ 构造要点：两条 Path **必须共用同一资源**。若资源不同，对被合并 Task 的 ResourceId 取
    ///    CapacityFactor 会因资格键带 (RouteCode, PathId) 而查不到 ⇒ Merge 本就被拒，
    ///    这样即使删掉跨 Path 检查用例也依然是绿的（**假反证**）。共用资源才真正把
    ///    「唯一拦截点 = RouteCode/PathId 比较」暴露出来。
    ///
    ///     D0 钉 RTA 先排；D1 钉 RTB ⇒ 必须各自成 Task、批键不同。
    /// </summary>
    [Fact]
    public async Task 两条Path同名工序_禁止PathA合并进PathB的Task()
    {
        var result = await _solver.SolveAsync(BuildSingleOp(
            new[]
            {
                new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd),
                new PathSpec("RTB", 1, 1, PlanningStart, PlanningEnd)   // 同资源、同 PathId，仅 RouteCode 不同
            },
            new[]
            {
                new DemandSpec2("D0", 1, 1m, "RTA", 1),
                new DemandSpec2("D1", 2, 1m, "RTB", 1)
            }));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.FinalTasks.Count);

        var d0 = result.FinalTasks.Single(t => t.SourceDraftId == "D0");
        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        Assert.Equal("RTA", d0.RouteCode);
        Assert.Equal("RTB", d1.RouteCode);
        Assert.NotEqual(d0.ExecutionBatchDraftKey, d1.ExecutionBatchDraftKey);
    }

    /// <summary>
    /// ⑤ P0-04 反证锁：Merge 候选必须用**合并后的真实 PlannedEndTime**参与择优。
    ///    D0 钉 RTA（qty1）先排；D1 自由（2 候选、qty1、AllowMerge=true）：
    ///      · 走 RTA：Merge 进 D0 的 Task ⇒ 合并后 end = t0 + 2×60min；
    ///      · 走 RTB：新建 Task ⇒ end = t0 + 1×60min。
    ///    FORWARD 取更早完成 ⇒ **必须选 RTB**。旧实现对 Merge 候选给 `DateTime.MinValue`
    ///    ⇒ 被误评为「0 延期、完成最早」而永远胜出 ⇒ 本用例在旧实现下红。
    /// </summary>
    [Fact]
    public async Task 合并候选真实完成更晚_必须选另一条Path()
    {
        var result = await _solver.SolveAsync(BuildSingleOp(
            new[]
            {
                new PathSpec("RTA", 1, 1, PlanningStart, PlanningEnd),
                new PathSpec("RTB", 1, 2, PlanningStart, PlanningEnd)
            },
            new[]
            {
                new DemandSpec2("D0", 1, 1m, "RTA", 1),
                new DemandSpec2("D1", 2, 1m, null, null)
            }));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.FinalTasks.Count);

        var d1 = result.FinalTasks.Single(t => t.SourceDraftId == "D1");
        Assert.Equal("RTB", d1.RouteCode);
        Assert.Equal(1m, d1.Quantity);   // 未被合并（若被 Merge 进 D0，则 D1 无自有 Task 且数量为 2）
    }
}
