using LPS.APS.Core.Authorization;
using LPS.APS.Core.Entities.Auth;
using LPS.APS.Core.Interfaces;
using LPS.APS.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 审计日志查询控制器（3号位）
/// 独立于 <see cref="RbacController"/>——其类级 auth.manage 门禁会与本类 audit.view 叠加为 AND，
/// 锁死仅持 audit.view 的审计用户（违背最小权限）。本控制器仅要求 audit.view，
/// 路由保持 api/rbac/audit-logs 不变（前端契约零改动）。
/// </summary>
[ApiController]
[Route("api/rbac")]
[Authorize(Policy = PermissionCodes.AuditView)]
public class AuditController : ControllerBase
{
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<AuditController> _logger;

    public AuditController(IAuditLogRepository auditLogRepo, ILogger<AuditController> logger)
    {
        _auditLogRepo = auditLogRepo ?? throw new ArgumentNullException(nameof(auditLogRepo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>审计日志分页查询（审计页；audit.view 权限；操作人/动作/时间范围 可空组合，倒序分页）</summary>
    [HttpGet("audit-logs")]
    public async Task<ApiResponse<IReadOnlyList<AuditLog>>> GetAuditLogs(
        [FromQuery] int? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _auditLogRepo.QueryPagedAsync(userId, action, from, to, page, size, ct);
            return ApiResponse<IReadOnlyList<AuditLog>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "审计日志查询失败");
            return ApiResponse<IReadOnlyList<AuditLog>>.Fail(500, "服务器内部错误");
        }
    }
}