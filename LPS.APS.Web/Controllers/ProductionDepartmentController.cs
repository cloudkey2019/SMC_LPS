using LPS.APS.Application.Services;
using LPS.APS.Application.Services.Dto;
using LPS.APS.Core.Authorization;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 生产部门查询控制器（5号位提供；G2 部门下拉数据源）
///
/// 路由规范：
///   GET /api/production-departments - 生产部门列表（可选仅排程部门）
///
/// 【权限码】挂 ResourceCalendarView（服务资源日历页部门下拉；与资源日历页同权限）
/// 【职责边界】只读 ProductionDepartment，不增删改
/// </summary>
[Authorize(Policy = PermissionCodes.ResourceCalendarView)]
[ApiController]
[Route("api/production-departments")]
public class ProductionDepartmentController : ControllerBase
{
    private readonly IProductionDepartmentService _service;
    private readonly ILogger<ProductionDepartmentController> _logger;

    public ProductionDepartmentController(IProductionDepartmentService service, ILogger<ProductionDepartmentController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>生产部门列表（默认仅活动部门）</summary>
    [HttpGet]
    public async Task<ApiResponse<List<ProductionDepartmentDto>>> GetList(
        [FromQuery] bool? schedulingOnly = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _service.GetListAsync(schedulingOnly, includeInactive, ct);
            return ApiResponse<List<ProductionDepartmentDto>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProductionDepartment GetList failed");
            return ApiResponse<List<ProductionDepartmentDto>>.Fail(400, ex.Message);
        }
    }
}