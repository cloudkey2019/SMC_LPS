using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using LPS.APS.Core.Dto;
using LPS.APS.Scheduling.Solvers;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// **B-005 Direction 上下文自决**（0号位 2026-10-08《未命名的Markdown文件 (2)(1).md》§三 / §十五 第 3 项）。
///
/// 覆盖 0号位 点名的四类 AUTO 场景：
///   ① 明显倒排场景 ② 明显正排场景 ③ 连续份额场景 ④ Slack / 物料 / 资源导致 Direction 变化场景。
/// 另覆盖：Firm/Frozen/Lock 硬锚点、DemandGoal 判据（含**载体缺失不静默**）、无上下文默认。
///
/// 直接驱动 <see cref="SchedulingDirectionResolver.Resolve"/>（纯函数、确定性），
/// 与 E2E 几何断言（<c>PhaseTwoRoutingCandidateTests</c>）互补：
///   本文件证明「**为什么**是这个方向」（判据层），E2E 证明「方向**确实生效**到排程结果」（行为层）。
/// </summary>
public class SchedulingDirectionResolverTests
{
    private static readonly DateTime Start = new(2026, 10, 8, 8, 0, 0);
    private static readonly DateTime NoMaterialFloor = DateTime.MinValue;

    /// <summary>单工序、60 分钟工时的工序节点（lead = 60min）。</summary>
    private static List<OperationNode> Ops60() => new()
    {
        new OperationNode { OperationCode = "OP10", StageCode = "STAGE1", StandardDuration = 60m }
    };

    private static LogicalProductionDemand Demand(
        DateTime? due = null,
        bool isContinuation = false,
        string? preferredResourceCode = null,
        int? preferredResourceId = null,
        int? fallbackResourceId = null,
        string key = "D1")
        => new()
        {
            LogicalDemandKey = key,
            DemandKey = key,
            AllocationSequence = 1,
            MaterialId = 1,
            FactoryId = 1,
            NetOutputQty = 10m,
            PlannedProcessQty = 10m,
            RequiredAvailableTime = due ?? Start.AddDays(30),
            DemandSequence = 1,
            IsContinuation = isContinuation,
            NoSplitMerge = isContinuation,
            PreferredResourceCode = preferredResourceCode,
            PreferredResourceId = preferredResourceId,
            FallbackResourceId = fallbackResourceId
        };

    private static ConstraintContext Ctx() => new();

    private static ConstraintContext CtxWithLock(string draftId)
    {
        var ctx = new ConstraintContext();
        ctx.LockedTasks[(draftId, "STAGE1", "OP10")] = new LockedTaskConstraint
        {
            DraftId = draftId,
            ResourceId = 1,
            LockedStart = Start,
            LockedEnd = Start.AddHours(1),
            ConstraintType = "ANCHOR"
        };
        return ctx;
    }

