using LPS.APS.Application.Services;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Authorization;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 人工能力槽维护控制器（5号位提供，供4号位页面）
///
/// 路由规范：
///   POST   /api/manual-capacity/slots                     - 新增人工槽主档
///   GET    /api/manual-capacity/slots                     - 查询人工槽主档（可按部门/工序过滤）
///   DELETE /api/manual-capacity/slots/{manualSlotId}      - 软删人工槽主档（IsActive=0）
///   POST   /api/manual-capacity/calendar                  - 批量铺人工槽窗口（Days=1 覆盖逐条）
///   GET    /api/manual-capacity/calendar/{manualSlotId}   - 查询某人工槽所有窗口
///   DELETE /api/manual-capacity/calendar/{id}             - 物理删除某窗口
///
/// 【权限码】3号位已落：ManualCapacityView / Edit / Delete（43 码）
/// 【职责边界】主档软删 IsActive=0；窗口物理删 DELETE；5号位维护，不重算业务
/// </summary>
[Authorize(Policy = PermissionCodes.ManualCapacityView)]
[ApiController]
[Route("api/manual-capacity")]
public class ManualCapacityController : ControllerBase
{
    private readonly IManualCapacityService _service;
    private readonly ILogger<ManualCapacityController> _logger;

    public ManualCapacityController(IManualCapacityService service, ILogger<ManualCapacityController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>新增人工槽主档</summary>
    [HttpPost("slots")]
    [Authorize(Policy = PermissionCodes.ManualCapacityEdit)]
    public async Task<ApiResponse<ManualCapacitySlotDto>> CreateSlot(ManualCapacitySlotDto slot, CancellationToken ct = default)
    {
        try
        {
            var created = await _service.CreateSlotAsync(slot, ct);
            return ApiResponse<ManualCapacitySlotDto>.Success(created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ManualCapacity CreateSlot failed");
            return ApiResponse<ManualCapacitySlotDto>.Fail(400, ex.Message);
        }
    }

    /// <summary>查询人工槽主档（软删不含在默认查询；可按部门/工序过滤）</summary>
    [HttpGet("slots")]
    public async Task<ApiResponse<List<ManualCapacitySlotDto>>> GetSlots(
        [FromQuery] int? departmentId = null,
        [FromQuery] string? operationName = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _service.GetSlotsAsync(departmentId, operationName, includeInactive, ct);
            return ApiResponse<List<ManualCapacitySlotDto>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ManualCapacity GetSlots failed");
            return ApiResponse<List<ManualCapacitySlotDto>>.Fail(400, ex.Message);
        }
    }

    /// <summary>软删人工槽主档（IsActive=0）</summary>
    [HttpDelete("slots/{manualSlotId}")]
    [Authorize(Policy = PermissionCodes.ManualCapacityDelete)]
    public async Task<ApiResponse<bool>> SoftDeleteSlot([FromRoute] int manualSlotId, CancellationToken ct = default)
    {
        try
        {
            var ok = await _service.SoftDeleteSlotAsync(manualSlotId, ct);
            return ApiResponse<bool>.Success(ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ManualCapacity SoftDeleteSlot failed");
            return ApiResponse<bool>.Fail(400, ex.Message);
        }
    }

    /// <summary>批量铺人工槽窗口</summary>
    [HttpPost("calendar")]
    [Authorize(Policy = PermissionCodes.ManualCapacityEdit)]
    public async Task<ApiResponse<int>> BulkCreateCalendar(ManualSlotCalendarBulkRequest request, CancellationToken ct = default)
    {
        try
        {
            var inserted = await _service.BulkCreateCalendarAsync(request, ct);
            return ApiResponse<int>.Success(inserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ManualCapacity BulkCreateCalendar failed");
            return ApiResponse<int>.Fail(400, ex.Message);
        }
    }

    /// <summary>查询某人工槽所有窗口</summary>
    [HttpGet("calendar/{manualSlotId}")]
    public async Task<ApiResponse<List<ManualSlotCalendarDto>>> GetCalendar([FromRoute] int manualSlotId, CancellationToken ct = default)
    {
        try
        {
            var rows = await _service.GetCalendarBySlotAsync(manualSlotId, ct);
            return ApiResponse<List<ManualSlotCalendarDto>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ManualCapacity GetCalendar failed");
            return ApiResponse<List<ManualSlotCalendarDto>>.Fail(400, ex.Message);
        }
    }

    /// <summary>物理删除某人工槽窗口</summary>
    [HttpDelete("calendar/{id}")]
    [Authorize(Policy = PermissionCodes.ManualCapacityDelete)]
    public async Task<ApiResponse<bool>> DeleteCalendar([FromRoute] long id, CancellationToken ct = default)
    {
        try
        {
            var ok = await _service.DeleteCalendarAsync(id, ct);
            return ApiResponse<bool>.Success(ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ManualCapacity DeleteCalendar failed");
            return ApiResponse<bool>.Fail(400, ex.Message);
        }
    }
}