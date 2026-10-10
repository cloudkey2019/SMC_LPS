using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 白天候选 BOM 实时展开服务（2号位职责）。
/// 链路与口径见 <see cref="IBomRealtimeExpandService"/> 接口注释。
///
/// 【跨库说明】Order_Canonical 在 APS 库；MES_API_BOM_Request_Detail 与展开结果在 ODS 库。
/// 故「读订单」在 APS、「写 Detail / 调 SP / 读结果」在 ODS，分两步完成。
/// </summary>
public class BomRealtimeExpandService : IBomRealtimeExpandService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<BomRealtimeExpandService> _logger;

    public BomRealtimeExpandService(
        DatabaseConnectionManager connectionManager,
        ILogger<BomRealtimeExpandService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BomRealtimeExpandResult> EnsureExpandedAsync(
        IReadOnlyList<long> orderCanonicalIds,
        CancellationToken cancellationToken = default)
    {
        if (orderCanonicalIds.Count == 0)
            return new BomRealtimeExpandResult();

        var ids = orderCanonicalIds.Distinct().ToList();
        var issues = new List<string>();

        // 白天独立批次号（5号位 回执 加护3：不得复用夜间批次号，否则 (BatchNo, OrderCanonicalId) 唯一约束冲突）
        var daytimeBatchNo = "RT:" + DateTime.Now.ToString("yyyyMMdd");

        // ── 步骤1：查本白天批次已存在的 Detail 行（幂等：已存在的跳过）──
        var existingDetailIds = (await _connectionManager.QueryAsync<RequestDetailRow>(
            @"SELECT Id, OrderCanonicalId
              FROM MES_API_BOM_Request_Detail
              WHERE BatchNo = @BatchNo AND OrderCanonicalId IN @Ids",
            new { BatchNo = daytimeBatchNo, Ids = ids },
            db: DatabaseId.ODS)).ToList();

        var existingOcIds = existingDetailIds.Select(r => r.OrderCanonicalId).ToHashSet();
        var missingOcIds = ids.Where(id => !existingOcIds.Contains(id)).ToList();

        // ── 步骤1.5：确保白天批次头存在 ──
        // ⚠️ 实测约束：MES_API_BOM_Request_Detail.BatchNo 有 FK 指向 MES_API_BOM_Request.BatchNo
        //    （FK__MES_API_B__Batch__27F8EE98）⇒ 只写 Detail 会 FK 冲突，必须先建批次头。
        //    该头仅为满足 FK 锚定 + 批次审计；白天的实际请求/状态载体是 MES_API_BOM_Request_Realtime（SP 内部自写）。
        if (missingOcIds.Count > 0)
        {
            var headerExists = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM MES_API_BOM_Request WHERE BatchNo = @BatchNo",
                new { BatchNo = daytimeBatchNo },
                db: DatabaseId.ODS);

            if (headerExists == 0)
            {
                await _connectionManager.ExecuteAsync(
                    @"INSERT INTO MES_API_BOM_Request (BatchNo, Status, RootCount, CreatedAt, RetryCount)
                      VALUES (@BatchNo, 'PENDING', 0, GETDATE(), 0)",
                    new { BatchNo = daytimeBatchNo },
                    db: DatabaseId.ODS);

                _logger.LogInformation("白天 BOM 实时展开：已建白天批次头 BatchNo={BatchNo}（FK 锚定用）", daytimeBatchNo);
            }
        }

        // ── 步骤2：补写缺失的 Detail（从 APS 的 Order_Canonical 取业务字段）──
        var inserted = 0;
        if (missingOcIds.Count > 0)
        {
            var orderRows = (await _connectionManager.QueryAsync<OrderCanonicalDetailRow>(
                @"SELECT Id AS OrderCanonicalId, OrderNo, SourceSystem, SourceOrderId,
                         MaterialCode, FactoryCode, OrderType, BOMNO AS RequestedBOMNO
                  FROM Order_Canonical
                  WHERE Id IN @Ids",
                new { Ids = missingOcIds },
                db: DatabaseId.APS)).ToList();

            foreach (var o in orderRows)
            {
                // 加护2：MaterialCode / FactoryCode / OrderType 必须填全（RequestedBOMNO 可空，为空则 SP 走物料路由）
                await _connectionManager.ExecuteAsync(
                    @"INSERT INTO MES_API_BOM_Request_Detail
                        (BatchNo, OrderCanonicalId, OrderNo, SourceSystem, SourceOrderId,
                         MaterialCode, FactoryCode, OrderType, RequestedBOMNO, CreatedAt)
                      VALUES
                        (@BatchNo, @OrderCanonicalId, @OrderNo, @SourceSystem, @SourceOrderId,
                         @MaterialCode, @FactoryCode, @OrderType, @RequestedBOMNO, GETDATE())",
                    new
                    {
                        BatchNo = daytimeBatchNo,
                        o.OrderCanonicalId,
                        o.OrderNo,
                        o.SourceSystem,
                        o.SourceOrderId,
                        o.MaterialCode,
                        o.FactoryCode,
                        o.OrderType,
                        o.RequestedBOMNO
                    },
                    db: DatabaseId.ODS);
                inserted++;
            }

            _logger.LogInformation(
                "白天 BOM 实时展开：补写 MES_API_BOM_Request_Detail {Inserted} 行（BatchNo={BatchNo}，请求 {Requested} 单，已存在 {Existing} 单）",
                inserted, daytimeBatchNo, ids.Count, existingOcIds.Count);
        }

        // ── 步骤3：取全部 DetailId（含已存在 + 刚补写），逐个调实时展开 SP ──
        var allDetails = (await _connectionManager.QueryAsync<RequestDetailRow>(
            @"SELECT Id, OrderCanonicalId
              FROM MES_API_BOM_Request_Detail
              WHERE BatchNo = @BatchNo AND OrderCanonicalId IN @Ids",
            new { BatchNo = daytimeBatchNo, Ids = ids },
            db: DatabaseId.ODS)).ToList();

        var expanded = 0;
        foreach (var d in allDetails)
        {
            try
            {
                // @BOMNO 传 NULL：SP 内部按 @RequestDetailId 反查 Detail 取 RequestedBOMNO/MaterialCode/FactoryCode/OrderType
                // SP 幂等：该 Detail 已 READY 则直接 RETURN（不重复展开）
                await _connectionManager.ExecuteAsync(
                    "sp_ExpandBOMRealtime_vNext",
                    new { BOMNO = (string?)null, RequestDetailId = d.Id },
                    CommandType.StoredProcedure,
                    DatabaseId.ODS);
                expanded++;
            }
            catch (Exception ex)
            {
                // 加护1：SP 前置校验（MES_BOM_Edge_RefreshLog 非 COMPLETED）会 RAISERROR
                //        → 捕获转可解释 Issue，**不得作为硬失败打断白天候选**
                issues.Add($"订单 {d.OrderCanonicalId}（RequestDetailId={d.Id}）实时展开失败：{ex.Message}");
                _logger.LogWarning(ex, "白天 BOM 实时展开失败：OrderCanonicalId={OcId}, RequestDetailId={DetailId}",
                    d.OrderCanonicalId, d.Id);
            }
        }

        // ── 步骤4：三重校验（5号位 回执 §3.3：不能只看 Status='READY'，实时链路「永不阻塞、状态机永远 READY」，0 行也 READY）──
        var detailIds = allDetails.Select(d => d.Id).ToList();

        // 校验①：以**结果表实际行数**为准。
        // 注：_Realtime.ExpandedRowCount 实测在实时链路为 NULL（5号位 SP 未回填），不可依赖。
        var rowCounts = (await _connectionManager.QueryAsync<RealtimeRowCountRow>(
            @"SELECT RequestDetailId, COUNT(*) AS [RowCount]
              FROM MES_APS_BOM_Workset_Realtime
              WHERE RequestDetailId IN @DetailIds
              GROUP BY RequestDetailId",
            new { DetailIds = detailIds },
            db: DatabaseId.ODS)).ToList();

        var rowCountByDetailId = rowCounts.ToDictionary(r => r.RequestDetailId, r => r.RowCount);
        var expandedRowCount = rowCounts.Sum(r => r.RowCount);

        foreach (var d in allDetails)
        {
            var rows = rowCountByDetailId.TryGetValue(d.Id, out var n) ? n : 0;
            if (rows <= 0)
                issues.Add($"订单 {d.OrderCanonicalId}（RequestDetailId={d.Id}）展开 0 行（该订单待人工核实 BOM/工厂）");
        }

        // 校验②③：_Realtime 状态面（ErrorMessage 非空 / 无状态行 = 展开未执行）
        var realtimeStatus = (await _connectionManager.QueryAsync<RealtimeStatusRow>(
            @"SELECT RequestDetailId, Status, ErrorMessage
              FROM MES_API_BOM_Request_Realtime
              WHERE RequestDetailId IN @DetailIds",
            new { DetailIds = detailIds },
            db: DatabaseId.ODS)).ToList();

        foreach (var st in realtimeStatus)
        {
            if (!string.IsNullOrWhiteSpace(st.ErrorMessage))
                issues.Add($"订单（RequestDetailId={st.RequestDetailId}）展开报错：{st.ErrorMessage}");
        }

        var statusDetailIds = realtimeStatus.Select(r => r.RequestDetailId).ToHashSet();
        foreach (var d in allDetails.Where(d => !statusDetailIds.Contains(d.Id)))
            issues.Add($"订单 {d.OrderCanonicalId}（RequestDetailId={d.Id}）无实时展开状态行（展开未执行）");

        _logger.LogInformation(
            "白天 BOM 实时展开完成：请求={Requested}，补写Detail={Inserted}，已执行展开={Expanded}，结果行数={Rows}，Issue={Issues}",
            ids.Count, inserted, expanded, expandedRowCount, issues.Count);

        return new BomRealtimeExpandResult
        {
            RequestedOrders = ids.Count,
            DetailInserted = inserted,
            ExpandedOrders = expanded,
            ExpandedRowCount = expandedRowCount,
            Issues = issues
        };
    }

    private sealed class OrderCanonicalDetailRow
    {
        public long OrderCanonicalId { get; set; }
        public string? OrderNo { get; set; }
        public string? SourceSystem { get; set; }
        public string? SourceOrderId { get; set; }
        public string? MaterialCode { get; set; }
        public string? FactoryCode { get; set; }
        public string? OrderType { get; set; }
        public string? RequestedBOMNO { get; set; }
    }

    private sealed class RequestDetailRow
    {
        public long Id { get; set; }
        public long OrderCanonicalId { get; set; }
    }

    private sealed class RealtimeStatusRow
    {
        public long RequestDetailId { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>实时展开结果行数（按 RequestDetailId 分组，替代不可依赖的 _Realtime.ExpandedRowCount）</summary>
    private sealed class RealtimeRowCountRow
    {
        public long RequestDetailId { get; set; }
        public int RowCount { get; set; }
    }
}
