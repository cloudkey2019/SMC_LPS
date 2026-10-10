using FluentAssertions;
using LPS.APS.Application.Services;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// D8 同TaskNo自阻挡（修改指导 §二十 / R17 / T18）。
/// 必须保证：本域 / 自身版本 / 基线版本 的旧 ACTIVE 块绝不作为 ExternalDomainResourceBlocks 阻挡自己；
/// 只保留「其它域 × 非当前/基线版本」的外部占用。本域旧块应走锚点（ExecutionConstraint）而非外部阻挡。
/// </summary>
public class D8SelfBlockingTests
{
    private const string CurrentDomain = "FAMILY_X";
    private const int CurrentPlanVersion = 330;
    private const int BasePlanVersion = 328;

    [Theory]
    [InlineData("FAMILY_X", 999, true)]  // 本域旧块 → 排除（锚点域，不得挡自己）
    [InlineData("family_x", 999, true)]  // 本域（大小写不敏感）→ 排除
    [InlineData("FAMILY_Y", 330, true)]  // 自身版本 → 排除
    [InlineData("FAMILY_Y", 328, true)]  // 基线版本（比较基线，非外部域）→ 排除
    [InlineData("FAMILY_Y", 999, false)] // 外域 + 非当前/基线 → 保留为外部占用
    public void 旧块是否作为外部阻挡(string blockDomainKey, int blockSourcePlanVersionId, bool expectedExcluded)
    {
        var excluded = SchedulingOrchestrator.IsSelfOrBaselineExternalBlock(
            blockDomainKey,
            blockSourcePlanVersionId,
            CurrentDomain,
            CurrentPlanVersion,
            BasePlanVersion);

        excluded.Should().Be(expectedExcluded);
    }
}