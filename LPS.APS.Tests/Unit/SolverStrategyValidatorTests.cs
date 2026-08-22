using FluentAssertions;
using LPS.APS.Application.Services;
using LPS.APS.Core.Dto;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// SolverStrategyValidator / CandidateGuardrailValidator 单元测试（阶段 E-4）
/// 纯内容校验：不依赖物理落点/DDL，直接对内存构造的 Block 断言合法性。
/// 设计意图：P0-02 裁决后接线到参数集发布路径时，本测试即发布前校验的验收证据；
/// 防止 On-time 越界、负拆分、Guardrail 时序倒挂等"隐形无效配置"越过发布门槛。
/// </summary>
public class SolverStrategyValidatorTests
{
    private readonly SolverStrategyValidator _validator = new();

    [Fact]
    public void E4_默认对象_校验通过()
    {
        // Arrange —— SolverStrategyBlock 默认值（Mode=Forward、TargetPercent=0、Split=3、Setup=30/5）
        var block = new SolverStrategyBlock();

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_合法配置_校验通过()
    {
        // Arrange —— 冻结示例级合法配置（MIXED + On-time 95 + 拆分 3 + 换型 30min）
        var block = new SolverStrategyBlock
        {
            Mode = SolverStrategyMode.Mixed,
            OnTimeTarget = new OnTimeTargetParams { TargetPercent = 95, IsPrimaryObjective = true },
            Split = new SplitParams { MaxOptimizationSplitCount = 3, LimitMandatorySplit = false, MinBatchQty = 1 },
            Setup = new SetupParams { Dimensions = ["Mold", "Color"], DefaultSetupMinutes = 30, SetupLookAheadSize = 5 },
            StageOverlap = new StageOverlapParams { AllowOverlap = false, TransferBatchQty = 10, ThresholdQty = 5, ThresholdPercent = 20 }
        };

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_OnTimeTargetPercent_超100_拒绝()
    {
        // Arrange
        var block = new SolverStrategyBlock();
        block.OnTimeTarget.TargetPercent = 101;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TargetPercent"));
    }

    [Fact]
    public void E4_OnTimeTargetPercent_负值_拒绝()
    {
        // Arrange
        var block = new SolverStrategyBlock();
        block.OnTimeTarget.TargetPercent = -1;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TargetPercent"));
    }

    [Fact]
    public void E4_SplitMaxOptimizationSplitCount_负值_拒绝()
    {
        // Arrange
        var block = new SolverStrategyBlock();
        block.Split.MaxOptimizationSplitCount = -1;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxOptimizationSplitCount"));
    }

    [Fact]
    public void E4_SetupDefaultSetupMinutes_非正_拒绝()
    {
        // Arrange
        var block = new SolverStrategyBlock();
        block.Setup.DefaultSetupMinutes = 0;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("DefaultSetupMinutes"));
    }

    [Fact]
    public void E4_StageOverlapThresholdPercent_越界_拒绝()
    {
        // Arrange
        var block = new SolverStrategyBlock();
        block.StageOverlap.ThresholdPercent = 120;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ThresholdPercent"));
    }

    [Fact]
    public void E4_SolverStrategyMode_数字越界_拒绝()
    {
        // Arrange —— 数字越界反序列化场景（System.Text.Json 对数字枚举越界不抛异常，需校验器拦截）
        var block = new SolverStrategyBlock();
        block.Mode = (SolverStrategyMode)99;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SolverStrategyMode"));
    }
}

/// <summary>
/// CandidateGuardrailValidator 单元测试（阶段 E-4）
/// 覆盖：60/90/180 阈值时序关系（Normal &lt;= Soft &lt;= LocalHard）、各 Ms 为正、警戒百分比 0~100、计数非负。
/// </summary>
public class CandidateGuardrailValidatorTests
{
    private readonly CandidateGuardrailValidator _validator = new();

    [Fact]
    public void E4_默认对象_校验通过()
    {
        // Arrange —— 默认值 60/90/180（DTO 注释）
        var block = new CandidateGuardrailBlock();

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_合法配置_校验通过()
    {
        // Arrange —— 冻结示例级合法配置
        var block = new CandidateGuardrailBlock
        {
            NormalMs = 60_000,
            SoftMs = 90_000,
            LocalHardMs = 180_000,
            ImpactedTaskWarningPercent = 30,
            MaxRepairAttempts = 5,
            MaxPropagationRounds = 10,
            ResourceTopN = 5,
            SplitAlternatives = 3,
            WarnOnlyOnMaxImpacted = true
        };

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeTrue(result.GetErrorMessage());
    }

    [Fact]
    public void E4_NormalMs_非正_拒绝()
    {
        // Arrange
        var block = new CandidateGuardrailBlock();
        block.NormalMs = 0;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NormalMs"));
    }

    [Fact]
    public void E4_NormalMs大于SoftMs_拒绝()
    {
        // Arrange —— 时序倒挂：正常超时晚于软超时
        var block = new CandidateGuardrailBlock();
        block.NormalMs = 100_000;
        block.SoftMs = 90_000;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("NormalMs"));
    }

    [Fact]
    public void E4_SoftMs大于LocalHardMs_拒绝()
    {
        // Arrange —— 时序倒挂：软超时晚于硬超时
        var block = new CandidateGuardrailBlock();
        block.SoftMs = 200_000;
        block.LocalHardMs = 180_000;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("SoftMs"));
    }

    [Fact]
    public void E4_ImpactedTaskWarningPercent_越界_拒绝()
    {
        // Arrange
        var block = new CandidateGuardrailBlock();
        block.ImpactedTaskWarningPercent = 101;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ImpactedTaskWarningPercent"));
    }

    [Fact]
    public void E4_MaxRepairAttempts_负值_拒绝()
    {
        // Arrange
        var block = new CandidateGuardrailBlock();
        block.MaxRepairAttempts = -1;

        // Act
        var result = _validator.Validate(block);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxRepairAttempts"));
    }
}
