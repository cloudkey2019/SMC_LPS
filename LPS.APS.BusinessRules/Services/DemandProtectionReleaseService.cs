using LPS.APS.Core.Dto;
using LPS.APS.Core.Interfaces;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// Demand Protection 释放业务服务实现（2号位，数量真相 Owner）
///
/// 只释放 LockType='DEMAND_PROTECTION' 的 ACTIVE 锁；STRICT_BINDING 不可释放。
/// 逐 lock 部分成功：合法锁置 Status='RELEASED'，非法锁返回 FAILED + FailureReason。
/// 释放=置 Status='RELEASED' + 记录 ReleasedAt/By/Reason；不直接改 Allocation
/// （下一次 Pegging 现读 ACTIVE 锁，锁置 RELEASED 后自然不再保护、量回流）。
/// </summary>
public class DemandProtectionReleaseService : IDemandProtectionReleaseService
{
    private readonly IDemandSupplyHardLockRepository _lockRepository;

    public DemandProtectionReleaseService(IDemandSupplyHardLockRepository lockRepository)
    {
        _lockRepository = lockRepository ?? throw new ArgumentNullException(nameof(lockRepository));
    }

    public async Task<List<DemandProtectionReleaseResult>> ReleaseLocksAsync(
        List<long> lockIds,
        string releasedBy,
        string releaseReason,
        CancellationToken ct = default)
    {
        // 批量级入参校验：仍抛异常（与逐 lock 的业务状态无关）
        if (lockIds == null || lockIds.Count == 0)
            throw new ArgumentException("lockIds 不能为空", nameof(lockIds));
        if (string.IsNullOrWhiteSpace(releasedBy))
            throw new ArgumentException("releasedBy 不能为空", nameof(releasedBy));
        // 仅必填校验（0号位 31-0 裁决：releaseReason ≥5字符 无冻结依据，撤销长度硬约束）
        if (string.IsNullOrWhiteSpace(releaseReason))
            throw new ArgumentException("releaseReason 不能为空", nameof(releaseReason));

        var ids = lockIds.Distinct().ToList();

        // 拉取（不做状态过滤，便于逐条给出精确诊断）
        var byId = (await _lockRepository.GetLocksByIdsAsync(ids, ct)).ToDictionary(l => l.Id);

        // 逐 lock 判定，保持请求（去重后）顺序
        var validIds = new List<long>(ids.Count);
        var results = new List<DemandProtectionReleaseResult>(ids.Count);

        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var lk))
            {
                results.Add(Failed(id, string.Empty, "Lock 不存在"));
                continue;
            }

            if (lk.Status != "ACTIVE")
            {
                results.Add(Failed(id, lk.DemandKey, $"非 ACTIVE（当前 {lk.Status}）"));
                continue;
            }

            if (lk.LockType != "DEMAND_PROTECTION")
            {
                results.Add(Failed(id, lk.DemandKey, $"{lk.LockType} 不可释放"));
                continue;
            }

            validIds.Add(id);
            results.Add(new DemandProtectionReleaseResult
            {
                LockId = id,
                DemandKey = lk.DemandKey,
                Status = "RELEASED"
            });
        }

        // 一次性批量释放合法子集
        if (validIds.Count > 0)
        {
            var affected = await _lockRepository.ReleaseLocksAsync(validIds, releasedBy, releaseReason.Trim(), ct);
            if (affected != validIds.Count)
                throw new InvalidOperationException(
                    $"释放受影响行数（{affected}）与预期（{validIds.Count}）不一致，可能存在并发，请重试");
        }

        return results;
    }

    private static DemandProtectionReleaseResult Failed(long lockId, string demandKey, string reason)
        => new()
        {
            LockId = lockId,
            DemandKey = demandKey,
            Status = "FAILED",
            FailureReason = reason
        };
}