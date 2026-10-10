using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 跨版本连续性分桶纯函数测试（逐 MES 工单契约，§8/§9/§16）。
/// 契约：Q=100、WO001 在制 E=30 → 1号位必须同时看到 Continuation 30 + Free 70；Q 不被 WIP 供给扣小。
/// 多工单绝不合并（P0 红线）；E&gt;Q 不砍单、不产新自由 Task、登记 Execution Over-Commit。
/// Slice 键（LogicalDemandKey）= 源 key + "/WO:" + MESWorkOrderNo；Free = 源 key + "/FREE"；共用源 AllocationSequence。
/// **ContinuationKey 与 Slice 键是两回事**（红线 Q3）：ContinuationKey = f(ScheduleRun, MESWorkOrderNo)，
/// 同一 MES 工单的全部 Slice 共享同一个 Key，**不得拼入 LogicalDemandKey**（否则同一工单裂分）。
/// PlannedProcessQty 逐片按材料级良率单位投入比（源 PlannedProcessQty/NetOutputQty）反算。
/// </summary>
public class ContinuityBucketingTests
{
    private static LogicalProductionDemand Demand(
        string key,
        long allocSeq,
        string? pi,
        decimal netQty,
        decimal processQty = 0m,
        string startStage = "ST")
        => new()
        {
            LogicalDemandKey       = key,
            PlanVersionId          = 328,
            DomainKey              = "FAMILY_X",
            AllocationSequence     = allocSeq,
            DemandKey              = $"D-{allocSeq}",
            OrderId                = 100L + allocSeq,
            MaterialId             = 5001,
            FactoryId              = 7,
            StartStageCode         = startStage,
            NetOutputQty           = netQty,
            PlannedProcessQty      = processQty > 0m ? processQty : netQty,
            ProductionInstructionNo = pi,
            IsContinuation         = false
        };

    private static ExistingExecutionContextDto Ctx(
        string pi, string wo, decimal remaining, string startStage = "MACH", string? startOp = null)
        => new()
        {
            ProductionInstructionNo = pi,
            MESWorkOrderNo          = wo,
            WorkOrderStatus         = "IN_PROGRESS",
            StartStageCode          = startStage,
            StartOperationCode      = startOp,
            DerivedRemainingQty     = remaining,
            DataCutoffTime          = DateTime.Now
        };

