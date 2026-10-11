using LPS.APS.Application.Services;
using LPS.APS.Core.Exceptions;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 原始 MES 快照源 Run 解析（Base 链）纯逻辑测试 —— 0号位《PM回复1009-1》§三 A-Q1/A-Q2/A-Q3。
/// 契约：
/// <list type="bullet">
/// <item>无 Base（夜间 FULL / 历史）⇒ 读本 PV 所属 Run，行为与旧单跳读取一致。</item>
/// <item>有 Base（候选 / FAILED 恢复）⇒ 沿 <c>ScheduleRun.BasePlanVersionId</c> 逐跳上溯，命中「有三类完整快照」的源 Run。</item>
/// <item>断链 / 成环 / Base PlanVersion 缺失 ⇒ <see cref="SnapshotRunResolutionException"/>（fail-closed，不静默降级）。</item>
/// </list>
/// </summary>
public class SnapshotRunResolverTests
{
    private static Dictionary<int, SnapshotRunResolver.PlanVersionNode> Pvs(params (int Id, int? Run)[] items)
        => items.ToDictionary(i => i.Id, i => new SnapshotRunResolver.PlanVersionNode(i.Id, i.Run));

    private static Dictionary<int, SnapshotRunResolver.ScheduleRunNode> Runs(params (int Id, int? BasePv)[] items)
        => items.ToDictionary(i => i.Id, i => new SnapshotRunResolver.ScheduleRunNode(i.Id, i.BasePv));

    private static Func<int, Task<bool>> Complete(params int[] completeRuns)
        => runId => Task.FromResult(completeRuns.Contains(runId));

    [Fact(DisplayName = "夜间无Base ⇒ 读本Run（行为与旧单跳一致）")]
    public async Task Nightly_NoBase_ReadsOwnRun()
    {
        var pvs = Pvs((10, 1));
        var runs = Runs((1, null));

        var resolved = await SnapshotRunResolver.ResolveCoreAsync(10, pvs, runs, Complete());

        Assert.Equal(1, resolved);
    }

    [Fact(DisplayName = "候选一跳 ⇒ 继承Base的完整快照Run，不读候选自身Run")]
    public async Task Candidate_OneHop_InheritsBaseSnapshotRun()
    {
        var pvs = Pvs((20, 5), (10, 1));      // 候选 PV20→Run5；Base PV10→Run1
        var runs = Runs((5, 10), (1, null));  // Run5.Base=PV10；Run1 无 Base
        var complete = Complete(1);           // 仅 Run1 有完整三类快照

        var resolved = await SnapshotRunResolver.ResolveCoreAsync(20, pvs, runs, complete);

        Assert.Equal(1, resolved);            // 不是候选自身的 Run5
    }

    [Fact(DisplayName = "候选两跳（基于已激活候选）⇒ 沿Base链继续上溯到夜间源Run")]
    public async Task Candidate_TwoHops_WalksChainToNightlyRun()
    {
        var pvs = Pvs((30, 7), (20, 5), (10, 1));
        var runs = Runs((7, 20), (5, 10), (1, null));
        var complete = Complete(1);

        var resolved = await SnapshotRunResolver.ResolveCoreAsync(30, pvs, runs, complete);

        Assert.Equal(1, resolved);
    }

    [Fact(DisplayName = "命中链中第一个有完整快照的Run即停（不越级到更上层）")]
    public async Task Candidate_StopsAtFirstCompleteRun()
    {
        var pvs = Pvs((30, 7), (20, 5), (10, 1));
        var runs = Runs((7, 20), (5, 10), (1, null));
        var complete = Complete(5, 1);        // Run5 也具备完整快照

        var resolved = await SnapshotRunResolver.ResolveCoreAsync(30, pvs, runs, complete);

        Assert.Equal(5, resolved);
    }

    [Fact(DisplayName = "Base链成环 ⇒ fail-closed")]
    public async Task Cycle_FailsClosed()
    {
        var pvs = Pvs((20, 5), (10, 1));
        var runs = Runs((5, 10), (1, 20));    // Run1.Base 回指 PV20 ⇒ 环
        var complete = Complete();            // 无任何 Run 完整

        await Assert.ThrowsAsync<SnapshotRunResolutionException>(
            () => SnapshotRunResolver.ResolveCoreAsync(20, pvs, runs, complete));
    }

    [Fact(DisplayName = "Base链断链（无更上层Base且未命中完整快照）⇒ fail-closed")]
    public async Task BrokenChain_FailsClosed()
    {
        var pvs = Pvs((20, 5), (10, 1));
        var runs = Runs((5, 10), (1, null));
        var complete = Complete();            // Run1 无完整快照且无 Base

        await Assert.ThrowsAsync<SnapshotRunResolutionException>(
            () => SnapshotRunResolver.ResolveCoreAsync(20, pvs, runs, complete));
    }

    [Fact(DisplayName = "Base链上PlanVersion无SourceScheduleRunId ⇒ fail-closed")]
    public async Task BasePvWithoutSourceRun_FailsClosed()
    {
        var pvs = Pvs((20, 5), (10, null));
        var runs = Runs((5, 10), (1, null));

        await Assert.ThrowsAsync<SnapshotRunResolutionException>(
            () => SnapshotRunResolver.ResolveCoreAsync(20, pvs, runs, Complete(1)));
    }

    [Fact(DisplayName = "起始PlanVersion不存在 ⇒ fail-closed")]
    public async Task StartPvMissing_FailsClosed()
    {
        await Assert.ThrowsAsync<SnapshotRunResolutionException>(
            () => SnapshotRunResolver.ResolveCoreAsync(99, Pvs(), Runs(), Complete()));
    }
}
