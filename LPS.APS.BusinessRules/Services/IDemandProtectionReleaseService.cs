using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Services;

/// <summary>
/// Demand Protection 释放业务服务接口（2号位，数量真相 Owner）
///
/// 【职责】释放 DemandSupplyHardLock 中 LockType='DEMAND_PROTECTION' 的锁，
///  由 DemandProtectionAppService（Application 层）中转调用
///  （4号位前端 → 5号位 API → DemandProtectionAppService → 本服务）。
///
/// 【边界】
/// - 5号位 只做权限/Scope 校验与中转，不得自己算释放数量、改 Lock / Allocation；
/// - 本服务负责业务校验（存在/ACTIVE/仅 DEMAND_PROTECTION）+ 实际释放（置 RELEASED）；
/// - 释放不直接改 Allocation：Pegging 每次现读 ACTIVE 锁，锁置 RELEASED 后下一轮
///   排程自然不再保护、量回流，无需在本接口内动 Allocation / ATS / PSA。
/// </summary>
public interface IDemandProtectionReleaseService
{
    /// <summary>
    /// 释放指定 Lock（逐 lock 部分成功：合法锁释放并标 RELEASED，非法锁标 FAILED 并给原因，
    ///   与请求 lockIds 一一对应返回；仅批量入参非法时抛异常）
    /// </summary>
    /// <param name="lockIds">要释放的 Lock Id 列表（去重后处理）</param>
    /// <param name="releasedBy">操作人</param>
    /// <param name="releaseReason">释放原因（必填）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>逐 Lock 的释放结果列表（顺序与请求去重后一致）</returns>
    Task<List<DemandProtectionReleaseResult>> ReleaseLocksAsync(
        List<long> lockIds,
        string releasedBy,
        string releaseReason,
        CancellationToken ct = default);
}