using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// DemandPriorityProjector 单元测试（2号位 投影层：3号位冻结快照子块 → 2号位执行器配置）。
///
/// 为什么要这一组：快照块 <see cref="DemandPriorityBlock"/> 与执行器配置 <see cref="DemandPriorityConfig"/>
/// 是**两套类型系统**（前者强类型枚举、无计算层；后者字符串词表、带 CalculationLayer），
/// 投影错了不会编译报错，只会静默错排或整条 Pegging 失败。
/// 本组用**生产实读的真实快照载荷**做端到端断言，并直接调真执行器验收 —— 投影不合法会在执行器白名单处炸出来。
///
/// 2026-09-29 真跑实测踩到的两个坑已在此钉死：
///   1. Tie-break 自由文本 "OrderId" 撞执行器白名单（原样透传 ⇒ 整条 Pegging 失败）；
///   2. CalculationLayer 投影成 0 会让全体 Demand 静默退化为 DemandKey ASC 兜底（冻结策略形同未生效）。
/// </summary>
public class DemandPriorityProjectorTests
{
    private readonly DemandPriorityExecutor _executor = new(NullLogger<DemandPriorityExecutor>.Instance);

    /// <summary>
    /// 生产默认策略包 StrategyProfileVersion 811 (SP-DEMO-V3.0, IsDefault=1) 冻结的 DemandPriority 子块，
    /// 实读自 APS_Production 的 RuleSetVersion Id=478 (RS-DEMO-V2) 的 ContentSnapshotJson。
    /// 注意枚举以**数值**序列化（Field:1=DelayStatus, 3=OrderType, 2=CustomerTier, 6=DueDate, 7=IssueDate；
    /// Operator:0=Equals；Direction:0=Asc / 1=Desc）。
    /// </summary>
    private const string RealSnapshot811Json = """
    {"Segments":[
      {"SegmentOrder":1,"SegmentName":"延迟订单","IsEnabled":true,
       "MatchConditions":[{"Field":1,"Operator":0,"Value":"Delayed"},{"Field":3,"Operator":0,"Value":"SO"}],
       "SortFields":[{"Field":6,"Direction":0},{"Field":2,"Direction":1},{"Field":7,"Direction":0}],
       "StableTieBreakFields":["OrderId"]},
      {"SegmentOrder":2,"SegmentName":"VIP","IsEnabled":true,
       "MatchConditions":[{"Field":2,"Operator":0,"Value":"VIP"}],
       "SortFields":[{"Field":6,"Direction":0}],
       "StableTieBreakFields":["OrderId"]},
      {"SegmentOrder":3,"SegmentName":"其余","IsEnabled":true,
       "MatchConditions":[],
       "SortFields":[{"Field":6,"Direction":0}],
       "StableTieBreakFields":["OrderId"]}
    ]}
    """;

    private static DemandPriorityBlock ParseRealBlock()
        => JsonSerializer.Deserialize<DemandPriorityBlock>(RealSnapshot811Json)!;

    // ── 真实快照端到端：投影 → 真执行器验收 ──

