using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using LPS.APS.Core.Authorization;
using LPS.APS.Core.Interfaces;
using LPS.APS.BusinessRules.Services;
using LPS.APS.Core.Dto;
using LPS.APS.Core.DTOs.Governance;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 治理查询控制器（G4/G7：5号位业务接口接入层）
///
/// 路由规范：
///   GET /api/governance-query/runs                    - 排程运行列表（G4）
///   GET /api/governance-query/domain-dependencies     - 排程域依赖（G7）
///
/// 【职责边界 - 2026-09-06审核确认】
/// - 普通结果查询：4 → 5 → 2（5号位中转，2号位运行真值）
/// - 治理动作：4 → 3 → 2（3号位治理，5号位不涉及）
/// - 5号位提供只读查询接口，不修改、不重算业务结果
/// - ScheduleRun状态由2号位运行收口产生，5号位只读展示
/// - 后续目标：G4/G7数据源从直读APS表迁移到2号位Query Service
/// </summary>
[Authorize(Policy = PermissionCodes.PlanView)]
[ApiController]
[Route("api/governance-query")]
public class GovernanceQueryController : ControllerBase
{
    private readonly GovernanceQueryService _service;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<GovernanceQueryController> _logger;

    public GovernanceQueryController(
        GovernanceQueryService service,
        IDataScopeService dataScopeService,
        ILogger<GovernanceQueryController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>解析当前登录用户 Id（无效返回 0 → 范围解析为拒绝全部，安全默认）</summary>
    private int GetCurrentUserId()
        => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0;

    /// <summary>
    /// 查询排程域依赖（G7）
    /// </summary>
    [HttpGet("domain-dependencies")]
    public async Task<ApiResponse<List<DomainDependencyDto>>> GetDomainDependencies(
        [FromQuery] string? domainCode = null,
        [FromQuery] string? direction = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var scope = await _dataScopeService.ResolveScopeAsync(GetCurrentUserId(), cancellationToken);

            // 校验：非空入参必须落在授权范围（Global 全放行，无范围全拒绝）
            if (!string.IsNullOrEmpty(domainCode) && !scope.Allows(DataScopeTypes.Domain, domainCode))
                return ApiResponse<List<DomainDependencyDto>>.Fail(403, "Domain 范围越界");

            // 过滤：空参按授权范围收窄结果集，避免越权返回全域数据
            var allowedDomains = scope.GetValues(DataScopeTypes.Domain);

            var result = await _service.QueryDomainDependenciesAsync(
                domainCode, direction, cancellationToken, allowedDomains);
            return ApiResponse<List<DomainDependencyDto>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query domain dependencies");
            return ApiResponse<List<DomainDependencyDto>>.Fail(500, $"Query failed: {ex.Message}");
        }
    }
}
