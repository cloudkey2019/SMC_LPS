namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 白天候选 BOM 实时展开服务（2号位职责）。
///
/// 【依据】PM 0923「白天 BOM=5号位 供接口 + 2号位 接线」；5号位 回执 v1.0（2026-09-24）三点确认
///         + Q1~Q4 口径（2↔5 直接闭环，不上 PM）。
///
/// 【链路】
///   新订单（OrderCanonicalId）
///     ↓ ① 补写 MES_API_BOM_Request_Detail（Independent BatchNo = "RT:{yyyyMMdd}"，不得复用夜间批次号）
///     ↓ ② 调 sp_ExpandBOMRealtime_vNext(@RequestDetailId)（SP 内部自写 _Realtime 请求行、幂等：已 READY 则跳过）
///     ↓ ③ 读 MES_APS_BOM_Workset_Realtime（按 RequestDetailId 隔离，与夜间 Workset 并轨）
///
/// 【加护约定（5号位 回执 §三）】
///   加护1：SP 前置校验 MES_BOM_Edge_RefreshLog 非 COMPLETED 会 RAISERROR —— **须捕获并转可解释结果，不得硬失败**
///   加护2：写 Detail 须填全 MaterialCode / FactoryCode / OrderType（RequestedBOMNO 可空）
///   加护3：Detail 补写前先查是否已展开；白天用独立 BatchNo；不得与夜间批次号冲突
/// </summary>
public interface IBomRealtimeExpandService
{
    /// <summary>
    /// 确保指定订单的 BOM 实时展开就绪（幂等）。
    /// 已展开（Detail 存在且展开结果就绪）的订单跳过；缺失的补写 Detail 后调实时展开。
    /// </summary>
    /// <param name="orderCanonicalIds">订单标准化身份集合（OrderBomRequestLink.OrderCanonicalId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>展开结果（含三重校验 Issue 清单）</returns>
    Task<Dto.BomRealtimeExpandResult> EnsureExpandedAsync(
        IReadOnlyList<long> orderCanonicalIds,
        CancellationToken cancellationToken = default);
}
