using LPS.APS.Application.Services;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Authorization;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 设备资源日历维护控制器（5号位提供，供4号位页面）
///
/// 路由规范：
///   POST   /api/resource-calendar/slots          - 批量铺窗（一次生成连续多天窗口；Days=1 覆盖逐条）
///   GET    /api/resource-calendar/{resourceId}    - 查询某设备所有窗口
///   DELETE /api/resource-calendar/slots/{id}      - 物理删除某窗口
///
/// 【权限码】3号位已落：ResourceCalendarView / Edit / Delete（43 码）
/// 【职责边界】5号位提供维护接口；不改 Solver / 不触 Pegging
/// </summary>
[Authorize(Policy = PermissionCodes.ResourceCalendarView)]
[ApiController]
[Route("api/resource-calendar")]
public class ResourceCalendarController : ControllerBase
{
    private readonly IResourceCalendarService _service;
    private readonly ILogger<ResourceCalendarController> _logger;

    public ResourceCalendarController(IResourceCalendarService service, ILogger<ResourceCalendarController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>批量铺窗（一次生成连续多天窗口）</summary>
    [HttpPost("slots")]
    [Authorize(Policy = PermissionCodes.ResourceCalendarEdit)]
    public async Task<ApiResponse<int>> BulkCreate(ResourceCalendarBulkRequest request, CancellationToken ct = default)
    {
        try
        {
            var inserted = await _service.BulkCreateAsync(request, ct);
            return ApiResponse<int>.Success(inserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResourceCalendar BulkCreate failed");
            return ApiResponse<int>.Fail(400, ex.Message);
        }
    }

    /// <summary>查询某设备所有窗口（可按时间范围过滤）</summary>
    [HttpGet("{resourceId}")]
    public async Task<ApiResponse<List<ResourceCalendarEntryDto>>> Get(
        [FromRoute] int resourceId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _service.GetByResourceAsync(resourceId, from, to, ct);
            return ApiResponse<List<ResourceCalendarEntryDto>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResourceCalendar Get failed");
            return ApiResponse<List<ResourceCalendarEntryDto>>.Fail(400, ex.Message);
        }
    }

    /// <summary>物理删除某窗口</summary>
    [HttpDelete("slots/{id}")]
    [Authorize(Policy = PermissionCodes.ResourceCalendarDelete)]
    public async Task<ApiResponse<bool>> Delete(
        [FromRoute] long id,
        CancellationToken ct = default)
    {
        try
        {
            var ok = await _service.DeleteAsync(id, ct);
            return ApiResponse<bool>.Success(ok);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResourceCalendar Delete failed");
            return ApiResponse<bool>.Fail(400, ex.Message);
        }
    }
}