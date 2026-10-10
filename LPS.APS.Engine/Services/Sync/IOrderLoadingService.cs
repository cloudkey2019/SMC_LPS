namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 订单装载服务接口（2号位职责）
/// 从 Order_Canonical 装载到 Order 分区表（sp_SyncOrdersToPartitionTable）
/// 调用时机：每天 00:05（夜间批次）或手动触发
/// </summary>
public interface IOrderLoadingService
{
    /// <summary>
    /// 将 Order_Canonical 中活跃订单装载到 Order 分区表
    /// </summary>
    /// <param name="planVersionId">排程计划版本ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>装载的订单数量</returns>
    Task<int> LoadOrdersToPartitionTableAsync(int planVersionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 【白天候选】把活跃订单增量压进当前所有 ACTIVE PlanVersion 的 Order 分区表。
    /// 场景：白天 ERP 每小时同步后调用——新订单进 Order_Canonical 后，需同步压进 ACTIVE PV，
    /// 用户才能从订单表选单做插单试排（PM 0923「白天 2号位 自己写 [Order]」）。
    /// 幂等：底层 sp_SyncOrdersToPartitionTable 有 NOT EXISTS(OrderNo, PlanVersionId) 保护，重复调用只补增量。
    /// 无 ACTIVE PlanVersion 时跳过（WARN 日志，不抛异常）；单 PV 失败不影响其它 PV。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本次新增装载的订单数合计</returns>
    Task<int> LoadOrdersToActivePlanVersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 归域失败捡漏：逐 Domain 装载后，检测本 Run 全部 PlanVersion 均未装走的活跃（Open/Released）订单。
    /// 这类订单因 ProductFamilyId 缺失 / 未映射到任何有效 DomainDefinition 而落空，
    /// 登记 APS_ETL_Log（WARN）标记为数据问题，供人工排查。非阻塞：不抛异常，仅登记日志。
    /// </summary>
    /// <param name="planVersionIds">本 Run 全部 Domain 的 PlanVersionId 集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>未归域订单数</returns>
    Task<int> DetectUnassignedOrdersAsync(IReadOnlyList<int> planVersionIds, CancellationToken cancellationToken = default);
}
