using System.Data;
using Dapper;
using LPS.APS.Engine.Data;
using LPS.APS.Engine.Services.Sync.Dto;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 工艺路线同步服务实现（2号位职责 — 每日 00:25）
///
/// 调用 sp_SyncRoutingData 存储过程，在 APS 库本地完成：
///   1. 从 ext_MES_APS_Routing_Operation_View 同步到 RoutingOperation（MERGE）
///   2. 从 ext_MES_APS_Routing_Dependency_View 同步到 RoutingDependency（MERGE）
///   3. 从 ext_MES_APS_Routing_Stage_View    同步到 RoutingStage（MERGE）
///   4. 从 ext_APS_OperationResourceEligibility_View 同步到 OperationResourceEligibility（MERGE）
///   5. 通过 MaterialMapping(Source='MES', IsCurrent=1) 映射 MES_ID → MaterialId
///   6. 通过 ProductionDepartment(DeptCode, IsActive=1) 映射 ProductionDeptCode → ProductionDepartmentId
///   7. 通过 Resource(IsActive=1) 映射 ResourceCode → ResourceId
///   8. 软删除：视图中不再出现的记录标记 IsActive=0
///
/// SP 契约：sp_SyncRoutingData.sql（参见 .windsurf/docs/sql/）
/// </summary>
public class RoutingSyncService : IRoutingSyncService
{
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<RoutingSyncService> _logger;

