using LPS.APS.Application.Services;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Authorization;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 资源主表查询控制器（5号位提供；G1 设备下拉数据源）
///
/// 路由规范：
///   GET /api/resource - 资源主表列表（可部门/类型过滤，含 hasCalendar 日历状态）
///
/// 【权限码】挂 ResourceCalendarView（服务资源日历页设备下拉；与资源日历页同权限）
/// 【职责边界】只读 Resource 主档，不增删改资源
/// </summary>
[Authorize(Policy = PermissionCodes.ResourceCalendarView)]
[ApiController]
[Route("api/resource")]
public class ResourceController : ControllerBase
{
    private readonly IResourceService _service;
    private readonly ILogger<ResourceController> _logger;

    public ResourceController(IResourceService service, ILogger<ResourceController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>资源主表列表（支持按部门/类型过滤；含 hasCalendar）</summary>
    [HttpGet]
    public async Task<ApiResponse<List<ResourceListItemDto>>> GetList(
        [FromQuery] int? departmentId = null,
        [FromQuery] string? resourceType = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _service.GetListAsync(departmentId, resourceType, includeInactive, ct);
            return ApiResponse<List<ResourceListItemDto>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resource GetList failed");
            return ApiResponse<List<ResourceListItemDto>>.Fail(400, ex.Message);
        }
    }
}