    [Fact]
    public void 真实811快照_投影后执行器接受_并按段序输出连续DemandSequence()
    {
        var config = DemandPriorityProjector.Project(ParseRealBlock());

        var demands = new List<UpstreamDemand>
        {
            // 延迟的 SO → 段1
            new() { DemandKey = "3", DelayStatus = "Delayed", OrderType = "SO", CustomerTier = "C", DueDate = new DateTime(2026, 9, 20) },
            // VIP → 段2
            new() { DemandKey = "1", DelayStatus = "OnTrack", OrderType = "SO", CustomerTier = "VIP", DueDate = new DateTime(2026, 9, 15) },
            // 其余 → 段3
            new() { DemandKey = "2", DelayStatus = "OnTrack", OrderType = "WO", CustomerTier = "B", DueDate = new DateTime(2026, 9, 10) },
        };

        var act = () => _executor.ExecutePrioritySort(demands, config);
        act.Should().NotThrow("投影结果必须能通过执行器白名单校验（Tie-break 名漂移会在这里炸）");

        var sorted = _executor.ExecutePrioritySort(demands, config);
        sorted.Select(d => d.DemandKey).Should().Equal(new[] { "3", "1", "2" }, because: "段序优先：段1 → 段2 → 段3");
        sorted.Select(d => d.DemandSequence).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void 真实811快照_每段计算层恒为1_防静默退化()
    {
        var config = DemandPriorityProjector.Project(ParseRealBlock());

        config.Segments.Should().NotBeEmpty();
        config.Segments.Should().OnlyContain(s => s.CalculationLayer == DemandPriorityProjector.TopLevelCalculationLayer,
            "投影成 0 会让 PeggingOrchestrator 的 Where(CalculationLayer == 1) 取到空集，"
            + "全体 Demand 静默退化为 DemandKey ASC 兜底 —— 冻结策略形同未生效，且没有任何报错");
    }

    [Fact]
    public void 真实811快照_TieBreak_OrderId换算为DEMANDKEY()
    {
        var config = DemandPriorityProjector.Project(ParseRealBlock());

        config.Segments.Should().OnlyContain(s =>
            s.StableTieBreakFields.Count == 1 && s.StableTieBreakFields[0] == "DEMANDKEY");
    }

    [Fact]
    public void 真实811快照_字段与方向逐值映射正确()
    {
        var config = DemandPriorityProjector.Project(ParseRealBlock());

        var seg1 = config.Segments.Single(s => s.SegmentOrder == 1);
        seg1.MatchConditions.Should().HaveCount(2);
        seg1.MatchConditions[0].FieldName.Should().Be("DELAYSTATUS");
        seg1.MatchConditions[0].Operator.Should().Be("EQ");
        seg1.MatchConditions[0].Value.Should().Be("Delayed");
        seg1.MatchConditions[1].FieldName.Should().Be("ORDERTYPE");
        seg1.MatchConditions[1].Value.Should().Be("SO");

        seg1.SortFields.Select(f => (f.FieldName, f.Direction))
            .Should().Equal(("DUEDATE", "ASC"), ("CUSTOMERTIER", "DESC"), ("ISSUEDATE", "ASC"));

        // 空 MatchConditions = 无条件命中（兜底段）
        config.Segments.Single(s => s.SegmentOrder == 3).MatchConditions.Should().BeEmpty();
    }

    // ── 生产数据事实：段1 空转（不是投影的错，是事实源缺列）──

    [Fact]
    public void 生产口径DelayStatus恒null_Segment1永不命中_冻结意图空转()
    {
        var config = DemandPriorityProjector.Project(ParseRealBlock());

        // BuildDemandSequenceMapAsync 逐字注释：「DelayStatus / ProtectionStatus：Order 表暂无对应列，保持 null（不造假），
        // 待5号位事实标准化后接入」⇒ 生产跑批时 DelayStatus 恒为 null。
        var demands = new List<UpstreamDemand>
        {
            new() { DemandKey = "late", DelayStatus = null, OrderType = "SO", CustomerTier = "C", DueDate = new DateTime(2026, 9, 20) },
            new() { DemandKey = "early", DelayStatus = null, OrderType = "SO", CustomerTier = "C", DueDate = new DateTime(2026, 9, 10) },
        };

        var sorted = _executor.ExecutePrioritySort(demands, config);

        // 段1 条件含 DelayStatus='Delayed'，恒 null ⇒ 永不命中；两条都落到段3，纯按 DueDate ASC
        sorted.Select(d => d.DemandKey).Should().Equal(new[] { "early", "late" },
            because: "段1「延迟订单优先」在当前事实源下是空转的 —— 需 5号位 补 DelayStatus 事实后才生效（非投影缺陷）");
    }

    // ── 显式失败纪律（不静默、不猜配）──

    [Fact]
    public void 快照子块为空_显式抛错_禁止静默当空策略执行()
    {
        var act = () => DemandPriorityProjector.Project(null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*DemandPriority 子块为空*");
    }

    [Fact]
    public void 白名单外的字段_显式抛错_不猜配()
    {
        // DemandField.RemainingTimeHours(=0) 在执行器白名单里没有对应字段。
        // 刻意不猜（例如猜成 DUEDATE）——静默错排比报错危险得多。
        var block = new DemandPriorityBlock
        {
            Segments =
            [
                new PrioritySegment
                {
                    SegmentOrder = 1,
                    IsEnabled = true,
                    SortFields = [new SegmentSortField { Field = DemandField.RemainingTimeHours, Direction = SortDirection.Asc }],
                }
            ]
        };

        var act = () => DemandPriorityProjector.Project(block);

        act.Should().Throw<NotSupportedException>().WithMessage("*RemainingTimeHours*");
    }

    [Fact]
    public void 未识别TieBreak名_原样透传_由执行器白名单统一报错()
    {
        // 决策：投影层**不**复制一份执行器白名单（复制 = 自造第二个漂移面）。
        // 非别名一律原样交给执行器 ValidateConfigFields 的 EnsureKnownField 报错，
        // 白名单唯一真源 = DemandPriorityExecutor.KnownFields。
        var block = new DemandPriorityBlock
        {
            Segments =
            [
                new PrioritySegment
                {
                    SegmentOrder = 1,
                    IsEnabled = true,
                    SortFields = [new SegmentSortField { Field = DemandField.DueDate, Direction = SortDirection.Asc }],
                    StableTieBreakFields = ["SomethingBrandNew"],
                }
            ]
        };

        var config = DemandPriorityProjector.Project(block);
        config.Segments[0].StableTieBreakFields.Should().Equal("SomethingBrandNew");

        var demands = new List<UpstreamDemand> { new() { DemandKey = "1", DueDate = new DateTime(2026, 9, 10) } };
        var act = () => _executor.ExecutePrioritySort(demands, config);

        act.Should().Throw<Exception>().WithMessage("*SomethingBrandNew*");
    }
}
