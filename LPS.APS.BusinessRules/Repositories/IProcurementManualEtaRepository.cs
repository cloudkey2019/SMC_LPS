using LPS.APS.Core.Dto;

namespace LPS.APS.BusinessRules.Repositories;

/// <summary>
/// 采购人工ETA Repository接口
///
/// 【职责边界 - 2026-08-25新基线】
/// - 5号位负责：表结构、Repository实现、后端维护/查询接口
/// - 4号位负责：页面维护
/// - 2号位负责：读取ManualEta并计算Effective ETA/AvailableTime
///
/// 参考：APS_V1_5号位新基线增量整改开发包_v1.0_20260825.md P0-2
/// </summary>
public interface IProcurementManualEtaRepository
{
    /// <summary>
    /// 查询指定范围内的有效Manual ETA覆盖
    /// </summary>
    /// <param name="materialIds">物料ID列表（可选，null表示不过滤）</param>
    /// <param name="poNos">采购订单号列表（可选，null表示不过滤）</param>
    /// <param name="activeOnly">是否只查询有效记录（IsActive=1）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Manual ETA覆盖列表</returns>
    Task<List<ProcurementManualEtaOverride>> QueryAsync(
        List<int>? materialIds = null,
        List<string>? materialCodes = null,
        List<string>? poNos = null,
        List<string>? receivingWarehouses = null,
        DateTime? etaBefore = null,
        DateTime? etaAfter = null,
        DateTime? updatedAfter = null,
        bool activeOnly = true,
        int skip = 0,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// 根据业务键查询单条Manual ETA
    /// </summary>
    /// <param name="poNo">采购订单号</param>
    /// <param name="lineNo">行号</param>
    /// <param name="materialId">物料ID</param>
    /// <param name="receivingWarehouse">接收仓库</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Manual ETA覆盖，不存在时返回null</returns>
    Task<ProcurementManualEtaOverride?> GetByBusinessKeyAsync(
        string poNo,
        int lineNo,
        int materialId,
        string receivingWarehouse,
        CancellationToken ct = default);

    /// <summary>
    /// 新增或更新Manual ETA（按业务键Upsert）
    /// </summary>
    /// <param name="override">Manual ETA覆盖数据</param>
    /// <param name="ct">取消令牌</param>
    Task<ProcurementManualEtaOverride> UpsertAsync(ProcurementManualEtaOverride @override, CancellationToken ct = default);

    /// <summary>
    /// 取消Manual ETA（设置IsActive=0，不物理删除）
    /// </summary>
    /// <param name="poNo">采购订单号</param>
    /// <param name="lineNo">行号</param>
    /// <param name="materialId">物料ID</param>
    /// <param name="receivingWarehouse">接收仓库</param>
    /// <param name="updatedBy">操作人</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功取消（false表示记录不存在）</returns>
    Task<bool> CancelAsync(
        string poNo,
        int lineNo,
        int materialId,
        string receivingWarehouse,
        string updatedBy,
        CancellationToken ct = default);
}
