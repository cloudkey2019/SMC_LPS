using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// BOM展开结果接货服务（2号位职责 — 2.3.4 批量接货）
/// 
/// 时序：夜间批次 Step 4（全量订单同步 → 创建PlanVersion → 订单装载 → 【BOM接货】）
/// 
/// 【校验红线】（来自防腐层设计 §2.3.4）：
///   ✅ 必须校验 BatchNo 匹配当前批次
///   ✅ 必须校验 Status = 'READY'（展开已完成）
///   ✅ 必须校验 ExpandedRowCount > 0（有展开结果）
///   ❌ 不得只按时间最新一批拉取
/// 
/// 数据路径：
///   ODS: MES_APS_BOM_Workset → 流式 DbDataReader
///   → SqlBulkCopy（BatchSize=10000, Timeout=600s）
///   → APS: APS_BOM_RAW
///   → APS: sp_CalculateLLC 计算低阶码（§2.4.1）
///   → ODS: MES_APS_BOM_Workset_StageDetail → APS: APS_BOM_STAGE_PATH_RAW（v5.0.7同批次拉取）
///   → APS: OrderBomRequestLink 生成（v5.0.31 Order→BOM追溯链闭合）
///   → ODS: MES_API_BOM_Request.Status = 'CONSUMED'
/// </summary>
public class BOMResultPullService : IBOMResultPullService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<BOMResultPullService> _logger;

    public BOMResultPullService(
        DatabaseConnectionManager connectionManager,
        ILogger<BOMResultPullService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> FindReadyBatchAsync(CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT TOP 1 BatchNo
            FROM MES_API_BOM_Request
            WHERE Status = 'READY'
              AND ExpandedRowCount > 0
            ORDER BY CompletedAt DESC";

        return await _connectionManager.QueryFirstOrDefaultAsync<string>(sql, db: DatabaseId.ODS);
    }

    /// <inheritdoc />
    public async Task<int> PullBOMResultFromODSAsync(string batchNo, int planVersionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("BOM展开结果接货开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // ═══════════════════════════════════════════
        // Step 1: 校验批次状态（校验红线）
        // ═══════════════════════════════════════════
        var request = await GetBOMRequestStatusAsync(batchNo);

        if (request == null)
            throw new InvalidOperationException($"BOM批次不存在: {batchNo}");

        if (request.Status != "READY")
            throw new InvalidOperationException($"BOM批次状态异常: {request.Status}，预期: READY (BatchNo={batchNo})");

        if ((request.ExpandedRowCount ?? 0) <= 0)
            throw new InvalidOperationException($"BOM批次展开结果为空: ExpandedRowCount={request.ExpandedRowCount} (BatchNo={batchNo})");

        _logger.LogInformation(
            "批次校验通过: BatchNo={BatchNo}, Status={Status}, ExpandedRowCount={ExpandedRowCount}",
            batchNo, request.Status, request.ExpandedRowCount);

        try
        {
            // ═══════════════════════════════════════════
            // Step 2: 清空 APS_BOM_RAW 全表（排程只用最新批次，历史不保留）
            // ═══════════════════════════════════════════
            await _connectionManager.ExecuteAsync(
                "TRUNCATE TABLE APS_BOM_RAW",
                db: DatabaseId.APS);

            // ═══════════════════════════════════════════
            // Step 3: 流式拉取 ODS → APS（DbDataReader → SqlBulkCopy）
            // ⚠️ 全程流式处理，百万行不膨胀内存
            // ═══════════════════════════════════════════
            var sourceSql = @"
                SELECT 
                    BatchNo,
                    BOMNO,
                    ParentMaterialCode,
                    ChildMaterialCode,
                    Quantity,
                    Level,
                    ChildRequiredStageCode,
                    ChildRequiredFactory
                FROM MES_APS_BOM_Workset
                WHERE BatchNo = @BatchNo
                ORDER BY Level, ParentMaterialCode";

            var columnMappings = new Dictionary<string, string>
            {
                ["BatchNo"] = "BatchNo",
                ["BOMNO"] = "BOMNO",
                ["ParentMaterialCode"] = "ParentMaterialCode",
                ["ChildMaterialCode"] = "ChildMaterialCode",
                ["Quantity"] = "Quantity",
                ["Level"] = "Level",
                ["ChildRequiredStageCode"] = "ChildRequiredStageCode",
                ["ChildRequiredFactory"] = "ChildRequiredFactory"
            };

            await _connectionManager.BulkCopyFromReaderAsync(
                sourceSql: sourceSql,
                sourceParameters: new { BatchNo = batchNo },
                sourceDb: DatabaseId.ODS,
                destinationTable: "APS_BOM_RAW",
                destinationDb: DatabaseId.APS,
                columnMappings: columnMappings,
                batchSize: 10000,
                timeoutSeconds: 600);

            // ═══════════════════════════════════════════
            // Step 4: 验证拉取行数
            // ═══════════════════════════════════════════
            var pulledCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM APS_BOM_RAW WHERE BatchNo = @BatchNo",
                new { BatchNo = batchNo },
                db: DatabaseId.APS);

            if (pulledCount != request.ExpandedRowCount)
            {
                _logger.LogWarning(
                    "拉取行数与预期不完全匹配: 实际={PulledCount}, 预期={ExpectedCount} (BatchNo={BatchNo})",
                    pulledCount, request.ExpandedRowCount, batchNo);
            }

            // ═══════════════════════════════════════════
            // Step 5: 计算低阶码（§2.4.1 sp_CalculateLLC）
            // ═══════════════════════════════════════════
            _logger.LogInformation("LLC计算开始: BatchNo={BatchNo}", batchNo);
            var llcResult = await CalculateLLCAsync(batchNo);
            _logger.LogInformation(
                "LLC计算完成: BatchNo={BatchNo}, 最大层级={MaxLevel}, 叶子节点={LeafCount}, 总行数={TotalRows}",
                batchNo, llcResult.MaxLevel, llcResult.LeafCount, llcResult.TotalRows);

            // ═══════════════════════════════════════════
            // Step 5b: 拉取 StageDetail → APS_BOM_STAGE_PATH_RAW（v5.0.7同批次）
            // ═══════════════════════════════════════════
            await PullStageDetailAsync(batchNo);

            // ═══════════════════════════════════════════
            // Step 5c: 生成 OrderBomRequestLink（v5.0.31 Order→BOM追溯链闭合）
            // ═══════════════════════════════════════════
            await GenerateOrderBomRequestLinkAsync(batchNo, planVersionId);

            // ═══════════════════════════════════════════
            // Step 6: 更新ODS批次状态为 CONSUMED
            // ═══════════════════════════════════════════
            await _connectionManager.ExecuteAsync(
                "UPDATE MES_API_BOM_Request SET Status = 'CONSUMED' WHERE BatchNo = @BatchNo",
                new { BatchNo = batchNo },
                db: DatabaseId.ODS);

            stopwatch.Stop();
            _logger.LogInformation(
                "BOM接货+LLC计算完成: BatchNo={BatchNo}, 行数={PulledCount}, 最大层级={MaxLevel}, 叶子={LeafCount}, 耗时={Elapsed}ms",
                batchNo, pulledCount, llcResult.MaxLevel, llcResult.LeafCount, stopwatch.ElapsedMilliseconds);

            return pulledCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BOM展开结果接货失败: BatchNo={BatchNo}", batchNo);
            throw;
        }
    }

    /// <summary>
    /// 拉取 StageDetail 阶段路径数据到 APS_BOM_STAGE_PATH_RAW（v5.0.7新增，与 APS_BOM_RAW 同批次）
    /// 数据来源：ODS库 MES_APS_BOM_Workset_StageDetail（含 EDGE + ROOT 两类记录）
    /// </summary>
    private async Task PullStageDetailAsync(string batchNo)
    {
        _logger.LogInformation("StageDetail拉取开始: BatchNo={BatchNo}", batchNo);

        // 清空全表（与 APS_BOM_RAW 同策略，只保留最新批次）
        await _connectionManager.ExecuteAsync(
            "TRUNCATE TABLE APS_BOM_STAGE_PATH_RAW",
            db: DatabaseId.APS);

        var sourceSql = @"
            SELECT 
                BatchNo,
                BOMNO,
                StageScopeType,
                ParentMaterialCode,
                ChildMaterialCode,
                StageSeq,
                StageCode,
                IsSupplyThreshold
            FROM MES_APS_BOM_Workset_StageDetail
            WHERE BatchNo = @BatchNo
            ORDER BY ChildMaterialCode, StageSeq";

        var columnMappings = new Dictionary<string, string>
        {
            ["BatchNo"] = "BatchNo",
            ["BOMNO"] = "BOMNO",
            ["StageScopeType"] = "StageScopeType",
            ["ParentMaterialCode"] = "ParentMaterialCode",
            ["ChildMaterialCode"] = "ChildMaterialCode",
            ["StageSeq"] = "StageSeq",
            ["StageCode"] = "StageCode",
            ["IsSupplyThreshold"] = "IsSupplyThreshold"
        };

        await _connectionManager.BulkCopyFromReaderAsync(
            sourceSql: sourceSql,
            sourceParameters: new { BatchNo = batchNo },
            sourceDb: DatabaseId.ODS,
            destinationTable: "APS_BOM_STAGE_PATH_RAW",
            destinationDb: DatabaseId.APS,
            columnMappings: columnMappings,
            batchSize: 10000,
            timeoutSeconds: 600);

        var pulledCount = await _connectionManager.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM APS_BOM_STAGE_PATH_RAW WHERE BatchNo = @BatchNo",
            new { BatchNo = batchNo },
            db: DatabaseId.APS);

        _logger.LogInformation("StageDetail拉取完成: BatchNo={BatchNo}, 行数={PulledCount}", batchNo, pulledCount);
    }

    /// <summary>
    /// 调用 sp_CalculateLLC 计算低阶码（§2.4.1）
    /// 在 APS 本地库执行，仅针对当批 APS_BOM_RAW 活跃工作集
    /// </summary>
    private async Task<(int MaxLevel, int LeafCount, int TotalRows)> CalculateLLCAsync(string batchNo)
    {
        var spParams = new DynamicParameters();
        spParams.Add("@BatchNo", batchNo);
        spParams.Add("@MaxLevel", dbType: DbType.Int32, direction: ParameterDirection.Output);
        spParams.Add("@LeafCount", dbType: DbType.Int32, direction: ParameterDirection.Output);
        spParams.Add("@TotalRows", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await _connectionManager.ExecuteAsync(
            "sp_CalculateLLC",
            spParams,
            CommandType.StoredProcedure,
            DatabaseId.APS,
            commandTimeout: 600);

        return (
            spParams.Get<int>("@MaxLevel"),
            spParams.Get<int>("@LeafCount"),
            spParams.Get<int>("@TotalRows")
        );
    }

    /// <summary>
    /// 查询ODS库的BOM批次状态
    /// </summary>
    private async Task<BOMRequestStatusDto?> GetBOMRequestStatusAsync(string batchNo)
    {
        var sql = @"
            SELECT BatchNo, Status, RootCount, ExpandedRowCount, CreatedAt, CompletedAt
            FROM MES_API_BOM_Request
            WHERE BatchNo = @BatchNo";

        return await _connectionManager.QueryFirstOrDefaultAsync<BOMRequestStatusDto>(
            sql, new { BatchNo = batchNo }, db: DatabaseId.ODS);
    }

    /// <summary>
    /// Step 5c: 生成 OrderBomRequestLink（v5.0.31）
    /// 数据源：ODS.MES_API_BOM_Request_Detail + ODS.MES_APS_BOM_Workset
    /// 映射：APS.[Order] 按 PlanVersionId + OrderCanonicalId 查找 OrderId
    /// </summary>
    private async Task GenerateOrderBomRequestLinkAsync(string batchNo, int planVersionId)
    {
        _logger.LogInformation("OrderBomRequestLink生成开始: BatchNo={BatchNo}, PlanVersionId={PlanVersionId}",
            batchNo, planVersionId);

        // 1. 从 ODS 获取 RequestDetail 信息
        var detailSql = @"
            SELECT
                d.Id AS RequestDetailId,
                d.OrderCanonicalId,
                d.OrderNo,
                d.SourceSystem,
                d.SourceOrderId,
                d.RequestedBOMNO
            FROM MES_API_BOM_Request_Detail d
            WHERE d.BatchNo = @BatchNo";

        var details = (await _connectionManager.QueryAsync<BomLinkDetailDto>(
            detailSql, new { BatchNo = batchNo }, db: DatabaseId.ODS, commandTimeout: 120)).ToList();

        if (details.Count == 0)
        {
            _logger.LogWarning("RequestDetail为空，跳过Link生成: BatchNo={BatchNo}", batchNo);
            return;
        }

        // 2. 从 ODS Workset 按 RequestDetailId 聚合 ResolvedBOMNO + RepWorksetId
        var worksetSql = @"
            SELECT
                RequestDetailId,
                MIN(CASE WHEN Level = 1 THEN BOMNO END) AS ResolvedBOMNO,
                MIN(CASE WHEN Level = 1 THEN Id END) AS RepWorksetId
            FROM MES_APS_BOM_Workset
            WHERE BatchNo = @BatchNo
              AND RequestDetailId IS NOT NULL
            GROUP BY RequestDetailId";

        var worksetMap = (await _connectionManager.QueryAsync<BomLinkWorksetDto>(
            worksetSql, new { BatchNo = batchNo }, db: DatabaseId.ODS, commandTimeout: 120))
            .ToDictionary(w => w.RequestDetailId);

        // 3. 从 APS [Order] 按 PlanVersionId + OrderCanonicalId 查找 OrderId
        var orderSql = @"
            SELECT OrderCanonicalId, Id AS OrderId
            FROM [Order]
            WHERE PlanVersionId = @PlanVersionId
              AND OrderCanonicalId IS NOT NULL";

        var orderMap = (await _connectionManager.QueryAsync<BomLinkOrderDto>(
            orderSql, new { PlanVersionId = planVersionId }, db: DatabaseId.APS))
            .ToDictionary(o => o.OrderCanonicalId);

        // 4. 幂等保护：清理该批次旧 Link 数据
        var deletedCount = await _connectionManager.ExecuteAsync(
            "DELETE FROM OrderBomRequestLink WHERE BatchNo = @BatchNo AND PlanVersionId = @PlanVersionId",
            new { BatchNo = batchNo, PlanVersionId = planVersionId },
            db: DatabaseId.APS);

        if (deletedCount > 0)
        {
            _logger.LogWarning("清理OrderBomRequestLink旧数据: BatchNo={BatchNo}, 删除={Count}行", batchNo, deletedCount);
        }

        // 5. 组装 DataTable 批量写入
        var dataTable = new DataTable("OrderBomRequestLink");
        dataTable.Columns.Add("PlanVersionId", typeof(long));
        dataTable.Columns.Add("BatchNo", typeof(string));
        dataTable.Columns.Add("OrderId", typeof(long));
        dataTable.Columns.Add("OrderCanonicalId", typeof(long));
        dataTable.Columns.Add("OrderNo", typeof(string));
        dataTable.Columns.Add("SourceSystem", typeof(string));
        dataTable.Columns.Add("SourceOrderId", typeof(string));
        dataTable.Columns.Add("RequestDetailId", typeof(long));
        dataTable.Columns.Add("RequestedBOMNO", typeof(string));
        dataTable.Columns.Add("ResolvedBOMNO", typeof(string));
        dataTable.Columns.Add("RepWorksetId", typeof(long));
        dataTable.Columns.Add("LinkStatus", typeof(string));
        dataTable.Columns.Add("ErrorMessage", typeof(string));
        dataTable.Columns.Add("SyncedAt", typeof(DateTime));

        var now = DateTime.UtcNow;
        var resolvedCount = 0;
        var skippedCount = 0;
        var noBomCount = 0;

        foreach (var detail in details)
        {
            worksetMap.TryGetValue(detail.RequestDetailId, out var workset);
            orderMap.TryGetValue(detail.OrderCanonicalId, out var order);

            string linkStatus;
            string? errorMessage = null;

            if (order == null)
            {
                linkStatus = "SKIPPED";
                errorMessage = "Order not loaded into this PlanVersion";
                skippedCount++;
            }
            else if (workset?.ResolvedBOMNO != null)
            {
                linkStatus = "RESOLVED";
                resolvedCount++;
            }
            else
            {
                linkStatus = "NO_BOM";
                noBomCount++;
            }

            dataTable.Rows.Add(
                (long)planVersionId,
                batchNo,
                order != null ? (object)order.OrderId : DBNull.Value,
                detail.OrderCanonicalId,
                (object?)detail.OrderNo ?? DBNull.Value,
                (object?)detail.SourceSystem ?? DBNull.Value,
                (object?)detail.SourceOrderId ?? DBNull.Value,
                detail.RequestDetailId,
                (object?)detail.RequestedBOMNO ?? DBNull.Value,
                (object?)workset?.ResolvedBOMNO ?? DBNull.Value,
                workset?.RepWorksetId != null ? (object)workset.RepWorksetId : DBNull.Value,
                linkStatus,
                (object?)errorMessage ?? DBNull.Value,
                now);
        }

        await _connectionManager.BulkInsertAsync(dataTable, "OrderBomRequestLink", DatabaseId.APS);

        _logger.LogInformation(
            "OrderBomRequestLink生成完成: BatchNo={BatchNo}, 总数={Total}, RESOLVED={Resolved}, NO_BOM={NoBom}, SKIPPED={Skipped}",
            batchNo, details.Count, resolvedCount, noBomCount, skippedCount);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 内部 DTO（仅本类使用）
    // ═══════════════════════════════════════════════════════════════════════════

    private class BomLinkDetailDto
    {
        public long RequestDetailId { get; set; }
        public long OrderCanonicalId { get; set; }
        public string? OrderNo { get; set; }
        public string? SourceSystem { get; set; }
        public string? SourceOrderId { get; set; }
        public string? RequestedBOMNO { get; set; }
    }

    private class BomLinkWorksetDto
    {
        public long RequestDetailId { get; set; }
        public string? ResolvedBOMNO { get; set; }
        public long? RepWorksetId { get; set; }
    }

    private class BomLinkOrderDto
    {
        public long OrderCanonicalId { get; set; }
        public long OrderId { get; set; }
    }
}
