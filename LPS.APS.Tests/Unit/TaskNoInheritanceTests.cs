using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// G4 TaskNo 三分支纯函数测试（冻结 §12.10 / T09/T10/T22 + R8 反证）。
/// 分支1 APS旧Task连续（有旧TaskNo→继承）；分支2 无TaskNo外部MES连续（无旧号→新号绑原工单）；分支3 自由（新号）。
/// MESWorkOrderNo 身份复用 LogicalDemandKey（"{源key}/WO:{wo}"）承载，落库前反解，不另设字段（§7.2）。
/// </summary>
public class TaskNoInheritanceTests
{
    [Theory]
    [InlineData("328_1/WO:WO001", "WO001")]
    [InlineData("328_9/WO:WO202", "WO202")]
    [InlineData("KEY/WO:MES-DUP-001", "MES-DUP-001")]
    public void ExtractMesWorkOrderNo_连续份额键_解析出工单号(string key, string expected)
        => PeggingOrchestrator.ExtractMesWorkOrderNo(key).Should().Be(expected);

    [Theory]
    [InlineData("328_1/FREE")]
    [InlineData("328_1")]
    [InlineData("PLAIN_KEY")]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractMesWorkOrderNo_非连续键_返回null(string? key)
        => PeggingOrchestrator.ExtractMesWorkOrderNo(key).Should().BeNull();

    /// <summary>
    /// 构造一条 FinalTaskDraft（只关心 TaskNo 归组所需的三项：批键 / Stage / 工序码）。
    /// </summary>
    private static FinalTaskDraft Draft(string operationCode, string stageCode = "ST", string? batchKey = "EB|328_1|001")
        => new()
        {
            FinalDraftId           = "abcdef12-3456",
            SourceDraftId          = "328_1",
            MaterialId             = 5001,
            StageCode              = stageCode,
            OperationCode          = operationCode,
            ExecutionBatchDraftKey = batchKey
        };

    [Fact]
    public void ResolveTaskNo_连续且反查到旧号_继承旧TaskNo()
    {
        var old = new Dictionary<string, string>(StringComparer.Ordinal) { ["WO001"] = "PEGG-100-OLDTASK1" };
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);

        var taskNo = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1"), "WO001", old, batch);

