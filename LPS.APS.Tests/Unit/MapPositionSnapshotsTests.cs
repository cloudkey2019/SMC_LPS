using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Enum;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// MapPositionSnapshots 纯函数测试（2号位 PI Position 快照落库前映射 + 数量闭环校验）。
/// 契约：Σ Position.Quantity = ERP RemainingQty；异常只登记 IssueCode/issue、不修正数量；
/// PositionType 落枚举名字符串；CurrentStageCode 落 StageCode。
/// </summary>
public class MapPositionSnapshotsTests
{
    private static PositionSlice Slice(PositionType type, decimal qty, string? stageCode = null, bool unlocated = false)
        => new()
        {
            PositionType = type,
            Quantity = qty,
            StageCode = stageCode,
            IsUnlocated = unlocated
        };

    [Fact]
    public void 闭合正常_映射全量行且无异常码()
    {
        var inputs = new List<ProductionInstructionPositionInput>
        {
            new() { ProductionInstructionNo = "PI-1", MaterialId = 1001, ErpRemainingQty = 100m },
            new() { ProductionInstructionNo = "PI-2", MaterialId = 2002, ErpRemainingQty = 60m }
        };

        var results = new Dictionary<string, ProductionInstructionPositionResult>
        {
            ["PI-1"] = new()
            {
                ProductionInstructionNo = "PI-1",
                TotalRemainingQty = 100m,
                IsSuccess = true,
                Positions = new[]
                {
                    Slice(PositionType.STAGE_WAITING, 70m, "S10"),
                    Slice(PositionType.XC, 30m)
                }
            },
            ["PI-2"] = new()
            {
                ProductionInstructionNo = "PI-2",
                TotalRemainingQty = 60m,
                IsSuccess = true,
                Positions = new[]
                {
                    Slice(PositionType.UNLOCATED, 60m, unlocated: true)
                }
            }
        };

        var materialCodeByPi = new Dictionary<string, string>
        {
            ["PI-1"] = "MAT-1",
            ["PI-2"] = "MAT-2"
        };

        var (rows, issues) = PeggingOrchestrator.MapPositionSnapshots(
            7, 42, inputs, results, materialCodeByPi);

        rows.Should().HaveCount(3);
        issues.Should().BeEmpty();
        rows.Should().OnlyContain(r => r.ScheduleRunId == 7 && r.PlanVersionId == 42 && r.IssueCode == null);

        var stageRow = rows.Single(r => r.ProductionInstructionNo == "PI-1" && r.PositionType == "STAGE_WAITING");
        stageRow.Quantity.Should().Be(70m);
        stageRow.CurrentStageCode.Should().Be("S10");
        stageRow.MaterialCode.Should().Be("MAT-1");
        stageRow.MaterialId.Should().Be(1001);

        rows.Should().ContainSingle(r => r.PositionType == "XC" && r.Quantity == 30m);
        rows.Should().ContainSingle(r => r.PositionType == "UNLOCATED" && r.Quantity == 60m);
    }

    [Fact]
    public void 数量不闭合_整PI打QUANTITY_GAP且不修正数量()
    {
        var inputs = new List<ProductionInstructionPositionInput>
        {
            new() { ProductionInstructionNo = "PI-1", MaterialId = 1001, ErpRemainingQty = 100m }
        };

        var results = new Dictionary<string, ProductionInstructionPositionResult>
        {
            ["PI-1"] = new()
            {
                ProductionInstructionNo = "PI-1",
                IsSuccess = true,
                Positions = new[]
                {
                    Slice(PositionType.STAGE_WAITING, 40m, "S10"),
                    Slice(PositionType.UNLOCATED, 40m, unlocated: true) // 合计 80 != 100
                }
            }
        };

        var (rows, issues) = PeggingOrchestrator.MapPositionSnapshots(
            7, 42, inputs, results, new Dictionary<string, string> { ["PI-1"] = "MAT-1" });

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.IssueCode == "QUANTITY_GAP");
        rows.Sum(r => r.Quantity).Should().Be(80m); // 不修正事实
        issues.Should().ContainSingle(i => i.Contains("PI-1") && i.Contains("数量不闭合"));
    }

    [Fact]
    public void 计算失败_打POSITION_FAILED()
    {
        var inputs = new List<ProductionInstructionPositionInput>
        {
            new() { ProductionInstructionNo = "PI-1", MaterialId = 1001, ErpRemainingQty = 100m }
        };

        var results = new Dictionary<string, ProductionInstructionPositionResult>
        {
            ["PI-1"] = new()
            {
                ProductionInstructionNo = "PI-1",
                IsSuccess = false,
                FailureReason = "无 Stage 事实",
                Positions = new[]
                {
                    Slice(PositionType.UNLOCATED, 100m, unlocated: true)
                }
            }
        };

        var (rows, issues) = PeggingOrchestrator.MapPositionSnapshots(
            7, 42, inputs, results, new Dictionary<string, string> { ["PI-1"] = "MAT-1" });

        rows.Should().HaveCount(1);
        rows[0].IssueCode.Should().Be("POSITION_FAILED");
        issues.Should().ContainSingle(i => i.Contains("计算失败"));
    }

    [Fact]
    public void 位置缺失_不落行仅登记()
    {
        var inputs = new List<ProductionInstructionPositionInput>
        {
            new() { ProductionInstructionNo = "PI-1", MaterialId = 1001, ErpRemainingQty = 100m }
        };

        var (rows, issues) = PeggingOrchestrator.MapPositionSnapshots(
            7, 42, inputs,
            new Dictionary<string, ProductionInstructionPositionResult>(),
            new Dictionary<string, string> { ["PI-1"] = "MAT-1" });

        rows.Should().BeEmpty();
        issues.Should().ContainSingle(i => i.Contains("PI-1") && i.Contains("位置缺失"));
    }
}