    private static Dictionary<string, IReadOnlyList<ExistingExecutionContextDto>> Map(
        params ExistingExecutionContextDto[] ctxs)
        => ctxs
            .GroupBy(c => c.ProductionInstructionNo, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ExistingExecutionContextDto>)g.ToList(),
                StringComparer.Ordinal);

    private static List<LogicalProductionDemand> Bucket(
        List<LogicalProductionDemand> demands,
        Dictionary<string, IReadOnlyList<ExistingExecutionContextDto>> contextsByPi,
        System.Action<PeggingOrchestrator.ContinuityOverCommitEvent>? onOverCommit = null)
        => PeggingOrchestrator.BucketContinuityShares(demands, contextsByPi, onOverCommit);

    [Fact]
    public void 单工单E小于Q_切出连续份额与自由份额()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_1", 1, "PI-C01", 100m) };
        var ctxs = Map(Ctx("PI-C01", "WO001", 30m));

        var result = Bucket(demands, ctxs);

        result.Should().HaveCount(2);
        var continuation = result.Single(d => d.IsContinuation);
        continuation.NetOutputQty.Should().Be(30m);
        continuation.LogicalDemandKey.Should().Be("328_1/WO:WO001");   // Slice 键带工单身份（≠ ContinuationKey）
        continuation.AllocationSequence.Should().Be(1);                 // 不新建 Allocation
        continuation.DemandKey.Should().Be("D-1");

        var free = result.Single(d => !d.IsContinuation);
        free.NetOutputQty.Should().Be(70m);                             // Free = Q − E，非 Q 被扣小后的 40
        free.LogicalDemandKey.Should().Be("328_1/FREE");
    }

    [Fact]
    public void 多工单绝不合并_每张独立连续份额()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_2", 2, "PI-C02", 100m) };
        var ctxs = Map(Ctx("PI-C02", "WO101", 20m), Ctx("PI-C02", "WO102", 30m));

        var result = Bucket(demands, ctxs);

        var continuations = result.Where(d => d.IsContinuation).ToList();
        continuations.Should().HaveCount(2);                            // P0 红线：不合并成一条 50
        continuations.Select(c => c.NetOutputQty).Should().BeEquivalentTo(new[] { 20m, 30m });

        result.Single(d => !d.IsContinuation).NetOutputQty.Should().Be(50m);   // Free = Q − ΣE = 50
    }

    [Fact]
    public void 连续份额键_按工单身份生成_不随Slice裂分()
    {
        // 红线 Q3（0号位 2026-10-07 裁决 §六）：一个 ScheduleRun 内，一个 MESWorkOrderNo
        // 有且仅有一个 ContinuationKey，**不得拼入 LogicalDemandKey**。
        // ⚠ 2026-10-09 修订：原用例用「两条需求共用同一 PI+同一 MES 工单」造出两个 Slice 来验键，
        //   该前提已被 PM T2-06.2（同工单份额不得跨需求重复计提）推翻 ⇒ 改用「一条需求 + 两张工单」，
        //   仍完整覆盖「键 = f(PlanVersionId, MESWorkOrderNo)、与 Slice 键无关」这条红线。
        var demands = new List<LogicalProductionDemand>
        {
            Demand("328_11", 11, "PI-C11", 100m),
        };
        var ctxs = Map(Ctx("PI-C11", "WO900", 30m), Ctx("PI-C11", "WO901", 20m));

        var result = Bucket(demands, ctxs);
        var conts = result.Where(d => d.IsContinuation).ToList();

        conts.Should().HaveCount(2);                                   // 逐工单各切一片，工单不合并
        conts.Select(c => c.ContinuationKey).Should().BeEquivalentTo(new[] { "CK-328-WO900", "CK-328-WO901" });
        conts.Should().OnlyContain(c => !c.ContinuationKey!.Contains(c.LogicalDemandKey));

        // A/B 恒 NoSplitMerge（1号位 PhaseTwoInitialScheduler.cs:292 硬校验）；Free 不得置位
        conts.Should().OnlyContain(c => c.NoSplitMerge);
        result.Where(d => !d.IsContinuation).Should().OnlyContain(d => !d.NoSplitMerge);
    }

    [Fact]
    public void 同一MES工单份额_跨需求只消费一次_不重复复制E()
    {
        // PM 2026-10-09 T2-06.2 / C-Q4（F-04）：一个 MESWorkOrderNo 的可承接既存执行份额
        // 在本 Run 内**不得跨需求重复计算**；合法多 Demand 共用同一 PI 时按真实份额逐次消耗，
        // 不得按 Q 比例猜分、也不得把同一条 E 完整复制 N 次。
        var demands = new List<LogicalProductionDemand>
        {
            Demand("328_21", 21, "PI-C21", 100m),
            Demand("328_22", 22, "PI-C21", 40m),   // 同一 PI 下的另一条合法需求
        };
        var ctxs = Map(Ctx("PI-C21", "WO910", 30m));   // 该工单只有一份 30 的物理可承接量

        var over = new List<PeggingOrchestrator.ContinuityOverCommitEvent>();
        var result = Bucket(demands, ctxs, over.Add);

        var conts = result.Where(d => d.IsContinuation).ToList();
        conts.Should().HaveCount(1);                                   // 只有先到的需求拿到这份份额
        conts.Single().NetOutputQty.Should().Be(30m);
        conts.Single().ContinuationKey.Should().Be("CK-328-WO910");
        conts.Select(c => c.ContinuationKey).Should().OnlyHaveUniqueItems();   // 一份份额 ⇒ 一个 Key

        // 后到的需求：连续份额 0 ⇒ 整条需求原样直通（全额走自由/规划侧），需求不被砍、不裂片
        var second = result.Single(d => d.LogicalDemandKey == "328_22");
        second.IsContinuation.Should().BeFalse();
        second.NetOutputQty.Should().Be(40m);
        result.Should().NotContain(d => d.LogicalDemandKey == "328_22/WO:WO910");

        // 总量守恒：30(连续) + 70(Free) + 40(Free) = 140 = 100 + 40，无放大
        result.Sum(d => d.NetOutputQty).Should().Be(140m);

        // 重复引用必须登记、不得静默（PM §九：保守保留 Issue）
        over.Should().ContainSingle(e => e.Kind == "DUPLICATE_WO_SHARE");
    }

    [Fact]
    public void 连续份额键_跨运行不同_同一运行内确定性()
    {
        // 红线：作用域 = 本 ScheduleRun ⇒ 同工单跨 Run 的 Key 不同；纯函数 ⇒ 同输入必得同值。
        PeggingOrchestrator.ContinuationKeyOf(328, "WO900").Should().Be("CK-328-WO900");
        PeggingOrchestrator.ContinuationKeyOf(329, "WO900").Should().Be("CK-329-WO900");
        PeggingOrchestrator.ContinuationKeyOf(328, "WO900")
            .Should().Be(PeggingOrchestrator.ContinuationKeyOf(328, "WO900"));
    }

    [Fact]
    public void 单工单E等于Q_只连续份额_不产生自由份额()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_3", 3, "PI-C03", 50m) };
        var ctxs = Map(Ctx("PI-C03", "WO001", 50m));

        var result = Bucket(demands, ctxs);

        result.Should().ContainSingle();
        result[0].IsContinuation.Should().BeTrue();
        result[0].NetOutputQty.Should().Be(50m);
    }

    [Fact]
    public void E大于Q_不砍单_只连续份额_登记执行超量()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_4", 4, "PI-C04", 50m) };
        var ctxs = Map(Ctx("PI-C04", "WO001", 70m));
        var over = new List<PeggingOrchestrator.ContinuityOverCommitEvent>();

        var result = Bucket(demands, ctxs, over.Add);

        result.Should().ContainSingle();                                // 不产自由 Task
        result[0].IsContinuation.Should().BeTrue();
        result[0].NetOutputQty.Should().Be(70m);                        // §9.1：不砍到 Q=50，C 原样 70
        over.Should().ContainSingle(o => o.Kind == "EXECUTION_OVER_COMMIT" && o.ExcessQty == 20m); // Execution Over-Commit=20
    }

    [Fact]
    public void Q等于0_在制仍IN_PROGRESS_不产需求_登记DemandMismatch()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_5", 5, "PI-C05", 0m) };
        var ctxs = Map(Ctx("PI-C05", "WO001", 20m));
        var over = new List<PeggingOrchestrator.ContinuityOverCommitEvent>();

        var result = Bucket(demands, ctxs, over.Add);

        result.Should().BeEmpty();                                      // §9.2：不生成正常 Demand/Task
        over.Should().ContainSingle(o => o.Kind == "DEMAND_MISMATCH" && o.ContinuationQty == 20m);
    }

    [Fact]
    public void 无PI_原样透传不切分()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_6", 6, null, 80m) };

        var result = Bucket(demands, Map());

        result.Should().ContainSingle();
        result[0].IsContinuation.Should().BeFalse();
        result[0].NetOutputQty.Should().Be(80m);
    }

    [Fact]
    public void 工单剩余为零_原样透传自由需求()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_7", 7, "PI-C07", 60m) };
        var ctxs = Map(Ctx("PI-C07", "WO001", 0m));

        var result = Bucket(demands, ctxs);

        result.Should().ContainSingle();
        result[0].IsContinuation.Should().BeFalse();
        result[0].NetOutputQty.Should().Be(60m);
    }

    [Fact]
    public void 续排起点从五号位带出_自由份额起点归零_PlannedProcessQty按良率反算()
    {
        var demands = new List<LogicalProductionDemand> { Demand("328_8", 8, "PI-C08", 100m, processQty: 120m) };
        var ctxs = Map(Ctx("PI-C08", "WO001", 30m, startStage: "MACH", startOp: "NC"));

        var result = Bucket(demands, ctxs);

        var continuation = result.Single(d => d.IsContinuation);
        continuation.StartOperationCode.Should().Be("NC");
        continuation.StartStageCode.Should().Be("MACH");
        continuation.PlannedProcessQty.Should().Be(36m);                // 30 × 120/100 反算

        var free = result.Single(d => !d.IsContinuation);
        free.StartOperationCode.Should().BeNull();                      // 自由=新生产量，起点归首工序
        free.StartStageCode.Should().Be("ST");                          // 沿用源需求起点
        free.PlannedProcessQty.Should().Be(84m);                        // 70 × 120/100 反算
    }

    [Fact]
    public void 实景修复后_多工单含部分完工_逐工单连续加一份自由_红线同现()
    {
        // 328 实景（5号位修好后）：一个 PI 下多张在制工单，有的有剩余、有的已完工(E=0)。
        // 本用例钉死两件事：① E=0 的工单绝不掺入连续份额；② 只要存在 E>0 的工单，Free 片必然同现。
        var demands = new List<LogicalProductionDemand> { Demand("328_9", 9, "PI-C09", 100m) };
        var ctxs = Map(
            Ctx("PI-C09", "WO201", 30m),
            Ctx("PI-C09", "WO202", 20m),
            Ctx("PI-C09", "WO203", 0m));    // 已完工，不产连续份额

        var result = Bucket(demands, ctxs);

        var continuations = result.Where(d => d.IsContinuation).ToList();
        continuations.Should().HaveCount(2);                              // 只 E>0 的两张，绝不掺入 E=0
        continuations.Sum(c => c.NetOutputQty).Should().Be(50m);
        continuations.Select(c => c.LogicalDemandKey)
            .Should().BeEquivalentTo(new[] { "328_9/WO:WO201", "328_9/WO:WO202" });

        var free = result.Single(d => !d.IsContinuation && d.LogicalDemandKey.EndsWith("/FREE"));
        free.NetOutputQty.Should().Be(50m);                               // Free = 100 − (30+20) 必然同现
    }
}