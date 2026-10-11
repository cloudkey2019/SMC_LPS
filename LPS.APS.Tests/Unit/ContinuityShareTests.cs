using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using LPS.APS.Scheduling.Solvers;
using LPS.APS.Core.Dto;
using RoutingOperation = LPS.APS.Core.Entities.APS.RoutingOperation;
using OperationResourceEligibility = LPS.APS.Core.Entities.APS.OperationResourceEligibility;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 跨版本排程连续性 P0-07 / P0-08 回归测试（纯内存，直接调 FiniteCapacitySolver）。
/// 对齐 0号位第15轮审核报告验收用例：
///   C01 两个不同 MES 工单连续份额不得被普通 Merge 合并
///   C02 同 PI 同 Operation：连续份额先于自由份额、不跨设备时间重叠
///   C03 全局 AllowSplit/AllowMerge 开启时，连续份额仍保持独立身份（不拆不合）
/// </summary>
public class ContinuityShareTests
{
    private static readonly DateTime PlanningStart = new DateTime(2026, 9, 1, 0, 0, 0);
    private static readonly DateTime PlanningEnd = new DateTime(2026, 9, 30, 0, 0, 0);

    private readonly FiniteCapacitySolver _solver = new();

    [Fact]
    public async Task C01_TwoContinuationWorkOrders_DoNotMerge()
    {
        // WO101 Qty20、WO102 Qty30 是两个不同 MES 工单连续份额，同物料同工序同 PI。
        // 即使 AllowMerge 开启，也不得合并成一个 Qty50 连续对象，两个 Key 必须各自返回。
        var request = Build(
            new[]
            {
                D("WO101", 1, 1, 20m, seq: 1, isContinuation: true, pi: "PI-1"),
                D("WO102", 1, 1, 30m, seq: 2, isContinuation: true, pi: "PI-1"),
            },
            allowMerge: true,
            allowSplit: false);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.FinalTasks.Count);

