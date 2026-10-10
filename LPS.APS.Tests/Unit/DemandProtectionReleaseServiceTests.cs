using FluentAssertions;
using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Interfaces;
using Moq;
using Xunit;
using DemandSupplyHardLock = LPS.APS.Core.Entities.APS.DemandSupplyHardLock;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// DemandProtectionReleaseService 单元测试（2号位 释放业务服务）。
/// 契约（逐 lock 部分成功）：
///   - 合法（ACTIVE + DEMAND_PROTECTION）→ RELEASED；STRICT_BINDING / 非 ACTIVE / 不存在 → FAILED + FailureReason；
///   - 结果与请求 lockIds（去重后）一一对应、顺序一致；
///   - 批量级入参非法（空 id / 空操作人 / reason<5 字符）仍抛异常；
///   - 只对合法子集调用仓库释放；受影响行数不一致时抛异常。
/// </summary>
public class DemandProtectionReleaseServiceTests
{
    private readonly Mock<IDemandSupplyHardLockRepository> _lockRepo = new();
    private readonly DemandProtectionReleaseService _service;

    public DemandProtectionReleaseServiceTests()
    {
        _service = new DemandProtectionReleaseService(_lockRepo.Object);
    }

    private static DemandSupplyHardLock Lock(
        long id,
        string lockType = "DEMAND_PROTECTION",
        string status = "ACTIVE",
        string demandKey = "D001")
        => new()
        {
            Id = id,
            LockType = lockType,
            DemandType = "ORDER",
            DemandKey = demandKey,
            SupplyType = "INVENTORY",
            SupplyKey = $"S{id}",
            LockedQty = 10m,
            Status = status,
            CreatedAt = new DateTime(2026, 9, 1)
        };

    private void SetupRepo(params DemandSupplyHardLock[] locks)
    {
        _lockRepo
            .Setup(r => r.GetLocksByIdsAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(locks);
    }

    private void SetupReleaseAffected(int affectedRows) =>
        _lockRepo
            .Setup(r => r.ReleaseLocksAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(affectedRows);

    [Fact]
    public async Task Release_全部合法_逐条RELEASED()
    {
        SetupRepo(Lock(1), Lock(2));
        SetupReleaseAffected(2);

        var result = await _service.ReleaseLocksAsync(new List<long> { 1, 2 }, "op1", "客户取消订单");

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Status == "RELEASED");
        result[0].LockId.Should().Be(1);
        result[0].DemandKey.Should().Be("D001");
        result[1].LockId.Should().Be(2);
        result.Should().OnlyContain(r => r.FailureReason == null);
    }

    [Fact]
    public async Task Release_部分非法_部分RELEASED部分FAILED_顺序一致()
    {
        // 1=合法，2=STRICT_BINDING，3=合法，4=非ACTIVE，5=不存在
        SetupRepo(Lock(1), Lock(2, lockType: "STRICT_BINDING"), Lock(3, demandKey: "D002"), Lock(4, status: "RELEASED"));
        SetupReleaseAffected(2);

        var result = await _service.ReleaseLocksAsync(
            new List<long> { 1, 2, 3, 4, 5 }, "op1", "客户确认取消");

        result.Should().HaveCount(5);
        result[0].Should().Match<LPS.APS.Core.Dto.DemandProtectionReleaseResult>(r => r.Status == "RELEASED" && r.LockId == 1);
        result[1].Should().Match<LPS.APS.Core.Dto.DemandProtectionReleaseResult>(r => r.Status == "FAILED" && r.FailureReason!.Contains("不可释放"));
        result[2].Should().Match<LPS.APS.Core.Dto.DemandProtectionReleaseResult>(r => r.Status == "RELEASED" && r.DemandKey == "D002");
        result[3].Should().Match<LPS.APS.Core.Dto.DemandProtectionReleaseResult>(r => r.Status == "FAILED" && r.FailureReason!.Contains("非 ACTIVE"));
        result[4].Should().Match<LPS.APS.Core.Dto.DemandProtectionReleaseResult>(r => r.Status == "FAILED" && r.FailureReason!.Contains("不存在"));

        // 只对合法子集 {1,3} 调用释放
        _lockRepo.Verify(r => r.ReleaseLocksAsync(
            It.Is<IEnumerable<long>>(ids => ids.SequenceEqual(new long[] { 1, 3 })),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Release_全部非法_全部FAILED_且不调用释放()
    {
        SetupRepo(Lock(1, lockType: "STRICT_BINDING"), Lock(2, status: "BROKEN"));

        var result = await _service.ReleaseLocksAsync(new List<long> { 1, 2 }, "op1", "客户取消订单");

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Status == "FAILED");
        result[0].FailureReason.Should().Contain("不可释放");
        result[1].FailureReason.Should().Contain("非 ACTIVE");
        _lockRepo.Verify(r => r.ReleaseLocksAsync(
            It.IsAny<IEnumerable<long>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Release_STRICT_BINDING_返回FAILED_且不释放()
    {
        SetupRepo(Lock(1, lockType: "STRICT_BINDING"));

        var result = await _service.ReleaseLocksAsync(new List<long> { 1 }, "op1", "客户取消订单");

        result.Should().HaveCount(1);
        result[0].Status.Should().Be("FAILED");
        result[0].FailureReason.Should().Contain("STRICT_BINDING");
        _lockRepo.Verify(r => r.ReleaseLocksAsync(
            It.IsAny<IEnumerable<long>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Release_不存在_返回FAILED()
    {
        SetupRepo(Lock(1));
        SetupReleaseAffected(1);

        var result = await _service.ReleaseLocksAsync(new List<long> { 1, 99 }, "op1", "客户取消订单");

        result.Should().HaveCount(2);
        result[1].LockId.Should().Be(99);
        result[1].Status.Should().Be("FAILED");
        result[1].FailureReason.Should().Contain("不存在");
    }

    [Fact]
    public async Task Release_重复Id_去重后只返回一条()
    {
        SetupRepo(Lock(1));
        SetupReleaseAffected(1);

        var result = await _service.ReleaseLocksAsync(new List<long> { 1, 1, 1 }, "op1", "客户取消订单");

        result.Should().HaveCount(1);
        result[0].LockId.Should().Be(1);
        result[0].Status.Should().Be("RELEASED");
        _lockRepo.Verify(r => r.ReleaseLocksAsync(
            It.Is<IEnumerable<long>>(ids => ids.SequenceEqual(new long[] { 1 })),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Release_空lockIds_抛ArgumentException()
    {
        var act = () => _service.ReleaseLocksAsync(new List<long>(), "op1", "客户取消订单");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Release_空releasedBy_抛ArgumentException()
    {
        var act = () => _service.ReleaseLocksAsync(new List<long> { 1 }, "  ", "客户取消订单");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Release_短reason_不抛异常_仅需非空白()
    {
        // 0号位 31-0 裁决撤销 ≥5 字符硬约束，仅保留非空白校验
        var act = () => _service.ReleaseLocksAsync(new List<long> { 1 }, "op1", "取消");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Release_纯空白reason_抛ArgumentException()
    {
        var act = () => _service.ReleaseLocksAsync(new List<long> { 1 }, "op1", "     ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Release_全部合法但受影响行数不一致_抛InvalidOperationException()
    {
        SetupRepo(Lock(1), Lock(2));
        SetupReleaseAffected(1); // 预期 2，实际 1

        var act = () => _service.ReleaseLocksAsync(new List<long> { 1, 2 }, "op1", "客户取消订单");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*不一致*");
    }
}