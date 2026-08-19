using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 跨域依赖静态扫描服务实现（2号位职责 — 每日 01:50）
/// 
/// 调用 sp_ScanDomainDependency 存储过程，在 APS 库本地完成：
///   1. TRUNCATE Domain_Dependency（全量清空，每天一张新图）
///   2. 扫描 APS_BOM_RAW + Material + ProductFamily 的跨产品族血缘
///   3. 只保留 跨族 + MaterialType='SEMI_FINISHED' 的边
///   4. INSERT 结果，DefaultLeadTimeDays 硬编码 = 2（V1 简化）
///   5. 写 APS_ETL_Log
/// 
/// SP 契约：sp_ScanDomainDependency（.windsurf/docs/APS_跨域依赖扫描DDL补充_v1.0.sql）
/// </summary>
public class DomainDependencyScanService : IDomainDependencyScanService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<DomainDependencyScanService> _logger;

    public DomainDependencyScanService(
        DatabaseConnectionManager connectionManager,
        ILogger<DomainDependencyScanService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DomainDependencyScanResultDto> ScanAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"DOMAIN_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("跨域依赖扫描开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@BatchNo", batchNo);
            spParams.Add("@RowCount", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_ScanDomainDependency",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS);

            stopwatch.Stop();

            var result = new DomainDependencyScanResultDto
            {
                BatchNo = batchNo,
                ScannedEdges = spParams.Get<int>("@RowCount"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "跨域依赖扫描完成: BatchNo={BatchNo}, 跨域边={Edges}, 耗时={Elapsed}ms",
                    batchNo, result.ScannedEdges, stopwatch.ElapsedMilliseconds);

                if (result.ScannedEdges == 0)
                {
                    _logger.LogWarning(
                        "跨域依赖扫描: 未发现任何跨产品族边。" +
                        "请确认 APS_BOM_RAW / Material / ProductFamily 数据已同步，" +
                        "且存在跨产品族半成品流转（MaterialType='SEMI_FINISHED'）。" +
                        "若持续为空，3 号位 02:00 拓扑会降级为全域串行。");
                }
            }
            else
            {
                _logger.LogError(
                    "跨域依赖扫描 SP 返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "跨域依赖扫描异常: BatchNo={BatchNo}", batchNo);

            // SP 内部已写 FAILED 日志（CATCH 分支），此处无需再写
            throw;
        }
    }
}
