using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
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
///   → SqlBulkCopy（BatchSize=10000, Timeout=<see cref="BulkCopyTimeoutSeconds"/>s）
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

    /// <summary>
    /// 跨机流式 BulkCopy 超时（秒）。
    /// 【2026-10-08 修复】原为两处写死的 <c>600</c>，而接货是**跨三机**搬运：
    /// ODS(10.116.2.73) 流式读 → 本机中转 → APS(10.116.2.75) 写。
    /// <b>2026-10-08 用户已将 APS_Production 改为 SIMPLE 恢复模式</b>（原 FULL）——
    /// 下文「回滚跑几分钟」的描述针对的是当时 FULL 的实况，现已缓解，但**回滚顶替异常的代码缺陷
    /// 与恢复模式无关，仍然必须修**（见 <c>DatabaseConnectionManager.ExecuteInTransactionAsync</c>）。
    /// <para>
    /// <b>⚠️ 2026-10-08 更正</b>：本常量原先由 600 抬到 1800，依据是「实测 7.4k 行/秒、600s 被撞满」——
    /// **该依据已被实测证伪**。加桩重测（见 <c>LogBulkCopyThroughput</c> 输出）真实吞吐为：
    /// <list type="bullet">
    /// <item>APS_BOM_RAW：4,425,669 行 / 124.9s = <b>35,435 行/秒</b>；</item>
    /// <item>APS_BOM_STAGE_PATH_RAW：2,384,758 行 / 38.1s = <b>62,623 行/秒</b>。</item>
    /// </list>
    /// 按此速率 600s 可覆盖约 2,100 万行，**当时根本没有触及 BulkCopy 上限**。那次失败的真实原因
    /// 是 <c>GenerateOrderBomRequestLinkAsync</c> 的 <c>ToDictionary</c> 重复键（业务异常，毫秒即抛），
    /// 被随后的巨额回滚超时顶替，才伪装成「接货超时」（详见该方法内注释）。
    /// </para>
    /// <para>
    /// 因此 <b>1800s 不再作为「修复」保留，而仅作为安全余量</b>：批次规模随手订单量增长，
    /// 留 4~5 倍当前批量（约 6,400 万行）的余量，避免将来真撞限时又是「整批回滚」的代价。
    /// ⚠️ 若后续批量继续放大到逼近 1800s，正解是改走**服务端到服务端**搬运
    /// （APS 经 <c>[mes]</c> 链接服务器直插，省掉本机中转），而非继续抬上限。
    /// </para>
    /// </summary>
    private const int BulkCopyTimeoutSeconds = 1800;

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
    public async Task<int> PullBOMResultFromODSAsync(string batchNo, IReadOnlyList<int> planVersionIds, CancellationToken cancellationToken = default)
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

        // ═══════════════════════════════════════════
        // Step 2-5c 前置：源 SQL 与列映射（纯声明，事务内外皆可用）
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

        try
        {
            var llcResult = (MaxLevel: 0, LeafCount: 0, TotalRows: 0);

            // ═══════════════════════════════════════════
            // Step 2-5c：清空 + 回填 + LLC + StageDetail + Link 全部包在一个 APS 事务内。
            // 任一步失败整体回滚，不残留「已 TRUNCATE 未回填」的空表（原实现无事务，失败即空）。
            // ⚠️ 事务持有 APS 信号量期间，禁止再经 _connectionManager 以 db:APS 取连接（同库信号量重入会死锁）：
            //    APS 读写全部直接走事务连接；ODS 读仍走连接管理器（异库信号量，不冲突）。
            // ═══════════════════════════════════════════
            var pulledCount = await _connectionManager.ExecuteInTransactionAsync(async (connection, transaction) =>
            {
                var sqlConn = (SqlConnection)connection;
                var sqlTx = (SqlTransaction)transaction;

                // Step 2: 清空 APS_BOM_RAW 全表（排程只用最新批次，历史不保留；事务内可回滚）
                await sqlConn.ExecuteAsync("TRUNCATE TABLE APS_BOM_RAW", transaction: sqlTx);

                // Step 3: 流式拉取 ODS → APS（DbDataReader → SqlBulkCopy，参与事务）
                var bulkSw = System.Diagnostics.Stopwatch.StartNew();
                await _connectionManager.BulkCopyFromReaderToTransactionAsync(
                    sourceSql: sourceSql,
                    sourceParameters: new { BatchNo = batchNo },
                    sourceDb: DatabaseId.ODS,
                    destinationTable: "APS_BOM_RAW",
                    destinationConnection: sqlConn,
                    destinationTransaction: sqlTx,
                    columnMappings: columnMappings,
                    batchSize: 10000,
                    timeoutSeconds: BulkCopyTimeoutSeconds);
                bulkSw.Stop();

                // Step 4: 验证拉取行数
                var pulled = await sqlConn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM APS_BOM_RAW WHERE BatchNo = @BatchNo",
                    new { BatchNo = batchNo },
                    transaction: sqlTx);

                if (pulled != request.ExpandedRowCount)
                {
                    _logger.LogWarning(
                        "拉取行数与预期不完全匹配: 实际={PulledCount}, 预期={ExpectedCount} (BatchNo={BatchNo})",
                        pulled, request.ExpandedRowCount, batchNo);
                }

                LogBulkCopyThroughput("APS_BOM_RAW", pulled, bulkSw.Elapsed, batchNo);

                // Step 5: 计算低阶码（§2.4.1 sp_CalculateLLC）
                _logger.LogInformation("LLC计算开始: BatchNo={BatchNo}", batchNo);
                llcResult = await CalculateLLCAsync(sqlConn, sqlTx, batchNo);
                _logger.LogInformation(
                    "LLC计算完成: BatchNo={BatchNo}, 最大层级={MaxLevel}, 叶子节点={LeafCount}, 总行数={TotalRows}",
                    batchNo, llcResult.MaxLevel, llcResult.LeafCount, llcResult.TotalRows);

                // Step 5b: 拉取 StageDetail → APS_BOM_STAGE_PATH_RAW（同批次，事务内）
                await PullStageDetailAsync(sqlConn, sqlTx, batchNo);

                // Step 5c: 生成 OrderBomRequestLink（v5.0.31 Order→BOM追溯链闭合）
                await GenerateOrderBomRequestLinkAsync(sqlConn, sqlTx, batchNo, planVersionIds);

                return pulled;
            }, DatabaseId.APS);

            // ═══════════════════════════════════════════
            // Step 6: 更新 ODS 批次状态为 CONSUMED（事务提交后执行；失败则批次保持 READY，下次重试自愈）
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

    /// <inheritdoc />
    public async Task<BOMIntakeResult> IntakeLatestReadyBatchAsync(IReadOnlyList<int> planVersionIds, CancellationToken cancellationToken = default)
    {
        var batchNo = await FindReadyBatchAsync(cancellationToken);

        if (batchNo == null)
        {
            _logger.LogWarning("独立接货：未找到 READY 状态的 BOM 批次，跳过接货");
            return new BOMIntakeResult { BatchNo = null, PulledCount = 0 };
        }

        _logger.LogInformation("独立接货：定位到 READY 批次 BatchNo={BatchNo}，开始接货", batchNo);
        var pulledCount = await PullBOMResultFromODSAsync(batchNo, planVersionIds, cancellationToken);
        _logger.LogInformation("独立接货完成: BatchNo={BatchNo}, 行数={PulledCount}", batchNo, pulledCount);

        return new BOMIntakeResult { BatchNo = batchNo, PulledCount = pulledCount };
    }

    /// <summary>
    /// 拉取 StageDetail 阶段路径数据到 APS_BOM_STAGE_PATH_RAW（v5.0.7新增，与 APS_BOM_RAW 同批次）
    /// 数据来源：ODS库 MES_APS_BOM_Workset_StageDetail（含 EDGE + ROOT 两类记录）
    /// </summary>
    private async Task PullStageDetailAsync(SqlConnection connection, SqlTransaction transaction, string batchNo)
    {
        _logger.LogInformation("StageDetail拉取开始: BatchNo={BatchNo}", batchNo);

        // 清空全表（与 APS_BOM_RAW 同策略，只保留最新批次；事务内可回滚）
        await connection.ExecuteAsync("TRUNCATE TABLE APS_BOM_STAGE_PATH_RAW", transaction: transaction);

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

        var bulkSw = System.Diagnostics.Stopwatch.StartNew();
        await _connectionManager.BulkCopyFromReaderToTransactionAsync(
            sourceSql: sourceSql,
            sourceParameters: new { BatchNo = batchNo },
            sourceDb: DatabaseId.ODS,
            destinationTable: "APS_BOM_STAGE_PATH_RAW",
            destinationConnection: connection,
            destinationTransaction: transaction,
            columnMappings: columnMappings,
            batchSize: 10000,
            timeoutSeconds: BulkCopyTimeoutSeconds);
        bulkSw.Stop();

        var pulledCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM APS_BOM_STAGE_PATH_RAW WHERE BatchNo = @BatchNo",
            new { BatchNo = batchNo },
            transaction: transaction);

        LogBulkCopyThroughput("APS_BOM_STAGE_PATH_RAW", pulledCount, bulkSw.Elapsed, batchNo);
        _logger.LogInformation("StageDetail拉取完成: BatchNo={BatchNo}, 行数={PulledCount}", batchNo, pulledCount);
    }

    /// <summary>
    /// 输出跨机 BulkCopy 的吞吐（行/秒）与预估「多少行会撞 <see cref="BulkCopyTimeoutSeconds"/>」。
    /// 【2026-10-08 新增】起因：写死 600s 时，4,425,669 行的批次恰好撞线整批回滚，而事后无任何
    /// 吞吐数字可复盘。此日志让「批次长到多大就会超时」变成可测的，而不是下次再撞。
    /// </summary>
    private void LogBulkCopyThroughput(string table, long rows, TimeSpan elapsed, string batchNo)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.001);
        var rowsPerSec = rows / seconds;
        var rowsAtTimeout = rowsPerSec * BulkCopyTimeoutSeconds;

        _logger.LogInformation(
            "BulkCopy 吞吐: 表={Table}, 行数={Rows}, 耗时={Elapsed:F1}s, {RowsPerSec:F0}行/秒, " +
            "按此速率 {Timeout}s 上限对应约 {RowsAtTimeout:F0} 行 (BatchNo={BatchNo})",
            table, rows, seconds, rowsPerSec, BulkCopyTimeoutSeconds, rowsAtTimeout, batchNo);
    }

    /// <summary>
    /// 调用 sp_CalculateLLC 计算低阶码（§2.4.1）
    /// 在 APS 本地库执行，仅针对当批 APS_BOM_RAW 活跃工作集
    /// </summary>
    private async Task<(int MaxLevel, int LeafCount, int TotalRows)> CalculateLLCAsync(
        SqlConnection connection, SqlTransaction transaction, string batchNo)
    {
        var spParams = new DynamicParameters();
        spParams.Add("@BatchNo", batchNo);
        spParams.Add("@MaxLevel", dbType: DbType.Int32, direction: ParameterDirection.Output);
        spParams.Add("@LeafCount", dbType: DbType.Int32, direction: ParameterDirection.Output);
        spParams.Add("@TotalRows", dbType: DbType.Int32, direction: ParameterDirection.Output);

        await connection.ExecuteAsync(
            "sp_CalculateLLC",
            spParams,
            commandType: CommandType.StoredProcedure,
            transaction: transaction,
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
    /// 映射：APS.[Order] 跨本批全部 Domain PlanVersion（每顶层 Order 只归一个真实 Domain）按 OrderCanonicalId 查找 OrderId + PlanVersionId
    /// </summary>
    private async Task GenerateOrderBomRequestLinkAsync(
        SqlConnection connection, SqlTransaction transaction, string batchNo, IReadOnlyList<int> planVersionIds)
    {
        _logger.LogInformation("OrderBomRequestLink生成开始: BatchNo={BatchNo}, PlanVersionCount={PlanVersionCount}",
            batchNo, planVersionIds.Count);

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

        // 3. 从 APS [Order] 跨本批全部 Domain PlanVersion 按 OrderCanonicalId 查找 OrderId + 所属 PlanVersionId
        var orderSql = @"
            SELECT OrderCanonicalId, Id AS OrderId, PlanVersionId
            FROM [Order]
            WHERE PlanVersionId IN @PlanVersionIds
              AND OrderCanonicalId IS NOT NULL";

        // ⚠️ 必须用 ToLookup 而**不能**用 ToDictionary：同一 OrderCanonicalId 会**跨 PlanVersion 重复出现**。
        //    2026-10-08 实测（本批 DAILY_BASELINE = PV1~PV4）：PV1/PV2 各 22,144 行、去重后正好
        //    22,144 个 OC —— 每个 OC 恰好 ×2；同一 (OC, PV) 内部重复 = 0。
        //    ★ 用 ToDictionary 会在建字典这一步**立刻抛 ArgumentException（重复键）**。
        //      这正是 2026-10-08 接货两次「接货超时」的真实根因：异常发生在一瞬间，但随后的
        //      catch 要回滚 680 万行插入（当时目标库还是 FULL 恢复、日志近满），回滚自身超时并**顶替**了原始异常，
        //      对外只剩一条只有 Rollback 栈的 SqlException —— 伪装的「超时」（该 catch 已一并修复）。
        //    OrderBomRequestLink 的冻结唯一键正是 (PlanVersionId, OrderCanonicalId)（DDL v5.0.34）,
        //    ⇒「每个命中分区各落一行」既满足唯一约束，又不丢任何分区信息（1 明细 → 2 行）。
        //
        //    ❓ 待定（非本类可自决，已另行提报）：同一订单为何会跨两个分区，**成因尚未定盘**。
        //       实测 DomainDefinition 中同时存在两个 **同 scope、同 ProductFamilyId** 的启用域：
        //         FAMILY2  (Id=373, CreatedBy='admin',       2026-09-29 03:10, scope=FAMILY, PF=1, Fty=NULL, SortOrder=3)
        //         FAMILY_X (Id=1,   CreatedBy='3号位-seed', 2026-09-02 11:58, scope=FAMILY, PF=1, Fty=NULL, SortOrder=99)
        //       订单按 ProductFamilyId(=1) 归域 ⇒ 两个域都会命中，遂各出一份分区。
        //       冻结基线 v1.8 未给域清单，仅规定「根据当前有效 DomainDefinition 确定真实 DomainKey」⇒
        //       **表即权威**，此处只保证代码不崩、且落库满足唯一键；域去重由 3号位/PM 定夺。
        //       ⚠️ 在成因定盘前，本方法按 [Order] 的实际行数**如实镜像**（有几个分区就落几行），不做去重。
        var orderLookup = (await connection.QueryAsync<BomLinkOrderDto>(
            orderSql, new { PlanVersionIds = planVersionIds }, transaction: transaction))
            .ToLookup(o => o.OrderCanonicalId);

        // 4. 幂等保护：清理该批次旧 Link 数据
        var deletedCount = await connection.ExecuteAsync(
            "DELETE FROM OrderBomRequestLink WHERE BatchNo = @BatchNo AND PlanVersionId IN @PlanVersionIds",
            new { BatchNo = batchNo, PlanVersionIds = planVersionIds },
            transaction: transaction);

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

        // 【2026-10-08 修复】原为 `DateTime.UtcNow` —— 本文件、乃至 `Services/Sync` 全目录**唯一**一处 UTC。
        //   同一次接货事务里：`APS_BOM_RAW.SyncedAt` 是 BulkCopy **直接从 ODS 源带过来的**（源系统本地时间，
        //   实测 13:46:47~13:48:49），而本表的 SyncedAt 却是 UTC（实测 05:56:08）⇒ **同事务两表差 8 小时**，
        //   按时间对账/排时序会直接看错（本日排查即被此绊住：OBRL 的时间戳看起来比 PlanVersion 建行还早 5 小时）。
        //   APS 侧其余生成时间戳统一用本地时间（如 `PeggingOrchestrator.PersistDomainAndPeggingInTransactionAsync`
        //   的 `var now = DateTime.Now;`）⇒ 此处对齐为本地时间。
        var now = DateTime.Now;
        var resolvedCount = 0;
        var skippedCount = 0;
        var noBomCount = 0;

        foreach (var detail in details)
        {
            worksetMap.TryGetValue(detail.RequestDetailId, out var workset);
            var matchedOrders = orderLookup[detail.OrderCanonicalId];

            // SKIPPED = 该 Order 未装入本批任一 PlanVersion 的 [Order] 快照。
            // OrderBomRequestLink.PlanVersionId 既是 NOT NULL 又是唯一键的一半 ⇒ 无法落库；仅计数，不写行。
            if (!matchedOrders.Any())
            {
                skippedCount++;
                continue;
            }

            var linkStatus = workset?.ResolvedBOMNO != null ? "RESOLVED" : "NO_BOM";

            if (linkStatus == "RESOLVED")
            {
                resolvedCount++;
            }
            else
            {
                noBomCount++;
            }

            // 本批**每个命中分区各落一行**（本批实测命中 2 个分区 ⇒ 1 明细 → 2 行）。
            // 唯一键 (PlanVersionId, OrderCanonicalId) 保证分区之间不冲突；同分区内 OC 唯一
            // （实测 ODS 明细与 OrderCanonicalId 为 1:1，OCsWithMultiDetail=0）⇒ 不会撞键。
            foreach (var order in matchedOrders)
            {
                dataTable.Rows.Add(
                    (long)order.PlanVersionId,
                    batchNo,
                    order.OrderId,
                    detail.OrderCanonicalId,
                    (object?)detail.OrderNo ?? DBNull.Value,
                    (object?)detail.SourceSystem ?? DBNull.Value,
                    (object?)detail.SourceOrderId ?? DBNull.Value,
                    detail.RequestDetailId,
                    (object?)detail.RequestedBOMNO ?? DBNull.Value,
                    (object?)workset?.ResolvedBOMNO ?? DBNull.Value,
                    workset?.RepWorksetId != null ? (object)workset.RepWorksetId : DBNull.Value,
                    linkStatus,
                    DBNull.Value,   // ErrorMessage：SKIPPED 已不落库，其余行无错误
                    now);
            }
        }

        // 全部 SKIPPED（无任何 Order 命中本批 PlanVersion）时表为空，跳过写入以避免空 DataTable 的 BulkCopy 边界。
        if (dataTable.Rows.Count > 0)
        {
            await _connectionManager.BulkInsertToTransactionAsync(dataTable, "OrderBomRequestLink", connection, transaction);
        }

        // 计数口径：Total/Skipped/Resolved/NoBom 都按 **RequestDetail 条数**；Rows 是**实际落库行数**
        // （同一明细在每个命中分区各一行 ⇒ 通常 Rows ≈ (Resolved+NoBom) × 分区数）。两者不可混用。
        _logger.LogInformation(
            "OrderBomRequestLink生成完成: BatchNo={BatchNo}, 明细={Total}, 落库={Rows}行, RESOLVED={Resolved}, NO_BOM={NoBom}, SKIPPED={Skipped}",
            batchNo, details.Count, dataTable.Rows.Count, resolvedCount, noBomCount, skippedCount);
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
        public int PlanVersionId { get; set; }
    }
}
