using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Dto;
using LPS.APS.Engine.Data;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 采购人工ETA维护控制器（5号位提供，供4号位前端调用）
///
/// 路由规范：
///   GET    /api/procurement-manual-eta                  - 查询Manual ETA列表
///   GET    /api/procurement-manual-eta/{poNo}/{lineNo}  - 查询单条记录
///   POST   /api/procurement-manual-eta                  - 新增或更新Manual ETA
///   DELETE /api/procurement-manual-eta/{poNo}/{lineNo}  - 取消Manual ETA
///
/// 【职责边界 - 2026-08-26】
/// - 5号位提供Manual ETA维护API
/// - 2号位消费Manual ETA并计算Effective ETA
///
/// 参考：复审报告P1-01
/// </summary>
[Authorize(Policy = PermissionCodes.ManualEtaView)]
[ApiController]
[Route("api/procurement-manual-eta")]
public class ProcurementManualEtaController : ControllerBase
{
    private readonly ProcurementManualEtaService _service;
    private readonly DatabaseConnectionManager _connectionManager;
    private readonly ILogger<ProcurementManualEtaController> _logger;

    public ProcurementManualEtaController(
        ProcurementManualEtaService service,
        DatabaseConnectionManager connectionManager,
        ILogger<ProcurementManualEtaController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 收货仓库下拉（数据源：ext_MES_ProcessCode_View，processcode→code、processname→name）
    /// GET /api/procurement-manual-eta/warehouses
    /// </summary>
    [HttpGet("warehouses")]
    public async Task<ApiResponse<IReadOnlyList<WarehouseLookupItem>>> ListWarehouses(
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sql = @"
SELECT processcode AS Code, processname AS Name
FROM ext_MES_ProcessCode_View
WHERE (@Search IS NULL OR processcode LIKE '%' + @Search + '%' OR processname LIKE '%' + @Search + '%')
ORDER BY processcode";
            var items = await _connectionManager.QueryAsync<WarehouseLookupItem>(
                sql, new { Search = search }, CommandType.Text, DatabaseId.APS, commandTimeout: 10);
            return ApiResponse<IReadOnlyList<WarehouseLookupItem>>.Success(items.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query warehouses");
            return ApiResponse<IReadOnlyList<WarehouseLookupItem>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询Manual ETA列表
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<PageResult<ProcurementManualEtaOverride>>> Query(
        [FromQuery] string? materialIds = null,
        [FromQuery] string? materialCodes = null,
        [FromQuery] string? poNos = null,
        [FromQuery] string? receivingWarehouses = null,
        [FromQuery] DateTime? etaBefore = null,
        [FromQuery] DateTime? etaAfter = null,
        [FromQuery] DateTime? updatedAfter = null,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<int>? materialIdList = null;
            if (!string.IsNullOrWhiteSpace(materialIds))
            {
                materialIdList = materialIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.Parse(x.Trim()))
                    .ToList();
            }

            List<string>? materialCodeList = null;
            if (!string.IsNullOrWhiteSpace(materialCodes))
            {
                materialCodeList = materialCodes.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList();
            }

            List<string>? poNoList = null;
            if (!string.IsNullOrWhiteSpace(poNos))
            {
                poNoList = poNos.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList();
            }

            List<string>? warehouseList = null;
            if (!string.IsNullOrWhiteSpace(receivingWarehouses))
            {
                warehouseList = receivingWarehouses.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .ToList();
            }

            if (pageIndex <= 0) pageIndex = 1;
            if (pageSize <= 0 || pageSize > 200) pageSize = 20;

            var result = await _service.QueryAsync(
                materialIds: materialIdList,
                materialCodes: materialCodeList,
                poNos: poNoList,
                receivingWarehouses: warehouseList,
                etaBefore: etaBefore,
                etaAfter: etaAfter,
                updatedAfter: updatedAfter,
                activeOnly: activeOnly,
                pageIndex: pageIndex,
                pageSize: pageSize,
                ct: cancellationToken);

            return ApiResponse<PageResult<ProcurementManualEtaOverride>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query Manual ETA records");
            return ApiResponse<PageResult<ProcurementManualEtaOverride>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据业务键查询单条Manual ETA记录
    /// </summary>
    [HttpGet("{poNo}/{lineNo}")]
    public async Task<ApiResponse<ProcurementManualEtaOverride?>> GetByBusinessKey(
        [FromRoute] string poNo,
        [FromRoute] int lineNo,
        [FromQuery] int materialId,
        [FromQuery] string receivingWarehouse,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetByBusinessKeyAsync(poNo, lineNo, materialId, receivingWarehouse, cancellationToken);
            if (result == null)
                return ApiResponse<ProcurementManualEtaOverride?>.Fail(404, "Record not found");

            return ApiResponse<ProcurementManualEtaOverride?>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<ProcurementManualEtaOverride?>.Fail(400, $"Invalid parameters: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Manual ETA record");
            return ApiResponse<ProcurementManualEtaOverride?>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 新增或更新Manual ETA
    /// </summary>
    [Authorize(Policy = PermissionCodes.ManualEtaMaintain)]
    [HttpPost]
    public async Task<ApiResponse<ProcurementManualEtaOverride>> Upsert(
        [FromBody] ProcurementManualEtaOverride etaOverride,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var saved = await _service.UpsertAsync(etaOverride, cancellationToken);
            return ApiResponse<ProcurementManualEtaOverride>.Success(saved, "Manual ETA saved successfully");
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<ProcurementManualEtaOverride>.Fail(400, $"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upsert Manual ETA");
            return ApiResponse<ProcurementManualEtaOverride>.Fail(500, $"Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 取消Manual ETA（设置IsActive=0）
    /// </summary>
    [Authorize(Policy = PermissionCodes.ManualEtaCancel)]
    [HttpDelete("{poNo}/{lineNo}")]
    public async Task<ApiResponse<CancelManualEtaResponse>> Cancel(
        [FromRoute] string poNo,
        [FromRoute] int lineNo,
        [FromBody] CancelManualEtaRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _service.CancelAsync(
                poNo, lineNo, request.MaterialId, request.ReceivingWarehouse,
                request.UpdatedBy, cancellationToken);

            if (!success)
            {
                // 可能是记录不存在，也可能是已经inactive（幂等）
                var existing = await _service.GetByBusinessKeyAsync(
                    poNo, lineNo, request.MaterialId, request.ReceivingWarehouse, cancellationToken);

                if (existing == null)
                    return ApiResponse<CancelManualEtaResponse>.Fail(404, "Record not found");

                // 记录存在但已inactive，幂等返回200
                if (!existing.IsActive)
                {
                    return ApiResponse<CancelManualEtaResponse>.Success(
                        new CancelManualEtaResponse
                        {
                            PONo = poNo,
                            LineNo = lineNo,
                            MaterialId = request.MaterialId,
                            ReceivingWarehouse = request.ReceivingWarehouse,
                            AlreadyCanceled = true,
                            CanceledAt = existing.UpdatedAt
                        },
                        "Manual ETA was already inactive");
                }

                return ApiResponse<CancelManualEtaResponse>.Fail(404, "Record not found");
            }

            return ApiResponse<CancelManualEtaResponse>.Success(
                new CancelManualEtaResponse
                {
                    PONo = poNo,
                    LineNo = lineNo,
                    MaterialId = request.MaterialId,
                    ReceivingWarehouse = request.ReceivingWarehouse,
                    AlreadyCanceled = false,
                    CanceledAt = DateTime.Now
                },
                "Manual ETA canceled successfully");
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<CancelManualEtaResponse>.Fail(400, $"Invalid parameters: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel Manual ETA");
            return ApiResponse<CancelManualEtaResponse>.Fail(500, $"Cancel failed: {ex.Message}");
        }
    }
}

/// <summary>
/// 收货仓库下拉项（数据源 ext_MES_ProcessCode_View：processcode→Code、processname→Name）
/// </summary>
public sealed class WarehouseLookupItem
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 取消Manual ETA请求体
/// </summary>
public class CancelManualEtaRequest
{
    public int MaterialId { get; init; }
    public string ReceivingWarehouse { get; init; } = string.Empty;
    public string UpdatedBy { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

/// <summary>
/// 取消Manual ETA响应体
/// </summary>
public class CancelManualEtaResponse
{
    public string PONo { get; init; } = string.Empty;
    public int LineNo { get; init; }
    public int MaterialId { get; init; }
    public string ReceivingWarehouse { get; init; } = string.Empty;
    public bool AlreadyCanceled { get; init; }
    public DateTime CanceledAt { get; init; }
}