    // ════════════════════════════════════════════════════════════
    // ① 明显倒排场景：交期极紧（Slack < 0）⇒ BACKWARD
    // ════════════════════════════════════════════════════════════
    [Fact]
    public void 明显倒排场景_交期紧_Slack为负_判BACKWARD()
    {
        // lead = 60min，Due 在起点后仅 30min ⇒ 最早完成 09:00 已晚于 Due 08:30 ⇒ slack = -30min
        var d = Demand(due: Start.AddMinutes(30));

        var decision = SchedulingDirectionResolver.Resolve(d, Ops60(), Ctx(), Start, NoMaterialFloor);

        Assert.Equal(SchedulingDirectionResolver.Backward, decision.Direction);
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalDueTight));
    }

    // ════════════════════════════════════════════════════════════
    // ② 明显正排场景：交期极松（Slack > lead）⇒ FORWARD
    // ════════════════════════════════════════════════════════════
    [Fact]
    public void 明显正排场景_交期松_Slack大于一个工艺周期_判FORWARD()
    {
        var d = Demand(due: Start.AddDays(30));

        var decision = SchedulingDirectionResolver.Resolve(d, Ops60(), Ctx(), Start, NoMaterialFloor);

        Assert.Equal(SchedulingDirectionResolver.Forward, decision.Direction);
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalDueLoose));
    }

    // ════════════════════════════════════════════════════════════
    // ③ 连续份额场景：A/B 既存执行批 ⇒ FORWARD（最早承诺）
    // ════════════════════════════════════════════════════════════
    [Fact]
    public void 连续份额场景_IsContinuation_判FORWARD()
    {
        var d = Demand(isContinuation: true);

        var decision = SchedulingDirectionResolver.Resolve(d, Ops60(), Ctx(), Start, NoMaterialFloor);

        Assert.Equal(SchedulingDirectionResolver.Forward, decision.Direction);
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalContinuationSlice));
    }

    // ════════════════════════════════════════════════════════════
    // ④ Slack / 物料 / 资源 导致 Direction 变化场景
    //    同一需求，只改**一个**上下文 ⇒ 方向必须随之变化
    // ════════════════════════════════════════════════════════════

    /// <summary>④-a：同一需求，只改 Slack（Due）⇒ 方向从 FORWARD 翻到 BACKWARD。</summary>
    [Fact]
    public void Direction变化_仅改Slack_方向随之翻转()
    {
        var ops = Ops60();
        var ctx = Ctx();

        var loose = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddDays(30)), ops, ctx, Start, NoMaterialFloor);
        var tight = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddMinutes(30)), ops, ctx, Start, NoMaterialFloor);

        Assert.Equal(SchedulingDirectionResolver.Forward, loose.Direction);
        Assert.Equal(SchedulingDirectionResolver.Backward, tight.Direction);
    }

    /// <summary>④-b：同一需求，只改 Material（子件下界晚于计划起点）⇒ 物料刚可用 ⇒ FORWARD 信号。</summary>
    [Fact]
    public void Direction变化_仅改物料下界_记MATERIAL_LATE()
    {
        var d = Demand(due: Start.AddMinutes(30));   // 交期紧（未过但装不下）

        var decision = SchedulingDirectionResolver.Resolve(
            d, Ops60(), Ctx(), Start, Start.AddDays(5));

        // 交期紧 + 物料晚到 ⇒ 信号冲突 ⇒ MIXED（先倒排、失败转正排）
        Assert.Equal(SchedulingDirectionResolver.Mixed, decision.Direction);
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalMaterialLate));
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalDueTight));
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalConflicting));
    }

    /// <summary>④-b'：交期松 + 物料晚到 ⇒ 同为正向信号 ⇒ FORWARD（不冲突）。</summary>
    [Fact]
    public void Direction变化_物料晚到且交期松_判FORWARD()
    {
        var decision = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddDays(30)), Ops60(), Ctx(), Start, Start.AddDays(5));

        Assert.Equal(SchedulingDirectionResolver.Forward, decision.Direction);
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalMaterialLate));
    }

    /// <summary>④-c：同一需求，只改 Resource（资源连续性软偏好）⇒ 正向信号。</summary>
    [Fact]
    public void Direction变化_仅改资源偏好_记为正向信号()
    {
        var withPref = SchedulingDirectionResolver.Resolve(
            Demand(preferredResourceCode: "R1"), Ops60(), Ctx(), Start, NoMaterialFloor);

        Assert.Equal(SchedulingDirectionResolver.Forward, withPref.Direction);
        Assert.True(withPref.Has(SchedulingDirectionResolver.SignalPreferredResource));

        // 旧 ID 形态（PreferredResourceId / FallbackResourceId）同样被认（P1-11 并存）
        Assert.True(SchedulingDirectionResolver.Resolve(
            Demand(preferredResourceId: 7), Ops60(), Ctx(), Start, NoMaterialFloor)
            .Has(SchedulingDirectionResolver.SignalPreferredResource));
        Assert.True(SchedulingDirectionResolver.Resolve(
            Demand(fallbackResourceId: 9), Ops60(), Ctx(), Start, NoMaterialFloor)
            .Has(SchedulingDirectionResolver.SignalPreferredResource));
    }

    // ════════════════════════════════════════════════════════════
    // Firm/Frozen/Lock 硬锚点：最高优先，压过一切软信号
    // ════════════════════════════════════════════════════════════
    [Fact]
    public void 锁定任务存在_硬锚点最高优先_即使交期紧也判FORWARD()
    {
        var decision = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddMinutes(-30)),   // 交期紧本应 BACKWARD
            Ops60(), CtxWithLock("D1"), Start, NoMaterialFloor);

        Assert.Equal(SchedulingDirectionResolver.Forward, decision.Direction);
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalLockedAnchor));
        Assert.False(decision.Has(SchedulingDirectionResolver.SignalDueTight));
    }

    // ════════════════════════════════════════════════════════════
    // DemandGoal 判据（P-006 两值）——含**载体缺失不静默**
    // ════════════════════════════════════════════════════════════
    [Fact]
    public void DemandGoal_载体缺失_登记缺口信号且不猜()
    {
        // 生产路径当前恒传 null（LogicalProductionDemand 无该字段）
        var decision = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddDays(30)), Ops60(), Ctx(), Start, NoMaterialFloor, demandGoal: null);

        Assert.True(decision.Has(SchedulingDirectionResolver.SignalDemandGoalAbsent));
    }

    [Fact]
    public void DemandGoal_客户承诺_为反向信号()
    {
        var decision = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddDays(30)),   // 交期松本应 FORWARD
            Ops60(), Ctx(), Start, NoMaterialFloor,
            demandGoal: SchedulingDirectionResolver.GoalCustomerCommitment);

        Assert.True(decision.Has(SchedulingDirectionResolver.SignalGoalCustomerCommitment));
        Assert.Equal(SchedulingDirectionResolver.Mixed, decision.Direction);   // 松 + 客户承诺 ⇒ 冲突
    }

    [Fact]
    public void DemandGoal_库存补充_为正向信号()
    {
        var decision = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddMinutes(30)),   // 交期紧本应 BACKWARD
            Ops60(), Ctx(), Start, NoMaterialFloor,
            demandGoal: SchedulingDirectionResolver.GoalInventoryReplenishment);

        Assert.True(decision.Has(SchedulingDirectionResolver.SignalGoalInventoryReplenishment));
        Assert.Equal(SchedulingDirectionResolver.Mixed, decision.Direction);   // 紧 + 库存补充 ⇒ 冲突
    }

    [Fact]
    public void DemandGoal_未知取值_同样登记缺口不猜()
    {
        var decision = SchedulingDirectionResolver.Resolve(
            Demand(due: Start.AddDays(30)), Ops60(), Ctx(), Start, NoMaterialFloor,
            demandGoal: "SOMETHING_ELSE");

        Assert.True(decision.Has(SchedulingDirectionResolver.SignalDemandGoalAbsent));
    }

    // ════════════════════════════════════════════════════════════
    // 无上下文可依 / 无有效交期：确定性默认，且**不是**「AUTO 恒等 MIXED」
    // ════════════════════════════════════════════════════════════
    [Fact]
    public void 无任何上下文信号_确定性默认BACKWARD且标记NO_CONTEXT_SIGNAL()
    {
        // Due 为 default ⇒ 交期缺失（不产生交期信号）；无锁定、无连续、无偏好、无物料下界
        var decision = SchedulingDirectionResolver.Resolve(
            Demand(due: DateTime.MinValue), Ops60(), Ctx(), Start, NoMaterialFloor);

        // 注意：「无交期」只认 default；「交期已过」是有效交期（DUE_OVERDUE ⇒ 正排），故不应产生 DUE_TIGHT
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalDueAbsent));
        Assert.False(decision.Has(SchedulingDirectionResolver.SignalDueTight));
        Assert.Equal(SchedulingDirectionResolver.Backward, decision.Direction);
        Assert.True(decision.Has(SchedulingDirectionResolver.SignalNoContext));
    }

    /// <summary>裁决必须是**纯函数**：同输入同输出（确定性、可重放）。</summary>
    [Fact]
    public void 同输入重复调用_结果完全一致()
    {
        var ops = Ops60();

        var a = SchedulingDirectionResolver.Resolve(Demand(), ops, Ctx(), Start, NoMaterialFloor);
        var b = SchedulingDirectionResolver.Resolve(Demand(), ops, Ctx(), Start, NoMaterialFloor);

        Assert.Equal(a.Direction, b.Direction);
        Assert.Equal(a.Signals, b.Signals);
        Assert.Equal(a.Reason, b.Reason);
    }

    /// <summary>Slack 是派生量：`Due − (max(起点,物料下界) + 总工时)`；无有效交期时不产生交期信号。</summary>
    [Fact]
    public void Slack派生口径_按最早可能完成计算()
    {
        var d = Demand(due: Start.AddHours(3));

        // 起点 08:00 + lead 60min = 09:00 ⇒ slack = 3h - 1h = 120min
        var slack = SchedulingDirectionResolver.ComputeSlackMinutes(
            d, Ops60(), Start, Start, out var absent);
        Assert.False(absent);
        Assert.Equal(120m, slack);

        // 物料下界推到 10:00 ⇒ 最早完成 11:00 ⇒ slack = 3h - 3h = 0
        var slackLate = SchedulingDirectionResolver.ComputeSlackMinutes(
            d, Ops60(), Start.AddHours(2), Start, out _);
        Assert.Equal(0m, slackLate);

        // 无有效交期（default）⇒ 明确标记 absent（不得被当成「交期极紧」）
        SchedulingDirectionResolver.ComputeSlackMinutes(
            Demand(due: DateTime.MinValue), Ops60(), Start, Start, out var absent2);
        Assert.True(absent2);
    }
}