    public RoutingSyncService(
        DatabaseConnectionManager connectionManager,
        ILogger<RoutingSyncService> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<RoutingSyncResultDto> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var batchNo = $"ROUTING_{DateTime.Now:yyyyMMdd_HHmmss}";
        _logger.LogInformation("工艺路线同步开始: BatchNo={BatchNo}", batchNo);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var spParams = new DynamicParameters();
            spParams.Add("@BatchNo", batchNo);

            // RoutingOperation 输出参数
            spParams.Add("@OperationInserted", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@OperationUpdated", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@OperationDeactivated", dbType: DbType.Int32, direction: ParameterDirection.Output);

            // RoutingDependency 输出参数
            spParams.Add("@DependencyInserted", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@DependencyUpdated", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@DependencyDeactivated", dbType: DbType.Int32, direction: ParameterDirection.Output);

            // RoutingStage 输出参数
            spParams.Add("@StageInserted", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@StageUpdated", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@StageDeactivated", dbType: DbType.Int32, direction: ParameterDirection.Output);

            // OperationResourceEligibility 输出参数
            spParams.Add("@EligibilityInserted", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@EligibilityUpdated", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@EligibilityDeactivated", dbType: DbType.Int32, direction: ParameterDirection.Output);

            // 合计未映射跳过行数 + ResourceCode未映射 + ProductionDeptCode未映射 + 错误信息
            spParams.Add("@UnmappedSkipped", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ResourceUnmappedSkipped", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@DeptUnmappedSkipped", dbType: DbType.Int32, direction: ParameterDirection.Output);
            spParams.Add("@ErrorMessage", dbType: DbType.String, size: 4000, direction: ParameterDirection.Output);

            await _connectionManager.ExecuteAsync(
                "sp_SyncRoutingData",
                spParams,
                CommandType.StoredProcedure,
                DatabaseId.APS,
                commandTimeout: 600);

            stopwatch.Stop();

            var result = new RoutingSyncResultDto
            {
                BatchNo = batchNo,
                OperationInserted = spParams.Get<int>("@OperationInserted"),
                OperationUpdated = spParams.Get<int>("@OperationUpdated"),
                OperationDeactivated = spParams.Get<int>("@OperationDeactivated"),
                DependencyInserted = spParams.Get<int>("@DependencyInserted"),
                DependencyUpdated = spParams.Get<int>("@DependencyUpdated"),
                DependencyDeactivated = spParams.Get<int>("@DependencyDeactivated"),
                StageInserted = spParams.Get<int>("@StageInserted"),
                StageUpdated = spParams.Get<int>("@StageUpdated"),
                StageDeactivated = spParams.Get<int>("@StageDeactivated"),
                EligibilityInserted = spParams.Get<int>("@EligibilityInserted"),
                EligibilityUpdated = spParams.Get<int>("@EligibilityUpdated"),
                EligibilityDeactivated = spParams.Get<int>("@EligibilityDeactivated"),
                UnmappedSkipped = spParams.Get<int>("@UnmappedSkipped"),
                ResourceUnmappedSkipped = spParams.Get<int>("@ResourceUnmappedSkipped"),
                DeptUnmappedSkipped = spParams.Get<int>("@DeptUnmappedSkipped"),
                ErrorMessage = spParams.Get<string?>("@ErrorMessage")
            };

            if (result.IsSuccess)
            {
                _logger.LogInformation(
                    "工艺路线同步完成: BatchNo={BatchNo}, 耗时={Elapsed}ms | " +
                    "Operation(I/U/D)={OpI}/{OpU}/{OpD} | " +
                    "Dependency(I/U/D)={DepI}/{DepU}/{DepD} | " +
                    "Stage(I/U/D)={StgI}/{StgU}/{StgD} | " +
                    "Eligibility(I/U/D)={EliI}/{EliU}/{EliD} | " +
                    "未映射跳过={Unmapped}, 资源未映射={ResUnmapped}, 部门未映射={DeptUnmapped}",
                    batchNo, stopwatch.ElapsedMilliseconds,
                    result.OperationInserted, result.OperationUpdated, result.OperationDeactivated,
                    result.DependencyInserted, result.DependencyUpdated, result.DependencyDeactivated,
                    result.StageInserted, result.StageUpdated, result.StageDeactivated,
                    result.EligibilityInserted, result.EligibilityUpdated, result.EligibilityDeactivated,
                    result.UnmappedSkipped, result.ResourceUnmappedSkipped, result.DeptUnmappedSkipped);

                if (result.UnmappedSkipped > 0)
                {
                    _logger.LogWarning(
                        "工艺路线同步: 有 {Unmapped} 行因 MaterialMapping 缺失被跳过（MES_ID 未在 Source='MES' 主数据中登记），" +
                        "请检查 MES 物料主数据同步是否先行完成（文档 §2.4.3）",
                        result.UnmappedSkipped);
                }

                if (result.ResourceUnmappedSkipped > 0)
                {
                    _logger.LogWarning(
                        "工艺路线同步: 有 {ResUnmapped} 行因 Resource 表中不存在对应 ResourceCode 被跳过，" +
                        "请检查 Resource 主数据同步是否先行完成",
                        result.ResourceUnmappedSkipped);
                }

                if (result.DeptUnmappedSkipped > 0)
                {
                    _logger.LogWarning(
                        "工艺路线同步: 有 {DeptUnmapped} 行因 ProductionDepartment 字典中不存在对应 ProductionDeptCode 被跳过，" +
                        "请检查 ProductionDepartment 字典表数据是否完整",
                        result.DeptUnmappedSkipped);
                }
            }
            else
            {
                _logger.LogError(
                    "工艺路线同步SP返回错误: BatchNo={BatchNo}, Error={ErrorMessage}",
                    batchNo, result.ErrorMessage);
            }

            await LogETLAsync(batchNo, "RoutingSync",
                $"Op(I/U/D)={result.OperationInserted}/{result.OperationUpdated}/{result.OperationDeactivated}, " +
                $"Dep(I/U/D)={result.DependencyInserted}/{result.DependencyUpdated}/{result.DependencyDeactivated}, " +
                $"Stage(I/U/D)={result.StageInserted}/{result.StageUpdated}/{result.StageDeactivated}, " +
                $"Elig(I/U/D)={result.EligibilityInserted}/{result.EligibilityUpdated}/{result.EligibilityDeactivated}, " +
                $"Unmapped={result.UnmappedSkipped}, ResUnmapped={result.ResourceUnmappedSkipped}, DeptUnmapped={result.DeptUnmappedSkipped}, Elapsed={stopwatch.ElapsedMilliseconds}ms",
                result.IsSuccess ? "SUCCESS" : "FAILED");

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "工艺路线同步异常: BatchNo={BatchNo}", batchNo);

            await LogETLAsync(batchNo, "RoutingSync",
                $"工艺路线同步异常: {ex.Message}", "FAILED");

            throw;
        }
    }

    /// <summary>
    /// 记录 ETL 日志到 APS_ETL_Log（对齐 NightlyBatchOrchestrator / MasterDataSyncService 风格）
    /// </summary>
    private async Task LogETLAsync(string batchNo, string step, string message, string status)
    {
        try
        {
            await _connectionManager.ExecuteAsync(
                @"INSERT INTO APS_ETL_Log (BatchNo, Step, Message, Status, CreatedAt)
                  VALUES (@BatchNo, @Step, @Message, @Status, GETDATE())",
                new { BatchNo = batchNo, Step = step, Message = message, Status = status },
                db: DatabaseId.APS);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写入 ETL 日志失败（非致命）");
        }
    }
}
