using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// PI Position查询控制器（5号位提供给4号位）
///
/// 路由规范：
///   GET /api/pi-position?planVersionId=        - PI Position列表查询
///   GET /api/pi-position/summary?planVersionId= - PI Position汇总
///
/// 【职责边界】
/// - 5号位提供只读查询接口，直接读取ProductionInstructionPositionSnapshot表
/// - 不重算Position，不修改Snapshot
/// </summary>
[Authorize(Policy = PermissionCodes.PlanView)]
[ApiController]
[Route("api/pi-position")]
public class PiPositionController : ControllerBase
{
    private readonly PiPositionQueryService _service;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<PiPositionController> _logger;

    public PiPositionController(
        PiPositionQueryService service,
        IDataScopeService dataScopeService,
        ILogger<PiPositionController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>解析当前登录用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>
    /// 查询PI Position列表
    /// </summary>
    [HttpGet]
    public async Task<ApiResponse<List<PiPositionDto>>> Query(
        [FromQuery] int planVersionId,
        [FromQuery] string? productionInstructionNo = null,
        [FromQuery] string? materialCode = null,
        [FromQuery] string? positionType = null,
        [FromQuery] string? stageCode = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), cancellationToken);

            // 过滤：PI Position 的 Factory Scope = PI 所属生产工厂（31-0 Q4 裁决）。
            // 授权后查看该 PI 完整 Position 集合，空参按授权范围收窄结果集。
            var allowedFactories = scope.GetValues(DataScopeTypes.Factory);

            var result = await _service.QueryAsync(
                planVersionId, productionInstructionNo, materialCode,
                positionType, stageCode, skip, take, cancellationToken,
                allowedFactories);

            return ApiResponse<List<PiPositionDto>>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<List<PiPositionDto>>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query PI positions");
            return ApiResponse<List<PiPositionDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询PI Position汇总
    /// </summary>
    [HttpGet("summary")]
    public async Task<ApiResponse<PiPositionSummaryDto>> GetSummary(
        [FromQuery] int planVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetSummaryAsync(planVersionId, cancellationToken);
            return ApiResponse<PiPositionSummaryDto>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<PiPositionSummaryDto>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PI position summary");
            return ApiResponse<PiPositionSummaryDto>.Fail(500, $"Query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 查询单个生产指令的所有 Position（单 PI 详情）
    /// </summary>
    [HttpGet("{productionInstructionNo}")]
    public async Task<ApiResponse<List<PiPositionDto>>> GetByProductionInstruction(
        string productionInstructionNo,
        [FromQuery] int planVersionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _service.GetByProductionInstructionAsync(
                planVersionId, productionInstructionNo, cancellationToken);

            if (result.Count == 0)
                return ApiResponse<List<PiPositionDto>>.Fail(404, "PI Position not found");

            return ApiResponse<List<PiPositionDto>>.Success(result);
        }
        catch (ArgumentException ex)
        {
            return ApiResponse<List<PiPositionDto>>.Fail(400, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PI position detail");
            return ApiResponse<List<PiPositionDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