        var wo101 = result.FinalTasks.Single(t => t.SourceDraftId == "WO101");
        var wo102 = result.FinalTasks.Single(t => t.SourceDraftId == "WO102");
        Assert.Equal(20m, wo101.Quantity);
        Assert.Equal(30m, wo102.Quantity);
        Assert.NotEqual(wo101.FinalDraftId, wo102.FinalDraftId);
    }

    [Fact]
    public async Task C02_Continuation30_Before_Free70_SamePI()
    {
        // 同 PI 同 Operation：连续 30 必须先于自由 70，自由不得排到连续之前，也不得与其时间重叠。
        var request = Build(
            new[]
            {
                D("CONT", 1, 1, 30m, seq: 1, isContinuation: true, pi: "PI-1"),
                D("FREE", 1, 1, 70m, seq: 2, isContinuation: false, pi: "PI-1"),
            },
            allowMerge: true,
            allowSplit: false);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var cont = result.FinalTasks.Single(t => t.SourceDraftId == "CONT");
        var free = result.FinalTasks.Single(t => t.SourceDraftId == "FREE");

        Assert.True(free.PlannedStartTime >= cont.PlannedEndTime,
            $"自由份额 {free.PlannedStartTime:O} 应不早于连续份额完成 {cont.PlannedEndTime:O}");
    }

    [Fact]
    public async Task C03_Continuation_StaysSingle_UnderGlobalSplitMerge()
    {
        // 全局 AllowSplit + AllowMerge 开启：连续份额仍保持单一独立 Task，数量不被拆合。
        var request = Build(
            new[]
            {
                D("CONT", 1, 1, 10m, seq: 1, isContinuation: true, pi: "PI-1"),
            },
            allowMerge: true,
            allowSplit: true);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var contTasks = result.FinalTasks.Where(t => t.SourceDraftId == "CONT").ToList();
        Assert.Single(contTasks);
        Assert.Equal(10m, contTasks[0].Quantity);
    }

    [Fact]
    public async Task C05_Continuation_NullPI_StaysSingle_UnderGlobalSplitMerge()
    {
        // 连续份额但 ProductionInstructionNo 为 null（外部 MES 连续、无 PI）：
        // 在全局 AllowSplit + AllowMerge 开启下，仍保持单一独立 Task，不拆不合。
        var request = Build(
            new[]
            {
                D("CONT", 1, 1, 10m, seq: 1, isContinuation: true, pi: null),
            },
            allowMerge: true,
            allowSplit: true);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var contTasks = result.FinalTasks.Where(t => t.SourceDraftId == "CONT").ToList();
        Assert.Single(contTasks);
        Assert.Equal(10m, contTasks[0].Quantity);
    }

    [Fact]
    public async Task C06_InputLogicalDemandKey_PassesThroughAsSourceDraftId()
    {
        // 输入的 LogicalProductionDemand.LogicalDemandKey 必须原样透传为 FinalTaskDraft.SourceDraftId，
        // 不得被重写/加前缀/替换，保证追溯链不丢身份。
        var request = Build(
            new[]
            {
                D("KEY-A", 1, 1, 5m, seq: 1, isContinuation: false),
                D("KEY-B", 2, 2, 8m, seq: 2, isContinuation: false),
            },
            allowMerge: false,
            allowSplit: false);

        var result = await _solver.SolveAsync(request);

        Assert.True(result.Success, result.ErrorMessage);
        var inputKeys = request.LogicalProductionDemands.Select(d => d.LogicalDemandKey).ToHashSet();
        Assert.Equal(2, result.FinalTasks.Count);
        foreach (var task in result.FinalTasks)
        {
            Assert.True(inputKeys.Contains(task.SourceDraftId),
                $"FinalTask.SourceDraftId={task.SourceDraftId} 应等于某个输入 LogicalDemandKey");
        }
    }

    // ─────────────────────────── 构造辅助 ───────────────────────────

    private sealed class DemandSpec
    {
        public string Key = string.Empty;
        public int MaterialId;
        public long AllocationSequence;
        public decimal Qty;
        public DateTime RequiredAvailableTime;
        public int DemandSequence;
        public bool IsContinuation;
        public string? PI;

        // ── v1.6 A/B（Continuation Slice）输入契约必备项 ──
        //   规则清单 v1.5 / 实施包 v1.6：`IsContinuation=true` 必须带
        //   ContinuationKey + 真实固定 RouteCode/PathId + StartOperation，并按 NoSplitMerge 处理。
        //   0号位 2026-10-07 (5).md **P0-05** 进一步要求：任一缺失 ⇒ Fail Closed（禁止进入自由候选选路）。
        public string? ContinuationKey;
        public string? RouteCode;
        public int? PathId;
        public string? StartOperationCode;
        public bool NoSplitMerge;
    }

    private static DemandSpec D(
        string key, int materialId, long allocSeq, decimal qty, int seq,
        bool isContinuation = false, string? pi = null)
        => new DemandSpec
        {
            Key = key,
            MaterialId = materialId,
            AllocationSequence = allocSeq,
            Qty = qty,
            RequiredAvailableTime = PlanningStart.AddDays(20),
            DemandSequence = seq,
            IsContinuation = isContinuation,
            PI = pi,
            // 连续份额按 v1.6 契约补齐固定路径身份（RouteCode/PathId 与下方 Build() 的 "DEFAULT"/0 一致）。
            // ContinuationKey：契约口径「一个 MESWorkOrderNo 一个且仅一个」⇒ 有 PI 用 PI，否则回落 Key。
            ContinuationKey = isContinuation ? (pi ?? key) : null,
            RouteCode = isContinuation ? "DEFAULT" : null,
            PathId = isContinuation ? 1 : null,
            StartOperationCode = isContinuation ? "OP10" : null,
            NoSplitMerge = isContinuation
        };

    /// <summary>
    /// 构造最小可运行的 DomainSolveRequest：每个物料一个单工序 OP10（60 分钟，无 Setup），
    /// 独立资源（ResourceId = MaterialId），部门锁定 (MaterialId, STAGE1) → deptId 100，
    /// 日历窗口 = [planningStart, planningEnd + 30 天]。
    /// </summary>
    private static DomainSolveRequest Build(
        IReadOnlyList<DemandSpec> demands,
        bool allowMerge,
        bool allowSplit)
    {
        const int deptId = 100;
        const string stage = "STAGE1";
        const string opCode = "OP10";

        var logicalDemands = demands.Select(d => new LogicalProductionDemand
        {
            LogicalDemandKey = d.Key,
            PlanVersionId = 1L,
            DomainKey = "DOMAIN",
            AllocationSequence = d.AllocationSequence,
            DemandKey = d.Key,
            MaterialId = d.MaterialId,
            FactoryId = 1,
            StartStageCode = stage,
            NetOutputQty = d.Qty,
            PlannedProcessQty = d.Qty,
            RequiredAvailableTime = d.RequiredAvailableTime,
            DemandSequence = d.DemandSequence,
            ProductionInstructionNo = d.PI,
            IsContinuation = d.IsContinuation,
            // v1.6 A/B 输入契约：连续份额必须带固定路径身份 + ContinuationKey + StartOperation + NoSplitMerge。
            ContinuationKey = d.ContinuationKey,
            RouteCode = d.RouteCode,
            PathId = d.PathId,
            StartOperationCode = d.StartOperationCode,
            NoSplitMerge = d.NoSplitMerge
        }).ToList();

        // 结构实体（路由/资格/部门/资源/日历）按「去重后的物料」各生成一份，
        // 多个需求共享同一物料时不得重复生成，否则资格字典以 ResourceId 为键会撞键。
        var materialIds = demands.Select(d => d.MaterialId).Distinct().ToList();

        var routingOps = materialIds.Select(mid => new RoutingOperation
        {
            MaterialId = mid,
            ProductionDepartmentId = deptId,
            RouteCode = "DEFAULT",
            PathId = 1,   // 与 D() 中连续份额声明的 PathId 一致（固定路径图键 = ("DEFAULT", 1)）
            OperationCode = opCode,
            StageCode = stage,
            StandardDuration = 60m,
            SetupTime = 0m
        }).ToList();

        var eligibilities = materialIds.Select(mid => new OperationResourceEligibility
        {
            MaterialId = mid,
            ProductionDepartmentId = deptId,
            RouteCode = "DEFAULT",
            OperationCode = opCode,
            ResourceId = mid,
            Priority = 1,
            CapacityFactor = 1m
        }).ToList();

        var deptContexts = materialIds.Select(mid => new MaterialStageDepartmentContextDto
        {
            MaterialId = mid,
            StageCode = stage,
            ProductionDepartmentId = deptId
        }).ToList();

        var resources = materialIds.Select(mid => new ResourceDefinition
        {
            ResourceId = mid,
            ResourceCode = $"R{mid}",
            FactoryCode = "F1",
            Capacity = 1m
        }).ToList();

        var calendarEnd = PlanningEnd.AddDays(30);
        var calendarSlots = materialIds.Select(mid => new ResourceCalendarSlot
        {
            ResourceId = mid,
            Start = PlanningStart,
            End = calendarEnd,
            IsAvailable = true
        }).ToList();

        return new DomainSolveRequest
        {
            PlanVersionId = 1,
            DomainKey = "DOMAIN",
            PlanningStart = PlanningStart,
            PlanningEnd = PlanningEnd,
            LogicalProductionDemands = logicalDemands,
            RoutingOperations = routingOps,
            OperationResourceEligibility = eligibilities,
            MaterialStageDepartmentContexts = deptContexts,
            Resources = resources,
            CalendarSlots = calendarSlots,
            StrategySnapshot = new SolverStrategySnapshot
            {
                Parameters = new FiniteCapacityParameters
                {
                    SchedulingDirection = "FORWARD",
                    AllowMerge = allowMerge,
                    AllowSplit = allowSplit
                },
                // P0-01（0号位 2026-10-08 §四）：C 桶必须显式给出有效 Batch Policy，否则 Fail Closed。
                //   本夹具验证的是连续份额（A/B 恒 1 批）与自由需求共存 ⇒ Material 级宽松策略。
                BatchPolicies = TestBatchPolicy.Permissive(logicalDemands.Select(d => d.MaterialId), deptId)
            }
        };
    }
}
