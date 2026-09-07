using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 物料×阶段→默认生产部门 上下文重建服务实现（2号位职责 — 每日 01:55）
///
/// 调用 sp_RebuildMaterialStageDeptContext 存储过程（@TriggerMode='FULL'），
/// 在 APS 库本地全量重建 MaterialStageDeptContext（SCD Type 2）。
/// V1：INCR/PARTIAL 触发源未接，统一走 FULL；SP 内部对非 FULL 模式登记 WARN 后降级。
///
/// 排程顺序：位于 domain-dependency-scan（01:50）之后、scheduling-trigger（02:00）之前，
/// 确保 1号位排程消费的 (MaterialId, StageCode) → DefaultProductionDepartmentId 映射为当日最新。
/// </summary>
public class MaterialStageDeptContextRebuildService : IMaterialStageDeptContextRebuildService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<MaterialStageDeptContextRebuildService> _logger;

    public MaterialStageDeptContextRebuildService(
        DatabaseConnectionManager connectionManager,
        ILogger<MaterialStageDeptContextRebuildService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MaterialStageDeptContextRebuildResultDto> RebuildAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"MSC_CTX_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("物料×阶段部门上下文重建开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@TriggerMode", "FULL");
            spParams.Add("@BatchNo", batchNo);
            // @TargetMaterialIds / @TargetStageCodes 仅 PARTIAL 模式使用，FULL 走默认 NULL，不传
            spParams.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_RebuildMaterialStageDeptContext",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS,
                commandTimeout: 600);

            stopwatch.Stop();

            var result = new MaterialStageDeptContextRebuildResultDto
            {
                BatchNo = batchNo,
                RowsAffected = spParams.Get<int>("@RowsAffected"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "物料×阶段部门上下文重建完成: BatchNo={BatchNo}, 新增/变更+失效={Rows}, 耗时={Elapsed}ms",
                    batchNo, result.RowsAffected, stopwatch.ElapsedMilliseconds);

                if (result.RowsAffected == 0)
                {
                    _logger.LogWarning(
                        "物料×阶段部门上下文重建: 受影响 0 行。" +
                        "请确认 MaterialSupplyContext(IsCurrent=1) 已同步，且 DefaultProductionDeptCode 能命中 ProductionDepartment 字典；" +
                        "若持续为 0，1号位将拿不到 (MaterialId,StageCode)→默认部门 映射。");
                }
            }
            else
            {
                _logger.LogError(
                    "物料×阶段部门上下文重建 SP 返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "物料×阶段部门上下文重建异常: BatchNo={BatchNo}", batchNo);

            // SP 内部已写 FAILED 日志（CATCH 分支），此处无需再写
            throw;
        }
    }
}