        taskNo.Should().Be("PEGG-100-OLDTASK1");   // 分支①（A桶）：继承旧号，不新建（T-004）
    }

    [Fact]
    public void ResolveTaskNo_连续但无旧号_新TaskNo()
    {
        var old = new Dictionary<string, string>(StringComparer.Ordinal);
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);

        var taskNo = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1"), "WO999", old, batch);

        taskNo.Should().StartWith("PEGG-328-");    // 分支②（B桶）：新号绑原工单（T-004）
        taskNo.Length.Should().BeLessThanOrEqualTo(50);   // 列宽 NVARCHAR(50)
    }

    [Fact]
    public void ResolveTaskNo_自由份额_新TaskNo()
    {
        var old = new Dictionary<string, string>(StringComparer.Ordinal) { ["WO001"] = "PEGG-100-OLDTASK1" };
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);

        var taskNo = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1"), null, old, batch);

        taskNo.Should().StartWith("PEGG-328-");    // 分支③（C桶）：自由=新号（不因字典有旧号而误继承）
    }

    [Fact]
    public void 同一执行批的多条工序_共用一个TaskNo()
    {
        // T-002 逐字：「TaskNo 是 Stage 内 MES 执行批的 APS 跨版本业务身份；**一个 TaskNo 可对应 N 条 Operation Task**」。
        // 旧实现按 finalDraftId 一条工序一个号 ⇒ 同批被劈成 N 个号，直接违反 T-002。
        var old = new Dictionary<string, string>(StringComparer.Ordinal);
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);

        var a = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1"), null, old, batch);
        var b = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP2"), null, old, batch);
        var c = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP3"), null, old, batch);

        a.Should().Be(b).And.Be(c);
    }

    [Fact]
    public void 同一批键但不同Stage_必须不同TaskNo()
    {
        // T-003 逐字：「一个 TaskNo 绑定一个 MESWorkOrderNo；**MES 工单不跨 Stage**」。
        // ExecutionBatchDraftKey 的键域是 (需求键, 批序号)，**不含 Stage** ⇒ 必须再叠 StageCode 才不跨 Stage。
        var old = new Dictionary<string, string>(StringComparer.Ordinal);
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);

        var stageA = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1", stageCode: "CN_MACH"), null, old, batch);
        var stageB = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP2", stageCode: "CN_SURF"), null, old, batch);

        stageA.Should().NotBe(stageB);
    }

    [Fact]
    public void 不同批键_必须不同TaskNo()
    {
        var old = new Dictionary<string, string>(StringComparer.Ordinal);
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);

        var b1 = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1", batchKey: "EB|328_1|001"), null, old, batch);
        var b2 = PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1", batchKey: "EB|328_1|002"), null, old, batch);

        b1.Should().NotBe(b2);
    }

    [Fact]
    public void 新TaskNo后缀_跨调用确定性_不随进程随机化()
    {
        // 防回归：若改用 string.GetHashCode()，.NET Core 每进程随机加盐 ⇒ 同批键跨运行得到不同号，
        // 破坏 T-002「跨版本业务身份」。此处直接锁纯函数确定性。
        PeggingOrchestrator.ShortHash16("EB|328_1|001|CN_MACH")
            .Should().Be(PeggingOrchestrator.ShortHash16("EB|328_1|001|CN_MACH"));
        PeggingOrchestrator.ShortHash16("A").Should().NotBe(PeggingOrchestrator.ShortHash16("B"));
        PeggingOrchestrator.ShortHash16("A").Should().HaveLength(16);
    }

    [Fact]
    public void 分桶产出连续片_反解工单号_三分支身份闭环()
    {
        // 关键键闭环：分桶产出的 Continuation Key 必含 "/WO:{wo}"，能被 ExtractMesWorkOrderNo 反解，
        // 进而命中旧 TaskNo 映射实现继承——从分桶到落盘身份一致性。
        var demands = new List<LogicalProductionDemand>
        {
            new()
            {
                LogicalDemandKey       = "328_1",
                PlanVersionId          = 328,
                DomainKey              = "FAMILY_X",
                AllocationSequence     = 1,
                DemandKey              = "D-1",
                MaterialId             = 5001,
                FactoryId              = 7,
                StartStageCode         = "ST",
                NetOutputQty           = 100m,
                PlannedProcessQty      = 100m,
                ProductionInstructionNo = "PI-C01",
                IsContinuation         = false
            }
        };
        var ctxs = new Dictionary<string, IReadOnlyList<ExistingExecutionContextDto>>(StringComparer.Ordinal)
        {
            ["PI-C01"] = new[]
            {
                new ExistingExecutionContextDto
                {
                    ProductionInstructionNo = "PI-C01",
                    MESWorkOrderNo          = "WO001",
                    WorkOrderStatus         = "IN_PROGRESS",
                    StartStageCode          = "ST",
                    DerivedRemainingQty     = 30m
                }
            }
        };

        var bucketed = PeggingOrchestrator.BucketContinuityShares(demands, ctxs);
        var cont     = bucketed.Single(d => d.IsContinuation);
        var wo       = PeggingOrchestrator.ExtractMesWorkOrderNo(cont.LogicalDemandKey);

        wo.Should().Be("WO001");

        var old = new Dictionary<string, string>(StringComparer.Ordinal) { ["WO001"] = "PEGG-100-OLDTASK1" };
        var batch = new Dictionary<string, string>(StringComparer.Ordinal);
        PeggingOrchestrator.ResolveTaskNo(328, Draft("OP1"), wo, old, batch).Should().Be("PEGG-100-OLDTASK1");
    }
}